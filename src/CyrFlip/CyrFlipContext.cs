using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// Background app shell living in the notification area (system tray). Owns the keyboard
    /// hook, the layout indicator and the tray icon/menu, and runs the flip pipeline off the
    /// hook on a dedicated background thread.
    ///
    /// Tray menu: a header showing the flip hotkey, a "Start with Windows" toggle, and Exit.
    /// </summary>
    internal sealed class CyrFlipContext : ApplicationContext
    {
        private readonly AppConfig _config;
        private readonly Hotkey _hotkey;
        private readonly KeyboardHook _hook = new KeyboardHook();
        private readonly CursorIndicator _indicator = new CursorIndicator();
        private readonly ClipboardHandler _clipboard = new ClipboardHandler();
        private readonly LayoutCursor _layoutCursor;
        private readonly CaretOverlay _caretOverlay;
        private readonly NotifyIcon _tray;
        private readonly ToolStripMenuItem _autostartItem;

        private readonly SynchronizationContext? _ui; // to post tray feedback back to the UI thread
        private Icon? _trayIcon;
        private int _flipping; // 0 = idle, 1 = a flip is in progress

        public CyrFlipContext(AppConfig config)
        {
            _config = config;
            _hotkey = Hotkey.Parse(_config.Hotkey);
            _layoutCursor = new LayoutCursor(_config.CursorSize);
            _caretOverlay = new CaretOverlay(_config.CursorSize);

            // SetSystemCursor is global — guarantee the default cursors are restored even
            // if the app is killed or throws.
            AppDomain.CurrentDomain.ProcessExit += (_, _) => LayoutCursor.ForceRestore();
            AppDomain.CurrentDomain.UnhandledException += (_, _) => LayoutCursor.ForceRestore();
            Application.ApplicationExit += (_, _) => LayoutCursor.ForceRestore();

            if (Autostart.ManagedByWindows)
            {
                // Packaged (MSIX): the OS owns the startup toggle (manifest startupTask);
                // open the Windows "Startup apps" settings page rather than flip a checkbox.
                _autostartItem = new ToolStripMenuItem("Start with Windows…", null, OnOpenStartupSettings);
            }
            else
            {
                _autostartItem = new ToolStripMenuItem("Start with Windows", null, OnToggleAutostart)
                {
                    CheckOnClick = true,
                    Checked = Autostart.IsEnabled,
                };
            }

            var menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem($"Flip EN ⇄ RU:  {_hotkey.Display}") { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_autostartItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()));
            // Keep the checkmark in sync with the registry (unpackaged only; packaged item isn't checkable).
            menu.Opening += (_, _) =>
            {
                if (!Autostart.ManagedByWindows)
                    _autostartItem.Checked = Autostart.IsEnabled;
            };

            Icon initialIcon = TryGetAppIcon();
            _tray = new NotifyIcon
            {
                Icon = initialIcon,
                Text = "CyrFlip",
                Visible = true,
                ContextMenuStrip = menu,
            };
            // Track it for disposal when the first layout icon replaces it — but never dispose
            // the shared SystemIcons.Application.
            if (initialIcon != SystemIcons.Application)
                _trayIcon = initialIcon;

            _indicator.LayoutChanged += OnLayoutChanged;

            _hook.HotkeyPressed += OnHotkeyPressed;
            _hook.Install(_hotkey);
            _caretOverlay.Start();
            _indicator.Start();

            // Captured after the overlay/indicator created control handles, so this is the
            // WinForms sync context — lets the background flip thread post tray feedback to the UI.
            _ui = SynchronizationContext.Current;
        }

        private void OnLayoutChanged(string code)
        {
            // Main feature: mark the active layout where the user types.
            _layoutCursor.Apply(code);   // the system text cursor (I-beam)
            _caretOverlay.SetLayout(code); // a marker pinned next to the blinking caret
            LayoutPublisher.Publish(code); // for the companion VS Code extension

            // Secondary: reflect the layout in the tray icon + tooltip too.
            _tray.Text = $"CyrFlip - {code}  ({_hotkey.Display} to flip)";

            Icon icon = CursorIndicator.RenderIcon(code);
            _tray.Icon = icon;
            _trayIcon?.Dispose();
            _trayIcon = icon;
        }

        private void OnHotkeyPressed(object? sender, EventArgs e)
        {
            // Ignore re-triggers (key auto-repeat, or a flip already running).
            if (Interlocked.CompareExchange(ref _flipping, 1, 0) != 0)
                return;

            var thread = new Thread(() =>
            {
                try
                {
                    ClipboardHandler.FlipResult result = _clipboard.Flip();
                    _ui?.Post(_ => ShowFlipResult(result), null);
                }
                catch { /* never let a flip take the app down */ }
                finally { Interlocked.Exchange(ref _flipping, 0); }
            })
            {
                IsBackground = true, // Win32Clipboard is apartment-agnostic — no STA needed
            };
            thread.Start();
        }

        private void ShowFlipResult(ClipboardHandler.FlipResult result)
        {
            // Only speak up when the flip didn't do anything; stay silent on success.
            switch (result)
            {
                case ClipboardHandler.FlipResult.NoSelection:
                    _tray.ShowBalloonTip(1500, "CyrFlip", "Nothing selected to flip.", ToolTipIcon.Info);
                    break;
                case ClipboardHandler.FlipResult.Failed:
                    _tray.ShowBalloonTip(2000, "CyrFlip", "Couldn't read or replace the selection.", ToolTipIcon.Warning);
                    break;
            }
        }

        private void OnToggleAutostart(object? sender, EventArgs e)
        {
            try
            {
                Autostart.Set(_autostartItem.Checked);
            }
            catch (Exception ex)
            {
                _autostartItem.Checked = Autostart.IsEnabled; // revert the checkmark on failure
                MessageBox.Show("Couldn't update Windows startup:\n" + ex.Message,
                    "CyrFlip", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // Packaged (MSIX) builds: autostart is a manifest startupTask the user controls in
        // Windows Settings ▸ Apps ▸ Startup. Take them straight there.
        private void OnOpenStartupSettings(object? sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true });
            }
            catch { /* best effort — never let the tray menu throw */ }
        }

        private static Icon TryGetAppIcon()
        {
            try
            {
                return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hook.Dispose();
                _indicator.Dispose();
                _layoutCursor.Dispose(); // restores the default system cursor
                _caretOverlay.Dispose();
                _tray.Visible = false;
                _tray.Dispose();
                _trayIcon?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

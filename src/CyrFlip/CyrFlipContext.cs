using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// Background app shell living in the notification area (system tray). Owns the keyboard
    /// hook, the layout indicator and the tray icon/menu, and runs the flip pipeline off the
    /// hook on a dedicated STA thread.
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

            _autostartItem = new ToolStripMenuItem("Start with Windows", null, OnToggleAutostart)
            {
                CheckOnClick = true,
                Checked = Autostart.IsEnabled,
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem($"Flip EN ⇄ RU:  {_hotkey.Display}") { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_autostartItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()));
            menu.Opening += (_, _) => _autostartItem.Checked = Autostart.IsEnabled;

            _tray = new NotifyIcon
            {
                Icon = TryGetAppIcon(),
                Text = "CyrFlip",
                Visible = true,
                ContextMenuStrip = menu,
            };

            _indicator.LayoutChanged += OnLayoutChanged;

            _hook.HotkeyPressed += OnHotkeyPressed;
            _hook.Install(_hotkey);
            _caretOverlay.Start();
            _indicator.Start();
        }

        private void OnLayoutChanged(string code)
        {
            // Main feature: mark the active layout where the user types.
            _layoutCursor.Apply(code);   // the system text cursor (I-beam)
            _caretOverlay.SetLayout(code); // a marker pinned next to the blinking caret

            // Secondary: reflect the layout in the tray icon + tooltip too.
            _tray.Text = $"CyrFlip — {code}  ({_hotkey.Display} to flip)";

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
                try { _clipboard.Flip(); }
                catch { /* never let a flip take the app down */ }
                finally { Interlocked.Exchange(ref _flipping, 0); }
            })
            {
                IsBackground = true,
            };
            thread.SetApartmentState(ApartmentState.STA); // WinForms clipboard requires STA
            thread.Start();
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

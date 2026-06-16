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
    /// Tray menu:
    ///   - disabled header showing the current hotkey
    ///   - "Set hotkey..." → opens HotkeyDialog
    ///   - toggles for cursor indicator, caret overlay, caret dot style
    ///   - "Start with Windows"
    ///   - Exit
    /// </summary>
    internal sealed class CyrFlipContext : ApplicationContext
    {
        private readonly AppConfig _config;
        private Hotkey _hotkey;
        private readonly KeyboardHook _hook = new KeyboardHook();
        private readonly CursorIndicator _indicator = new CursorIndicator();
        private readonly ClipboardHandler _clipboard = new ClipboardHandler();
        private readonly LayoutCursor _layoutCursor;
        private readonly CaretOverlay _caretOverlay;
        private readonly NotifyIcon _tray;
        private readonly ToolStripMenuItem _autostartItem;
        private readonly ToolStripMenuItem _flipHeader;
        private readonly ToolStripMenuItem _cursorItem;
        private readonly ToolStripMenuItem _caretItem;
        private readonly ToolStripMenuItem _dotModeItem;

        private readonly SynchronizationContext? _ui;
        private Icon? _trayIcon;
        private int _flipping; // 0 = idle, 1 = flip in progress
        private string _currentLayout = "";

        public CyrFlipContext(AppConfig config)
        {
            _config = config;
            _hotkey = Hotkey.Parse(_config.Hotkey);
            _layoutCursor = new LayoutCursor(_config.CursorSize);
            _caretOverlay = new CaretOverlay(_config.CursorSize, _config.CaretDotMode);

            // SetSystemCursor is global - guarantee the default cursors are restored even
            // if the app is killed or throws.
            AppDomain.CurrentDomain.ProcessExit += (_, _) => LayoutCursor.ForceRestore();
            AppDomain.CurrentDomain.UnhandledException += (_, _) => LayoutCursor.ForceRestore();
            Application.ApplicationExit += (_, _) => LayoutCursor.ForceRestore();

            // ---- Autostart ----
            if (Autostart.ManagedByWindows)
            {
                _autostartItem = new ToolStripMenuItem("Start with Windows..", null, OnOpenStartupSettings);
            }
            else
            {
                _autostartItem = new ToolStripMenuItem("Start with Windows", null, OnToggleAutostart)
                {
                    CheckOnClick = true,
                    Checked = Autostart.IsEnabled,
                };
            }

            // ---- Feature toggle items ----
            _flipHeader = new ToolStripMenuItem($"Flip EN ⇄ RU:  {_hotkey.Display}") { Enabled = false };

            _cursorItem = new ToolStripMenuItem("Cursor: layout indicator")
            {
                CheckOnClick = true,
                Checked = _config.EnableCursorChange,
            };
            _cursorItem.CheckedChanged += OnCursorToggle;

            _caretItem = new ToolStripMenuItem("Caret: overlay label")
            {
                CheckOnClick = true,
                Checked = _config.EnableCaretOverlay,
            };
            _caretItem.CheckedChanged += OnCaretToggle;

            _dotModeItem = new ToolStripMenuItem("Caret: dot style")
            {
                CheckOnClick = true,
                Checked = _config.CaretDotMode,
                Enabled = _config.EnableCaretOverlay,
            };
            _dotModeItem.CheckedChanged += OnDotModeToggle;

            // ---- Menu ----
            var menu = new ContextMenuStrip();
            menu.Items.Add(_flipHeader);
            menu.Items.Add(new ToolStripMenuItem("Set hotkey...", null, OnSetHotkey));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_cursorItem);
            menu.Items.Add(_caretItem);
            menu.Items.Add(_dotModeItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_autostartItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()));

            // Keep dynamic state in sync when the menu opens.
            menu.Opening += (_, _) =>
            {
                if (!Autostart.ManagedByWindows)
                    _autostartItem.Checked = Autostart.IsEnabled;
                // Dot style only makes sense when the caret overlay is on.
                _dotModeItem.Enabled = _caretItem.Checked;
            };

            Icon initialIcon = TryGetAppIcon();
            _tray = new NotifyIcon
            {
                Icon = initialIcon,
                Text = "CyrFlip",
                Visible = true,
                ContextMenuStrip = menu,
            };
            // Track it for disposal when the first layout icon replaces it - but never dispose
            // the shared SystemIcons.Application.
            if (initialIcon != SystemIcons.Application)
                _trayIcon = initialIcon;

            _indicator.LayoutChanged += OnLayoutChanged;
            _hook.HotkeyPressed += OnHotkeyPressed;
            _hook.Install(_hotkey);
            _caretOverlay.Start();
            _indicator.Start();

            // Captured after the overlay/indicator created control handles, so this is the
            // WinForms sync context - lets the background flip thread post tray feedback to the UI.
            _ui = SynchronizationContext.Current;
        }

        private void OnLayoutChanged(string code)
        {
            _currentLayout = code;

            // Cursor indicator (global I-beam replacement).
            if (_config.EnableCursorChange)
                _layoutCursor.Apply(code);
            else
                _layoutCursor.Restore();

            // Caret overlay (text label or dot near the blinking caret).
            _caretOverlay.SetLayout(_config.EnableCaretOverlay ? code : "");
            LayoutPublisher.Publish(code);

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
                    if (result == ClipboardHandler.FlipResult.Flipped)
                        _config.IncrementFlipCount();
                    _ui?.Post(_ => ShowFlipResult(result), null);
                }
                catch { /* never let a flip take the app down */ }
                finally { Interlocked.Exchange(ref _flipping, 0); }
            })
            {
                IsBackground = true,
            };
            thread.Start();
        }

        private void ShowFlipResult(ClipboardHandler.FlipResult result)
        {
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

        // ---- Tray menu handlers ----

        private void OnSetHotkey(object? sender, EventArgs e)
        {
            using var dlg = new HotkeyDialog(_hotkey.Display);
            if (dlg.ShowDialog() != DialogResult.OK || dlg.CapturedHotkey == null)
                return;

            var newHotkey = Hotkey.Parse(dlg.CapturedHotkey);
            if (newHotkey.Vk == 0)
                return; // parse produced no trigger key - shouldn't happen if dialog is correct

            _hotkey = newHotkey;
            _config.Hotkey = dlg.CapturedHotkey;
            _config.Save();

            _hook.UpdateHotkey(_hotkey);
            _flipHeader.Text = $"Flip EN ⇄ RU:  {_hotkey.Display}";
            _tray.Text = $"CyrFlip - {_currentLayout}  ({_hotkey.Display} to flip)";
        }

        private void OnCursorToggle(object? sender, EventArgs e)
        {
            _config.EnableCursorChange = _cursorItem.Checked;
            if (_cursorItem.Checked)
            {
                if (_currentLayout.Length > 0) _layoutCursor.Apply(_currentLayout);
            }
            else
            {
                _layoutCursor.Restore();
            }
            _config.Save();
        }

        private void OnCaretToggle(object? sender, EventArgs e)
        {
            _config.EnableCaretOverlay = _caretItem.Checked;
            _dotModeItem.Enabled = _caretItem.Checked;
            _caretOverlay.SetLayout(_caretItem.Checked && _currentLayout.Length > 0 ? _currentLayout : "");
            _config.Save();
        }

        private void OnDotModeToggle(object? sender, EventArgs e)
        {
            _config.CaretDotMode = _dotModeItem.Checked;
            _caretOverlay.SetDotMode(_dotModeItem.Checked);
            _config.Save();
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

        private void OnOpenStartupSettings(object? sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true });
            }
            catch { /* best effort - never let the tray menu throw */ }
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

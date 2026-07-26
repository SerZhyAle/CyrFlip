using System;
using System.Drawing;
using System.Windows.Forms;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// The translation popup: appears next to the mouse pointer, fills in while the model writes, and
    /// never steals focus (<see cref="ShowWithoutActivation"/>), so the user can keep typing in the
    /// window they came from. Clicking it does activate it - that is what makes Esc and text selection
    /// work - which is the compromise the spec asks for (§6.1).
    ///
    /// It is built from ordinary controls in a <see cref="TableLayoutPanel"/> rather than owner-drawn:
    /// captions exist in 13 languages, and <c>DialogLayoutTests</c> can only prove nothing is clipped
    /// when there are real <see cref="Label"/>s and <see cref="Button"/>s to measure.
    /// </summary>
    internal sealed class TranslationResultWindow : Form
    {
        /// <summary>The size the first build shipped with - see the constructor for why it matters.</summary>
        private const int FirstReleaseWidth = 460;
        private const int FirstReleaseHeight = 260;

        private readonly AppConfig _config;
        private readonly Label _header = new Label { AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        private readonly TextBox _source = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BackColor = SystemColors.Control };
        private readonly TextBox _result = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
        // Width is measured from the widest language label in MeasureLanguageWidth - a fixed number
        // clipped "Язык активной раскладки" in the longer languages.
        private readonly ComboBox _language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly FlowLayoutPanel _buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        private readonly TableLayoutPanel _grid = new TableLayoutPanel { ColumnCount = 1, RowCount = 4, Dock = DockStyle.Fill, Padding = new Padding(8) };
        private readonly Button _copy;
        private readonly Button _paste;
        private readonly Button _retry;
        private readonly Button _start;
        private readonly Button _settings;
        private readonly Timer _autoClose = new Timer();
        private readonly Timer _watchForeground = new Timer { Interval = 400 };

        private string _uiLanguage;
        // The script font this window built for itself, if any: WinForms disposes neither the font a
        // control was given nor the one it replaces, so this window owns it (see ApplyScript/Dispose).
        private Font? _ownFont;
        private IntPtr _openedOver = IntPtr.Zero;
        private bool _placed;
        private bool _busy;
        private bool _canPaste = true;
        private bool _loading;

        public TranslationResultWindow(AppConfig config, string uiLanguage)
        {
            _config = config;
            _uiLanguage = uiLanguage;

            // A tool window gives the close button and the resize grip for free, and keeps the popup
            // out of the taskbar and Alt+Tab - all of which an owner-painted window would owe us.
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            MinimumSize = new Size(360, 240);

            int width = config.TranslateWindowWidth;
            int height = config.TranslateWindowHeight;
            // A size the user chose is theirs. But the first build shipped a box too small to read a
            // paragraph in, and it was written to the registry before anyone resized anything - so
            // exactly that size means "never customised" and gets today's roomier default instead.
            if (width == FirstReleaseWidth && height == FirstReleaseHeight)
            {
                width = AppConfig.DefaultTranslateWindowWidth;
                height = AppConfig.DefaultTranslateWindowHeight;
            }
            Size = new Size(Math.Max(MinimumSize.Width, width), Math.Max(MinimumSize.Height, height));

            _copy = Command("Копировать", () => CopyRequested?.Invoke(this, EventArgs.Empty));
            _paste = Command("Вставить", () => PasteRequested?.Invoke(this, EventArgs.Empty));
            _retry = Command("Ещё раз", () => RetryRequested?.Invoke(this, EventArgs.Empty));
            _start = Command("Запустить Ollama", () => StartServerRequested?.Invoke(this, EventArgs.Empty));
            _settings = Command("Настройки", () => SettingsRequested?.Invoke(this, EventArgs.Empty));

            _language.SelectedIndexChanged += (_, _) =>
            {
                if (_loading || !(_language.SelectedItem is LanguageItem item)) return;
                TargetLanguageChosen?.Invoke(item.Code);
            };

            _buttons.Controls.Add(_copy);
            _buttons.Controls.Add(_paste);
            _buttons.Controls.Add(_retry);
            _buttons.Controls.Add(_start);
            _buttons.Controls.Add(_settings);
            _buttons.Controls.Add(_language);

            _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _grid.RowStyles.Add(new RowStyle(SizeType.Percent, 30f));
            _grid.RowStyles.Add(new RowStyle(SizeType.Percent, 70f));
            _grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _grid.Controls.Add(_header, 0, 0);
            _grid.Controls.Add(_source, 0, 1);
            _grid.Controls.Add(_result, 0, 2);
            _grid.Controls.Add(_buttons, 0, 3);
            Controls.Add(_grid);

            _header.Height = Math.Max(20, Font.Height + 6);
            // Every way of dismissing the popup goes through Dismiss(), which cancels a translation
            // still in flight. Hiding without cancelling would let the answer arrive minutes later and
            // paste itself into whatever the user is doing by then.
            _autoClose.Tick += (_, _) => { _autoClose.Stop(); Dismiss(); };
            _watchForeground.Tick += (_, _) => WatchForeground();
            ResizeEnd += (_, _) => SaveBounds();
            FormClosing += (_, e) => { e.Cancel = true; Dismiss(); };
            VisibleChanged += (_, _) => { if (!Visible) { _autoClose.Stop(); _watchForeground.Stop(); } };

            ApplyScript();
            ApplyOpacity();
            ApplyTexts();
            ShowSourcePanel(config.TranslateShowSource);
        }

        /// <summary>The language the shown translation was asked for.</summary>
        public string TargetCode { get; private set; } = TranslationLanguages.UiToken;

        /// <summary>What is currently in the result box - what Copy and Paste act on.</summary>
        public string ResultText => _result.Text;

        public event EventHandler? CopyRequested;
        public event EventHandler? PasteRequested;
        public event EventHandler? RetryRequested;
        public event EventHandler? SettingsRequested;
        /// <summary>The "start Ollama" button offered when the server is installed but not running.</summary>
        public event EventHandler? StartServerRequested;
        /// <summary>The popup was dismissed while a translation was still running.</summary>
        public event EventHandler? CancelRequested;
        public event Action<string>? TargetLanguageChosen;

        /// <summary>True while the model is still writing - the caller must not act on a stale answer.</summary>
        public bool IsTranslating => _busy;

        /// <summary>Never take focus from the window the user was typing in.</summary>
        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        /// <summary>
        /// Show the popup beside the mouse pointer, clamped to the monitor the pointer is on, and
        /// start it in the "translating" state.
        /// </summary>
        /// <param name="target">
        /// The window the selection came from, or <see cref="IntPtr.Zero"/> when there is none (the
        /// tray entry translates the clipboard). It is what the foreground watch anchors on: deriving
        /// the anchor from the foreground window would capture the popup itself on the retry and
        /// change-language paths, which are reached by clicking the popup.
        /// </param>
        public void ShowTranslating(string targetCode, string sourceText, string status, IntPtr target)
        {
            TargetCode = targetCode;
            _busy = true;
            AnchorOn(target);
            SelectLanguage(targetCode);
            _source.Text = sourceText;
            _result.Text = "";
            _header.Text = status;
            SetActions(result: false, settings: false, retry: false, start: false);
            _autoClose.Stop();
            _watchForeground.Stop();
            PlaceAtCursor();
        }

        /// <summary>A chunk of the streamed answer (already on the UI thread).</summary>
        public void AppendChunk(string text)
        {
            if (IsDisposed || text.Length == 0) return;
            _result.AppendText(text);
        }

        /// <summary>The retry starts over, so drop what the first attempt already painted.</summary>
        public void ResetStream()
        {
            if (IsDisposed) return;
            _result.Text = "";
        }

        /// <summary>The finished translation plus the one-line note above it.</summary>
        /// <param name="canPaste">
        /// False when the translation did not come from a selection (the tray entry translates the
        /// clipboard), so there is nothing to paste over and offering the button would be a lie.
        /// Null keeps whatever the current answer was shown with - re-showing the same text with a
        /// different note must not resurrect a button that does not apply.
        /// </param>
        public void ShowResult(string text, string note, bool? canPaste = null)
        {
            if (IsDisposed) return;
            _busy = false;
            if (canPaste.HasValue) _canPaste = canPaste.Value;
            _result.Text = text;
            _result.SelectionStart = 0;
            _result.SelectionLength = 0;
            _header.Text = note;
            SetActions(result: true, settings: false, retry: true, start: false);
            StartWatchers();
        }

        /// <summary>An error or a refusal: one honest line, plus the buttons that can fix it.</summary>
        public void ShowMessage(string message, bool offerSettings, bool offerRetry, bool offerStart = false)
        {
            if (IsDisposed) return;
            _busy = false;
            _result.Text = message;
            _header.Text = "";
            SetActions(result: false, settings: offerSettings, retry: offerRetry, start: offerStart);
            StartWatchers();
        }

        public void ShowSourcePanel(bool visible)
        {
            _source.Visible = visible;
            _grid.RowStyles[1] = visible ? new RowStyle(SizeType.Percent, 30f) : new RowStyle(SizeType.Absolute, 0f);
        }

        public void ApplyOpacity()
            => Opacity = Math.Max(30, Math.Min(100, _config.TranslateWindowOpacity)) / 100.0;

        public void ApplyLanguage(string uiLanguage)
        {
            _uiLanguage = uiLanguage;
            ApplyScript();
            ApplyTexts();
            SelectLanguage(TargetCode);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Dismiss();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// The window the popup belongs to: the one the selection came from, or - when there is none,
        /// or when a retry was started by clicking the popup - whatever is in front that is not us.
        /// </summary>
        private void AnchorOn(IntPtr target)
        {
            if (target != IntPtr.Zero) { _openedOver = target; return; }
            IntPtr foreground = GetForegroundWindow();
            if (foreground != IntPtr.Zero && foreground != Handle) _openedOver = foreground;
        }

        /// <summary>
        /// The single way the popup goes away - Esc, the title-bar ×, the auto-close timer or the
        /// foreground watch. A translation still in flight is cancelled here, because an answer that
        /// arrives after the user dismissed the window would otherwise paste itself into whatever they
        /// moved on to.
        /// </summary>
        public void Dismiss()
        {
            if (_busy)
            {
                _busy = false;
                CancelRequested?.Invoke(this, EventArgs.Empty);
            }
            Hide();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _autoClose.Dispose();
                _watchForeground.Dispose();
            }
            base.Dispose(disposing);
            if (disposing) _ownFont?.Dispose(); // after the controls stop drawing with it
        }

        // ---- internals -------------------------------------------------------------------

        private void SetActions(bool result, bool settings, bool retry, bool start)
        {
            _copy.Visible = result;
            _paste.Visible = result && _canPaste;
            _retry.Visible = retry;
            _start.Visible = start;
            _settings.Visible = settings;
            _language.Visible = result || retry;
        }

        private void StartWatchers()
        {
            // Re-anchor on where the user is *now*, not on where they were when they pressed the
            // chord: someone who looked at another window while the model wrote would otherwise have
            // the finished translation dismissed 400 ms after it appeared.
            AnchorOn(IntPtr.Zero);
            // Only after the answer is in: closing the popup the moment the user clicks back into
            // their editor would throw away the translation they are waiting for.
            _watchForeground.Start();
            int seconds = Math.Max(0, _config.TranslateWindowTimeout);
            if (seconds <= 0) return;
            _autoClose.Interval = seconds * 1000;
            _autoClose.Start();
        }

        /// <summary>
        /// A window that never takes focus never gets <c>Deactivate</c>, so "close when the user goes
        /// somewhere else" is a poll: the popup lives as long as the window it opened over stays in
        /// front (the user is still reading), and closes as soon as they move on.
        /// </summary>
        private void WatchForeground()
        {
            if (!Visible) return;

            // Reading it counts as using it: while the pointer is over the popup the auto-close
            // countdown keeps restarting, so a translation never vanishes mid-sentence. It resumes
            // the moment the pointer leaves.
            if (_autoClose.Enabled && Bounds.Contains(MousePosition))
            {
                _autoClose.Stop();
                _autoClose.Start();
                return;
            }

            if (ContainsFocus) return;
            IntPtr foreground = GetForegroundWindow();
            if (foreground == Handle || foreground == _openedOver || foreground == IntPtr.Zero) return;
            Dismiss();
        }

        private void PlaceAtCursor()
        {
            Point cursor = MousePosition;
            Rectangle area = Screen.FromPoint(cursor).WorkingArea;
            // A remembered size from a bigger monitor must not hang off this one.
            int fitWidth = Math.Max(MinimumSize.Width, Math.Min(Width, area.Width - 24));
            int fitHeight = Math.Max(MinimumSize.Height, Math.Min(Height, area.Height - 24));
            if (fitWidth != Width || fitHeight != Height) Size = new Size(fitWidth, fitHeight);

            int x = cursor.X + 12;
            int y = cursor.Y + 12;
            if (x + Width > area.Right) x = cursor.X - Width - 12;
            if (y + Height > area.Bottom) y = cursor.Y - Height - 12;
            x = Math.Max(area.Left, Math.Min(x, area.Right - Width));
            y = Math.Max(area.Top, Math.Min(y, area.Bottom - Height));

            if (!Visible)
            {
                Location = new Point(x, y);
                Show(); // ShowWithoutActivation - the source window keeps the focus
            }
            else
            {
                SetWindowPos(Handle, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
            }
            _placed = true;
        }

        private void SaveBounds()
        {
            // Not before the window has been placed once: a layout pass during construction would
            // otherwise persist the default size as if the user had chosen it.
            if (!_placed || WindowState != FormWindowState.Normal) return;
            _config.TranslateWindowWidth = Width;
            _config.TranslateWindowHeight = Height;
            _config.Save();
        }

        private void ApplyTexts()
        {
            Text = T("Перевод");
            _copy.Text = T("Копировать");
            _paste.Text = T("Вставить");
            _retry.Text = T("Ещё раз");
            _start.Text = T("Запустить Ollama");
            _settings.Text = T("Настройки");
            ReloadLanguages();
        }

        private void ReloadLanguages()
        {
            _loading = true;
            _language.Items.Clear();
            _language.Items.Add(new LanguageItem(TranslationLanguages.UiToken, _uiLanguage));
            _language.Items.Add(new LanguageItem(TranslationLanguages.ActiveToken, _uiLanguage));
            foreach (string code in TranslationLanguages.Codes)
                _language.Items.Add(new LanguageItem(code, _uiLanguage));
            _loading = false;
            MeasureLanguageWidth();
        }

        /// <summary>Wide enough for the longest label in the current language, never a fixed number.</summary>
        private void MeasureLanguageWidth()
        {
            int width = 0;
            foreach (object item in _language.Items)
                width = Math.Max(width, TextRenderer.MeasureText(item.ToString() ?? "", Font).Width);
            _language.Width = width + SystemInformation.VerticalScrollBarWidth + 16;
        }

        private void SelectLanguage(string code)
        {
            _loading = true;
            for (int i = 0; i < _language.Items.Count; i++)
                if (_language.Items[i] is LanguageItem item
                    && string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase))
                {
                    _language.SelectedIndex = i;
                    break;
                }
            _loading = false;
        }

        private void ApplyScript()
        {
            bool rtl = Localization.IsRightToLeft(_uiLanguage);
            RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
            RightToLeftLayout = rtl;
            // Not "null means leave it alone": switching from Hindi back to English has to switch the
            // family back too, so the wanted family is always resolved, exactly as SettingsForm does.
            string wanted = Localization.FontFamily(_uiLanguage) ?? SystemFonts.MessageBoxFont.FontFamily.Name;
            if (Font.FontFamily.Name == wanted) return;
            try
            {
                // A font handed to a control is never disposed by WinForms, and this runs again on
                // every language change - so the one we replace has to be released by hand.
                Font? replaced = _ownFont;
                Font = _ownFont = new Font(wanted, SystemFonts.MessageBoxFont.SizeInPoints);
                replaced?.Dispose();
            }
            catch { /* the font is missing on this machine - keep the default */ }
        }

        private Button Command(string russian, Action action)
        {
            var button = new Button
            {
                Text = T(russian),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(3, 3, 6, 3),
                // A popup that never activates would otherwise hand the first click to focus.
                TabStop = false,
            };
            button.Click += (_, _) => action();
            return button;
        }

        private string T(string ru) => Localization.Translate(_uiLanguage, ru);

        /// <summary>Combo entry: shows the language label, carries the code the profile stores.</summary>
        private sealed class LanguageItem
        {
            public readonly string Code;
            private readonly string _label;

            public LanguageItem(string code, string uiLanguage)
            {
                Code = code;
                _label = TranslationLanguages.Label(code, uiLanguage);
            }

            public override string ToString() => _label;
        }
    }
}

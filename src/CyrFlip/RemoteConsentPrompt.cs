using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// One request of another device waiting for the person's answer (REMOTE-CONTROL rule 10, ticket CYRFLP-0142). Lives on
    /// the UI thread: the prompt and section T2 of the External control window show it, and whichever answers first calls
    /// <see cref="Answer"/>, which settles it once.
    /// </summary>
    internal sealed class RemoteConsentPending
    {
        public int Id { get; set; }
        /// <summary>The controller's <c>deviceName</c> from this PC's own list.</summary>
        public string DeviceName { get; set; } = "";
        public string Verb { get; set; } = "";
        /// <summary>The arguments in the product's words: the option names and values, one line.</summary>
        public string Arguments { get; set; } = "";
        public string Risk { get; set; } = "safe";
        public DateTime DeadlineUtc { get; set; }
        /// <summary>Settles the request: true = allowed, false = declined. A second call does nothing.</summary>
        public Action<bool>? Answer { get; set; }
    }

    /// <summary>
    /// The confirmation on the target (`WINDOW.md` section 5, Windows row): a small topmost window of its own, non-modal,
    /// shown without taking the focus from the program the person is typing in. Title <c>{deviceName} asks CyrFlip</c>, the
    /// verb's summary and its arguments, <c>Kind: {class}</c>, a countdown from 60; Allow and Decline, Decline holding the
    /// focus and answering Enter and Escape. The close box and the end of the countdown decline.
    /// </summary>
    internal sealed class RemoteConsentPrompt : ThemedForm
    {
        private readonly string _language;
        private readonly RemoteConsentPending _pending;
        private readonly Label _countdown = new Label { AutoSize = true, UseMnemonic = false, Margin = new Padding(3, 4, 3, 8) };
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        private readonly Button _decline;
        private Font? _ownFont;
        private Font? _boldFont;
        private bool _answered;
        private bool _closing;

        public RemoteConsentPrompt(string uiLanguage, RemoteConsentPending pending, string title, string body)
        {
            _language = uiLanguage;
            _pending = pending ?? throw new ArgumentNullException(nameof(pending));
            HelpLanguage = uiLanguage;

            Text = T("External control");
            Name = "remote-consent-prompt";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            WindowScript.Apply(this, uiLanguage, ref _ownFont);
            _boldFont = new Font(Font, FontStyle.Bold);

            int width = TextRenderer.MeasureText(new string('W', 34), Font).Width;
            var grid = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Padding = new Padding(12) };
            grid.Controls.Add(new Label { Text = title, AutoSize = true, UseMnemonic = false, Font = _boldFont, MaximumSize = new Size(width, 0), Margin = new Padding(3, 2, 3, 6) });
            grid.Controls.Add(new Label { Text = body, AutoSize = true, UseMnemonic = false, MaximumSize = new Size(width, 0), Margin = new Padding(3, 2, 3, 4) });
            grid.Controls.Add(new Label
            {
                Text = Localization.Format(T, "Kind: {0}", T(RemoteControlWords.ClassCaption(pending.Risk))), AutoSize = true, UseMnemonic = false,
                MaximumSize = new Size(width, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 2, 3, 4),
            });
            _countdown.MaximumSize = new Size(width, 0);
            _countdown.ForeColor = ThemePalette.Light.TextMuted;
            grid.Controls.Add(_countdown);

            var allow = new Button { Text = T("Allow"), AutoSize = true, Name = "remote-consent.allow", Margin = new Padding(4, 3, 4, 3) };
            _decline = new Button { Text = T("Decline"), AutoSize = true, Name = "remote-consent.decline", Margin = new Padding(4, 3, 4, 3) };
            allow.Click += (_, _) => Settle(true);
            _decline.Click += (_, _) => Settle(false);
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 0) };
            buttons.Controls.Add(_decline);
            buttons.Controls.Add(allow);
            grid.Controls.Add(buttons);
            Controls.Add(grid);

            // Decline is the answer to Enter and to Escape (section 5): the box comes up over whatever the person types into.
            AcceptButton = _decline;
            CancelButton = _decline;
            ActiveControl = _decline;
            UpdateCountdown();
            _timer.Tick += (_, _) => UpdateCountdown();
            FormClosing += (_, _) => { _closing = true; if (!_answered) Settle(false); };
            Load += (_, _) => PlaceNearCorner();
            Shown += (_, _) => _timer.Start();
        }

        /// <summary>Raised once, with the answer; the window closes itself.</summary>
        public event Action<bool>? Answered;

        protected override bool ShowWithoutActivation => true;

        /// <summary>Closes the prompt without raising <see cref="Answered"/> (the request was answered in T2).</summary>
        public void Dismiss()
        {
            _answered = true;
            _timer.Stop();
            if (!_closing && !IsDisposed) Close();
        }

        private void Settle(bool allowed)
        {
            if (_answered) return;
            _answered = true;
            _timer.Stop();
            Answered?.Invoke(allowed);
            if (!_closing && !IsDisposed) Close();
        }

        private void UpdateCountdown()
        {
            int left = Math.Max(0, (int)Math.Ceiling((_pending.DeadlineUtc - DateTime.UtcNow).TotalSeconds));
            _countdown.Text = Localization.Format(T, "Answer within {0} s. Without an answer the command is not run.", left.ToString(CultureInfo.InvariantCulture));
            if (left == 0) Settle(false);
        }

        private void PlaceNearCorner()
        {
            Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromPoint(Point.Empty).WorkingArea;
            int margin = (int)Math.Round(16 * DeviceDpi / 96.0);
            bool rtl = RightToLeft == RightToLeft.Yes;
            Location = new Point(rtl ? area.Left + margin : area.Right - Width - margin, area.Bottom - Height - margin);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _boldFont?.Dispose();
                _ownFont?.Dispose();
            }
            base.Dispose(disposing);
        }

        private string T(string en) => Localization.Translate(_language, en);
    }
}

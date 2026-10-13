using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The details panel of one target (ticket S0069 section 6): "Recognized as" with the kind chosen first,
    /// <b>only the fields that kind uses</b>, live validation, the options that are opt-in (a balloon, the
    /// journal) and a collapsed "Advanced" group - and a <b>Check now</b> button, so a target is green before
    /// the dialog closes. Laid out by auto-sizing <see cref="TableLayoutPanel"/>s like every dialog here -
    /// captions exist in 13 languages and fixed geometry clips them.
    /// </summary>
    internal sealed class TrafficLightTargetDialog : ThemedForm
    {
        private readonly string _uiLanguage;
        private readonly TrafficLightTarget _original;
        private readonly Func<TrafficLightTarget, CancellationToken, Task<ProbeOutcome>> _check;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private Font? _ownFont;

        private readonly ComboBox _kind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox _address = new TextBox { Width = 320 };
        private readonly NumericUpDown _port = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 22, Width = 80 };
        private readonly TextBox _fingerprint = new TextBox { Width = 320 };
        private readonly TextBox _label = new TextBox { Width = 320, MaxLength = TrafficLightTargetList.MaxLabelLength };
        private readonly ComboBox _group = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 220, MaxLength = TrafficLightTargetList.MaxGroupLength };
        private readonly CheckBox _enabled = new CheckBox { AutoSize = true };
        private readonly CheckBox _notify = new CheckBox { AutoSize = true };
        private readonly CheckBox _record = new CheckBox { AutoSize = true };
        private readonly Button _advanced = new Button { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        private readonly NumericUpDown _interval = new NumericUpDown { Minimum = TrafficLightTarget.MinIntervalSeconds, Maximum = 7 * 24 * 3600, Value = 60, Width = 90 };
        private readonly CheckBox _manual = new CheckBox { AutoSize = true };
        private readonly NumericUpDown _timeout = new NumericUpDown { Minimum = 100, Maximum = TrafficLightTarget.MaxTimeoutMs, Increment = 100, Width = 90 };
        private readonly NumericUpDown _yellow = new NumericUpDown { Minimum = 10, Maximum = 600000, Increment = 100, Width = 90 };
        private readonly NumericUpDown _failures = new NumericUpDown { Minimum = 1, Maximum = TrafficLightTarget.MaxFailuresBeforeDown, Width = 70 };
        private readonly TextBox _codes = new TextBox { Width = 200 };
        private readonly CheckBox _untrusted = new CheckBox { AutoSize = true };
        private readonly Label _hint = new Label { AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(3, 4, 3, 4) };
        private readonly Label _problem = new Label { AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(3, 4, 3, 4) };
        private readonly Label _intervalHint = new Label { AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(3, 0, 3, 4) };
        private readonly Button _checkNow = new Button { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        private readonly Label _checkResult = new Label { AutoSize = true, MaximumSize = new Size(420, 0), Margin = new Padding(8, 8, 3, 3) };
        private readonly Button _ok;
        private readonly TableLayoutPanel _grid = new TableLayoutPanel();
        private readonly TableLayoutPanel _advancedGrid = new TableLayoutPanel();
        private readonly List<Control[]> _rows = new List<Control[]>();
        private bool _loading;
        private bool _checking;

        public TrafficLightTargetDialog(TrafficLightTarget target, IEnumerable<string> groups, string uiLanguage,
            Func<TrafficLightTarget, CancellationToken, Task<ProbeOutcome>> check, bool isNew)
        {
            _original = target ?? throw new ArgumentNullException(nameof(target));
            _uiLanguage = uiLanguage;
            _check = check;

            Text = T(isNew ? "New target" : "Edit target");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ApplyScript(uiLanguage);

            foreach (string kind in TrafficLightKinds.All)
                _kind.Items.Add(new KindItem(kind, TrafficLightText.KindWord(kind, T)));
            _kind.Width = ComboWidth(_kind);
            foreach (string group in groups) if (group.Length > 0 && !_group.Items.Contains(group)) _group.Items.Add(group);

            _enabled.Text = T("Target is on");
            _notify.Text = T("Tell me in the tray when the target becomes unavailable and when it comes back");
            _record.Text = T("Include in change history");
            _manual.Text = T("Manual only");
            _untrusted.Text = T("Accept a certificate that Windows does not trust");
            _advanced.Click += (_, _) => ToggleAdvanced();
            _checkNow.Text = T("Check now");
            ButtonGlyphs.Attach(_checkNow, AppGlyphs.Verify);
            _checkNow.Click += async (_, _) => await RunCheck();

            _problem.ForeColor = ThemePalette.Light.Danger;
            _hint.ForeColor = ThemePalette.Light.TextMuted;
            _intervalHint.ForeColor = ThemePalette.Light.TextMuted;

            _ok = ButtonGlyphs.Answer(new Button
            {
                Text = "OK", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(TextWidth("Cancel"), 0), DialogResult = DialogResult.OK,
            });
            _ok.Click += (_, _) => Accept();
            Button cancel = ButtonGlyphs.Answer(new Button
            {
                Text = T("Cancel"), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(TextWidth("Cancel"), 0), DialogResult = DialogResult.Cancel,
            });

            BuildLayout(cancel);
            LoadTarget(target);

            _kind.SelectedIndexChanged += (_, _) => { if (!_loading) OnKindChanged(); };
            foreach (Control c in new Control[] { _address, _fingerprint, _label, _group, _codes })
                c.TextChanged += (_, _) => Revalidate();
            foreach (NumericUpDown n in new[] { _port, _interval, _timeout, _yellow, _failures })
                n.ValueChanged += (_, _) => Revalidate();
            _manual.CheckedChanged += (_, _) => { _interval.Enabled = !_manual.Checked; Revalidate(); };
            Revalidate();

            AcceptButton = _ok;
            CancelButton = cancel;
        }

        /// <summary>The edited target, or null when the dialog was cancelled.</summary>
        public TrafficLightTarget? Result { get; private set; }

        // ---- layout ----

        private void BuildLayout(Button cancel)
        {
            _grid.ColumnCount = 3;
            _grid.AutoSize = true;
            _grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _grid.Dock = DockStyle.Fill;
            _grid.Padding = new Padding(12);

            int row = 0;
            void Add(string caption, Control editor, params Control[] extra)
            {
                Label cap = Caption(T(caption));
                _grid.Controls.Add(cap, 0, row);
                _grid.Controls.Add(editor, 1, row);
                _grid.SetColumnSpan(editor, extra.Length == 0 ? 2 : 1);
                foreach (Control e in extra) _grid.Controls.Add(e, 2, row);
                var all = new List<Control> { cap, editor };
                all.AddRange(extra);
                _rows.Add(all.ToArray());
                row++;
            }

            Add("Type: (target kind)", _kind);
            _grid.Controls.Add(_hint, 1, row); _grid.SetColumnSpan(_hint, 2); row++;
            Add("Address:", _address, _port);
            Add("Certificate fingerprint:", _fingerprint);
            Add("Label:", _label);
            Add("Group:", _group);
            _grid.Controls.Add(_enabled, 1, row); _grid.SetColumnSpan(_enabled, 2); row++;
            _grid.Controls.Add(_notify, 1, row); _grid.SetColumnSpan(_notify, 2); row++;
            _grid.Controls.Add(_record, 1, row); _grid.SetColumnSpan(_record, 2); row++;
            _grid.Controls.Add(_advanced, 0, row); _grid.SetColumnSpan(_advanced, 3); row++;

            // The advanced group lives in its own table, taken out of the tree while collapsed so the
            // dialog sizes to what is shown.
            _advancedGrid.ColumnCount = 2;
            _advancedGrid.AutoSize = true;
            _advancedGrid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _advancedGrid.Dock = DockStyle.Fill;
            _advancedGrid.Margin = new Padding(0, 0, 0, 0);
            int adv = 0;
            void AddAdvanced(string caption, Control editor)
            {
                _advancedGrid.Controls.Add(Caption(T(caption)), 0, adv);
                _advancedGrid.Controls.Add(editor, 1, adv);
                adv++;
            }
            var intervalRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
            intervalRow.Controls.Add(_interval);
            intervalRow.Controls.Add(_manual);
            AddAdvanced("Check every (s):", intervalRow);
            _advancedGrid.Controls.Add(_intervalHint, 1, adv); adv++;
            AddAdvanced("Time limit for an answer (ms):", _timeout);
            AddAdvanced("Treat as slow after (ms):", _yellow);
            AddAdvanced("Confirm unavailability after this many checks:", _failures);
            AddAdvanced("Accepted HTTP status codes:", _codes);
            _advancedGrid.Controls.Add(_untrusted, 1, adv); adv++;
            _grid.Controls.Add(_advancedGrid, 0, row);
            _grid.SetColumnSpan(_advancedGrid, 3);
            _advancedGrid.Visible = false;
            row++;

            _grid.Controls.Add(_problem, 0, row); _grid.SetColumnSpan(_problem, 3); row++;

            var checkRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
            checkRow.Controls.Add(_checkNow);
            checkRow.Controls.Add(_checkResult);
            _grid.Controls.Add(checkRow, 0, row); _grid.SetColumnSpan(checkRow, 3); row++;

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0),
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(_ok);
            _grid.Controls.Add(buttons, 0, row); _grid.SetColumnSpan(buttons, 3);
            Controls.Add(_grid);
        }

        private void ToggleAdvanced()
        {
            _advancedGrid.Visible = !_advancedGrid.Visible;
            UpdateAdvancedCaption();
        }

        private void UpdateAdvancedCaption()
            => _advanced.Text = (_advancedGrid.Visible ? "\u25BE " : "\u25B8 ") + T("Advanced");

        // ---- state in and out ----

        private void LoadTarget(TrafficLightTarget target)
        {
            _loading = true;
            try
            {
                SelectKind(TrafficLightKinds.IsKnown(target.Kind) ? target.Kind : "");
                string kind = target.Kind;
                if (TrafficLightKinds.HasPort(kind) && TrafficLightAddress.TryParseHostPort(target.Address, out string host, out int port))
                {
                    _address.Text = host;
                    _port.Value = Math.Max(_port.Minimum, Math.Min(_port.Maximum, port));
                }
                else
                {
                    _address.Text = target.Address;
                    _port.Value = DefaultPort(kind);
                }
                _fingerprint.Text = target.Fingerprint;
                _label.Text = target.Label;
                _group.Text = target.Group;
                _enabled.Checked = target.Enabled;
                _notify.Checked = target.Notify;
                _record.Checked = target.RecordHistory;
                _manual.Checked = target.ManualOnly;
                _interval.Value = Math.Max(_interval.Minimum, Math.Min(_interval.Maximum,
                    target.IntervalSeconds > 0 ? target.IntervalSeconds : TrafficLightTarget.DefaultIntervalSeconds));
                _interval.Enabled = !_manual.Checked;
                _timeout.Value = Math.Max(_timeout.Minimum, Math.Min(_timeout.Maximum, target.EffectiveTimeoutMs));
                _yellow.Value = Math.Max(_yellow.Minimum, Math.Min(_yellow.Maximum, target.EffectiveYellowMs));
                _failures.Value = Math.Max(_failures.Minimum, Math.Min(_failures.Maximum, target.FailuresBeforeDown));
                _codes.Text = string.IsNullOrWhiteSpace(target.AcceptedStatusCodes) ? TrafficLightTarget.DefaultAcceptedStatusCodes : target.AcceptedStatusCodes;
                _untrusted.Checked = target.AllowUntrustedCertificate;
            }
            finally { _loading = false; }
            UpdateAdvancedCaption();
            ShowFieldsOfKind();
        }

        private static int DefaultPort(string kind)
            => kind == TrafficLightKinds.Exchange ? 22022 : kind == TrafficLightKinds.Tcp ? 445 : 22;

        private string CurrentKind => _kind.SelectedItem is KindItem item ? item.Kind : "";

        private void SelectKind(string kind)
        {
            for (int i = 0; i < _kind.Items.Count; i++)
                if (_kind.Items[i] is KindItem item && item.Kind == kind) { _kind.SelectedIndex = i; return; }
            _kind.SelectedIndex = -1; // an unrecognized text: the chooser starts empty (D24)
        }

        private void OnKindChanged()
        {
            string kind = CurrentKind;
            if (TrafficLightKinds.HasPort(kind) && _port.Value == DefaultPort(TrafficLightKinds.Sftp)
                || _port.Value == DefaultPort(TrafficLightKinds.Exchange) || _port.Value == DefaultPort(TrafficLightKinds.Tcp))
                _port.Value = DefaultPort(kind);
            // The per-kind defaults follow the kind until the user sets their own.
            _timeout.Value = Math.Max(_timeout.Minimum, Math.Min(_timeout.Maximum, TrafficLightKinds.DefaultTimeoutMs(kind)));
            _yellow.Value = Math.Max(_yellow.Minimum, Math.Min(_yellow.Maximum, TrafficLightKinds.DefaultYellowMs(kind)));
            ShowFieldsOfKind();
            Revalidate();
        }

        /// <summary>Only the fields the chosen kind uses are shown (section 6).</summary>
        private void ShowFieldsOfKind()
        {
            string kind = CurrentKind;
            SetRow(_rows[2], kind == TrafficLightKinds.Exchange);   // the fingerprint of the exchange server's leaf
            _port.Visible = TrafficLightKinds.HasPort(kind);        // the address row itself is always there
            SetAdvanced(_codes, kind == TrafficLightKinds.Http);
            _untrusted.Visible = kind == TrafficLightKinds.Http;
            _hint.Text = HintOf(kind);
            _hint.Visible = _hint.Text.Length > 0;
            UpdateIntervalHint();
        }

        private static void SetRow(Control[] row, bool visible)
        {
            foreach (Control c in row) c.Visible = visible;
        }

        private void SetAdvanced(Control editor, bool visible)
        {
            editor.Visible = visible;
            if (_advancedGrid.GetPositionFromControl(editor).Row is int r && r >= 0)
            {
                Control? caption = _advancedGrid.GetControlFromPosition(0, r);
                if (caption != null) caption.Visible = visible;
            }
        }

        private string HintOf(string kind)
        {
            switch (kind)
            {
                case TrafficLightKinds.Ping: return T("One ICMP echo. Windows computers often ignore it; for them use \"TCP port\" 445 or 3389.");
                case TrafficLightKinds.Tcp: return T("One connection to the port, closed at once: RDP, SMB, SSH, a database.");
                case TrafficLightKinds.Http: return T("A HEAD request (GET of headers on 4xx/5xx), up to five redirects, system proxy, no cookies or login.");
                case TrafficLightKinds.Folder: return T("The folder must open; at most one entry is read, nothing is written. If a password is needed: \"access denied\".");
                case TrafficLightKinds.Drive: return T("A drive is found by its letter: if it comes back under another letter, it is shown as \"not connected\".");
                case TrafficLightKinds.Sftp: return T("Checks that an SSH server answers on the port. The resource and the server's key are not checked.");
                case TrafficLightKinds.Exchange: return T("The exchange server is its own type, not SFTP: a TLS handshake and a fingerprint check, no data.");
                default: return T("Choose the type of target.");
            }
        }

        private void UpdateIntervalHint()
        {
            string kind = CurrentKind;
            bool remote = kind == TrafficLightKinds.Http || kind == TrafficLightKinds.Sftp || kind == TrafficLightKinds.Exchange;
            bool frequent = !_manual.Checked && _interval.Value < 30;
            _intervalHint.Text = remote && frequent
                ? T("It is better not to query someone else's site or server this often: it may treat it as load.")
                : "";
            _intervalHint.Visible = _intervalHint.Text.Length > 0;
        }

        // ---- validation ----

        private TrafficLightTarget Compose()
        {
            string kind = CurrentKind;
            string address = _address.Text.Trim();
            if (TrafficLightKinds.HasPort(kind) && address.Length > 0)
                address = TrafficLightAddress.JoinHostPort(address, (int)_port.Value);
            else if (kind == TrafficLightKinds.Drive)
                address = TrafficLightAddress.NormalizeDrive(address);

            int timeout = (int)_timeout.Value;
            int yellow = (int)_yellow.Value;
            return new TrafficLightTarget
            {
                Id = _original.Id,
                Kind = kind,
                Address = address,
                Fingerprint = kind == TrafficLightKinds.Exchange ? _fingerprint.Text.Trim() : "",
                Label = _label.Text.Trim(),
                Group = _group.Text.Trim(),
                Ordinal = _original.Ordinal,
                Enabled = _enabled.Checked,
                IntervalSeconds = _manual.Checked ? 0 : (int)_interval.Value,
                // The kind's own value is stored as "default" so it keeps following the kind (section 4).
                TimeoutMs = timeout == TrafficLightKinds.DefaultTimeoutMs(kind) ? 0 : timeout,
                YellowThresholdMs = yellow == TrafficLightKinds.DefaultYellowMs(kind) ? 0 : yellow,
                FailuresBeforeDown = (int)_failures.Value,
                AcceptedStatusCodes = kind == TrafficLightKinds.Http ? _codes.Text.Trim() : TrafficLightTarget.DefaultAcceptedStatusCodes,
                AllowUntrustedCertificate = kind == TrafficLightKinds.Http && _untrusted.Checked,
                Notify = _notify.Checked,
                RecordHistory = _record.Checked,
            };
        }

        private AddressIssue Issue(TrafficLightTarget target)
        {
            AddressIssue issue = TrafficLightAddress.Validate(target);
            // "host" without the port is the Host problem here: the port has its own field.
            return issue;
        }

        private string ProblemText(AddressIssue issue)
        {
            switch (issue)
            {
                case AddressIssue.None: return "";
                case AddressIssue.Empty: return T("Enter an address.");
                case AddressIssue.UnknownKind: return T("Choose the type of target.");
                case AddressIssue.BadHost: return T("The host address is not valid: use an IP address or a name without spaces.");
                case AddressIssue.BadPort: return T("The port is a number from 1 to 65535.");
                case AddressIssue.BadUrl: return T("A website address such as https://example.org is needed, without a login or password.");
                case AddressIssue.BadPath: return T("A path such as C:\\Folder or \\\\server\\share is needed, without the characters * ? \" < > |.");
                case AddressIssue.BadDrive: return T("A single drive letter and a colon are needed, for example E:");
                case AddressIssue.BadFingerprint: return T("A fingerprint is written as SHA256: and 43 base64 characters, and only for the exchange server.");
                case AddressIssue.BadStatusCodes: return T("Status codes are numbers from 100 to 599, as a list or a range: 200-399,401");
                default: return "";
            }
        }

        private void Revalidate()
        {
            if (_loading) return;
            UpdateIntervalHint();
            TrafficLightTarget candidate = Compose();
            AddressIssue issue = Issue(candidate);
            _problem.Text = ProblemText(issue);
            _problem.Visible = _problem.Text.Length > 0;
            _ok.Enabled = issue == AddressIssue.None;
            _checkNow.Enabled = issue == AddressIssue.None && !_checking;
        }

        private void Accept()
        {
            TrafficLightTarget candidate = Compose();
            if (Issue(candidate) != AddressIssue.None)
            {
                DialogResult = DialogResult.None;
                return;
            }
            if (candidate.Label.Length == 0) candidate.Label = TargetRecognizer.SuggestLabel(candidate.Kind, candidate.Address);
            _lifetime.Cancel();
            Result = candidate;
        }

        // ---- "Check now" ----

        private async Task RunCheck()
        {
            TrafficLightTarget candidate = Compose();
            if (Issue(candidate) != AddressIssue.None || _checking) return;
            _checking = true;
            _checkNow.Enabled = false;
            _checkResult.Text = T("Checking..");
            try
            {
                ProbeOutcome outcome = await _check(candidate, _lifetime.Token);
                if (IsDisposed) return;
                TrafficLightVerdict.Judge(candidate, outcome, out TrafficState state, out string reason);
                string word = state == TrafficState.Ok ? T("Available")
                    : state == TrafficState.Degraded ? T("Degraded") : T("Unavailable");
                string detail = state == TrafficState.Ok && outcome.Reached
                    ? Localization.Format(T, "{0} ms", outcome.LatencyMs)
                    : TrafficLightText.ReasonWord(reason, T);
                _checkResult.Text = detail.Length > 0 ? word + " - " + detail : word;
            }
            catch (OperationCanceledException) { if (!IsDisposed) _checkResult.Text = ""; }
            catch (Exception)
            {
                if (!IsDisposed) _checkResult.Text = TrafficLightText.ReasonWord(TrafficReasons.Failed, T);
            }
            finally
            {
                _checking = false;
                if (!IsDisposed) Revalidate();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _lifetime.Cancel(); } catch (Exception ex) { TrafficLightLog.Fault("dialog-cancel", ex); }
                _lifetime.Dispose();
                _ownFont?.Dispose();
            }
            base.Dispose(disposing);
        }

        // ---- helpers ----

        private void ApplyScript(string uiLanguage)
        {
            if (Localization.IsRightToLeft(uiLanguage)) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }
            string? family = Localization.FontFamily(uiLanguage);
            if (family == null) return;
            try { Font = _ownFont = new Font(family, Font.SizeInPoints); }
            catch (Exception ex) { TrafficLightLog.Fault("dialog-font", ex); }
        }

        private static Label Caption(string text)
            => new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 10, 3) };

        private int TextWidth(string text) => TextRenderer.MeasureText(text, Font).Width + 16;

        private int ComboWidth(ComboBox box)
        {
            int width = TextWidth(new string('W', 14));
            foreach (object item in box.Items) width = Math.Max(width, TextWidth(item.ToString() ?? ""));
            return width;
        }

        private string T(string en) => Localization.Translate(_uiLanguage, en);

        private sealed class KindItem
        {
            public readonly string Kind;
            private readonly string _label;
            public KindItem(string kind, string label) { Kind = kind; _label = label; }
            public override string ToString() => _label;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The External control window (REMOTE-CONTROL rule 18, `WINDOW.md`, ticket CYRFLP-0142): the one place where the
    /// person sets up and reads remote control on this PC. Top-level, non-modal, resizable, one instance (the context
    /// keeps it), 720 by 600 logical pixels by default and 560 by 480 at the least, its bounds remembered. The sections are
    /// the target's T1 to T5, in that order and with the contract's words; a change takes effect at once (no Save, no
    /// Refresh: the context calls <see cref="RefreshFromSource"/> whenever the directory, the stream, the log or a pending
    /// confirmation changes). The window does no I/O of its own beyond opening the log file: it reads a
    /// <see cref="RemoteCommandsView"/> and hands every change to the context through its delegates.
    /// </summary>
    internal sealed class ExternalControlWindow : ThemedForm
    {
        internal const int DefaultWidth = 720, DefaultHeight = 600, MinWidth = 560, MinHeight = 480;

        private readonly AppConfig _config;
        private readonly string _language;
        private readonly Func<RemoteCommandsView> _source;
        private Font? _ownFont;
        private Font? _titleFont;
        private Font? _boldFont;

        private readonly Label _title = new Label { AutoSize = true, UseMnemonic = false, Margin = new Padding(12, 10, 3, 6) };
        private readonly Panel _scroll = new Panel { AutoScroll = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
        private readonly FlowLayoutPanel _sections = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(6, 0, 6, 6),
        };
        private readonly CollapsibleGroup _t1, _t2, _t3, _t4, _t5;
        private readonly CheckBox _listen = new BooleanOptionCheckBox { AutoSize = true, Margin = new Padding(3, 6, 3, 2) };
        private readonly Label _state = MutedLabel();
        private readonly Button _stateAction = new Button { AutoSize = true, Margin = new Padding(3, 3, 3, 6), Visible = false };
        private readonly Label _log = new Label { AutoSize = true, UseMnemonic = false, Margin = new Padding(3, 4, 3, 4) };
        private readonly Button _close = new Button { AutoSize = true, Margin = new Padding(6) };
        private readonly System.Windows.Forms.Timer _countdown = new System.Windows.Forms.Timer { Interval = 1000 };
        private readonly List<KeyValuePair<Label, RemoteConsentPending>> _countdownLabels = new List<KeyValuePair<Label, RemoteConsentPending>>();
        /// <summary>The rows of T3 whose last change could not be saved: "entry|device" keys.</summary>
        private readonly HashSet<string> _saveFailed = new HashSet<string>(StringComparer.Ordinal);
        private bool _refreshing;
        // A handler of a control in a section is running: a refresh then waits one turn, since it replaces that control.
        private int _handling;
        private bool _placed;
        private string _t1Shown = "", _t2Shown = "", _t3Shown = "", _t5Shown = "";
        private Action? _stateActionHandler;

        public ExternalControlWindow(AppConfig config, string uiLanguage, Func<RemoteCommandsView> source)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _language = uiLanguage;
            _source = source ?? throw new ArgumentNullException(nameof(source));
            HelpLanguage = uiLanguage;

            Text = T("External control");
            Name = "external-control";
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;
            WindowScript.Apply(this, uiLanguage, ref _ownFont);
            _titleFont = new Font(Font.FontFamily, Font.SizeInPoints * 1.4f, FontStyle.Bold);
            _boldFont = new Font(Font, FontStyle.Bold);

            _t1 = new CollapsibleGroup("external-control/t1", T("This device"));
            _t2 = new CollapsibleGroup("external-control/t2", T("Waiting for your answer"));
            _t3 = new CollapsibleGroup("external-control/t3", T("Who can control this device"));
            _t4 = new CollapsibleGroup("external-control/t4", T("Commands this program accepts"));
            _t5 = new CollapsibleGroup("external-control/t5", T("Recent requests"));
            _t4.Expanded = false; // WINDOW.md section 4 T4: collapsed when the window opens

            _title.Text = T("External control");
            _title.Font = _titleFont;
            _listen.Text = T("Listen to commands from my other devices");
            _listen.Name = "external-control.listen";
            _listen.Font = _boldFont;
            _listen.CheckedChanged += (_, _) =>
            {
                if (_refreshing) return;
                _handling++;
                try { ListenChanged?.Invoke(_listen.Checked); }
                finally { _handling--; }
            };
            _stateAction.Click += (_, _) => _stateActionHandler?.Invoke();
            _close.Text = T("Close");
            _close.Name = "external-control.close";
            _close.Click += (_, _) => Close();
            CancelButton = _close;
            _countdown.Tick += (_, _) => TickCountdown();

            BuildLayout();
            Load += (_, _) => Place();
            ResizeEnd += (_, _) => SaveBounds();
            Resize += (_, _) => FitWidths();
            FormClosing += (_, _) => SaveBounds();
            RefreshFromSource();
        }

        /// <summary>The person turned the switch of rule 8 on or off in T1.</summary>
        public event Action<bool>? ListenChanged;

        /// <summary>A device was ticked or unticked (entry id, device id, name, allowed); false when the change was not saved.</summary>
        public Func<string, string, string, bool, bool>? SetDeviceAllowed { get; set; }

        /// <summary>A class choice of a ticked device (entry id, device id, name, class, consent or null for No access); false when not saved.</summary>
        public Func<string, string, string, string, string?, bool>? SetClassConsent { get; set; }

        /// <summary>T1's <c>open-exchange</c> and <c>sign-in</c>: the Exchange server page of the settings.</summary>
        public Action? OpenExchangeSettings { get; set; }

        /// <summary>Scrolls to T3, the list rule 9 says turning the switch on opens (`WINDOW.md` section 2 item 5).</summary>
        public void ShowDevices()
        {
            _t3.Expanded = true;
            _scroll.ScrollControlIntoView(_t3);
        }

        // ---- building ----

        private void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0), Padding = new Padding(0) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(_title, 0, 0);
            _scroll.Controls.Add(_sections);
            root.Controls.Add(_scroll, 0, 1);
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill, Margin = new Padding(0),
            };
            buttons.Controls.Add(_close);
            root.Controls.Add(buttons, 0, 2);
            Controls.Add(root);

            _t1.Body.Controls.Add(_listen);
            _t1.Body.Controls.Add(_state);
            _t1.Body.Controls.Add(_stateAction);
            var openLog = new Button { Text = T("Open the full log"), AutoSize = true, Name = "external-control.open-log", Margin = new Padding(3, 4, 3, 6) };
            openLog.Click += (_, _) => OpenLog();
            _t5.Body.Controls.Add(_log);
            _t5.Body.Controls.Add(openLog);
            foreach (CollapsibleGroup g in new[] { _t1, _t2, _t3, _t4, _t5 }) _sections.Controls.Add(g);
            BuildT4();
        }

        /// <summary>T4: one read-only line per verb this program answers remotely (rule 7).</summary>
        private void BuildT4()
        {
            foreach (AppCommandSpec spec in RemoteControlWords.RemoteVerbs(AppCommandManifest.Shipped))
            {
                string line = Summary(spec.Name) + " - " + T(RemoteControlWords.ClassCaption(spec.Risk));
                if (spec.RemoteConfirm == RemoteControl.ConfirmDevice) line += " - " + T("Asks on this device every time");
                _t4.Body.Controls.Add(new Label { Text = line, AutoSize = true, UseMnemonic = false, Margin = new Padding(3, 2, 3, 2) });
            }
        }

        // ---- refreshing ----

        /// <summary>Reads the context's view and redraws the sections whose content changed. UI thread.</summary>
        public void RefreshFromSource()
        {
            if (IsDisposed) return;
            if (_handling > 0 && IsHandleCreated) { BeginInvoke(new Action(RefreshFromSource)); return; }
            RemoteCommandsView view;
            try { view = _source(); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                RemoteCommandsLog.Fault("window-view", ex);
                return;
            }
            _refreshing = true;
            SuspendLayout();
            try
            {
                _listen.Checked = view.Listening;
                RefreshT1(view);
                RefreshT2(view);
                RefreshT3(view);
                RefreshT5(view);
            }
            finally
            {
                ResumeLayout(true);
                _refreshing = false;
            }
            FitWidths();
        }

        private void RefreshT1(RemoteCommandsView view)
        {
            RemoteThisDeviceState state = RemoteCommandsView.StateOf(view);
            string signature = state.ToString();
            if (signature == _t1Shown) return;
            _t1Shown = signature;
            switch (state)
            {
                case RemoteThisDeviceState.Off: SetState("Off. Commands from other devices are not accepted.", null, null); break;
                case RemoteThisDeviceState.NoServer: SetState("No connected exchange server carries commands.", "Open exchange settings", () => OpenExchangeSettings?.Invoke()); break;
                case RemoteThisDeviceState.SignIn: SetState("Sign in to this account again to accept commands.", "Sign in again", () => OpenExchangeSettings?.Invoke()); break;
                case RemoteThisDeviceState.Offline: SetState("On. The exchange server cannot be reached right now.", null, null); break;
                default: SetState("On. The devices you allow can send commands now.", null, null); break;
            }
        }

        private void SetState(string line, string? action, Action? handler)
        {
            _state.Text = T(line);
            _stateAction.Visible = action != null;
            _stateAction.Text = action != null ? T(action) : "";
            _stateAction.Name = "external-control.state-action";
            _stateActionHandler = handler;
        }

        private void RefreshT2(RemoteCommandsView view)
        {
            string signature = string.Join(",", view.Pending.Select(p => p.Id.ToString(CultureInfo.InvariantCulture)));
            _t2.Visible = view.Pending.Count > 0; // shown only while a confirmation is open (section 4 T2)
            if (signature == _t2Shown) return;
            _t2Shown = signature;
            ClearBody(_t2);
            _countdownLabels.Clear();
            foreach (RemoteConsentPending pending in view.Pending)
            {
                var block = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(3, 4, 3, 8) };
                block.Controls.Add(new Label { Text = PromptTitle(pending), AutoSize = true, UseMnemonic = false, Font = _boldFont, Margin = new Padding(3, 2, 3, 2) });
                block.Controls.Add(new Label { Text = PromptBody(pending), AutoSize = true, UseMnemonic = false, Margin = new Padding(3, 2, 3, 2) });
                block.Controls.Add(MutedLabel(Localization.Format(T, "Kind: {0}", T(RemoteControlWords.ClassCaption(pending.Risk)))));
                Label countdown = MutedLabel(Countdown(pending));
                block.Controls.Add(countdown);
                _countdownLabels.Add(new KeyValuePair<Label, RemoteConsentPending>(countdown, pending));
                var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
                var allow = new Button { Text = T("Allow"), AutoSize = true, AccessibleName = T("Allow") + ": " + PromptTitle(pending) };
                var decline = new Button { Text = T("Decline"), AutoSize = true, AccessibleName = T("Decline") + ": " + PromptTitle(pending) };
                allow.Click += (_, _) => BeginInvoke(new Action(() => pending.Answer?.Invoke(true)));
                decline.Click += (_, _) => BeginInvoke(new Action(() => pending.Answer?.Invoke(false)));
                buttons.Controls.Add(allow);
                buttons.Controls.Add(decline);
                block.Controls.Add(buttons);
                _t2.Body.Controls.Add(block);
            }
            if (view.Pending.Count > 0) _countdown.Start(); else _countdown.Stop();
        }

        private void TickCountdown()
        {
            foreach (KeyValuePair<Label, RemoteConsentPending> pair in _countdownLabels)
                if (!pair.Key.IsDisposed) pair.Key.Text = Countdown(pair.Value);
        }

        private void RefreshT3(RemoteCommandsView view)
        {
            List<string> classes = RemoteControlWords.ShownClasses(AppCommandManifest.Shipped);
            var sb = new StringBuilder(view.Listening ? "on" : "off");
            foreach (RemoteCommandsEntryView e in view.Entries)
            {
                sb.Append('|').Append(e.EntryId).Append(':').Append(e.Label);
                foreach (RemoteCommandsDeviceView d in e.Devices)
                {
                    sb.Append(';').Append(d.DeviceId).Append(',').Append(d.Name).Append(',').Append(d.Product).Append(',').Append(d.Online)
                      .Append(',').Append(d.LastSeenUtcTicks).Append(',').Append(d.Allowed);
                    foreach (KeyValuePair<string, string> c in d.Classes.OrderBy(c => c.Key, StringComparer.Ordinal)) sb.Append(',').Append(c.Key).Append('=').Append(c.Value);
                    if (_saveFailed.Contains(e.EntryId + "|" + d.DeviceId)) sb.Append(",failed");
                }
            }
            string signature = sb.ToString();
            if (signature == _t3Shown) return;
            _t3Shown = signature;

            Control? focused = ActiveControl;
            string? focusName = focused != null && _t3.Body.ContainsFocus ? focused.Name : null;
            ClearBody(_t3);
            if (!view.Listening)
            {
                _t3.Body.Controls.Add(MutedLabel(T("Turn on listening to choose devices.")));
                return;
            }
            int devices = view.Entries.Sum(e => e.Devices.Count);
            if (devices == 0)
            {
                _t3.Body.Controls.Add(MutedLabel(T("No other device of your account can send commands yet. Sign in on it with the same account.")));
                return;
            }
            bool several = view.Entries.Count(e => e.Devices.Count > 0) > 1;
            foreach (RemoteCommandsEntryView entry in view.Entries)
            {
                if (entry.Devices.Count == 0) continue;
                if (several) _t3.Body.Controls.Add(new Label { Text = entry.Label, AutoSize = true, UseMnemonic = false, Font = _boldFont, Margin = new Padding(3, 6, 3, 2) });
                foreach (RemoteCommandsDeviceView device in entry.Devices)
                    _t3.Body.Controls.Add(DeviceRow(entry.EntryId, device, classes));
            }
            if (focusName != null)
            {
                Control? again = _t3.Body.Controls.Find(focusName, true).FirstOrDefault();
                if (again != null && again.CanFocus) again.Focus();
            }
        }

        private Control DeviceRow(string entryId, RemoteCommandsDeviceView device, List<string> classes)
        {
            string key = entryId + "|" + device.DeviceId;
            string idName = "external-control.device." + Safe(device.DeviceId);
            var row = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(3, 6, 3, 6) };
            string program = RemoteControlWords.ProgramName(device.Product);
            string presence = device.Online
                ? T("Online")
                : device.LastSeenUtcTicks > 0
                    ? Localization.Format(T, "Offline since {0}", new DateTime(device.LastSeenUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
                    : "";
            string name = RemoteControlWords.CleanName(device.Name);
            row.Controls.Add(new Label { Text = name, AutoSize = true, UseMnemonic = false, Font = _boldFont, Margin = new Padding(3, 2, 3, 0) });
            string sub = string.Join(" - ", new[] { program, presence }.Where(s => s.Length > 0));
            if (sub.Length > 0) row.Controls.Add(MutedLabel(sub));
            var allow = new BooleanOptionCheckBox
            {
                Text = T("Allow this device"), AutoSize = true, Checked = device.Allowed, Name = idName + ".allow",
                AccessibleName = T("Allow this device") + ": " + name, Margin = new Padding(3, 2, 3, 2),
            };
            allow.CheckedChanged += (_, _) =>
            {
                if (_refreshing) return;
                bool saved;
                _handling++;
                try { saved = SetDeviceAllowed?.Invoke(entryId, device.DeviceId, device.Name, allow.Checked) ?? false; }
                finally { _handling--; }
                if (saved) _saveFailed.Remove(key); else _saveFailed.Add(key);
                RefreshFromSourceLater();
            };
            row.Controls.Add(allow);
            if (device.Allowed)
                foreach (string riskClass in classes)
                    row.Controls.Add(ClassRow(entryId, device, riskClass, idName, name, key));
            if (_saveFailed.Contains(key))
            {
                var failed = new Label { Text = T("This change was not saved. Try again."), AutoSize = true, UseMnemonic = false, ForeColor = ThemePalette.Light.Danger, Margin = new Padding(3, 2, 3, 2) };
                row.Controls.Add(failed);
            }
            return row;
        }

        private Control ClassRow(string entryId, RemoteCommandsDeviceView device, string riskClass, string idName, string name, string key)
        {
            var block = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(24, 2, 3, 2) };
            string caption = T(RemoteControlWords.ClassCaption(riskClass));
            block.Controls.Add(new Label { Text = caption, AutoSize = true, UseMnemonic = false, Margin = new Padding(3, 2, 3, 0) });
            string verbs = string.Join(", ", RemoteControlWords.RemoteVerbs(AppCommandManifest.Shipped).Where(v => v.Risk == riskClass).Select(v => Summary(v.Name)));
            block.Controls.Add(new Label { Text = verbs, AutoSize = false, AutoEllipsis = true, UseMnemonic = false, ForeColor = ThemePalette.Light.TextMuted,
                Height = Font.Height + 4, Width = 480, Margin = new Padding(3, 0, 3, 2), Tag = "one-line" });
            device.Classes.TryGetValue(riskClass, out string? current);
            var choices = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
            foreach (string? consent in new[] { null, RemoteControl.ConsentAsk, RemoteControl.ConsentAlways })
            {
                string label = consent == null ? T("No access") : consent == RemoteControl.ConsentAsk ? T("Ask each time") : T("Always allow");
                var radio = new RadioButton
                {
                    Text = label, AutoSize = true, Checked = consent == current, UseMnemonic = false,
                    Name = idName + "." + riskClass + "." + (consent ?? "none"),
                    AccessibleName = name + ", " + caption + ": " + label, Margin = new Padding(3, 2, 12, 2),
                };
                string? chosen = consent;
                radio.CheckedChanged += (_, _) =>
                {
                    if (_refreshing || !radio.Checked) return;
                    if (chosen == RemoteControl.ConsentAlways && riskClass != "safe" && !ConfirmAlways(name))
                    {
                        RefreshFromSourceLater(force: true);
                        return;
                    }
                    bool saved;
                    _handling++;
                    try { saved = SetClassConsent?.Invoke(entryId, device.DeviceId, device.Name, riskClass, chosen) ?? false; }
                    finally { _handling--; }
                    if (saved) _saveFailed.Remove(key); else _saveFailed.Add(key);
                    RefreshFromSourceLater(force: !saved);
                };
                choices.Controls.Add(radio);
            }
            block.Controls.Add(choices);
            return block;
        }

        /// <summary>`always-title` / `always-body`; Cancel (No) holds the focus.</summary>
        private bool ConfirmAlways(string deviceName)
        {
            string message = T("Always allow this kind?") + "\n\n" + Localization.Format(T, "{0} will run commands of this kind on this device without asking you.", deviceName);
            return ConfirmDialog.Show(this, _language, message, MessageBoxButtons.YesNo, MessageBoxIcon.Question, danger: true) == DialogResult.Yes;
        }

        private void RefreshT5(RemoteCommandsView view)
        {
            List<string> lines = RemoteControlWords.Recent(view.Log, T);
            string text = lines.Count == 0 ? T("No requests yet.") : string.Join("\n", lines);
            if (text == _t5Shown) return;
            _t5Shown = text;
            _log.Text = text;
        }

        /// <summary>A change made from a handler of a control that the refresh is about to replace: redrawn one turn later.</summary>
        private void RefreshFromSourceLater(bool force = false)
        {
            if (force) _t3Shown = "";
            if (!IsHandleCreated) { RefreshFromSource(); return; }
            BeginInvoke(new Action(RefreshFromSource));
        }

        // ---- the pieces ----

        private string Summary(string verb) => RemoteControlWords.HasSummary(verb) ? T(RemoteControlWords.VerbSummary(verb)) : verb;

        internal string PromptTitle(RemoteConsentPending pending)
            => Localization.Format(T, "{0} asks {1}", RemoteControlWords.CleanName(pending.DeviceName), "CyrFlip");

        internal string PromptBody(RemoteConsentPending pending)
            => Summary(pending.Verb) + (pending.Arguments.Length > 0 ? " (" + pending.Arguments + ")" : "");

        private string Countdown(RemoteConsentPending pending)
            => Localization.Format(T, "Answer within {0} s. Without an answer the command is not run.",
                Math.Max(0, (int)Math.Ceiling((pending.DeadlineUtc - DateTime.UtcNow).TotalSeconds)).ToString(CultureInfo.InvariantCulture));

        private static void ClearBody(CollapsibleGroup group)
        {
            foreach (Control old in group.Body.Controls.Cast<Control>().ToList())
            {
                group.Body.Controls.Remove(old);
                old.Dispose();
            }
        }

        private static Label MutedLabel(string text = "") => new Label
        {
            Text = text, AutoSize = true, UseMnemonic = false, ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 2, 3, 4),
        };

        private static string Safe(string id)
        {
            var sb = new StringBuilder();
            foreach (char c in id) if (char.IsLetterOrDigit(c) || c == '-') sb.Append(c);
            return sb.ToString();
        }

        /// <summary>Wrapped labels follow the window's width; a one-line summary is cut with an ellipsis instead.</summary>
        private void FitWidths()
        {
            int width = Math.Max(200, _scroll.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 48);
            foreach (Label label in Labels(_sections))
            {
                if ("one-line".Equals(label.Tag)) { label.Width = Math.Max(100, width - 40); continue; }
                if (label.MaximumSize.Width != width) label.MaximumSize = new Size(width, 0);
            }
        }

        private static IEnumerable<Label> Labels(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is Label l) yield return l;
                foreach (Label inner in Labels(c)) yield return inner;
            }
        }

        private void OpenLog()
        {
            string path = RemoteCommandsLog.FilePath;
            if (!File.Exists(path))
            {
                ConfirmDialog.Show(this, _language, T("No requests yet."));
                return;
            }
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose(); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is FileNotFoundException || ex is InvalidOperationException)
            {
                ConfirmDialog.Show(this, _language, T("Windows could not open the log file."));
            }
        }

        // ---- geometry ----

        private void Place()
        {
            if (_placed) return;
            _placed = true;
            double scale = DeviceDpi / 96.0;
            MinimumSize = new Size((int)Math.Round(MinWidth * scale), (int)Math.Round(MinHeight * scale));
            int width = _config.ExternalControlWidth > 0 ? _config.ExternalControlWidth : (int)Math.Round(DefaultWidth * scale);
            int height = _config.ExternalControlHeight > 0 ? _config.ExternalControlHeight : (int)Math.Round(DefaultHeight * scale);
            List<Rectangle> work = Screen.AllScreens.Select(s => s.WorkingArea).ToList();
            Rectangle bounds;
            if (_config.ExternalControlX != int.MinValue && _config.ExternalControlY != int.MinValue)
                bounds = new Rectangle(_config.ExternalControlX, _config.ExternalControlY, width, height);
            else
            {
                Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
                bounds = new Rectangle(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2, width, height);
            }
            Bounds = ScreenPlacement.Clamp(bounds, work);
            FitWidths();
        }

        private void SaveBounds()
        {
            if (!_placed || WindowState != FormWindowState.Normal) return;
            Rectangle b = Bounds;
            if (b == new Rectangle(_config.ExternalControlX, _config.ExternalControlY, _config.ExternalControlWidth, _config.ExternalControlHeight)) return;
            _config.ExternalControlX = b.X;
            _config.ExternalControlY = b.Y;
            _config.ExternalControlWidth = b.Width;
            _config.ExternalControlHeight = b.Height;
            _config.SaveExternalControlWindow();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _countdown.Dispose();
                _titleFont?.Dispose();
                _boldFont?.Dispose();
                _ownFont?.Dispose();
            }
            base.Dispose(disposing);
        }

        private string T(string en) => Localization.Translate(_language, en);
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The board's "add" button (ticket S0069 section 6): two fields - what to watch and, if the person wants
    /// one, a label - and the recognizer's reading of the first, live, under them. Everything else a target
    /// has (interval, thresholds, the balloon, the journal) is on the settings page, one click away, and the
    /// dialog says so. Laid out by an auto-sizing <see cref="TableLayoutPanel"/> like every dialog here.
    /// <para>
    /// Typing costs the network nothing: the recognition is offline. The one question a <c>\\host</c> asks
    /// (ping, then port 445) is asked when the person confirms, with Esc to skip it.
    /// </para>
    /// </summary>
    internal sealed class TrafficLightQuickAddDialog : ThemedForm
    {
        private readonly string _uiLanguage;
        private readonly Func<IReadOnlyCollection<TrafficLightTarget>> _existing;
        private readonly TrafficLightProbeSet _probes;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private CancellationTokenSource? _resolving;
        private Font? _ownFont;

        private readonly TextBox _address = new TextBox();
        private readonly TextBox _label = new TextBox { MaxLength = TrafficLightTargetList.MaxLabelLength };
        private readonly Label _hint = new Label { AutoSize = true, MaximumSize = new Size(460, 0), Margin = new Padding(3, 4, 3, 8) };
        private readonly Label _status = new Label { AutoSize = true, MaximumSize = new Size(460, 0), Margin = new Padding(3, 8, 3, 4) };
        private readonly Label _more = new Label { AutoSize = true, MaximumSize = new Size(460, 0), Margin = new Padding(3, 4, 3, 4) };
        private readonly Button _ok;
        private readonly Button _cancel;
        private QuickAddEvaluation _current = new QuickAddEvaluation();

        public TrafficLightQuickAddDialog(string uiLanguage, Func<IReadOnlyCollection<TrafficLightTarget>> existing,
            TrafficLightProbeSet probes, string address = "", string label = "")
        {
            _uiLanguage = uiLanguage;
            _existing = existing ?? throw new ArgumentNullException(nameof(existing));
            _probes = probes ?? throw new ArgumentNullException(nameof(probes));

            Text = T("New target");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            // Opened from a board that never took the focus, so there is no owner to centre on: like the
            // message box it comes to the front on its own and has a taskbar button.
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ApplyScript(uiLanguage);

            int editor = TextRenderer.MeasureText(new string('W', 30), Font).Width + 16;
            _address.Width = editor;
            _label.Width = editor;
            _hint.ForeColor = ThemePalette.Light.TextMuted;
            _more.ForeColor = ThemePalette.Light.TextMuted;
            _hint.Text = T("Type an address, name, folder or drive; CyrFlip works out what it is.");
            _more.Text = T("Other options are on the settings page (the pencil button on the board).");
            _address.AccessibleName = T("Address");
            _label.AccessibleName = T("Label");

            // Not a DialogResult button: the dialog closes only after the machine question, if it has one.
            _ok = new Button
            {
                Text = T("Add"), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(TextRenderer.MeasureText(T("Cancel"), Font).Width + 16, 0),
            };
            ButtonGlyphs.Attach(_ok, AppGlyphs.Add, ButtonLook.Affirmative);
            _ok.Click += async (_, _) => await ConfirmAsync();
            _cancel = ButtonGlyphs.Answer(new Button
            {
                Text = T("Cancel"), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(TextRenderer.MeasureText(T("Cancel"), Font).Width + 16, 0),
                DialogResult = DialogResult.Cancel,
            });

            BuildLayout();
            AcceptButton = _ok;
            CancelButton = _cancel;
            _address.Text = address ?? "";
            _label.Text = label ?? "";
            _address.TextChanged += (_, _) => Reevaluate();
            _label.TextChanged += (_, _) => Reevaluate();
            Reevaluate();
        }

        /// <summary>The targets to add (one, or several for a contract string), or empty when cancelled.</summary>
        public List<TrafficLightTarget> Result { get; } = new List<TrafficLightTarget>();

        private void BuildLayout()
        {
            var grid = new TableLayoutPanel
            {
                ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill, Padding = new Padding(12),
            };
            int row = 0;
            grid.Controls.Add(_hint, 0, row); grid.SetColumnSpan(_hint, 2); row++;
            grid.Controls.Add(Caption(T("Address:")), 0, row); grid.Controls.Add(_address, 1, row); row++;
            grid.Controls.Add(Caption(T("Label:")), 0, row); grid.Controls.Add(_label, 1, row); row++;
            grid.Controls.Add(_status, 0, row); grid.SetColumnSpan(_status, 2); row++;
            grid.Controls.Add(_more, 0, row); grid.SetColumnSpan(_more, 2); row++;

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0),
            };
            buttons.Controls.Add(_cancel);
            buttons.Controls.Add(_ok);
            grid.Controls.Add(buttons, 0, row); grid.SetColumnSpan(buttons, 2);
            Controls.Add(grid);
        }

        // ---- what the fields say ----

        private void Reevaluate()
        {
            if (_resolving != null) return;
            _current = TrafficLightQuickAdd.Evaluate(_address.Text, _label.Text, _existing(), T);
            _ok.Enabled = _current.Verdict == QuickAddVerdict.Ready;
            _status.Text = StatusText(_current);
        }

        private string StatusText(QuickAddEvaluation e)
        {
            string head;
            switch (e.Verdict)
            {
                case QuickAddVerdict.Ready:
                    head = e.Plan.Targets.Count == 1
                        ? Localization.Format(T, "Recognized: {0}.", TrafficLightText.KindWord(e.Plan.Targets[0].Kind, T))
                        : Localization.Format(T, "Targets added: {0}.", e.Plan.Targets.Count);
                    break;
                case QuickAddVerdict.NotRecognized:
                    head = T("Not recognized: enter a host, a site address, a folder or a drive.");
                    break;
                case QuickAddVerdict.Duplicate:
                    head = T("These targets are already on the board.");
                    break;
                case QuickAddVerdict.TooMany:
                    head = Localization.Format(T, "The board holds at most {0} targets.", TrafficLightTargetList.MaxTargets);
                    break;
                default:
                    head = "";
                    break;
            }
            if (e.Notes.Count == 0) return head;
            return head.Length == 0 ? string.Join(" ", e.Notes) : head + " " + string.Join(" ", e.Notes);
        }

        // ---- confirming ----

        private async System.Threading.Tasks.Task ConfirmAsync()
        {
            if (_resolving != null || _current.Verdict != QuickAddVerdict.Ready) return;
            SmartAddPlan plan = _current.Plan;
            if (plan.MachineIds.Count > 0)
            {
                // "A Windows PC usually drops ICMP": ping, then port 445 - the one fallback, ever.
                _resolving = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                SetBusy(true);
                _status.Text = T("Checking: ping, then port 445..  Esc - skip.");
                bool skipped = false;
                try
                {
                    for (int i = 0; i < plan.Targets.Count; i++)
                    {
                        if (!plan.MachineIds.Contains(plan.Targets[i].Id)) continue;
                        TrafficLightTarget resolved = await TrafficLightSmartAdd.ResolveMachineAsync(plan.Targets[i], _probes, _resolving.Token)
                            .ConfigureAwait(true);
                        resolved.Id = plan.Targets[i].Id;
                        resolved.Ordinal = plan.Targets[i].Ordinal;
                        resolved.Label = plan.Targets[i].Label;
                        plan.Targets[i] = resolved;
                    }
                }
                catch (OperationCanceledException)
                {
                    // Skipped: the target is added as it was recognized - a ping.
                    skipped = true;
                }
                finally
                {
                    _resolving.Dispose();
                    _resolving = null;
                }
                if (IsDisposed || _lifetime.IsCancellationRequested) return;
                SetBusy(false);
                if (skipped) _status.Text = T("The check was skipped.");
            }
            Result.Clear();
            Result.AddRange(plan.Targets);
            DialogResult = DialogResult.OK;
            Close();
        }

        private void SetBusy(bool busy)
        {
            _address.Enabled = !busy;
            _label.Enabled = !busy;
            _ok.Enabled = !busy;
        }

        /// <summary>Esc while the machine question runs skips the question; the next Esc closes the dialog.</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && _resolving != null)
            {
                try { _resolving.Cancel(); } catch (Exception ex) { TrafficLightLog.Fault("quick-add-skip", ex); }
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ForegroundActivator.Activate(this);
            _address.Focus();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { _lifetime.Cancel(); } catch (Exception ex) { TrafficLightLog.Fault("quick-add-close", ex); }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
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

        private string T(string en) => Localization.Translate(_uiLanguage, en);
    }
}

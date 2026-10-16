using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The traffic light's settings page (ticket S0069 section 6): the module switch, the chord, the board's
    /// options, the one-field smart add with the target table beneath it, and the change journal. A partial
    /// class, like the hotkeys overview, because the form is already large.
    /// </summary>
    internal sealed partial class SettingsForm
    {
        private readonly CheckBox _tlEnabled = Check("Enable the availability traffic light");
        private readonly CheckBox _tlHotkeyEnabled = Check("Availability traffic light");
        private readonly Label _tlHotkeyValue = new Label { AutoSize = true };
        private readonly CheckBox _tlPinned = Check("Keep the board on top of other windows");
        private readonly ComboBox _tlTileSize = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly NumericUpDown _tlColumns = new NumericUpDown
        {
            Minimum = AppConfig.MinTrafficLightColumns, Maximum = AppConfig.MaxTrafficLightColumns,
            Value = AppConfig.DefaultTrafficLightColumns, Width = 70, Margin = new Padding(3, 4, 3, 4),
        };
        private readonly TrackBar _tlTransparency = new TrackBar { Minimum = 0, Maximum = 100, TickFrequency = 10, SmallChange = 5, LargeChange = 10, Width = 245 };
        private readonly Label _tlTransparencyValue = new Label { AutoSize = true };
        private readonly TextBox _tlAddBox = new TextBox
        {
            Multiline = true, AcceptsReturn = false, WordWrap = false, ScrollBars = ScrollBars.Vertical, Width = 520,
        };
        private readonly Label _tlAddStatus = new Label { AutoSize = true, MaximumSize = new Size(890, 0), Margin = new Padding(3, 4, 3, 4) };
        private Button _tlUndo = null!;
        private readonly FlowLayoutPanel _tlRows = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(3, 4, 3, 4) };
        private readonly List<Control> _tlControls = new List<Control>();
        private readonly Dictionary<string, KeyValuePair<Label, Label>> _tlRowLabels = new Dictionary<string, KeyValuePair<Label, Label>>(StringComparer.Ordinal);
        private readonly List<string> _tlLastAdded = new List<string>();
        private CancellationTokenSource? _tlAdding;

        /// <summary>The module's switches, the chord, the pin or the tile size changed.</summary>
        internal event EventHandler? TrafficLightChanged;
        internal event EventHandler? TrafficLightHotkeyChangeRequested;
        /// <summary>The list of targets was edited here (added, changed, removed, moved).</summary>
        internal event EventHandler? TrafficLightTargetsChanged;
        internal event EventHandler? OpenTrafficLightRequested;
        internal event EventHandler? TrafficLightCheckAllRequested;
        /// <summary><c>open</c>, <c>folder</c> or <c>clear</c> - the journal lives with the context.</summary>
        internal event Action<string>? TrafficLightHistoryRequested;

        /// <summary>The board's last known results, for the table; the context hands it over.</summary>
        internal Func<List<TrafficLightTile>>? TrafficLightSnapshot { get; set; }

        /// <summary>One check of a candidate target for the editor's "Check now"; the default runs the real probes.</summary>
        internal Func<TrafficLightTarget, CancellationToken, Task<ProbeOutcome>>? TrafficLightCheck { get; set; }

        private void TrafficLightSettingChanged(Action apply, bool reload = false)
        {
            if (_loading) return;
            apply();
            TrafficLightChanged?.Invoke(this, EventArgs.Empty);
            if (reload) Reload();
        }

        private void NotifyTrafficLightTargetsChanged() => TrafficLightTargetsChanged?.Invoke(this, EventArgs.Empty);

        private Control TrackTrafficLight(Control control) { _tlControls.Add(control); return control; }

        /// <summary>Wires the handlers; called once, after the page is built.</summary>
        private void WireTrafficLight()
        {
            _tlEnabled.Name = "setting.traffic-light";
            _tlHotkeyEnabled.Name = "setting.traffic-light-chord";
            _tlPinned.Name = "setting.traffic-light-pin";
            _tlTileSize.Name = "setting.traffic-light-tile-size";
            _tlColumns.Name = "setting.traffic-light-columns";
            _tlTransparency.Name = "setting.traffic-light-transparency";
            _tlAddBox.Name = "setting.traffic-light-add";
            _tlEnabled.CheckedChanged += (_, _) =>
                TrafficLightSettingChanged(() => _config.EnableTrafficLight = _tlEnabled.Checked, reload: true);
            _tlHotkeyEnabled.CheckedChanged += (_, _) =>
                TrafficLightSettingChanged(() => _config.EnableTrafficLightHotkey = _tlHotkeyEnabled.Checked);
            _tlPinned.CheckedChanged += (_, _) =>
                TrafficLightSettingChanged(() => _config.TrafficLightPinned = _tlPinned.Checked);
            _tlTileSize.SelectedIndexChanged += (_, _) =>
            {
                if (_loading || _tlTileSize.SelectedIndex < 0) return;
                TrafficLightSettingChanged(() => _config.TrafficLightTileSize = _tlTileSize.SelectedIndex == 0
                    ? AppConfig.MicroscopicTrafficLightTileSize : _tlTileSize.SelectedIndex - 1);
            };
            _tlColumns.ValueChanged += (_, _) =>
                TrafficLightSettingChanged(() => _config.TrafficLightColumns = (int)_tlColumns.Value);
            _tlTransparency.ValueChanged += (_, _) =>
            {
                if (_loading) return;
                _tlTransparencyValue.Text = _tlTransparency.Value + "%";
                TrafficLightSettingChanged(() => _config.TrafficLightWindowTransparency = _tlTransparency.Value);
            };
            _tlAddBox.KeyDown += OnTrafficLightAddKeyDown;
            _tlAddBox.TextChanged += (_, _) => ClearTrafficLightAddStatus();
        }

        private void ReloadTrafficLightState()
        {
            _tlEnabled.Checked = _config.EnableTrafficLight;
            _tlHotkeyEnabled.Checked = _config.EnableTrafficLightHotkey;
            _tlHotkeyValue.Text = _config.TrafficLightHotkey;
            _tlPinned.Checked = _config.TrafficLightPinned;
            FillTileSizeCombo();
            _tlTileSize.SelectedIndex = _config.TrafficLightTileSize == AppConfig.MicroscopicTrafficLightTileSize
                ? 0 : Math.Max(0, Math.Min(2, _config.TrafficLightTileSize)) + 1;
            _tlColumns.Value = Math.Max(_tlColumns.Minimum, Math.Min(_tlColumns.Maximum, _config.TrafficLightColumns));
            _tlTransparency.Value = Math.Max(0, Math.Min(100, _config.TrafficLightWindowTransparency));
            _tlTransparencyValue.Text = _tlTransparency.Value + "%";
            foreach (Control control in _tlControls) control.Enabled = _config.EnableTrafficLight;
        }

        private void FillTileSizeCombo()
        {
            int selected = _tlTileSize.SelectedIndex;
            _tlTileSize.Items.Clear();
            _tlTileSize.Items.Add(Translate("Microscopic (tiles)"));
            _tlTileSize.Items.Add(Translate("Small (tiles)"));
            _tlTileSize.Items.Add(Translate("Medium (tiles)"));
            _tlTileSize.Items.Add(Translate("Large (tiles)"));
            if (selected >= 0 && selected < 4) _tlTileSize.SelectedIndex = selected;
        }

        private TabPage TrafficLightPage()
        {
            _tlUndo = Button("Undo the addition", AppGlyphs.Undo, UndoTrafficLightAdd);
            _tlUndo.Visible = false;
            TabPage page = Page("Availability traffic light",
                "A board of tiles for your hosts, sites, folders and drives; checks run while CyrFlip runs, results stay in memory.");
            FlowLayoutPanel panel = ContentPanel(page);

            // The module switch leads in a group of its own, like the hotkeys page's (S0060 M4).
            panel.Controls.Add(Group("trafficlight/switches", "Main switches",
                Setting(_tlEnabled, "While off, there is no timer, no shortcut, no tray item and no window, and CyrFlip makes no network checks.")));

            panel.Controls.Add(Group("trafficlight/module", "Board",
                TrackTrafficLight(HotkeyRow(_tlHotkeyEnabled, _tlHotkeyValue, () => TrafficLightHotkeyChangeRequested?.Invoke(this, EventArgs.Empty),
                    "Shows the board and gives it focus; press again to hide. On the board: arrows, Enter, F5, Esc.")),
                TrackTrafficLight(Setting(_tlPinned,
                    "The board stays on top in a corner; drag it anywhere. It is hidden from screenshots and screen sharing.")),
                TrackTrafficLight(Setting(LabeledRow("Tile size:", _tlTileSize),
                    "Microscopic: half-size squares without buttons. Drag any square to move; double-click to open these settings.")),
                TrackTrafficLight(Setting(LabeledRow("Columns:", _tlColumns),
                    "Columns: the board puts this many tiles in a row; the rows follow from the number of targets.")),
                TrackTrafficLight(Setting(TrafficLightTransparencyRow(),
                    "Board window transparency: 0 is a solid window, 100 leaves only the buttons and the tiles visible. Applied immediately.")),
                TrackTrafficLight(Button("Show the board", AppGlyphs.Run, () => OpenTrafficLightRequested?.Invoke(this, EventArgs.Empty)))));

            panel.Controls.Add(Group("trafficlight/targets", "Targets",
                TrackTrafficLight(new Label
                {
                    Text = "Type an address, name, folder or drive; CyrFlip works out what it is. Enter adds, Shift+Enter opens details.",
                    AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 4, 3, 4),
                }),
                TrackTrafficLight(_tlAddBox),
                TrackTrafficLight(TrafficLightAddButtons()),
                TrackTrafficLight(_tlAddStatus),
                TrackTrafficLight(_tlUndo),
                TrackTrafficLight(_tlRows),
                TrackTrafficLight(Button("Check everything now", AppGlyphs.Refresh, () => TrafficLightCheckAllRequested?.Invoke(this, EventArgs.Empty)))));

            panel.Controls.Add(Group("trafficlight/history", "Change history",
                TrackTrafficLight(new Label
                {
                    Text = "A plain text file of state changes for marked targets. It holds addresses and never goes into a log archive.",
                    AutoSize = true, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 4, 3, 4),
                }),
                TrackTrafficLight(TrafficLightHistoryButtons())));
            WireTrafficLight();
            return page;
        }

        /// <summary>The board window's transparency: a caption, a slider and the live percent beside it.</summary>
        private Control TrafficLightTransparencyRow()
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 7, 0, 0) };
            row.Controls.Add(new Label { Text = "Board window transparency:", AutoSize = true, Tag = "caption", Padding = new Padding(0, 8, 5, 0) });
            row.Controls.Add(_tlTransparency);
            row.Controls.Add(_tlTransparencyValue);
            return row;
        }

        private Control TrafficLightAddButtons()
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 2) };
            row.Controls.Add(Button("Add", AppGlyphs.Add, () => _ = AddTrafficLightFromField(details: false)));
            row.Controls.Add(Button("Details...", AppGlyphs.Edit, () => _ = AddTrafficLightFromField(details: true)));
            return row;
        }

        private Control TrafficLightHistoryButtons()
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 2) };
            row.Controls.Add(Button("Open the history file", AppGlyphs.ChooseFile, () => TrafficLightHistoryRequested?.Invoke("open")));
            row.Controls.Add(Button("Show in folder", AppGlyphs.ChooseFolder, () => TrafficLightHistoryRequested?.Invoke("folder")));
            row.Controls.Add(Button("Clear the history...", AppGlyphs.ClearAll, () => TrafficLightHistoryRequested?.Invoke("clear")));
            return row;
        }

        // ---- the table ----

        private int _tlStateColumn, _tlLabelColumn, _tlKindColumn, _tlAddressColumn, _tlIntervalColumn, _tlCheckedColumn;

        private void MeasureTrafficLightColumns(List<TrafficLightTile> tiles)
        {
            using var bold = new Font(Font, FontStyle.Bold);
            _tlStateColumn = Math.Max(Column(170), TextWidth(Translate("State"), bold));
            _tlLabelColumn = Math.Max(Column(150), TextWidth(Translate("Label"), bold));
            _tlKindColumn = Math.Max(Column(110), TextWidth(Translate("Type"), bold));
            _tlAddressColumn = Math.Max(Column(240), TextWidth(Translate("Address"), bold));
            _tlIntervalColumn = Math.Max(Column(90), TextWidth(Translate("Every"), bold));
            _tlCheckedColumn = Math.Max(Column(110), TextWidth(Translate("Checked"), bold));
            foreach (TrafficLightTarget target in _config.TrafficLightTargets)
            {
                _tlLabelColumn = Math.Max(_tlLabelColumn, TextWidth(TrafficLightText.Caption(target), Font));
                _tlKindColumn = Math.Max(_tlKindColumn, TextWidth(TrafficLightText.KindWord(target.Kind, Translate), Font));
            }
            _tlLabelColumn = Math.Min(_tlLabelColumn, Column(260));
            _tlKindColumn = Math.Min(_tlKindColumn, Column(190));
            _tlAddressColumn = Math.Min(_tlAddressColumn, Column(380));
        }

        private List<TrafficLightTile> TrafficLightTiles()
        {
            try { return TrafficLightSnapshot?.Invoke() ?? new List<TrafficLightTile>(); }
            catch { return new List<TrafficLightTile>(); }
        }

        private void ReloadTrafficLightRows()
        {
            // A two-line box, measured from the font: a multiline box would otherwise stay one line high
            // and a pasted list would be impossible to see.
            _tlAddBox.Height = Font.Height * 2 + 10;
            _tlAddBox.AccessibleName = Translate("Address");
            RowFocus focus = RowFocus.Capture(_tlRows);
            _tlRows.SuspendLayout();
            var old = new Control[_tlRows.Controls.Count];
            _tlRows.Controls.CopyTo(old, 0);
            _tlRows.Controls.Clear();
            foreach (Control control in old) control.Dispose();
            _tlRowLabels.Clear();

            List<TrafficLightTile> tiles = TrafficLightTiles();
            MeasureTrafficLightColumns(tiles);
            _tlRows.Controls.Add(TrafficLightHeaderRow());
            List<TrafficLightTarget> ordered = TrafficLightTargetList.InOrder(_config.TrafficLightTargets);
            for (int i = 0; i < ordered.Count; i++)
                _tlRows.Controls.Add(TrafficLightRow(ordered[i], tiles.FirstOrDefault(t => t.Target.Id == ordered[i].Id), i, ordered.Count));
            if (ordered.Count == 0)
                _tlRows.Controls.Add(new Label
                {
                    Text = Translate("There are no targets yet - type an address in the field above and press Enter."),
                    AutoSize = true, ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(25, 6, 3, 4),
                });
            // Gated here, as the translator's table is: ApplyLanguage rebuilds the rows after the gating pass.
            _tlRows.Enabled = _config.EnableTrafficLight;
            _tlRows.ResumeLayout();
            focus.Restore(_tlRows, TakeRowFocusDelta());
        }

        /// <summary>The state and last-check columns follow the board without rebuilding a row under the user's hand.</summary>
        internal void RefreshTrafficLightStates()
        {
            if (IsDisposed || _tlRowLabels.Count == 0 || !Visible) return;
            List<TrafficLightTile> tiles = TrafficLightTiles();
            foreach (TrafficLightTile tile in tiles)
            {
                if (!_tlRowLabels.TryGetValue(tile.Target.Id, out KeyValuePair<Label, Label> labels)) continue;
                labels.Key.Text = StateText(tile);
                labels.Value.Text = CheckedText(tile);
            }
        }

        private string StateText(TrafficLightTile? tile)
        {
            if (tile == null) return "○ " + Translate("Not checked yet");
            string mark = TrafficLightBoard.MarkOf(tile);
            string word = TrafficLightText.AccessibleValue(tile, Translate);
            return mark.Length > 0 ? mark + " " + word : word;
        }

        private string CheckedText(TrafficLightTile? tile)
            => tile?.CheckedAtUtc == null ? "-" : TrafficLightText.When(tile.CheckedAtUtc.Value, DateTime.UtcNow, TimeZoneInfo.Local);

        private Control TrafficLightHeaderRow()
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 2, 3, 0) };
            row.Controls.Add(new Label { Text = "", AutoSize = false, Width = 22, Height = 18 });
            Label[] heads =
            {
                ColumnLabel(Translate("State"), _tlStateColumn), ColumnLabel(Translate("Label"), _tlLabelColumn),
                ColumnLabel(Translate("Type"), _tlKindColumn), ColumnLabel(Translate("Address"), _tlAddressColumn),
                ColumnLabel(Translate("Every"), _tlIntervalColumn), ColumnLabel(Translate("Checked"), _tlCheckedColumn),
            };
            foreach (Label head in heads) { head.Font = BoldFont; row.Controls.Add(head); }
            return row;
        }

        private string IntervalText(TrafficLightTarget target)
        {
            if (target.ManualOnly) return Translate("manual");
            int s = target.IntervalSeconds;
            if (s % 3600 == 0) return Localization.Format(Translate, "{0} h", s / 3600);
            if (s % 60 == 0) return Localization.Format(Translate, "{0} min", s / 60);
            return Localization.Format(Translate, "{0} s", s);
        }

        private Control TrafficLightRow(TrafficLightTarget target, TrafficLightTile? tile, int index, int count)
        {
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 1, 3, 1) };
            var enabled = new RowCheckBox { Checked = target.Enabled, AutoSize = true, Margin = new Padding(3, 0, 2, 0), Anchor = AnchorStyles.Left };
            enabled.CheckedChanged += (_, _) =>
            {
                if (_loading) return;
                target.Enabled = enabled.Checked;
                NotifyTrafficLightTargetsChanged();
                ReloadTrafficLightRows();
            };
            row.Controls.Add(enabled);

            Label state = ColumnLabel(StateText(tile), _tlStateColumn);
            if (!target.Enabled) state.Text = Translate("Off");
            Label caption = ColumnLabel(TrafficLightText.Caption(target), _tlLabelColumn);
            Label kind = ColumnLabel(TrafficLightText.KindWord(target.Kind, Translate), _tlKindColumn);
            Label address = ColumnLabel(target.Address, _tlAddressColumn);
            Label interval = ColumnLabel(IntervalText(target), _tlIntervalColumn);
            Label checkedAt = ColumnLabel(CheckedText(tile), _tlCheckedColumn);
            foreach (Label muted in new[] { kind, address, interval, checkedAt }) muted.ForeColor = ThemePalette.Light.TextMuted;
            foreach (Label l in new[] { state, caption, kind, address, interval, checkedAt }) row.Controls.Add(l);
            _tlRowLabels[target.Id] = new KeyValuePair<Label, Label>(state, checkedAt);

            row.Controls.Add(Button(Translate("Change..."), AppGlyphs.Edit, () => EditTrafficLightTarget(target)));
            Button up = Button("", AppGlyphs.MoveUp, () => MoveTrafficLightTarget(target, -1));
            Button down = Button("", AppGlyphs.MoveDown, () => MoveTrafficLightTarget(target, 1));
            up.Enabled = index > 0; down.Enabled = index < count - 1;
            up.AccessibleName = Translate("Up"); down.AccessibleName = Translate("Down");
            row.Controls.Add(up);
            row.Controls.Add(down);
            row.Controls.Add(Button(Translate("Delete"), AppGlyphs.Delete, () => RemoveTrafficLightTarget(target)));
            NameRow(row, TrafficLightText.Caption(target));
            return row;
        }

        // ---- editing ----

        private IEnumerable<string> TrafficLightGroups()
            => _config.TrafficLightTargets.Select(t => (t.Group ?? "").Trim()).Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);

        private Func<TrafficLightTarget, CancellationToken, Task<ProbeOutcome>> TrafficLightCheckFunction()
            => TrafficLightCheck ?? ((target, ct) => CyrFlip.TrafficLightQuickCheck.RunAsync(target, TrafficLightProbeSet.CreateDefault(), ct));

        private void EditTrafficLightTarget(TrafficLightTarget target)
        {
            using var dialog = new TrafficLightTargetDialog(target.Clone(), TrafficLightGroups(), _config.UiLanguage,
                TrafficLightCheckFunction(), isNew: false);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result == null) return;
            int at = _config.TrafficLightTargets.FindIndex(t => t.Id == target.Id);
            if (at < 0) return;
            _config.TrafficLightTargets[at] = dialog.Result;
            NotifyTrafficLightTargetsChanged();
            ReloadTrafficLightRows();
        }

        private void RemoveTrafficLightTarget(TrafficLightTarget target)
        {
            if (ConfirmDialog.Show(this, _config.UiLanguage,
                    Localization.Format(Translate, "Delete the target \"{0}\"? The deletion cannot be undone.", TrafficLightText.Caption(target)),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true) != DialogResult.Yes)
                return;
            _config.TrafficLightTargets.RemoveAll(t => t.Id == target.Id);
            TrafficLightTargetList.Renumber(_config.TrafficLightTargets);
            NotifyTrafficLightTargetsChanged();
            ReloadTrafficLightRows();
        }

        private void MoveTrafficLightTarget(TrafficLightTarget target, int direction)
        {
            if (!TrafficLightTargetList.Move(_config.TrafficLightTargets, target.Id, direction)) return;
            NotifyTrafficLightTargetsChanged();
            ReloadTrafficLightRows();
        }

        // ---- the one field (D24) ----

        private void OnTrafficLightAddKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && _tlAdding != null)
            {
                try { _tlAdding.Cancel(); } catch (Exception ex) { TrafficLightLog.Fault("add-cancel", ex); }
                e.Handled = true; e.SuppressKeyPress = true;
                return;
            }
            if (e.KeyCode != Keys.Enter) return;
            // Enter adds at once with the defaults; Shift+Enter opens the details first.
            e.Handled = true;
            e.SuppressKeyPress = true;
            _ = AddTrafficLightFromField(details: e.Shift);
        }

        private void ClearTrafficLightAddStatus()
        {
            if (_tlAdding != null) return;
            _tlAddStatus.Text = "";
        }

        private async Task AddTrafficLightFromField(bool details)
        {
            if (_tlAdding != null) return;
            string text = _tlAddBox.Text;
            RecognitionResult result = TargetRecognizer.Recognize(text);
            if (result.Lines.Count == 0) { _tlAddStatus.Text = Translate("Enter an address, a host name, a folder or a drive."); return; }

            // A .fmscfg document or a broken contract string says why and adds nothing of it.
            var notes = new List<string>();
            foreach (RecognizedLine line in result.Lines)
            {
                if (line.Status == LineStatus.FmsCfgFile) notes.Add(Translate("A file with a login is not read: enter the host and the port."));
                else if (line.Status == LineStatus.Refused) notes.Add(Localization.Format(Translate, "The connection string was refused as a whole: {0}.", line.Reason));
                else if (line.CommaReadAs.Length > 0) notes.Add(Localization.Format(Translate, "Read as {0}.", line.CommaReadAs));
                if (line.SecretDropped && line.Status == LineStatus.Recognized) notes.Add(Translate("The login and password were not kept."));
            }
            if (result.Truncated) notes.Add(Localization.Format(Translate, "The first {0} lines were read.", TargetRecognizer.MaxLines));

            List<RecognizedLine> recognized = result.Lines.Where(l => l.IsRecognized).ToList();
            if (recognized.Count == 0)
            {
                // Nothing here is recognized: the details panel opens with the kind chooser empty (D24).
                if (result.Lines.Count == 1 && result.Lines[0].Status == LineStatus.NotRecognized)
                {
                    OpenTrafficLightDetails(new TrafficLightTarget { Kind = "", Address = result.Lines[0].Text, Ordinal = NextTrafficLightOrdinal() });
                    return;
                }
                _tlAddStatus.Text = string.Join(" ", notes);
                return;
            }

            SmartAddPlan plan = TrafficLightSmartAdd.Plan(recognized, _config.TrafficLightTargets);
            if (details)
            {
                OpenTrafficLightDetails(plan.Targets);
                return;
            }

            _tlAdding = new CancellationTokenSource();
            try
            {
                if (plan.MachineIds.Count > 0)
                {
                    _tlAddStatus.Text = Translate("Checking: ping, then port 445..  Esc - skip.");
                    for (int i = 0; i < plan.Targets.Count; i++)
                    {
                        if (!plan.MachineIds.Contains(plan.Targets[i].Id)) continue;
                        TrafficLightTarget resolved = await TrafficLightSmartAdd.ResolveMachineAsync(plan.Targets[i],
                            TrafficLightProbeSet.CreateDefault(), _tlAdding.Token).ConfigureAwait(true);
                        resolved.Id = plan.Targets[i].Id;
                        resolved.Ordinal = plan.Targets[i].Ordinal;
                        plan.Targets[i] = resolved;
                    }
                }
            }
            catch (OperationCanceledException) { _tlAddStatus.Text = Translate("The check was skipped."); }
            finally
            {
                _tlAdding.Dispose();
                _tlAdding = null;
            }
            if (IsDisposed) return;
            CommitTrafficLightAdd(plan, notes);
        }

        private int NextTrafficLightOrdinal()
            => _config.TrafficLightTargets.Count == 0 ? 0 : _config.TrafficLightTargets.Max(t => t.Ordinal) + 1;

        private void CommitTrafficLightAdd(SmartAddPlan plan, List<string> notes)
        {
            if (_config.TrafficLightTargets.Count + plan.Targets.Count > TrafficLightTargetList.MaxTargets)
            {
                _tlAddStatus.Text = Localization.Format(Translate, "The board holds at most {0} targets.", TrafficLightTargetList.MaxTargets);
                return;
            }
            _tlLastAdded.Clear();
            foreach (TrafficLightTarget target in plan.Targets)
            {
                _config.TrafficLightTargets.Add(target);
                _tlLastAdded.Add(target.Id);
            }

            string line;
            if (plan.Targets.Count == 1)
                line = Localization.Format(Translate, "Added: \"{0}\" - {1}.", TrafficLightText.Caption(plan.Targets[0]),
                    TrafficLightText.KindWord(plan.Targets[0].Kind, Translate));
            else if (plan.Targets.Count > 1)
                line = Localization.Format(Translate, "Targets added: {0}.", plan.Targets.Count);
            else
                line = Translate("These targets are already on the board.");
            if (plan.Duplicates.Count > 0 && plan.Targets.Count > 0)
                notes.Add(Localization.Format(Translate, "Already there: {0}.", plan.Duplicates.Count));
            _tlAddStatus.Text = notes.Count > 0 ? line + " " + string.Join(" ", notes) : line;
            _tlUndo.Visible = plan.Targets.Count > 0;

            if (plan.Targets.Count > 0)
            {
                _loading = true;
                _tlAddBox.Clear();
                _loading = false;
                NotifyTrafficLightTargetsChanged();
                ReloadTrafficLightRows();
            }
        }

        private void UndoTrafficLightAdd()
        {
            if (_tlLastAdded.Count == 0) return;
            _config.TrafficLightTargets.RemoveAll(t => _tlLastAdded.Contains(t.Id));
            TrafficLightTargetList.Renumber(_config.TrafficLightTargets);
            _tlLastAdded.Clear();
            _tlUndo.Visible = false;
            _tlAddStatus.Text = Translate("The addition was undone.");
            NotifyTrafficLightTargetsChanged();
            ReloadTrafficLightRows();
        }

        /// <summary>The details panel for one target (a new one when its id is not in the list yet).</summary>
        private void OpenTrafficLightDetails(TrafficLightTarget target)
            => OpenTrafficLightDetails(new List<TrafficLightTarget> { target });

        private void OpenTrafficLightDetails(List<TrafficLightTarget> targets)
        {
            var accepted = new List<TrafficLightTarget>();
            int ordinal = NextTrafficLightOrdinal();
            foreach (TrafficLightTarget candidate in targets)
            {
                using var dialog = new TrafficLightTargetDialog(candidate.Clone(), TrafficLightGroups(), _config.UiLanguage,
                    TrafficLightCheckFunction(), isNew: true);
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result == null) continue;
                TrafficLightTarget result = dialog.Result;
                result.Ordinal = ordinal++;
                accepted.Add(result);
            }
            if (accepted.Count == 0) return;
            var plan = new SmartAddPlan();
            plan.Targets.AddRange(accepted);
            CommitTrafficLightAdd(plan, new List<string>());
        }
    }
}

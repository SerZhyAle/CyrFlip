using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The "Горячие клавиши" page (tickets S0056 and S0062): the master switch and the RDP option on top,
    /// then every chord CyrFlip binds in one list, whether it is really active and if not why, and a way
    /// to change, switch and reset each one without opening the owning page. The list holds no state of
    /// its own and stores no value: every edit goes through the setter the owning page already calls, so
    /// the page and the owning page are one value and neither can write the other's stale copy back.
    /// The rows are rebuilt from <see cref="ChordOverviewModel"/> on every refresh (<see cref="Reload"/> ends
    /// in <see cref="ApplyLanguage"/>), in the current language.
    /// </summary>
    internal sealed partial class SettingsForm
    {
        private readonly Label _overviewNote = new Label { AutoSize = true, UseMnemonic = false, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 4, 3, 6), Visible = false };
        private readonly Label _overviewStatus = new Label { AutoSize = true, UseMnemonic = false, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 4, 3, 6), Visible = false };
        private Button _overviewResetAll = null!;
        // The Windows group's rows show the system's own language names, the user's OS data like the Languages page.
        private FlowLayoutPanel? _overviewWindowsRows;
        private readonly Dictionary<ChordGroup, CollapsibleGroup> _overviewGroups = new Dictionary<ChordGroup, CollapsibleGroup>();
        private List<ChordOverviewRow> _overviewRows = new List<ChordOverviewRow>();
        private int _ovCaptionColumn, _ovChordColumn, _ovStateColumn;
        // What the last "reset all" did, kept so the line under the button follows a language switch.
        private ChordResetResult? _overviewLastResult;

        /// <summary>Raised after a reset rewrote chords: the context saves and rebinds the hook.</summary>
        internal event EventHandler? ChordsEdited;

        /// <summary>The rows the page was last built from - the audit-facing view the tests read.</summary>
        internal IReadOnlyList<ChordOverviewRow> OverviewRows => _overviewRows;

        private TabPage HotkeysOverviewPage()
        {
            // The master switch and the RDP option are this page's own controls now (S0062 D9): there is one
            // control and one view of it, where the overview used to carry a second switch beside the first.
            _enableHotkeys.Name = "setting.hotkeys-master";
            var controls = new List<Control>
            {
                Group("hotkeys/switches", "Main switches",
                    Setting(_enableHotkeys, "Master switch for all hotkeys. When off, CyrFlip intercepts none of the chords — keys reach the app as usual."),
                    Setting(_deferRdp, "When a remote-desktop client window (mstsc/msrdc) is focused, CyrFlip does not intercept the hotkeys — the key travels to the remote session where that machine's CyrFlip handles it. Enable this if the app runs on both ends of the RDP connection.")),
                _overviewNote,
            };
            _overviewResetAll = Button("Reset all...", AppGlyphs.Reset, ResetAllChords);
            _overviewResetAll.Name = "group.overview";
            controls.Add(Setting(_overviewResetAll, "Puts the hotkeys and their switches back to what CyrFlip ships with. Modules, table rows and scenarios are not deleted or switched off."));
            controls.Add(_overviewStatus);

            Dictionary<string, bool> saved = ParseGroupState(_config.SettingsGroupsState);
            foreach (ChordGroup group in Enum.GetValues(typeof(ChordGroup)))
            {
                CollapsibleGroup box = Group(ChordGroups.Id(group), ChordGroups.Caption(group));
                // Windows' own chords are the reference at the bottom, not the working part of the page.
                if (group == ChordGroup.Windows && !saved.ContainsKey(box.Id)) box.Expanded = false;
                _overviewGroups[group] = box;
                if (group == ChordGroup.Windows) _overviewWindowsRows = box.Body;
                controls.Add(box);
            }

            return Page("Hotkeys", "The master switch and the full list of CyrFlip hotkeys: what each one does, whether it works right now and why. A hotkey can be changed or reset here; the feature's own settings stay on its page.", controls.ToArray());
        }

        /// <summary>Rebuilds every row from the live state, in the current language.</summary>
        private void ReloadHotkeysOverview()
        {
            if (_overviewGroups.Count == 0) return;
            _overviewRows = ChordOverviewModel.Build(_config, _launcherStore.All, LanguageHotkeys.ReadAll(),
                LanguageHotkeys.ToggleCode(), _config.UiLanguage, LayoutName);
            MeasureOverviewColumns(_overviewRows);

            foreach (KeyValuePair<ChordGroup, CollapsibleGroup> pair in _overviewGroups)
            {
                FlowLayoutPanel body = pair.Value.Body;
                RowFocus focus = RowFocus.Capture(body);
                body.SuspendLayout();
                var old = new Control[body.Controls.Count];
                body.Controls.CopyTo(old, 0);
                body.Controls.Clear();
                foreach (Control control in old) control.Dispose();

                if (pair.Key == ChordGroup.Windows) body.Controls.Add(WindowsGroupNote());
                int added = 0;
                foreach (ChordOverviewRow row in _overviewRows.Where(r => r.Group == pair.Key))
                {
                    body.Controls.Add(OverviewRowControl(row, body));
                    added++;
                }
                if (added == 0 && pair.Key != ChordGroup.Windows)
                    body.Controls.Add(new Label
                    {
                        Text = Translate("There are no hotkeys here yet."), AutoSize = true,
                        ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(25, 6, 3, 4),
                    });
                body.ResumeLayout();
                focus.Restore(body, TakeRowFocusDelta());
            }

            // The master switch is read back by Reload() itself (_enableHotkeys): a reset or an import that
            // changed it under the page reaches it there, and there is no second control to keep in step.
            string note = "";
            if (!_config.EnableHotkeys) note = Translate("Hotkeys are off: no CyrFlip hotkey works right now.");
            else if (_config.DeferToRemoteDesktop)
                note = Translate("While a remote desktop client (mstsc/msrdc) has the focus, hotkeys go to the remote session.");
            _overviewNote.Text = note;
            _overviewNote.Visible = note.Length > 0;

            _overviewStatus.Text = _overviewLastResult == null ? "" : ResetResultText(_overviewLastResult);
            _overviewStatus.Visible = _overviewStatus.Text.Length > 0;
            // Never disabled: with nothing to reset the click answers so (APP-BEHAVIOUR rule 5), a greyed button would not say why.
        }

        private void MeasureOverviewColumns(List<ChordOverviewRow> rows)
        {
            _ovCaptionColumn = Column(230); _ovChordColumn = Column(170); _ovStateColumn = Column(160);
            foreach (ChordOverviewRow row in rows)
            {
                _ovCaptionColumn = Math.Max(_ovCaptionColumn, TextWidth(row.Caption, Font));
                _ovChordColumn = Math.Max(_ovChordColumn, TextWidth(row.ChordText, Font));
                _ovStateColumn = Math.Max(_ovStateColumn, TextWidth(row.StateText(Translate), Font));
            }
            _ovCaptionColumn = Math.Min(_ovCaptionColumn, Column(440));
            _ovChordColumn = Math.Min(_ovChordColumn, Column(320));
            _ovStateColumn = Math.Min(_ovStateColumn, Column(440));
        }

        private Control WindowsGroupNote()
        {
            var note = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(3, 2, 3, 4) };
            note.Controls.Add(new Label
            {
                Text = Translate("Windows handles these hotkeys, not CyrFlip: they are only shown here so you can see what is taken."),
                AutoSize = true, UseMnemonic = false, MaximumSize = new Size(Column(700), 0),
                ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 0, 3, 2),
            });
            var link = new LinkLabel { Text = Translate("Open “Windows languages”"), AutoSize = true, Margin = new Padding(3, 0, 3, 2) };
            link.LinkClicked += (_, _) => SelectPage(SettingsPageKey.WindowsLanguages);
            note.Controls.Add(link);
            return note;
        }

        private Control OverviewRowControl(ChordOverviewRow row, FlowLayoutPanel body)
        {
            var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(3, 1, 3, 1) };
            bool muted = row.State != ChordBindingState.Active;
            Color ink = muted ? ThemePalette.Light.TextMuted : Color.Empty;

            if (row.HasSwitch)
            {
                var on = new RowCheckBox { Checked = row.SwitchOn, AutoSize = true, Margin = new Padding(3, 6, 2, 2) };
                on.CheckedChanged += (_, _) => { if (!_loading) OverviewSwitch(row, on.Checked); };
                panel.Controls.Add(on);
            }
            else panel.Controls.Add(new Label { AutoSize = false, Width = 26, Height = 18 });

            Label caption = ColumnLabel(row.Caption, _ovCaptionColumn);
            Label chord = ColumnLabel(row.ChordText, _ovChordColumn, ellipsis: false);
            if (muted) { caption.ForeColor = ink; chord.ForeColor = ink; }
            panel.Controls.Add(caption);
            panel.Controls.Add(chord);

            string stateText = row.StateText(Translate);
            Label state;
            // "Off by module" and "incomplete" both have a page that answers them: the module's, or the table's.
            if ((row.State == ChordBindingState.OffByModule || row.State == ChordBindingState.Incomplete) && row.ModulePage.HasValue)
            {
                SettingsPageKey page = row.ModulePage.Value;
                var link = new LinkLabel
                {
                    Text = stateText, AutoSize = false, Width = _ovStateColumn, Height = RowHeight,
                    TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = new Padding(3, 4, 3, 4),
                };
                link.LinkClicked += (_, _) => SelectPage(page);
                state = link;
            }
            else
            {
                state = ColumnLabel(stateText, _ovStateColumn);
                if (row.ClashWith != null) state.ForeColor = ThemePalette.Light.Warning;
                else if (muted) state.ForeColor = ink;
            }
            panel.Controls.Add(state);

            if (!row.ReadOnly)
            {
                panel.Controls.Add(Button(Translate("Change..."), AppGlyphs.Edit, () => OverviewChange(row)));
                Button reset = Button(Translate(row.ResetRestoresDefault ? "Set default" : "Clear"), row.ResetRestoresDefault ? AppGlyphs.Reset : AppGlyphs.ClearInput, () => OverviewReset(row));
                reset.Enabled = row.CanReset;
                panel.Controls.Add(reset);
            }

            NameRow(panel, row.Caption + " " + row.ChordText);
            foreach (Control control in panel.Controls)
                if (control is ButtonBase) control.KeyDown += (_, e) => OverviewKey(row, body, panel, control, e);
            return panel;
        }

        /// <summary>
        /// The keyboard route (INPUT-PARITY): ↑/↓ move between rows on the same column, Delete clears a
        /// chord that can be left empty. Enter and Space are the buttons' and the switch's own.
        /// </summary>
        private void OverviewKey(ChordOverviewRow row, FlowLayoutPanel body, Control panel, Control control, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                int index = body.Controls.IndexOf(panel);
                Control? target = RowFocus.Find(body, index + (e.KeyCode == Keys.Down ? 1 : -1), panel.Controls.IndexOf(control));
                if (target != null && !ReferenceEquals(target.Parent, panel)) { target.Focus(); e.Handled = true; e.SuppressKeyPress = true; }
            }
            else if (e.KeyCode == Keys.Delete && row.CanClear)
            {
                e.Handled = true; e.SuppressKeyPress = true;
                OverviewReset(row);
            }
        }

        // ---- change ---------------------------------------------------------------------------------

        private void OverviewChange(ChordOverviewRow row)
        {
            if (row.IsMouse) { ChangeMouseChord(); return; }
            switch (row.Kind)
            {
                case ChordKind.Case: _setCaseHotkey(); break;
                case ChordKind.History: _setHistoryHotkey(); break;
                case ChordKind.QuickNotes: _setQuickNotesHotkey(); break;
                case ChordKind.Screenshot: ScreenshotHotkeyChangeRequested?.Invoke(this, EventArgs.Empty); break;
                case ChordKind.ScreenshotFolder: ScreenshotFolderHotkeyChangeRequested?.Invoke(this, EventArgs.Empty); break;
                case ChordKind.TextMenu: TextMenuHotkeyChangeRequested?.Invoke(this, EventArgs.Empty); break;
                case ChordKind.Symbols: SymbolsHotkeyChangeRequested?.Invoke(this, EventArgs.Empty); break;
                case ChordKind.TrafficLight: TrafficLightHotkeyChangeRequested?.Invoke(this, EventArgs.Empty); break;
                case ChordKind.Conversion:
                case ChordKind.Translation:
                case ChordKind.Launcher:
                    ChangeRowChord(row); break;
            }
        }

        /// <summary>A table row's or a scenario's chord: the capture dialog, the one ownership check, the owner's own notification.</summary>
        private void ChangeRowChord(ChordOverviewRow row)
        {
            string start = Hotkey.TryParse(row.StoredChord, out Hotkey current) ? current.Display : row.StoredChord;
            using var dialog = new HotkeyDialog(start, Localization.Format(Translate, "Hotkey: {0}", row.Caption), _config.UiLanguage);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.CapturedHotkey == null) return;
            if (!Hotkey.TryParse(dialog.CapturedHotkey, out Hotkey next) || !ChordIsFree(next, row.Kind!.Value, row.Id)) return;

            switch (row.Kind)
            {
                case ChordKind.Conversion when row.Source is LayoutConversionProfile conversion:
                    conversion.Hotkey = next.Display;
                    ConversionProfilesChanged?.Invoke(this, EventArgs.Empty);
                    break;
                case ChordKind.Translation when row.Source is TranslationProfile translation:
                    translation.Hotkey = next.Display;
                    NotifyTranslationChanged();
                    break;
                case ChordKind.Launcher when row.Source is LauncherScenario scenario:
                    LauncherScenario edited = scenario.Clone();
                    edited.Hotkey = next.Display;
                    if (!ReportScenarioSave(_launcherStore.Update(edited))) return;
                    LauncherNotifyChanged();
                    break;
            }
            Reload();
        }

        /// <summary>The mouse chord is a fixed list of six (a bare right click would eat every menu in Windows): a menu, not a capture.</summary>
        private void ChangeMouseChord()
        {
            string current = MouseChord.Parse(_config.ContextMenuChord).Token;
            var menu = new ContextMenuStrip();
            foreach (string token in MouseChord.Choices)
            {
                string chosen = token;
                var item = new ToolStripMenuItem(MouseChord.Parse(token).Display(Translate)) { Checked = token == current };
                item.Click += (_, _) => TextMenuSettingChanged(() => _config.ContextMenuChord = chosen, reload: true);
                menu.Items.Add(item);
            }
            menu.Closed += (_, _) => BeginInvoke(new Action(() => menu.Dispose()));
            menu.Show(Cursor.Position);
        }

        // ---- switch -----------------------------------------------------------------------------------

        private void OverviewSwitch(ChordOverviewRow row, bool on)
        {
            switch (row.Kind)
            {
                case ChordKind.Case: Changed(_setCaseEnabled, on); break;
                case ChordKind.History: Changed(_setHistoryEnabled, on); break;
                case ChordKind.QuickNotes: QuickNotesSettingChanged(() => _config.EnableQuickNotesHotkey = on, reload: true); break;
                case ChordKind.Symbols: SymbolsSettingChanged(() => _config.EnableSymbolsHotkey = on, reload: true); break;
                case ChordKind.TrafficLight: TrafficLightSettingChanged(() => _config.EnableTrafficLightHotkey = on, reload: true); break;
                case ChordKind.Screenshot: GraphicsSettingChanged(() => _config.EnableScreenshotHotkey = on, reload: true); break;
                case ChordKind.ScreenshotFolder: GraphicsSettingChanged(() => _config.EnableScreenshotFolderHotkey = on, reload: true); break;
                case ChordKind.TextMenu: TextMenuSettingChanged(() => _config.EnableTextMenuHotkey = on, reload: true); break;
                case ChordKind.Conversion when row.Source is LayoutConversionProfile conversion:
                    // Switching a row back on is checked like assigning it (KC-6), as on the table's own page.
                    if (on && Hotkey.TryParse(conversion.Hotkey, out Hotkey conversionChord)
                        && !ChordIsFree(conversionChord, ChordKind.Conversion, conversion.Id, askAboutWindows: false))
                    { Reload(); break; }
                    conversion.Enabled = on;
                    ConversionProfilesChanged?.Invoke(this, EventArgs.Empty);
                    Reload();
                    break;
                case ChordKind.Translation when row.Source is TranslationProfile translation:
                    if (on && Hotkey.TryParse(translation.Hotkey, out Hotkey translationChord)
                        && !ChordIsFree(translationChord, ChordKind.Translation, translation.Id, askAboutWindows: false))
                    { Reload(); break; }
                    translation.Enabled = on;
                    NotifyTranslationChanged();
                    Reload();
                    break;
            }
        }

        // ---- reset ------------------------------------------------------------------------------------

        /// <summary>One row, one visible edit: applied at once, no confirmation (R6).</summary>
        private void OverviewReset(ChordOverviewRow row)
        {
            ChordResetPlan plan = ChordResetPlan.ForRow(_config, row, _launcherStore.All, LanguageHotkeys.ReadAll(),
                _config.UiLanguage, LayoutName);
            if (plan.Steps.Count == 0)
            {
                foreach (ChordResetSkip skip in plan.Skipped) Warn(SkipText(skip));
                return;
            }
            // Windows' owners are asked about, never refused (R5).
            if (plan.WindowsHolds.Count > 0 && ConfirmDialog.Show(this, _config.UiLanguage,
                    string.Join("\n", plan.WindowsHolds.Select(WindowsHoldText)),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, danger: true) != DialogResult.Yes)
                return;
            ApplyResetPlan(plan, showResult: false);
        }

        private void ResetAllChords()
        {
            List<ChordOverviewRow> rows = ChordOverviewModel.Build(_config, _launcherStore.All, LanguageHotkeys.ReadAll(),
                LanguageHotkeys.ToggleCode(), _config.UiLanguage, LayoutName);
            ChordResetPlan plan = ChordResetPlan.ForAll(_config, rows, _launcherStore.All, LanguageHotkeys.ReadAll(),
                _config.UiLanguage, LayoutName);

            // Nothing to do is an answer, never a question (APP-BEHAVIOUR rule 5).
            if (plan.IsEmpty)
            {
                string none = plan.Skipped.Count == 0
                    ? Translate("Hotkeys are already as they were when installed.")
                    : string.Join("\n", plan.Skipped.Select(SkipText));
                ConfirmDialog.Show(this, _config.UiLanguage, none, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (ConfirmDialog.Show(this, _config.UiLanguage, ResetPlanText(plan), MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning, danger: plan.ChangesChords) != DialogResult.Yes)
                return;
            ApplyResetPlan(plan, showResult: true);
        }

        private void ApplyResetPlan(ChordResetPlan plan, bool showResult)
        {
            ChordResetResult result = plan.Apply(_config, _launcherStore.All, LanguageHotkeys.ReadAll(),
                _config.UiLanguage, LayoutName, scenario => { Exception? failure = _launcherStore.Update(scenario); if (failure != null) throw failure; });
            _overviewLastResult = showResult ? result : null;
            if (result.Applied.Count > 0 || result.MasterRestored)
            {
                ChordsEdited?.Invoke(this, EventArgs.Empty);
                if (result.ScenariosChanged) LauncherNotifyChanged();
            }
            Reload();
            if (!showResult)
                foreach (ChordResetSkip skip in result.Skipped) Warn(SkipText(skip));
        }

        /// <summary>The confirmation is built from the plan itself: counts, the rows that cannot be reset, Windows' holds.</summary>
        private string ResetPlanText(ChordResetPlan plan)
        {
            var lines = new List<string> { Translate("Put the hotkeys back to what CyrFlip ships with?") };
            lines.Add(Localization.Format(Translate, "Will change: {0}. Already as installed: {1}.",
                plan.Steps.Count, plan.AlreadyDefault));
            if (plan.Steps.Any(step => step.Clears))
                lines.Add(Translate("Table rows and scenarios are not deleted; those that shipped without a hotkey will have theirs cleared."));
            if (plan.RestoreMaster)
                lines.Add(Translate("The master hotkey switch will be turned on."));
            if (plan.Skipped.Count > 0)
            {
                lines.Add(Translate("Will not be changed:"));
                lines.AddRange(plan.Skipped.Select(SkipText));
            }
            lines.AddRange(plan.WindowsHolds.Select(WindowsHoldText));
            return string.Join("\n", lines);
        }

        private string SkipText(ChordResetSkip skip)
        {
            switch (skip.Reason)
            {
                case ChordSkipReason.SaveFailed:
                    return skip.Caption + ": " + skip.HeldBy;
                case ChordSkipReason.Taken:
                    return Localization.Format(Translate, "“{0}”: the hotkey {1} is taken by “{2}”.", skip.Caption, skip.Chord, skip.HeldBy ?? "");
                case ChordSkipReason.Changed:
                    return Localization.Format(Translate, "“{0}”: the row changed during the reset, so it was left alone.", skip.Caption);
                default:
                    return Localization.Format(Translate, "“{0}”: the row no longer exists.", skip.Caption);
            }
        }

        private string WindowsHoldText(KeyValuePair<string, string> hold)
            => Localization.Format(Translate, "“{0}”: the shipped hotkey is used by Windows (language “{1}”). If you continue, Windows will no longer receive it.", hold.Key, hold.Value);

        private string ResetResultText(ChordResetResult result)
        {
            string text = Localization.Format(Translate, "Reset: {0}. Not reset: {1}.", result.Applied.Count, result.Skipped.Count);
            if (result.MasterRestored) text += " " + Translate("The master hotkey switch is on again.");
            foreach (ChordResetSkip skip in result.Skipped) text += "\n" + SkipText(skip);
            return text;
        }
    }
}

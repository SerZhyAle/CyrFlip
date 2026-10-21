using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// Why a control is unavailable and how to make it available (WINDOWS-UI section 3 rule 6, ticket S0060
    /// M5). A greyed control used to say nothing: the master switch was somewhere else on the page - or on
    /// another page - and the only signal was a pale caption. This is the "dependency pass": one declarative
    /// table (<see cref="BuildDependencyNotes"/>) of which controls a switch (or a missing backup) takes away,
    /// and one method (<see cref="ApplyDependencyNotes"/>) that, after every refresh, shows a muted one-line
    /// note next to each row that is really unavailable and hides it when the row is available again.
    ///
    /// <para>Three decisions carry it. <b>The note is a label of its own</b>, never the row's hint rewritten:
    /// the hint is a registered source caption the language pass owns, and a note written into it would be
    /// captured as a "source" in a translated form. <b>It is a sibling of the row, not a child of it</b>: a
    /// module's rows are disabled as blocks, and a label inside a disabled block is drawn disabled - the one
    /// sentence that explains the greying must not be the palest thing on the page (WINDOWS-UI section 6 rule
    /// 4). <b>It is derived, never stored</b>: whether it shows is read from the controls' own
    /// <c>Enabled</c> and the master's state at the end of the language pass, so the greying code and the
    /// explanation cannot disagree - a control is explained exactly when it is greyed for that reason.</para>
    ///
    /// <para>A screen reader reaches the reason too: every editor of a row that shows a note carries the same
    /// sentence as its accessible description, and loses it when the row is available.</para>
    /// </summary>
    internal sealed partial class SettingsForm
    {
        /// <summary>The <see cref="Control.Tag"/> of a dependency note, so the search index and the width clamp can tell it from a row.</summary>
        internal const string DependencyNoteTag = "dependency-note";

        /// <summary>One row's explanation: the controls it greys, the sentence, and the label that carries it.</summary>
        private sealed class DependencyRule
        {
            public DependencyRule(Control row, Label? hint, Func<string?> reason)
            {
                Row = row;
                Hint = hint;
                Reason = reason;
                Note = new Label
                {
                    AutoSize = true,
                    UseMnemonic = false,
                    Visible = false,
                    MaximumSize = new Size(890, 0),
                    ForeColor = ThemePalette.Light.TextMuted,
                    Margin = new Padding(row.Margin.Left + (hint?.Margin.Left ?? 3), 0, 3, 6),
                    Tag = DependencyNoteTag,
                };
            }

            /// <summary>The row block the note stands beside: a direct child of a group's body (or of a flat page's panel).</summary>
            public Control Row { get; set; }
            /// <summary>The row's own hint, when it has one - the note then sits under it and takes its inset.</summary>
            public Label? Hint { get; }
            /// <summary>The sentence, or null while the rule does not apply (the switch is on).</summary>
            public Func<string?> Reason { get; }
            public List<Control> Dependents { get; } = new List<Control>();
            public Label Note { get; }
            /// <summary>The sentence last put on the note ("" = hidden). Remembered here, not read back from
            /// <see cref="Control.Visible"/>: that is false for every note on a page that is not on screen, so
            /// reading it would make each pass re-lay-out the whole window for nothing.</summary>
            public string Applied { get; set; } = "";
        }

        private readonly List<DependencyRule> _dependencyRules = new List<DependencyRule>();

        /// <summary>The notes the window carries - the audit-facing view the conformance tests read.</summary>
        internal IReadOnlyList<Label> DependencyNotes => _dependencyRules.Select(rule => rule.Note).ToList();

        /// <summary>Every control a note stands for, with the note that explains it, for the tests.</summary>
        internal IEnumerable<KeyValuePair<Control, Label>> DependencyTable
            => _dependencyRules.SelectMany(rule => rule.Dependents.Select(d => new KeyValuePair<Control, Label>(d, rule.Note)));

        // ---- the table ------------------------------------------------------------------------------

        /// <summary>
        /// The one place that says what takes what away. Called once, after every page is built and
        /// flattened, because a note is placed by the row its control ended up in.
        /// </summary>
        private void BuildDependencyNotes()
        {
            // The clipboard history: its own switch greys the pause, the window memory and the opacity.
            WhileOff(_history, _pause, _historyStartup, _opacity);
            // The master hotkey switch greys the two chords that live on other pages than itself.
            WhileOff(_enableHotkeys, _caseEnabled, _historyEnabled);
            // The text menu is a mouse chord: the keyboard master does not gate it, its own switch does.
            WhileOff(_contextMenuEnabled, _contextMenuChord, _textMenuHotkeyEnabled);
            // Saving captures to a folder: the folder row and the chord that opens the folder.
            WhileOff(_screenshotSave, _screenshotFolderControls.ToArray());
            // The translator: every setting, button and table row of the page, but not the busy "Cancel" -
            // that one is live only while an operation runs and is explained by the status line.
            var translator = new List<Control>
            {
                _translateAutoStart, _translateCopy, _translatePaste, _translateShowSource,
                _translateEndpoint, _translateModel, _translateKeepAlive, _translateTimeout, _translateWindowTimeout,
                _translationRows,
            };
            translator.AddRange(_translateButtons);
            WhileOff(_translateEnabled, translator.ToArray());
            WhileOff(_quickNotesEnabled, _quickNotesControls.ToArray());
            WhileOff(_symbolsEnabled, _symbolsControls.ToArray());
            WhileOff(_tlEnabled, _tlControls.ToArray());
            var launcher = new List<Control> { _launcherList, _launcherSearch };
            launcher.AddRange(_launcherButtons);
            WhileOff(_launcherEnabled, launcher.ToArray());
            WhileOff(_exchangeClipboardEnabled, _exchangeControls.ToArray());
            // The three restore buttons: nothing was saved yet.
            NothingToRestore(_restoreSettings);
            NothingToRestore(_restoreLayouts);
            NothingToRestore(_restoreLanguageHotkeys);

            PlaceDependencyNotes();
        }

        /// <summary>These controls are unavailable while <paramref name="master"/> is off.</summary>
        private void WhileOff(CheckBox master, params Control[] dependents)
        {
            // The master's own caption is what the user sees on the switch, in the language on screen. The
            // literals are written at the call, so the source gate sees them as registered keys.
            Func<string?> reason = () => master.Checked ? null
                : Localization.Format(Translate, "Unavailable while «{0}» is off - turn that switch on first.", master.Text);
            foreach (Control dependent in dependents) Depend(dependent, master, reason);
        }

        /// <summary>The button is unavailable because the state it would restore has not been saved yet.</summary>
        private void NothingToRestore(Button button)
            => Depend(button, button, () => Translate("Nothing to restore yet: CyrFlip saves the original state just before the first change."));

        /// <summary>
        /// Registers one dependent under its row. A row with a hint of its own gets its own note under it;
        /// the unhinted rows of one group that wait on the same cause share one note, shown above the first
        /// of them - a table and its buttons are one fact, not five.
        /// </summary>
        private void Depend(Control dependent, object cause, Func<string?> reason)
        {
            // A prose label is greyed with its row but is no editor: it has nothing to explain.
            if (dependent is Label) return;
            Control? row = RowBlockOf(dependent);
            if (row == null) return;
            Label? hint = HintWithin(row);

            DependencyRule? rule = null;
            if (hint != null)
                rule = _dependencyRules.FirstOrDefault(r => ReferenceEquals(r.Row, row) && r.Hint != null);
            else
                rule = _dependencyRules.FirstOrDefault(r => r.Hint == null && ReferenceEquals(r.Row.Parent, row.Parent)
                    && ReferenceEquals(CauseOf(r), cause));
            if (rule == null)
            {
                rule = new DependencyRule(row, hint, reason);
                _dependencyRules.Add(rule);
                _ruleCauses[rule] = cause;
            }
            else if (rule.Hint == null && row.Parent!.Controls.GetChildIndex(row) < rule.Row.Parent!.Controls.GetChildIndex(rule.Row))
                rule.Row = row;   // the note goes above the first of the rows it covers
            if (!rule.Dependents.Contains(dependent)) rule.Dependents.Add(dependent);
        }

        private readonly Dictionary<DependencyRule, object> _ruleCauses = new Dictionary<DependencyRule, object>();

        private object? CauseOf(DependencyRule rule) => _ruleCauses.TryGetValue(rule, out object? cause) ? cause : null;

        /// <summary>
        /// The direct child of a group's body - or of a flat page's panel - that holds the control: the row.
        /// Null for a control that is not built into a page (yet).
        /// </summary>
        private static Control? RowBlockOf(Control control)
        {
            for (Control current = control; current.Parent != null; current = current.Parent)
            {
                Control parent = current.Parent;
                if (parent.Parent is CollapsibleGroup group && ReferenceEquals(group.Body, parent)) return current;
                if (parent.Parent is TabPage) return current;
            }
            return null;
        }

        /// <summary>The hint of the setting built inside this row, if the row was made by <c>Setting</c> or <c>HotkeyRow</c>.</summary>
        private Label? HintWithin(Control row)
        {
            foreach (KeyValuePair<Control, Label> pair in _rowHints)
            {
                if (ReferenceEquals(pair.Key, row)) return pair.Value;
                for (Control? up = pair.Key.Parent; up != null; up = up.Parent)
                    if (ReferenceEquals(up, row)) return pair.Value;
            }
            return null;
        }

        /// <summary>Puts every note in the tree: under its row's hint, or above the first row it covers.</summary>
        private void PlaceDependencyNotes()
        {
            foreach (DependencyRule rule in _dependencyRules)
            {
                Control? parent = rule.Row.Parent;
                if (parent == null) continue;
                parent.Controls.Add(rule.Note);
                int at = parent.Controls.GetChildIndex(rule.Row);
                parent.Controls.SetChildIndex(rule.Note, rule.Hint != null ? at + 1 : at);
            }
        }

        // ---- the pass -------------------------------------------------------------------------------

        /// <summary>
        /// Shows the note next to every row whose controls are greyed for the cause the table names, and
        /// hides it again when they are not - read from the controls' own state, so it can only run after the
        /// refresh that set them. A note is shown only while the cause holds <i>and</i> a control of the row is
        /// really unavailable: a control that is off for another reason (a running operation) is not explained
        /// by a switch that is on.
        /// </summary>
        private void ApplyDependencyNotes()
        {
            if (_dependencyRules.Count == 0) return;
            var wanted = new List<KeyValuePair<DependencyRule, string>>(_dependencyRules.Count);
            bool layoutChanges = false;
            foreach (DependencyRule rule in _dependencyRules)
            {
                string? reason = rule.Reason();
                bool unavailable = reason != null && rule.Dependents.Any(d => !d.Enabled);
                string text = unavailable ? reason! : "";
                wanted.Add(new KeyValuePair<DependencyRule, string>(rule, text));
                // Text, or the inset under the hint that the font pass has just moved.
                if (rule.Applied != text || rule.Note.Margin.Left != InsetOf(rule)) layoutChanges = true;
            }

            if (layoutChanges)
            {
                // One layout for all of it (WINDOWS-UI section 5): a note's visibility and text change the
                // size of every auto-sized container above it.
                SuspendLayoutDeep(this);
                try
                {
                    foreach (KeyValuePair<DependencyRule, string> pair in wanted)
                    {
                        Label note = pair.Key.Note;
                        note.Text = pair.Value;
                        note.Visible = pair.Value.Length > 0;
                        pair.Key.Applied = pair.Value;
                        int inset = InsetOf(pair.Key);
                        if (note.Margin.Left != inset) note.Margin = new Padding(inset, 0, 3, 6);
                    }
                }
                finally { ResumeLayoutDeep(this); }
                ClampPageTextWidths();
            }

            // The accessible description is not layout: a reader finds the reason on the control itself.
            foreach (KeyValuePair<DependencyRule, string> pair in wanted)
                foreach (Control dependent in pair.Key.Dependents)
                    foreach (Control editor in EditorsOf(dependent))
                    {
                        string? description = pair.Value.Length > 0 ? pair.Value : null;
                        if (editor.AccessibleDescription != description) editor.AccessibleDescription = description;
                    }
        }

        /// <summary>
        /// True while a dependency note explains why this editor is greyed. The reason is then its accessible
        /// description, and the row's hint must not replace it: a reader that hears what the switch does but not
        /// why it cannot be used has lost the one sentence this pass exists to carry.
        /// </summary>
        private bool IsExplainedByDependencyNote(Control editor)
        {
            foreach (DependencyRule rule in _dependencyRules)
            {
                if (rule.Applied.Length == 0) continue;
                foreach (Control dependent in rule.Dependents)
                    foreach (Control candidate in EditorsOf(dependent))
                        if (ReferenceEquals(candidate, editor)) return true;
            }
            return false;
        }

        /// <summary>The note's left inset: the hint's (re-measured with the font), the row's own margin on top.</summary>
        private static int InsetOf(DependencyRule rule) => rule.Row.Margin.Left + (rule.Hint?.Margin.Left ?? 3);

        /// <summary>The controls a screen reader lands on inside a dependent: itself, or every editor and command under a block.</summary>
        private static IEnumerable<Control> EditorsOf(Control control)
        {
            if (control is ButtonBase || control is ComboBox || control is NumericUpDown || control is TrackBar
                || control is TextBoxBase || control is ListView)
            {
                yield return control;
                yield break;
            }
            foreach (Control child in control.Controls)
                foreach (Control editor in EditorsOf(child))
                    yield return editor;
        }
    }
}

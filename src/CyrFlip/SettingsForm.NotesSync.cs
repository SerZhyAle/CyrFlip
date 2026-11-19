using System;
using System.Drawing;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The notes sync group of the quick-notes page (ticket S0105, <c>DEVICE-EXCHANGE</c> 1.13 item FK): the option of
    /// its own, off by default; the one account entry the notes go to; "Sync now"; and one status line the context
    /// fills. Like every page here it does no I/O of its own - the status is a function the context hands in.
    /// </summary>
    internal sealed partial class SettingsForm
    {
        private readonly CheckBox _quickNotesSync = Check("Sync the notes with my other devices");
        private readonly ComboBox _quickNotesSyncEntry = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340 };
        private readonly Label _quickNotesSyncStatus = new Label
        {
            AutoSize = true, UseMnemonic = false, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted,
            Margin = new Padding(3, 4, 3, 6),
        };
        private bool _quickNotesSyncFilling;

        /// <summary>The person pressed "Sync now".</summary>
        internal event EventHandler? QuickNotesSyncNowRequested;

        /// <summary>The line under the group, in the interface language; null shows nothing. Set by the context.</summary>
        internal Func<string?>? QuickNotesSyncStatusText { get; set; }

        /// <summary>One entry of the picker: the account entry's local id and how a person reads it.</summary>
        private sealed class SyncEntryItem
        {
            public SyncEntryItem(string id, string label) { Id = id; Label = label; }
            public string Id { get; }
            public string Label { get; }
            public override string ToString() => Label;
        }

        private Control QuickNotesSyncGroup()
        {
            _quickNotesSync.Name = "setting.quick-notes-sync";
            _quickNotesSyncEntry.Name = "setting.quick-notes-sync-entry";
            _quickNotesSync.CheckedChanged += (_, _) =>
                QuickNotesSettingChanged(() => _config.QuickNotesSync = _quickNotesSync.Checked, reload: true);
            _quickNotesSyncEntry.SelectedIndexChanged += (_, _) =>
            {
                if (_quickNotesSyncFilling || !(_quickNotesSyncEntry.SelectedItem is SyncEntryItem item)) return;
                QuickNotesSettingChanged(() => _config.QuickNotesSyncEntry = item.Id, reload: true);
            };

            var entryRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(3, 5, 3, 5) };
            entryRow.Controls.Add(new Label { Text = "Account for the notes:", AutoSize = true, Tag = "caption", Padding = new Padding(0, 7, 0, 0) });
            entryRow.Controls.Add(_quickNotesSyncEntry);

            Button syncNow = Button("Sync now", () => QuickNotesSyncNowRequested?.Invoke(this, EventArgs.Empty));
            syncNow.Name = "setting.quick-notes-sync-now";

            return Group("notes/sync", "Sync with your other devices",
                Track(Setting(_quickNotesSync,
                    "Off by default. When on, the notes go through the exchange server to your other devices of the same account and come back from them. They are encrypted on this computer for your own devices: whoever runs the server sees only how many notes there are, their sizes and when they changed. Turning it off stops the sync and deletes nothing, here or on the server.")),
                Track(Setting(entryRow,
                    "The notes sync with one account only. Add an account on the \"Exchange server\" page.")),
                Track(Setting(syncNow,
                    "Sends what changed on this computer and brings what changed on your other devices. The sync also runs on its own a few seconds after you change a note.")),
                _quickNotesSyncStatus);
        }

        /// <summary>Mirror the sync option, the entry picker and the status line (part of the quick-notes refresh).</summary>
        private void ReloadQuickNotesSyncState()
        {
            _quickNotesSync.Checked = _config.QuickNotesSync;

            _quickNotesSyncFilling = true;
            try
            {
                _quickNotesSyncEntry.Items.Clear();
                ExchangeAccountEntry? chosen = NotesSyncGate.Entry(_config);
                foreach (ExchangeAccountEntry entry in NotesSyncGate.Candidates(_config))
                {
                    var item = new SyncEntryItem(entry.Id, ExchangeClipboardService.LabelOf(entry));
                    _quickNotesSyncEntry.Items.Add(item);
                    if (chosen != null && chosen.Id == entry.Id) _quickNotesSyncEntry.SelectedItem = item;
                }
                _quickNotesSyncEntry.Enabled = _config.EnableQuickNotes && _quickNotesSyncEntry.Items.Count > 1;
            }
            finally { _quickNotesSyncFilling = false; }

            RefreshQuickNotesSyncStatus();
        }

        /// <summary>Re-read the status line; the context calls it when a sync pass ends.</summary>
        internal void RefreshQuickNotesSyncStatus()
        {
            string? text = QuickNotesSyncStatusText?.Invoke();
            _quickNotesSyncStatus.Text = text ?? "";
            _quickNotesSyncStatus.Visible = !string.IsNullOrEmpty(text);
        }
    }
}

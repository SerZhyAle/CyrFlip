using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The exchange server page (ticket S0074): what the feature is, the module switch, the accounts this device is
    /// signed in to (add, sign in again, remove), the two chords and desktop shortcuts, and the folder received files
    /// land in. The page says what the thing is in its own words - the portal link is an addition, never the carrier.
    /// </summary>
    internal sealed partial class SettingsForm
    {
        private readonly CheckBox _exchangeClipboardEnabled = Check("Enable the clipboard on the server");
        private readonly CheckBox _exchangeClipboardSendHotkeyEnabled = Check("Send clipboard to server");
        private readonly Label _exchangeClipboardSendHotkeyValue = new Label { AutoSize = true };
        private readonly CheckBox _exchangeClipboardGetHotkeyEnabled = Check("Get clipboard from server");
        private readonly Label _exchangeClipboardGetHotkeyValue = new Label { AutoSize = true };
        private readonly TextBox _exchangeClipboardReceivedFolderBox = new TextBox { Width = 340, ReadOnly = true };
        private readonly FlowLayoutPanel _exchangeAccountsList = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(3, 4, 3, 4)
        };
        private readonly List<Control> _exchangeControls = new List<Control>();

        public event EventHandler? ExchangeClipboardChanged;
        public event EventHandler? ExchangeClipboardSendHotkeyChangeRequested;
        public event EventHandler? ExchangeClipboardGetHotkeyChangeRequested;
        public event EventHandler? ExchangeClipboardFolderOpenRequested;

        private void ExchangeClipboardSettingChanged(Action apply, bool reload = false)
        {
            if (_loading) return;
            apply();
            ExchangeClipboardChanged?.Invoke(this, EventArgs.Empty);
            if (reload) Reload();
        }

        private Control TrackExchange(Control control)
        {
            _exchangeControls.Add(control);
            return control;
        }

        private void WireExchangeServer()
        {
            _exchangeClipboardEnabled.Name = "setting.exchange-clipboard";
            _exchangeClipboardSendHotkeyEnabled.Name = "setting.exchange-clipboard-send-chord";
            _exchangeClipboardGetHotkeyEnabled.Name = "setting.exchange-clipboard-get-chord";
            _exchangeClipboardReceivedFolderBox.Name = "setting.exchange-clipboard-received-folder";

            _exchangeClipboardEnabled.CheckedChanged += (_, _) =>
                ExchangeClipboardSettingChanged(() => _config.EnableExchangeClipboard = _exchangeClipboardEnabled.Checked, reload: true);
            _exchangeClipboardSendHotkeyEnabled.CheckedChanged += (_, _) =>
                ExchangeClipboardSettingChanged(() => _config.EnableExchangeClipboardSendHotkey = _exchangeClipboardSendHotkeyEnabled.Checked);
            _exchangeClipboardGetHotkeyEnabled.CheckedChanged += (_, _) =>
                ExchangeClipboardSettingChanged(() => _config.EnableExchangeClipboardGetHotkey = _exchangeClipboardGetHotkeyEnabled.Checked);
        }

        private TabPage ExchangeServerPage()
        {
            WireExchangeServer();

            var page = Page("Exchange server", "Pass what you copied to your other devices through an exchange server of your own.");
            FlowLayoutPanel panel = ContentPanel(page);

            // What it is, before anything can be switched on. The words of the honest text are the contract's
            // (DEVICE-EXCHANGE item DE), shown whole; the portal link adds to them.
            panel.Controls.Add(Group("exchange/about", "What this is",
                Note("Your other devices get one thing you copied: a text, a picture or one file."),
                Note("It goes through an exchange server that you or someone you trust runs."),
                Note("The server keeps only the last value, in its memory, for your account alone; a restart loses it."),
                Note("One object at a time: put several files or a folder into one archive first."),
                Note("What is sent is exactly what is on the clipboard when you press the button."),
                Note("A password you copied a minute ago goes too. There is no preview and no confirmation."),
                Note(ExchangeMessages.HonestText),
                PortalLink("About the clipboard on the server", ExchangePortalPage.Clipboard),
                PortalLink("About the exchange server", ExchangePortalPage.Server)));

            panel.Controls.Add(Group("exchange/module", "Main switches",
                Setting(_exchangeClipboardEnabled,
                    "Off by default: while off, CyrFlip opens no connection, and the tray items, shortcuts and chords are unavailable.")));

            panel.Controls.Add(Group("exchange/accounts", "Accounts",
                Note("CyrFlip only signs in to an existing account. To get one, ask whoever runs the server for an invitation."),
                TrackExchange(Button("Add account..", AppGlyphs.Add, AddExchangeAccount)),
                _exchangeAccountsList));

            var shortcutRows = new List<Control>
            {
                TrackExchange(HotkeyRow(_exchangeClipboardSendHotkeyEnabled, _exchangeClipboardSendHotkeyValue,
                    () => ExchangeClipboardSendHotkeyChangeRequested?.Invoke(this, EventArgs.Empty),
                    "Sends what the clipboard holds now to the server of every account above. Shipped without a combination: choose one.")),
                TrackExchange(HotkeyRow(_exchangeClipboardGetHotkeyEnabled, _exchangeClipboardGetHotkeyValue,
                    () => ExchangeClipboardGetHotkeyChangeRequested?.Invoke(this, EventArgs.Empty),
                    "Takes the newest value from the servers of your accounts and puts it on the clipboard, ready to paste.")),
            };
            if (!AppShortcuts.ManagedByWindows)
            {
                shortcutRows.Add(TrackExchange(Setting(Button("Put \"Send\" on the desktop", AppGlyphs.SendClipboard, () => CreateExchangeShortcut(ExchangeShortcutKind.Send)),
                    "A desktop shortcut runs the action by itself. Delete the file to remove it.")));
                shortcutRows.Add(TrackExchange(Setting(Button("Put \"Get\" on the desktop", AppGlyphs.ReceiveClipboard, () => CreateExchangeShortcut(ExchangeShortcutKind.Get)),
                    "The same for getting the clipboard from the server.")));
            }
            panel.Controls.Add(Group("exchange/shortcuts", "Shortcuts", shortcutRows.ToArray()));

            panel.Controls.Add(Group("exchange/folder", "Received files", ExchangeFolderRow()));

            return page;
        }

        /// <summary>A muted paragraph of a group: a note, wrapped at the page's text width.</summary>
        private static Label Note(string english) => new Label
        {
            Text = english, AutoSize = true, UseMnemonic = false, MaximumSize = new Size(890, 0),
            ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 4, 3, 4),
        };

        /// <summary>A link to a page of the exchange portal, in the interface language when the portal has it.</summary>
        private LinkLabel PortalLink(string english, ExchangePortalPage page)
        {
            var link = new LinkLabel { Text = english, AutoSize = true, Margin = new Padding(3, 4, 3, 4) };
            link.LinkClicked += (_, _) => OpenAddress(ExchangePortalLinks.ForLanguage(page, _config.UiLanguage));
            return link;
        }

        private Control ExchangeFolderRow()
        {
            var folderCaption = new Label { Text = "Folder for files received from the server", AutoSize = true, Tag = "caption" };
            var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
            Button choose = Button("Choose..", AppGlyphs.ChooseFolder, ChooseExchangeClipboardFolder);
            Button open = Button("Open folder", AppGlyphs.Folder, OpenExchangeClipboardFolder);
            buttons.Controls.Add(choose);
            buttons.Controls.Add(open);

            var strip = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(3, 5, 3, 5) };
            strip.Controls.Add(_exchangeClipboardReceivedFolderBox);
            strip.Controls.Add(buttons);

            Control row = CaptionedSetting(strip, folderCaption,
                "A received file is saved here first and then put on the clipboard; a file that exists is never overwritten.");
            _rowHints[strip] = ((CaptionedRow)row.Tag!).Hint;

            TrackExchange(folderCaption);
            TrackExchange(_exchangeClipboardReceivedFolderBox);
            TrackExchange(choose);
            TrackExchange(open);
            return row;
        }

        private void ReloadExchangeServerState()
        {
            _exchangeClipboardEnabled.Checked = _config.EnableExchangeClipboard;
            _exchangeClipboardSendHotkeyEnabled.Checked = _config.EnableExchangeClipboardSendHotkey;
            _exchangeClipboardSendHotkeyValue.Text = ChordText(_config.ExchangeClipboardSendHotkey);
            _exchangeClipboardGetHotkeyEnabled.Checked = _config.EnableExchangeClipboardGetHotkey;
            _exchangeClipboardGetHotkeyValue.Text = ChordText(_config.ExchangeClipboardGetHotkey);
            _exchangeClipboardReceivedFolderBox.Text = ExchangeClipboardSink.FolderOf(_config);

            foreach (Control control in _exchangeControls) control.Enabled = _config.EnableExchangeClipboard;
            RebuildExchangeAccountsList();
        }

        /// <summary>Every exchange mark of the window: the band's, the About credit's and each account entry's.</summary>
        private IEnumerable<ExchangeMarkPicture> AllExchangeMarks()
        {
            foreach (ExchangeMarkPicture mark in _exchangeMarks) yield return mark;
            foreach (Control row in _exchangeAccountsList.Controls)
                foreach (ExchangeMarkPicture mark in MarksIn(row)) yield return mark;
        }

        private static IEnumerable<ExchangeMarkPicture> MarksIn(Control root)
        {
            if (root is ExchangeMarkPicture self) yield return self;
            foreach (Control child in root.Controls)
                foreach (ExchangeMarkPicture mark in MarksIn(child)) yield return mark;
        }

        /// <summary>An image-only mark is named by the term, in the interface language (ICON-RENDER rule 8, EXCHANGE-MARK rule 14).</summary>
        private void NameExchangeMarks()
        {
            foreach (ExchangeMarkPicture mark in AllExchangeMarks()) mark.NameIn(Translate);
        }

        /// <summary>The window landed on a monitor of another DPI: each mark takes the size of the new one.</summary>
        private void RescaleExchangeMarks()
        {
            foreach (ExchangeMarkPicture mark in AllExchangeMarks()) mark.Rescale();
        }

        /// <summary>A chord for a label, or the word for "none" when it is unassigned.</summary>
        private string ChordText(string token)
            => token.Length == 0 ? Translate("Not assigned") : token;

        private void RebuildExchangeAccountsList()
        {
            _exchangeAccountsList.SuspendLayout();
            foreach (Control old in _exchangeAccountsList.Controls.Cast<Control>().ToList())
            {
                _exchangeAccountsList.Controls.Remove(old);
                old.Dispose();
            }

            List<ExchangeAccountEntry> accounts = _config.ExchangeClipboardAccounts;
            if (accounts.Count == 0)
            {
                _exchangeAccountsList.Controls.Add(new Label
                {
                    Text = Translate("No account yet."), AutoSize = true, UseMnemonic = false,
                    ForeColor = ThemePalette.Light.TextMuted, Margin = new Padding(3, 6, 3, 6),
                });
            }
            else
            {
                foreach (ExchangeAccountEntry entry in accounts)
                    _exchangeAccountsList.Controls.Add(BuildAccountRow(entry));
            }
            _exchangeAccountsList.ResumeLayout(true);
        }

        private Control BuildAccountRow(ExchangeAccountEntry entry)
        {
            Func<string, string> t = s => Translate(s);
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(3, 6, 3, 6),
            };

            var head = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
            // Each account entry wears the exchange server's mark before its label (EXCHANGE-MARK rule 8 (b), S0079 M4).
            // The state line under it (signed in, sign-in needed, the server lacks a feature) carries none: it sits in
            // this entry's row and a second picture in one row is noise (M5).
            var mark = new ExchangeMarkPicture(24) { Anchor = AnchorStyles.Left, Margin = new Padding(3, 3, 6, 3) };
            mark.NameIn(Translate);
            head.Controls.Add(mark);
            head.Controls.Add(new Label
            {
                Text = ExchangeClipboardService.LabelOf(entry), AutoSize = true, UseMnemonic = false, Font = BoldFont,
                Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 12, 4),
            });
            if (entry.NeedsSignIn)
                head.Controls.Add(TrackExchange(Button(t("Sign in again.."), AppGlyphs.Edit, () => ReauthExchangeAccount(entry))));
            head.Controls.Add(TrackExchange(Button(t("Remove account"), AppGlyphs.Remove, () => RemoveExchangeAccount(entry))));
            row.Controls.Add(head);

            // What the entry stands at, in the words of the contract's table (item CZ), never a bare code.
            string state;
            Color ink = ThemePalette.Light.TextMuted;
            if (entry.NeedsSignIn)
            {
                string code = entry.SignIn == ExchangeSignInState.SignedOut ? ExchangeCodes.SignedOut
                            : entry.SignIn == ExchangeSignInState.DeviceRemoved ? ExchangeCodes.DeviceRevoked
                            : ExchangeCodes.BadCredentials;
                state = ExchangeMessages.Describe(code, ExchangeContext.Enroll, t).What;
                ink = ThemePalette.Light.Danger;
            }
            else if (entry.IsGuest)
            {
                state = ExchangeMessages.Describe(ExchangeCodes.NotPermitted, ExchangeContext.Send, t).What;
            }
            else if (entry.Features.Count > 0 && !entry.ServerHas(ExchangeWire.FeatureClipboard))
            {
                state = ExchangeMessages.Describe(ExchangeCodes.ServerNoClipboard, ExchangeContext.Send, t).What;
            }
            else if (entry.Features.Count > 0 && !entry.ServerHas(ExchangeWire.FeatureSealed))
            {
                state = ExchangeMessages.Describe(ExchangeCodes.ServerNoE2e, ExchangeContext.Send, t).What;
                ink = ThemePalette.Light.Danger;
            }
            else
            {
                state = t("Signed in");
            }
            row.Controls.Add(new Label
            {
                Text = state, AutoSize = true, UseMnemonic = false, MaximumSize = new Size(860, 0),
                ForeColor = ink, Margin = new Padding(3, 0, 3, 2),
            });
            return row;
        }

        private void AddExchangeAccount()
        {
            string lang = _uiLanguage.SelectedItem as string ?? _config.UiLanguage;
            using (var dlg = new ExchangeAccountDialog(lang, _config.ExchangeClipboardAccounts))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.ResultEntry == null) return;
                // The same identity signing in again is the same entry (item AA), not a second one.
                ExchangeAccountEntry? same = ExchangeEnrollment.FindSameIdentity(_config.ExchangeClipboardAccounts, dlg.ResultEntry.Host, dlg.ResultEntry.Port, dlg.ResultEntry.Login);
                if (same == null) _config.ExchangeClipboardAccounts.Add(dlg.ResultEntry);
                _config.SaveExchangeAccounts();
            }
            Reload();
            ExchangeClipboardChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ReauthExchangeAccount(ExchangeAccountEntry entry)
        {
            string lang = _uiLanguage.SelectedItem as string ?? _config.UiLanguage;
            using (var dlg = new ExchangeAccountDialog(lang, _config.ExchangeClipboardAccounts, entry))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _config.SaveExchangeAccounts();
            }
            Reload();
            ExchangeClipboardChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Sign out on this device (items AB and AV): <c>revoke</c> the entry's own device on a live stream, best
        /// effort, then forget the token and the entry. With the server unreachable the old device stays in the
        /// account's list until another device removes it - said, not retried in the background.
        /// </summary>
        private async void RemoveExchangeAccount(ExchangeAccountEntry entry)
        {
            string lang = _uiLanguage.SelectedItem as string ?? _config.UiLanguage;
            string prompt = Localization.Format(Translate, "Remove the account {0} from this computer?", ExchangeClipboardService.LabelOf(entry));
            if (ConfirmDialog.Show(this, lang, prompt, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true) != DialogResult.Yes)
                return;

            bool revoked = false;
            try
            {
                UseWaitCursor = true;
                using (var cts = new CancellationTokenSource(5000))
                    revoked = !entry.NeedsSignIn && await new ExchangeClient().RevokeAsync(entry, cts.Token).ConfigureAwait(true);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                ExchangeClipboardLog.Fault("revoke-failed", ex);
            }
            finally { UseWaitCursor = false; }

            entry.DeleteSecret();
            _config.ExchangeClipboardAccounts.Remove(entry);
            _config.SaveExchangeAccounts();
            Reload();
            ExchangeClipboardChanged?.Invoke(this, EventArgs.Empty);

            if (!revoked)
                ConfirmDialog.Show(this, lang,
                    Translate("The account is removed from this computer.") + "\n" + Translate("The server could not be told: this device stays in the account's device list until another device removes it."),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void CreateExchangeShortcut(ExchangeShortcutKind kind)
        {
            string lang = _uiLanguage.SelectedItem as string ?? _config.UiLanguage;
            string? path = ExchangeShortcuts.Create(kind, s => Localization.Translate(lang, s));
            string message = path != null
                ? Localization.Format(Translate, "The shortcut is on the desktop: {0}", Path.GetFileName(path))
                : Translate("The shortcut could not be created on the desktop.");
            ConfirmDialog.Show(this, lang, message, MessageBoxButtons.OK, path != null ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void ChooseExchangeClipboardFolder()
        {
            using (var dlg = new FolderBrowserDialog
            {
                Description = Translate("Choose the folder for received files"),
                SelectedPath = _exchangeClipboardReceivedFolderBox.Text,
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                ExchangeClipboardSettingChanged(() =>
                {
                    _config.ExchangeClipboardReceivedFolder = dlg.SelectedPath;
                    _exchangeClipboardReceivedFolderBox.Text = dlg.SelectedPath;
                });
            }
        }

        private void OpenExchangeClipboardFolder()
        {
            string folder = _exchangeClipboardReceivedFolderBox.Text;
            if (string.IsNullOrEmpty(folder)) folder = AppConfig.DefaultExchangeClipboardReceivedFolder;
            try
            {
                Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = folder, UseShellExecute = true });
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception) { }
            ExchangeClipboardFolderOpenRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}

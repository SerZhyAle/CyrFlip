using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// "Save as STANDARD" and "Read STANDARD" of the exchange server page (ticket S0095, <c>DEVICE-EXCHANGE</c> 1.12):
    /// each the person's own act, confirmed before anything is sent (item ER). A read is applied through the same
    /// preview and transaction as a settings file, so a standard and a file are judged by one rule; after it, a
    /// local change is saved locally and nothing more - no frame on a change, at start or on connecting.
    /// </summary>
    internal sealed partial class CyrFlipContext
    {
        private bool _standardRunning;

        private void RunSettingsStandard(ExchangeAccountEntry entry, bool save, IReadOnlyList<string> groupIds)
        {
            if (_disposed || _standardRunning) return;
            string language = _config.UiLanguage;
            if (groupIds.Count == 0)
            {
                ConfirmDialog.Show(_settings, language, T("Choose at least one group of settings."), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _standardRunning = true;
            try
            {
                if (save) SaveSettingsStandard(entry, groupIds, language);
                else ReadSettingsStandard(entry, groupIds, language);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                ExchangeClipboardLog.Fault(save ? "standard-save-failed" : "standard-read-failed", ex);
                ShowStandardFailure(ExchangeCodes.Unanswered, save, language);
            }
            finally { _standardRunning = false; }
        }

        private string GroupList(IEnumerable<string> groupIds)
            => string.Join("\n", SettingsStandard.Groups.Where(g => groupIds.Contains(g.Id)).Select(g => "- " + T(g.Caption)));

        private void SaveSettingsStandard(ExchangeAccountEntry entry, IReadOnlyList<string> groupIds, string language)
        {
            string prompt = Localization.Format(T, "Save these groups of settings as the standard on {0}?", ExchangeClipboardService.LabelOf(entry))
                + "\n\n" + GroupList(groupIds) + "\n\n"
                + T("This replaces the standard saved on the server: a group you leave out is no longer in it.") + "\n"
                + T("Whoever runs the server can read the saved settings.") + " " + T("Passwords, folders, addresses and window positions stay on this computer.");
            if (ConfirmDialog.Show(_settings, language, prompt, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            string? payload = SettingsStandard.Serialize(_config, groupIds);
            if (payload == null) { ShowStandardFailure(ExchangeCodes.InvalidPayload, save: true, language); return; }

            var service = new ExchangeSettingsStandardService(new ExchangeClient(), () => _config.SaveExchangeAccounts());
            ExchangeOutcome<ExchangeStandardSaved> saved = BusyDialog.Wait(_settings, language,
                Task.Run(() => service.SaveAsync(entry, payload, CancellationToken.None)));
            ExchangeClipboardLog.Log("standard-save", "settings", payload.Length, groupIds.Count, saved.Ok ? "" : saved.Code);
            if (!saved.Ok) { ShowStandardFailure(saved.Code, save: true, language); _settings?.Reload(); return; }

            SettingsStandard.RememberSeen(entry.Id, saved: true, saved.Value!.UpdatedAt);
            _settings?.Reload();
            ConfirmDialog.Show(_settings, language, T("The settings are saved on the server as the standard."), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ReadSettingsStandard(ExchangeAccountEntry entry, IReadOnlyList<string> groupIds, string language)
        {
            string prompt = Localization.Format(T, "Read the standard from {0} and apply these groups of settings?", ExchangeClipboardService.LabelOf(entry))
                + "\n\n" + GroupList(groupIds) + "\n\n"
                + T("Their settings on this computer will be replaced by the saved ones; you will see every change before it is applied.") + "\n"
                + T("Passwords, folders, addresses and window positions stay on this computer.");
            if (ConfirmDialog.Show(_settings, language, prompt, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            var service = new ExchangeSettingsStandardService(new ExchangeClient(), () => _config.SaveExchangeAccounts());
            ExchangeOutcome<ExchangeStandardPayload> got = BusyDialog.Wait(_settings, language,
                Task.Run(() => service.GetAsync(entry, CancellationToken.None)));
            if (!got.Ok)
            {
                ExchangeClipboardLog.Log("standard-read", "settings", 0, groupIds.Count, got.Code);
                ShowStandardFailure(got.Code, save: false, language);
                _settings?.Reload();
                return;
            }

            // Nothing is applied until the whole answer is understood (item ER).
            SettingsStandardRead read = SettingsStandard.Parse(got.Value!.Payload, got.Value.FormatVersion, groupIds);
            ExchangeClipboardLog.Log("standard-read", "settings", got.Value.Payload.Length, groupIds.Count, read.Code);
            if (!read.Ok) { ShowStandardFailure(read.Code, save: false, language); return; }
            if (read.Document.Values.Count == 0)
            {
                ConfirmDialog.Show(_settings, language, T("The standard on the server holds none of the chosen groups. Nothing was changed."),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string source = Localization.Format(T, "Standard settings from {0}, saved {1}", ExchangeClipboardService.LabelOf(entry), ShownTime(got.Value.UpdatedAt));
            if (ImportDocument(() => read.Document, source))
                SettingsStandard.RememberSeen(entry.Id, saved: false, got.Value.UpdatedAt);
            _settings?.Reload();
        }

        /// <summary>An RFC 3339 instant in this computer's local time, or the text as it came when it does not parse.</summary>
        internal static string ShownTime(string rfc3339)
            => DateTimeOffset.TryParse(rfc3339, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset at)
                ? at.LocalDateTime.ToString("g", CultureInfo.CurrentCulture) : rfc3339;

        private void ShowStandardFailure(string code, bool save, string language)
        {
            ExchangeMessage message = ExchangeMessages.Describe(code, save ? ExchangeContext.Send : ExchangeContext.Get, T);
            ConfirmDialog.Show(_settings, language, message.Text + "\n\n" + Localization.Format(T, "Details: {0}", message.Details),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

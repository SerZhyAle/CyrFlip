using System;
using System.Threading;
using System.Threading.Tasks;

namespace CyrFlip
{
    /// <summary>
    /// The two acts of the settings standard on one account entry (<c>DEVICE-EXCHANGE</c> 1.12, items EN to ER,
    /// ticket S0095): open the entry's control stream, learn what the <c>welcome</c> says, and send the one frame -
    /// only when the server lists <c>settings</c>, and never for a guest. Nothing here runs by itself: each call is
    /// the person's explicit act, and nothing is sent at start, on connecting or on a local change (item ER).
    /// </summary>
    internal sealed class ExchangeSettingsStandardService
    {
        private readonly ExchangeClient _client;
        private readonly Action _saveAccounts;

        public ExchangeSettingsStandardService(ExchangeClient client, Action saveAccounts)
        {
            _client = client;
            _saveAccounts = saveAccounts;
        }

        /// <summary>Whether the entry can show the actions at all: its server listed <c>settings</c> (item EO).</summary>
        public static bool Offered(ExchangeAccountEntry entry) => entry.ServerHas(ExchangeWire.FeatureSettings);

        /// <summary>Replaces the standard of this program on the entry's server with <paramref name="payload"/>.</summary>
        public async Task<ExchangeOutcome<ExchangeStandardSaved>> SaveAsync(ExchangeAccountEntry entry, string payload, CancellationToken ct)
        {
            ExchangeOutcome<ExchangeSession> opened = await OpenAsync(entry, ct).ConfigureAwait(false);
            if (!opened.Ok) return ExchangeOutcome.Fail<ExchangeStandardSaved>(opened.Code, opened.Trust);
            using (ExchangeSession session = opened.Value!)
                return await _client.SaveStandardAsync(session, payload, ct).ConfigureAwait(false);
        }

        /// <summary>The standard of this program on the entry's server; an answer for another <c>appId</c> is not understood.</summary>
        public async Task<ExchangeOutcome<ExchangeStandardPayload>> GetAsync(ExchangeAccountEntry entry, CancellationToken ct)
        {
            ExchangeOutcome<ExchangeSession> opened = await OpenAsync(entry, ct).ConfigureAwait(false);
            if (!opened.Ok) return ExchangeOutcome.Fail<ExchangeStandardPayload>(opened.Code, opened.Trust);
            using (ExchangeSession session = opened.Value!)
            {
                ExchangeOutcome<ExchangeStandardPayload> got = await _client.GetStandardAsync(session, ct).ConfigureAwait(false);
                if (got.Ok && got.Value!.AppId != SettingsStandard.AppId)
                    return ExchangeOutcome.Fail<ExchangeStandardPayload>(ExchangeCodes.InvalidPayload);
                return got;
            }
        }

        /// <summary>An authenticated control stream whose server keeps standards, or the code of why not; nothing is sent on a refusal.</summary>
        private async Task<ExchangeOutcome<ExchangeSession>> OpenAsync(ExchangeAccountEntry entry, CancellationToken ct)
        {
            ExchangeOutcome<ExchangeSession> opened = await _client.OpenAsync(entry, ct).ConfigureAwait(false);
            if (!opened.Ok)
            {
                if (ExchangeClipboardService.LearnFrom(entry, opened.Code, null)) _saveAccounts();
                return opened;
            }
            ExchangeSession session = opened.Value!;
            if (ExchangeClipboardService.LearnFrom(entry, null, session)) _saveAccounts();
            string? blocked = !session.Welcome.Has(ExchangeWire.FeatureSettings) ? ExchangeCodes.ServerNoSettings
                            : session.Welcome.IsGuest ? ExchangeCodes.NotPermitted
                            : null;
            if (blocked == null) return opened;
            session.Dispose();
            return ExchangeOutcome.Fail<ExchangeSession>(blocked);
        }
    }
}

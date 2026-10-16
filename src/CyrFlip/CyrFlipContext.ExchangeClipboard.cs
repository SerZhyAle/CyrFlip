using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The exchange server clipboard on the tray shell (ticket S0074): "Send clipboard to server" and "Get clipboard
    /// from server" from the tray, the two chords and the two pipe commands. Three phases, each on the guard it
    /// needs and no longer: the clipboard is read under <c>_busy</c> (<c>CLIPBOARD-GUARD</c> item N), the seconds of
    /// network hold nothing, and a received value is placed under <c>_busy</c> again (item Q).
    /// </summary>
    internal sealed partial class CyrFlipContext
    {
        /// <summary>How long a program that owns the clipboard may take to render a format before the send gives up (item N).</summary>
        private const int ExchangeOwnerRenderTimeoutMs = 5000;

        /// <summary>How long a finished receive waits for a flip that is still running before it reports the clipboard busy.</summary>
        private const int ExchangePlaceWaitMs = 3000;

        private ToolStripMenuItem _exchangeSendItem = null!;
        private ToolStripMenuItem _exchangeGetItem = null!;
        private ToolStripMenuItem _exchangeCancelItem = null!;
        private string? _lastReceivedExchangeFile;
        private DateTime _lastReceivedExchangeFileUtc;
        private Hotkey _exchangeSendHotkey;
        private Hotkey _exchangeGetHotkey;
        private int _exchangeRunning; // one operation at a time per program (item N)
        private CancellationTokenSource? _exchangeCts;

        private bool ExchangeSendChordLive => ChordBinding.ExchangeSendLive(_config) && Hotkey.TryParse(_config.ExchangeClipboardSendHotkey, out _);
        private bool ExchangeGetChordLive => ChordBinding.ExchangeGetLive(_config) && Hotkey.TryParse(_config.ExchangeClipboardGetHotkey, out _);

        private void InitializeExchangeClipboard()
        {
            _exchangeSendItem = new ToolStripMenuItem(null, null, (_, _) => SendExchangeClipboard());
            _exchangeGetItem = new ToolStripMenuItem(null, null, (_, _) => GetExchangeClipboard());
            _exchangeCancelItem = new ToolStripMenuItem(null, null, (_, _) => CancelExchangeOperation()) { Visible = false };

            _hook.ExchangeSendHotkeyPressed += (_, _) => _ui?.Post(_ => SendExchangeClipboard(), null);
            _hook.ExchangeGetHotkeyPressed += (_, _) => _ui?.Post(_ => GetExchangeClipboard(), null);

            RefreshExchangeHotkeys();
        }

        /// <summary>
        /// The one tray subscription of the exchange clipboard. It lives apart from <see cref="InitializeExchangeClipboard"/>
        /// because the tray icon is created after the menu is built, which is after that method has to run: touching
        /// <c>_tray</c> there was a <see cref="NullReferenceException"/> in the constructor, i.e. a fatal stop at every start.
        /// </summary>
        private void WireExchangeTrayEvents()
        {
            _tray.BalloonTipClicked += (_, _) =>
            {
                if (!string.IsNullOrEmpty(_lastReceivedExchangeFile) && (DateTime.UtcNow - _lastReceivedExchangeFileUtc).TotalSeconds < 30)
                {
                    string path = _lastReceivedExchangeFile!;
                    _lastReceivedExchangeFile = null;
                    OpenFileInExplorer(path);
                }
            };
        }

        private void RefreshExchangeHotkeys()
        {
            _exchangeSendHotkey = Hotkey.TryParse(_config.ExchangeClipboardSendHotkey, out Hotkey sendChord) ? sendChord : default;
            _exchangeGetHotkey = Hotkey.TryParse(_config.ExchangeClipboardGetHotkey, out Hotkey getChord) ? getChord : default;

            _hook.UpdateExchangeSendHotkey(_exchangeSendHotkey);
            _hook.UpdateExchangeSendEnabled(_config.EnableHotkeys && ExchangeSendChordLive);
            _hook.UpdateExchangeGetHotkey(_exchangeGetHotkey);
            _hook.UpdateExchangeGetEnabled(_config.EnableHotkeys && ExchangeGetChordLive);
        }

        /// <summary>
        /// Whether the module is on. A shortcut on the desktop or a Jump List task outlives the switch, so a command that
        /// arrives while the module is off is answered in words instead of vanishing.
        /// </summary>
        private bool ExchangeModuleOn()
        {
            if (_config.EnableExchangeClipboard) return true;
            ShowInfoBalloon(4000, T("The clipboard on the server is switched off. Turn it on in the settings of CyrFlip."));
            return false;
        }

        // ---------------------------------------------------------------- one operation at a time

        private bool TryBeginExchangeOperation()
        {
            if (Interlocked.CompareExchange(ref _exchangeRunning, 1, 0) != 0)
            {
                ShowInfoBalloon(3000, ExchangeMessages.Describe(ExchangeCodes.OperationRunning, ExchangeContext.Send, T).Text);
                return false;
            }
            _exchangeCts = new CancellationTokenSource();
            _exchangeCancelItem.Visible = true;
            return true;
        }

        private void EndExchangeOperation()
        {
            CancellationTokenSource? cts = Interlocked.Exchange(ref _exchangeCts, null);
            _ui?.Post(_ =>
            {
                if (!_disposed) _exchangeCancelItem.Visible = false;
            }, null);
            cts?.Dispose();
            Interlocked.Exchange(ref _exchangeRunning, 0);
        }

        private void CancelExchangeOperation()
        {
            try { _exchangeCts?.Cancel(); }
            catch (ObjectDisposedException) { /* it ended while the menu was open */ }
        }

        private ExchangeClipboardService NewExchangeService()
            => new ExchangeClipboardService(_config, new ExchangeClient(), () => _config.SaveExchangeAccounts());

        // ---------------------------------------------------------------- send

        private void SendExchangeClipboard()
        {
            if (_disposed || !ExchangeModuleOn()) return;
            if (!TryBeginExchangeOperation()) return;
            CancellationToken ct = _exchangeCts!.Token;

            Task.Run(async () =>
            {
                try
                {
                    ExchangeOutcome<ExchangeClipboardPayload> read = ReadClipboardForExchange();
                    if (!read.Ok)
                    {
                        PostExchangeFailure(read.Code, ExchangeContext.Send);
                        ExchangeClipboardLog.Log("send", "none", 0, 0, read.Code);
                        return;
                    }

                    ExchangeClipboardPayload payload = read.Value!;
                    if (payload.ClearSize > 8L * 1024 * 1024)
                        _ui?.Post(_ => ShowInfoBalloon(3000, T("Sending the clipboard to the server..")), null);

                    ExchangeSendReport report = await NewExchangeService().SendAsync(payload, ct).ConfigureAwait(false);
                    _ui?.Post(_ => ShowSendReport(report), null);
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException))
                {
                    ExchangeClipboardLog.Fault("send-failed", ex);
                    PostExchangeFailure(ExchangeCodes.TransferFailed, ExchangeContext.Send);
                }
                finally { EndExchangeOperation(); }
            });
        }

        /// <summary>
        /// Phase A: the one read of the clipboard, under the shared guard and off the UI thread. A program that owns
        /// the clipboard and does not render in time is abandoned - the send stops and says so (item N); the abandoned
        /// read keeps its thread until the owner answers, and releases the clipboard itself when it does.
        /// </summary>
        private ExchangeOutcome<ExchangeClipboardPayload> ReadClipboardForExchange()
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
                return ExchangeOutcome.Fail<ExchangeClipboardPayload>(ExchangeCodes.OperationRunning);
            try
            {
                ExchangeOutcome<ExchangeClipboardPayload>? result = null;
                using (var done = new ManualResetEventSlim(false))
                {
                    var reader = new Thread(() =>
                    {
                        try { result = ExchangeClipboardSource.Read(Win32Clipboard.Reader, ExchangeClipboardService.DefaultClipboardBytes); }
                        catch (Exception ex) when (!(ex is OutOfMemoryException))
                        {
                            ExchangeClipboardLog.Fault("read-failed", ex);
                            result = ExchangeOutcome.Fail<ExchangeClipboardPayload>(ExchangeCodes.NothingToSend);
                        }
                        finally { try { done.Set(); } catch (ObjectDisposedException) { /* abandoned */ } }
                    })
                    { IsBackground = true, Name = "CyrFlip exchange clipboard read" };
                    reader.Start();
                    if (!done.Wait(ExchangeOwnerRenderTimeoutMs))
                        return ExchangeOutcome.Fail<ExchangeClipboardPayload>(ExchangeCodes.OwnerTimeout);
                }
                return result ?? ExchangeOutcome.Fail<ExchangeClipboardPayload>(ExchangeCodes.NothingToSend);
            }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        private void ShowSendReport(ExchangeSendReport report)
        {
            if (_disposed) return;
            if (report.Code.Length > 0)
            {
                ShowExchangeFailure(report.Code, ExchangeContext.Send);
                return;
            }

            var sb = new StringBuilder();
            if (report.Ok)
            {
                sb.Append(report.Entries.Count == 1 || report.Stored == report.Entries.Count
                    ? (report.Stored == 1 ? T("The clipboard is on the server.") : Localization.Format(T, "The clipboard is on the server ({0} accounts).", report.Stored))
                    : T("The clipboard is on the server."));
            }
            else
            {
                sb.Append(T("The clipboard was not sent."));
            }

            // every entry that did not take part is named with the words of the table (item CA, item DA)
            int shown = 0;
            foreach (ExchangeEntryResult e in report.Entries)
            {
                if (e.Ok || shown >= 3) continue;
                shown++;
                ExchangeMessage m = ExchangeMessages.Describe(e.Code, ExchangeContext.Send, T);
                sb.Append('\n').Append(e.Label).Append(": ").Append(m.What);
                if (shown == 1 && report.Stored == 0 && m.HasStep) sb.Append('\n').Append(m.Try);
            }
            foreach (ExchangeEntryResult e in report.Entries)
            {
                if (!e.Ok) continue;
                AppendDeviceStates(sb, e);
            }

            if (report.Ok) ShowInfoBalloon(report.Stored == report.Entries.Count ? 3000 : 6000, Trim(sb.ToString()));
            else ShowWarningBalloon(6000, Trim(sb.ToString()));
        }

        /// <summary>The devices of an account a value was not sealed to, each with the table's words (items DG and DR).</summary>
        private void AppendDeviceStates(StringBuilder sb, ExchangeEntryResult e)
        {
            foreach (string name in e.DevicesWithoutKeyNames)
                sb.Append('\n').Append(name).Append(": ").Append(ExchangeMessages.Describe(ExchangeCodes.E2eMissing, ExchangeContext.Send, T).What);
            foreach (string name in e.DevicesKeyChangedNames)
                sb.Append('\n').Append(name).Append(": ").Append(ExchangeMessages.Describe(ExchangeCodes.KeyChanged, ExchangeContext.Send, T).What);
        }

        // ---------------------------------------------------------------- get

        private void GetExchangeClipboard()
        {
            if (_disposed || !ExchangeModuleOn()) return;
            if (!TryBeginExchangeOperation()) return;
            CancellationToken ct = _exchangeCts!.Token;

            Task.Run(async () =>
            {
                try
                {
                    ExchangeFetchReport report = await NewExchangeService().FetchAsync(ct).ConfigureAwait(false);
                    if (!report.Ok)
                    {
                        _ui?.Post(_ => ShowFetchFailure(report), null);
                        return;
                    }

                    ExchangeReceivedValue value = report.Value!;
                    bool placed = PlaceReceivedValue(value);
                    if (!placed)
                    {
                        ExchangeClipboardLog.Log("place", value.Kind, value.ClearSize, 0, ExchangeCodes.PlaceFailed);
                        PostExchangeFailure(ExchangeCodes.PlaceFailed, ExchangeContext.Get);
                        return;
                    }
                    _ui?.Post(_ => ShowReceived(value, report), null);
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException))
                {
                    ExchangeClipboardLog.Fault("get-failed", ex);
                    PostExchangeFailure(ExchangeCodes.TransferFailed, ExchangeContext.Get);
                }
                finally { EndExchangeOperation(); }
            });
        }

        /// <summary>Phase C: one open, one empty, every format - a copy the person asked for (item Q), under the guard.</summary>
        private bool PlaceReceivedValue(ExchangeReceivedValue value)
        {
            if (!AcquireClipboardGuard(ExchangePlaceWaitMs)) return false;
            try
            {
                switch (value.Kind)
                {
                    case ExchangeClipboardPayload.Text: return value.Bytes != null && ExchangeClipboardSink.PlaceText(value.Bytes);
                    case ExchangeClipboardPayload.Image: return value.Bytes != null && ExchangeClipboardSink.PlaceImage(value.Bytes);
                    case ExchangeClipboardPayload.File: return value.FilePath.Length > 0 && ExchangeClipboardSink.PlaceFile(value.FilePath);
                    default: return false;
                }
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                ExchangeClipboardLog.Fault("place-failed", ex);
                return false;
            }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        private void ShowReceived(ExchangeReceivedValue value, ExchangeFetchReport report)
        {
            if (_disposed) return;
            string when = FormatStoredAt(value.StoredAt);
            string from = value.SourceDeviceName.Length > 0 ? value.SourceDeviceName : value.EntryLabel;
            var sb = new StringBuilder();
            if (value.Kind == ExchangeClipboardPayload.File)
            {
                _lastReceivedExchangeFile = value.FilePath;
                _lastReceivedExchangeFileUtc = DateTime.UtcNow;
                sb.Append(Localization.Format(T, "Received file «{0}». Click to open its folder.", Path.GetFileName(value.FilePath)));
                sb.Append('\n').Append(Localization.Format(T, "Sent from {0}, {1}.", from, when));
            }
            else
            {
                sb.Append(Localization.Format(T, "The clipboard received from the server is ready to paste. Sent from {0}, {1}.", from, when));
            }
            foreach (ExchangeEntryResult e in report.Entries)
            {
                if (e.Ok) continue;
                sb.Append('\n').Append(e.Label).Append(": ").Append(ExchangeMessages.Describe(e.Code, ExchangeContext.Get, T).What);
            }
            ShowInfoBalloon(value.Kind == ExchangeClipboardPayload.File ? 8000 : 4000, Trim(sb.ToString()));
        }

        private static string FormatStoredAt(string rfc3339)
        {
            return DateTimeOffset.TryParse(rfc3339, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset t)
                ? t.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture)
                : rfc3339;
        }

        private void ShowFetchFailure(ExchangeFetchReport report)
        {
            if (_disposed) return;
            ExchangeMessage m = ExchangeMessages.Describe(report.Code, ExchangeContext.Get, T);
            var sb = new StringBuilder(m.Text);
            if (report.Code != ExchangeCodes.NoAccounts)
            {
                int shown = 0;
                foreach (ExchangeEntryResult e in report.Entries)
                {
                    if (e.Ok || e.Code == report.Code && report.Entries.Count == 1 || shown >= 3) continue;
                    shown++;
                    sb.Append('\n').Append(e.Label).Append(": ").Append(ExchangeMessages.Describe(e.Code, ExchangeContext.Get, T).What);
                }
            }
            if (report.Code == ExchangeCodes.ClipboardEmpty) ShowInfoBalloon(3000, Trim(sb.ToString()));
            else ShowWarningBalloon(6000, Trim(sb.ToString()));
        }

        // ---------------------------------------------------------------- messages

        private void PostExchangeFailure(string code, ExchangeContext context)
            => _ui?.Post(_ => ShowExchangeFailure(code, context), null);

        private void ShowExchangeFailure(string code, ExchangeContext context)
        {
            if (_disposed) return;
            ExchangeMessage m = ExchangeMessages.Describe(code, context, T);
            ShowWarningBalloon(6000, Trim(m.Text));
        }

        /// <summary>A balloon holds about 250 characters; nothing of the message is reworded, only cut at a line.</summary>
        private static string Trim(string text) => text.Length <= 240 ? text : text.Substring(0, 237) + "..";

        // ---------------------------------------------------------------- chords

        private void OnSetExchangeSendHotkey()
        {
            using var dlg = new HotkeyDialog(_exchangeSendHotkey.Display, T("Set send clipboard shortcut"), _config.UiLanguage);
            if (dlg.ShowDialog(DialogOwner()) != DialogResult.OK || dlg.CapturedHotkey == null) return;
            if (!Hotkey.TryParse(dlg.CapturedHotkey, out Hotkey next) || !ChordIsFree(next, ChordKind.ExchangeSend)) return;

            _exchangeSendHotkey = next;
            _config.ExchangeClipboardSendHotkey = next.Display;
            _config.Save();
            _hook.UpdateExchangeSendHotkey(next);
            RefreshExchangeHotkeys();
            UpdateTrayTexts();
            _settings?.Reload();
        }

        private void OnSetExchangeGetHotkey()
        {
            using var dlg = new HotkeyDialog(_exchangeGetHotkey.Display, T("Set get clipboard shortcut"), _config.UiLanguage);
            if (dlg.ShowDialog(DialogOwner()) != DialogResult.OK || dlg.CapturedHotkey == null) return;
            if (!Hotkey.TryParse(dlg.CapturedHotkey, out Hotkey next) || !ChordIsFree(next, ChordKind.ExchangeGet)) return;

            _exchangeGetHotkey = next;
            _config.ExchangeClipboardGetHotkey = next.Display;
            _config.Save();
            _hook.UpdateExchangeGetHotkey(next);
            RefreshExchangeHotkeys();
            UpdateTrayTexts();
            _settings?.Reload();
        }

        private static void OpenFileInExplorer(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                    Process.Start("explorer.exe", "/select,\"" + filePath + "\"");
                else if (Directory.Exists(filePath))
                    Process.Start("explorer.exe", "\"" + filePath + "\"");
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException) { }
        }
    }
}

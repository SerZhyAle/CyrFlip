using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CyrFlip
{
    /// <summary>
    /// What the pause verb needs of the running copy (ticket S0121): whether the history module is on, and the one
    /// persisted paused state - the check item, the settings page and the verb all show the same value.
    /// <see cref="SetPaused"/> is the tray item's own path in the app (config, live service, save), so a verb writes
    /// exactly what a click on the check item writes; the tests fake the host.
    /// </summary>
    internal interface IHistoryCommandsHost
    {
        /// <summary>Whether the history module is on; off answers <c>module-off</c> before anything of the module is read.</summary>
        bool ClipboardHistoryOn { get; }

        /// <summary>The one persisted paused state, applied live and saved - the tray item's own path.</summary>
        void SetPaused(bool paused);
    }

    /// <summary>
    /// The three clipboard-history verbs (ticket S0121, APP-COMMANDS 0.2, the research rows C28, C29 and C30):
    /// <c>clipboard-history list</c>, <c>clipboard-history put</c> and <c>clipboard-history pause</c>. Each is a pure
    /// function over the parsed invocation and a provider of the live service, so the tests need no context and no
    /// window; <c>CyrFlipContext</c> supplies the strip's own <see cref="ClipboardHistoryService"/> when the history
    /// module is on and null when it is off (the journal is then neither read nor created).
    ///
    /// <para>The history is the most private store in the product, so its verbs answer previews only, and nothing here
    /// puts a character of an entry, a source app or a source window title into an answer item, a message or a log
    /// line. The id is the store's own (the SHA-256 hex of the entry's text), the preview is the strip's preview (the
    /// first 1024 characters on one line), and the date is the store's second-resolution UTC the exchange writer
    /// formats. Order inside each verb: the arguments (exit 10) -> the provider (null is a switched-off module, 11
    /// <c>module-off</c>) -> the journal's replay, awaited without blocking the thread that pumps the keyboard hook ->
    /// the work. The two verbs that read and write the store answer 12 with "Commands from other programs" off because
    /// their risk class is above <c>safe</c>; the base's gate does that before any handler runs. <c>pause</c> is ranked
    /// <c>safe</c> (its answer repeats the value the caller's own command supplied), so the history module gates it
    /// and the setting never does.</para>
    /// </summary>
    internal static class AppCommandsClipboardHistory
    {
        /// <summary>How many entries <c>list</c> answers when the caller gives no <c>--limit</c>.</summary>
        public const int DefaultLimit = 10;
        /// <summary>The most entries one answer may carry: an unbounded list cannot honor the answer bound.</summary>
        public const int MaxLimit = 50;
        /// <summary>The answer bound of the S0108 list discipline, one source for every list verb.</summary>
        internal const int MaxAnswerDataBytes = AppCommandsNotes.MaxAnswerDataBytes;

        // ------------------------------------------------------------------ clipboard-history list

        /// <summary>
        /// The history's previews, pinned first then newest first, at most <paramref name="limit"/> of them and only
        /// while the answer stays under <see cref="MaxAnswerDataBytes"/>. The store is searched exactly as the search
        /// window searches it: raw text, the standard order, a query of at least 3 characters.
        /// </summary>
        public static async Task<AppCommandResponse> ListHistory(AppCommandInvocation invocation,
            Func<ClipboardHistoryService?> provider, Func<string, string> T, CancellationToken token)
        {
            try
            {
                string? query = invocation.Option("query");
                if (query != null && !ClipboardHistorySearch.IsReady(query))
                    return AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                        Localization.Format(T, "The query needs at least {0} characters.", ClipboardHistorySearch.MinimumQueryLength));

                int limit = DefaultLimit;
                string? rawLimit = invocation.Option("limit");
                if (rawLimit != null
                    && !(int.TryParse(rawLimit, NumberStyles.Integer, CultureInfo.InvariantCulture, out limit)
                         && limit >= 1 && limit <= MaxLimit))
                    return AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                        Localization.Format(T, "The limit must be a whole number from {0} to {1}.", 1, MaxLimit));

                bool pinnedOnly = invocation.Flag("pinned");

                ClipboardHistoryService? service = provider();
                if (service == null) return ModuleOff(T);
                AppCommandResponse? notReady = await Ready(service, T, token);
                if (notReady != null) return notReady;
                // Nothing is read after the bound: the caller has been told "not proven" by then.
                if (token.IsCancellationRequested) return TimedOut(T);

                var matches = new List<ClipboardHistoryEntry>();
                foreach (ClipboardHistoryEntry entry in service.Entries)
                {
                    if (pinnedOnly && !entry.IsPinned) continue;
                    if (query != null && !ClipboardHistorySearch.Matches(entry, query)) continue;
                    matches.Add(entry);
                }

                object[] items = Items(matches, limit);
                var data = new Dictionary<string, object>
                {
                    ["count"] = items.Length,
                    ["total"] = matches.Count,
                    ["truncated"] = items.Length < Math.Min(limit, matches.Count),
                    ["items"] = items,
                };
                return AppCommandResponse.Done(Localization.Format(T, "History entries listed: {0} of {1}.", items.Length, matches.Count), data);
            }
            catch (Exception ex)
            {
                return Crashed("clipboard-history list", ex, T);
            }
        }

        /// <summary>
        /// The first <paramref name="limit"/> matches as answer items, fewer when the next one would take the answer
        /// past <see cref="MaxAnswerDataBytes"/> (the S0108 list discipline; one item always fits - a preview is cut,
        /// the fixed members are short).
        /// </summary>
        internal static object[] Items(IReadOnlyList<ClipboardHistoryEntry> entries, int limit)
        {
            var items = new List<object>();
            var json = new JavaScriptSerializer();
            int used = 0;
            foreach (ClipboardHistoryEntry entry in entries)
            {
                if (items.Count >= limit) break;
                Dictionary<string, object> item = Item(entry);
                int bytes = Encoding.UTF8.GetByteCount(json.Serialize(item)) + 1;
                if (items.Count > 0 && used + bytes > MaxAnswerDataBytes) break;
                items.Add(item);
                used += bytes;
            }
            return items.ToArray();
        }

        /// <summary>
        /// One entry of a list: the store's own id, the strip's preview, the pin and the copy date. Never the whole
        /// text, never a source app, never a source window title - the answer carries the least an entry can be
        /// recognized by.
        /// </summary>
        internal static Dictionary<string, object> Item(ClipboardHistoryEntry entry)
            => new Dictionary<string, object>
            {
                ["id"] = entry.Uuid,
                ["preview"] = entry.Preview,
                ["pinned"] = entry.IsPinned,
                ["copiedUtc"] = CyrFlipExchangeWriter.Date(entry.CreatedAt),
            };

        // ------------------------------------------------------------------ clipboard-history put

        /// <summary>
        /// Puts one entry back exactly as the strip's restore does, through <paramref name="restore"/> - the service's
        /// own path in the app, a fake in the tests, because the restore writes the real clipboard. The answer carries
        /// the outcome only (the caller named the entry by its id and can list it); the parser has already refused an
        /// <c>--id</c> that is missing, and this method refuses one that is not the store's id shape (the SHA-256 hex).
        /// </summary>
        public static async Task<AppCommandResponse> PutHistory(AppCommandInvocation invocation,
            Func<ClipboardHistoryService?> provider, Func<ClipboardHistoryEntry, bool> restore,
            Func<string, string> T, CancellationToken token)
        {
            try
            {
                string id = invocation.Option("id") ?? "";
                if (!IsEntryId(id))
                    return AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                        T("The --id must be an id a clipboard-history list answered."));

                ClipboardHistoryService? service = provider();
                if (service == null) return ModuleOff(T);
                AppCommandResponse? notReady = await Ready(service, T, token);
                if (notReady != null) return notReady;
                if (token.IsCancellationRequested) return TimedOut(T);

                ClipboardHistoryEntry? entry = null;
                foreach (ClipboardHistoryEntry candidate in service.Entries)
                    if (string.Equals(candidate.Uuid, id, StringComparison.OrdinalIgnoreCase))
                    {
                        entry = candidate;
                        break;
                    }
                if (entry == null)
                    // The message names nothing of the entry - the id is the caller's own text, so it is not echoed either.
                    return AppCommandResponse.Refused(AppCommandReasons.TargetMissing, AppCommandExit.NotReady,
                        T("No entry has this id."));

                return restore(entry)
                    ? AppCommandResponse.Done(T("The entry is back on the clipboard."))
                    : AppCommandResponse.Failed(AppCommandReasons.ClipboardFailed, AppCommandExit.Failed,
                        T("The clipboard could not be written. Nothing was restored."));
            }
            catch (Exception ex)
            {
                return Crashed("clipboard-history put", ex, T);
            }
        }

        /// <summary>The store's own id shape: the SHA-256 hex of the entry's text, 64 characters.</summary>
        internal static bool IsEntryId(string id)
        {
            if (id.Length != 64) return false;
            foreach (char c in id)
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') && !(c >= 'A' && c <= 'F')) return false;
            return true;
        }

        // ------------------------------------------------------------------ clipboard-history pause

        /// <summary>
        /// The one persisted paused state (row C30): the same value the tray's check item and the settings page own,
        /// applied live and saved by the host, and the new state in the answer. Safe, so the history module gates it
        /// and "Commands from other programs" never does; idempotent, so pausing a paused history succeeds.
        /// </summary>
        public static AppCommandResponse PauseHistory(AppCommandInvocation invocation, IHistoryCommandsHost host,
            Func<string, string> T)
        {
            try
            {
                if (!host.ClipboardHistoryOn) return ModuleOff(T);
                bool paused = invocation.Flag("paused");
                host.SetPaused(paused);
                return AppCommandResponse.Done(
                    T(paused ? "History recording is paused." : "History recording is on."),
                    new Dictionary<string, object> { ["paused"] = paused });
            }
            catch (Exception ex)
            {
                return Crashed("clipboard-history pause", ex, T);
            }
        }

        // ------------------------------------------------------------------ shared

        /// <summary>
        /// Waits - under the token, never blocking this thread - for the journal's replay, then merges what the pool
        /// delivered into the live list on the owner thread (the posted message may still sit in the queue). The
        /// history's own rule is that a record that could not be read costs only itself, so there is no store-failed
        /// answer here: the skipped count is the service's own session report.
        /// </summary>
        internal static async Task<AppCommandResponse?> Ready(ClipboardHistoryService service,
            Func<string, string> T, CancellationToken token)
        {
            Task replay = service.ReplayAsync();
            if (!replay.IsCompleted)
            {
                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (token.Register(() => cancelled.TrySetResult(true)))
                {
                    if (await Task.WhenAny(replay, cancelled.Task) != replay) return TimedOut(T);
                }
            }
            // The thread that owns the list - the UI thread in the app, the test thread in the tests.
            service.CompleteReplay();
            return null;
        }

        private static AppCommandResponse ModuleOff(Func<string, string> T)
            => AppCommandVerbs.ModuleOff(T("The clipboard history is switched off. Turn it on in the settings of CyrFlip."));

        private static AppCommandResponse TimedOut(Func<string, string> T)
            => AppCommandResponse.Failed(AppCommandReasons.Timeout, AppCommandExit.NotProven,
                T("CyrFlip did not answer in time. It may still be working."));

        /// <summary>A fault after the first await would otherwise fault the task: the caller always gets an answer. Only the type is logged.</summary>
        private static AppCommandResponse Crashed(string verb, Exception ex, Func<string, string> T)
        {
            LauncherLog.Log("Command: " + verb + " failed: " + ex.GetType().Name);
            return AppCommandResponse.Failed(AppCommandReasons.Error, AppCommandExit.Failed,
                T("The command failed. The log bundle on the About page has the details."));
        }
    }
}

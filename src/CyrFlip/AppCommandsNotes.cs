using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CyrFlip
{
    /// <summary>
    /// The four verbs of the quick notes (tickets S0108 and S0120, APP-COMMANDS 0.2): <c>note add</c>, <c>note list</c>,
    /// <c>note get</c> and <c>note export</c>. Each is a pure function over the parsed invocation, a provider of the
    /// service and a cancellation token, so the tests need no context and no window; <c>CyrFlipContext</c> supplies
    /// <c>EnsureQuickNotesService</c> as the provider.
    ///
    /// <para>Order inside each verb: the arguments (exit 10) -> the provider (null is a switched-off module, 11
    /// <c>module-off</c>) -> <c>note export</c> checks its path here, before anything of the notes is touched -> the
    /// journal's replay, awaited without blocking the thread that pumps the keyboard hook -> a journal that could not be
    /// read (1 <c>store-failed</c>) -> the work. The caller resumes on the thread it was started on - the UI thread in
    /// the app - because the service has one owner thread. Nothing here puts a character of a note into a log line, a
    /// message or an exception.</para>
    /// </summary>
    internal static class AppCommandsNotes
    {
        /// <summary>The verb's own bound on <c>--text</c>; the parser's 32 KiB as UTF-8 always stops a text first.</summary>
        public const int MaxTextChars = 32768;
        public const int DefaultLimit = 50;
        public const int MaxLimit = 500;
        /// <summary>The longest <c>preview</c> of an answer.</summary>
        public const int PreviewChars = 120;
        /// <summary>The longest <c>title</c> of an answer: a name is a short thing, and the answer has a bound.</summary>
        public const int TitleChars = 256;
        /// <summary>
        /// How many bytes of notes one answer carries. The caller reads a response line of at most 64 KiB
        /// (<see cref="AppCommandLine.MaxRequestBytes"/>), so a list that would pass it ends earlier and says so by its
        /// <c>count</c> being below both <c>limit</c> and <c>total</c>.
        /// </summary>
        public const int MaxAnswerDataBytes = 48 * 1024;

        // ------------------------------------------------------------------ note add

        public static async Task<AppCommandResponse> NoteAdd(AppCommandInvocation invocation, Func<QuickNotesService?> provider,
            Func<string, string> T, CancellationToken token)
        {
            try
            {
                string text = invocation.Option("text") ?? "";
                if (text.Length == 0)
                    return AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                        T("The note needs some text. Give it with --text."));
                if (text.Length > MaxTextChars)
                    return AppCommandResponse.Refused(AppCommandReasons.TooLarge, AppCommandExit.Usage,
                        Localization.Format(T, "The text is too long. The limit is {0} characters.", MaxTextChars));

                QuickNotesService? service = provider();
                if (service == null) return ModuleOff(T);
                AppCommandResponse? notReady = await Ready(service, T, token);
                if (notReady != null) return notReady;
                // Nothing is written after the bound: the caller has been told "not proven" by then.
                if (token.IsCancellationRequested) return TimedOut(T);

                switch (service.TryAdd(invocation.Option("title"), text, out Guid id))
                {
                    case QuickNoteAddResult.Added:
                        return AppCommandResponse.Done(T("Added the note."),
                            new Dictionary<string, object> { ["noteId"] = id.ToString("D") });
                    case QuickNoteAddResult.Failed:
                        return AppCommandResponse.Failed(AppCommandReasons.StoreFailed, AppCommandExit.Failed,
                            T("The note could not be written. It was not saved."));
                    default:
                        return ModuleOff(T);
                }
            }
            catch (Exception ex)
            {
                return Crashed("note add", ex, T);
            }
        }

        // ------------------------------------------------------------------ note list

        public static async Task<AppCommandResponse> NoteList(AppCommandInvocation invocation, Func<QuickNotesService?> provider,
            Func<string, string> T, CancellationToken token)
        {
            try
            {
                int limit = DefaultLimit;
                string? rawLimit = invocation.Option("limit");
                if (rawLimit != null
                    && !(int.TryParse(rawLimit, NumberStyles.Integer, CultureInfo.InvariantCulture, out limit)
                         && limit >= 1 && limit <= MaxLimit))
                    return AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                        Localization.Format(T, "The limit must be a whole number from {0} to {1}.", 1, MaxLimit));

                QuickNotesService? service = provider();
                if (service == null) return ModuleOff(T);
                AppCommandResponse? notReady = await Ready(service, T, token);
                if (notReady != null) return notReady;
                if (token.IsCancellationRequested) return TimedOut(T);

                // The very call the notes window makes: the name, the text and every checklist item, newest first.
                List<QuickNote> matches = service.Search(invocation.Option("query"));
                object[] notes = Entries(matches, limit);
                var data = new Dictionary<string, object>
                {
                    ["count"] = notes.Length,
                    ["total"] = matches.Count,
                    ["notes"] = notes,
                };
                return AppCommandResponse.Done(Localization.Format(T, "Notes listed: {0} of {1}.", notes.Length, matches.Count), data);
            }
            catch (Exception ex)
            {
                return Crashed("note list", ex, T);
            }
        }

        /// <summary>
        /// The first <paramref name="limit"/> notes as answer entries, fewer when the next one would take the answer past
        /// <see cref="MaxAnswerDataBytes"/> (one entry always fits: a title is cut, a preview is short).
        /// </summary>
        internal static object[] Entries(IReadOnlyList<QuickNote> notes, int limit)
        {
            var entries = new List<object>();
            var json = new JavaScriptSerializer();
            int used = 0;
            foreach (QuickNote note in notes)
            {
                if (entries.Count >= limit) break;
                Dictionary<string, object> entry = Entry(note);
                int bytes = Encoding.UTF8.GetByteCount(json.Serialize(entry)) + 1;
                if (entries.Count > 0 && used + bytes > MaxAnswerDataBytes) break;
                entries.Add(entry);
                used += bytes;
            }
            return entries.ToArray();
        }

        /// <summary>
        /// One note of a list: its id, its name when it has one, what the body says (never the name again) and its dates
        /// to the second in UTC, as the open exchange file writes them.
        /// </summary>
        internal static Dictionary<string, object> Entry(QuickNote note)
        {
            var entry = new Dictionary<string, object> { ["id"] = note.Id.ToString("D") };
            if (note.Title != null) entry["title"] = QuickNote.Clip(note.Title, TitleChars);
            entry["preview"] = note.BodyPreview(PreviewChars);
            entry["createdUtc"] = CyrFlipExchangeWriter.Date(note.CreatedAtUtc);
            entry["updatedUtc"] = CyrFlipExchangeWriter.Date(note.UpdatedAtUtc);
            return entry;
        }

        // ------------------------------------------------------------------ note get

        /// <summary>
        /// One note read back whole (ticket S0120, the C24 row): the answer holds the note as it is stored - a text note
        /// as <c>text</c> verbatim, a checklist note as <c>kind</c> and <c>items[]</c> - and, when a note is bigger than
        /// the answer bound, the only thing cut is the content, and the cut is told (<c>truncated</c> true, <c>length</c>
        /// the full character count). The parser has already refused an <c>--id</c> that is not a guid (exit 10).
        /// </summary>
        public static async Task<AppCommandResponse> NoteGet(AppCommandInvocation invocation, Func<QuickNotesService?> provider,
            Func<string, string> T, CancellationToken token)
        {
            try
            {
                // The manifest declares --id as a guid and required: the parser both refused anything else and
                // normalized it to the D form before this handler ran.
                Guid id = Guid.Parse(invocation.Option("id")!);
                QuickNotesService? service = provider();
                if (service == null) return ModuleOff(T);
                AppCommandResponse? notReady = await Ready(service, T, token);
                if (notReady != null) return notReady;
                if (token.IsCancellationRequested) return TimedOut(T);

                QuickNote? note = service.Find(id);
                if (note == null)
                    return AppCommandResponse.Refused(AppCommandReasons.TargetMissing, AppCommandExit.NotReady,
                        T("No note has this id."));
                return AppCommandResponse.Done(T("Read the note."), NoteData(note));
            }
            catch (Exception ex)
            {
                return Crashed("note get", ex, T);
            }
        }

        /// <summary>
        /// One note as <see cref="NoteGet"/> answers it: everything the store holds, cut only at <see cref="MaxAnswerDataBytes"/>
        /// and only in the note's content, surrogate-safe, with the cut told.
        /// </summary>
        internal static Dictionary<string, object> NoteData(QuickNote note)
        {
            var data = new Dictionary<string, object>
            {
                ["id"] = note.Id.ToString("D"),
                ["kind"] = note.Kind == QuickNoteKind.Checklist ? "checklist" : "text",
            };
            if (note.Title != null) data["title"] = note.Title;
            if (note.Kind == QuickNoteKind.Checklist)
            {
                var items = new List<object>();
                foreach (QuickNoteItem item in note.Items)
                    items.Add(new Dictionary<string, object> { ["text"] = item.Text, ["done"] = item.IsChecked });
                data["items"] = items.ToArray();
            }
            else
            {
                data["text"] = note.RawText;
            }
            data["createdUtc"] = CyrFlipExchangeWriter.Date(note.CreatedAtUtc);
            data["updatedUtc"] = CyrFlipExchangeWriter.Date(note.UpdatedAtUtc);
            data["truncated"] = false;
            data["length"] = note.CharacterCount;

            var json = new JavaScriptSerializer();
            if (Encoding.UTF8.GetByteCount(json.Serialize(data)) > MaxAnswerDataBytes)
                CutToBound(data, note, json);
            return data;
        }

        /// <summary>
        /// Shrink the answer of <see cref="NoteData"/> under the bound. Only the note's content gives way, and the caller
        /// is told: <c>truncated</c> true, <c>length</c> the full character count of the note as stored. The <paramref name="budget"/>
        /// shrinks by the measured overflow and the content is cut again, because JSON escaping can grow what the raw
        /// byte count promised fit; a title alone over the bound (a name is a short thing) is the one cut outside the content.
        /// </summary>
        private static void CutToBound(Dictionary<string, object> data, QuickNote note, JavaScriptSerializer json)
        {
            string fullText = note.Kind == QuickNoteKind.Checklist ? "" : note.RawText;
            int budget = MaxAnswerDataBytes;
            bool contentCut = false, titleCut = false;
            for (int round = 0; ; round++)
            {
                int room = RoomForContent(data, json);
                int contentBudget = Math.Max(0, Math.Min(budget, MaxAnswerDataBytes - room));
                if (note.Kind == QuickNoteKind.Checklist)
                {
                    List<object> kept = ItemsWithinBudget(note.Items, contentBudget, json, out bool cut);
                    contentCut = cut;
                    data["items"] = kept.ToArray();
                }
                else
                {
                    string cutText = CutToBytes(fullText, contentBudget);
                    contentCut = cutText.Length < fullText.Length;
                    data["text"] = cutText;
                }
                data["truncated"] = contentCut || titleCut;
                int size = Encoding.UTF8.GetByteCount(json.Serialize(data));
                if (size <= MaxAnswerDataBytes) return;
                if (data.TryGetValue("title", out object? title) && title is string longTitle && longTitle.Length > 0 && round >= 24)
                {
                    // The pathological case: even no content does not fit, so the name is the only thing left to cut.
                    string cutTitle = CutToBytes(longTitle, (int)(longTitle.Length * (long)MaxAnswerDataBytes / size) - 1);
                    titleCut = cutTitle.Length < longTitle.Length;
                    data["title"] = cutTitle;
                    continue;
                }
                if (contentBudget <= 0) return; // nothing of the content is left to take
                budget = Math.Max(0, contentBudget - (size - MaxAnswerDataBytes) - 64);
            }
        }

        /// <summary>The bytes of the answer without its content: everything else the answer carries, measured as serialized.</summary>
        private static int RoomForContent(Dictionary<string, object> data, JavaScriptSerializer json)
        {
            var rest = new Dictionary<string, object>(data);
            rest["text"] = "";
            rest["items"] = new object[0];
            return Encoding.UTF8.GetByteCount(json.Serialize(rest));
        }

        /// <summary>
        /// The checklist items whose serialized entries fit <paramref name="budget"/> bytes, whole ones first; when even the
        /// first does not fit, it is the one entry, its text cut. <paramref name="cut"/> is true when anything was left out or
        /// clipped - whole items kept is not the same as nothing lost. Never half an item, never a cut inside a surrogate pair.
        /// </summary>
        private static List<object> ItemsWithinBudget(List<QuickNoteItem> items, int budget, JavaScriptSerializer json, out bool cut)
        {
            cut = false;
            var kept = new List<object>();
            int used = 0;
            foreach (QuickNoteItem item in items)
            {
                Dictionary<string, object> entry = new Dictionary<string, object> { ["text"] = item.Text, ["done"] = item.IsChecked };
                int bytes = Encoding.UTF8.GetByteCount(json.Serialize(entry)) + 1;
                if (kept.Count > 0 && used + bytes > budget)
                {
                    cut = true;
                    break;
                }
                if (bytes > budget && kept.Count == 0)
                {
                    int room = Math.Max(0, budget - (bytes - Encoding.UTF8.GetByteCount(json.Serialize(item.Text))));
                    string clipped = CutToBytes(item.Text, room);
                    cut = clipped.Length < item.Text.Length;
                    entry["text"] = clipped;
                    kept.Add(entry);
                    break;
                }
                kept.Add(entry);
                used += bytes;
            }
            return kept;
        }

        /// <summary>The longest prefix of <paramref name="source"/> whose UTF-8 encoding fits <paramref name="budget"/> bytes, never cut inside a surrogate pair.</summary>
        private static string CutToBytes(string source, int budget)
        {
            if (budget < 0) return "";
            if (Encoding.UTF8.GetByteCount(source) <= budget) return source;
            int low = 0, high = source.Length;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (Encoding.UTF8.GetByteCount(source.Substring(0, mid)) <= budget) low = mid;
                else high = mid - 1;
            }
            if (low > 0 && low < source.Length && char.IsHighSurrogate(source[low - 1])) low--;
            return source.Substring(0, low);
        }

        // ------------------------------------------------------------------ note export

        /// <summary>What became of writing the export file; the token's outcome is answered by the caller.</summary>
        private enum ExportWriteOutcome
        {
            Saved,
            /// <summary>A file in the way and <c>--overwrite</c> was not given: 11 <c>exists</c>.</summary>
            Exists,
            /// <summary>Win32 39 or 112, the volume full: 13 <c>disk-full</c>.</summary>
            DiskFull,
            /// <summary>The folder vanished between the check and the write: 11 <c>target-missing</c>.</summary>
            FolderMissing,
            /// <summary>Anything else a write can meet: 1 <c>write-failed</c>.</summary>
            WriteFailed,
        }

        /// <summary>
        /// Every note written to one caller-named file (ticket S0120, the C25 row): a "CyrFlip Exchange Text v1" file,
        /// the Quick notes section only, newest first by creation - the file the window's own export writes, through the
        /// same writer (<see cref="CyrFlipExchangeWriter.ToText"/>) and with the whole-or-not-at-all discipline S0110
        /// shipped for <c>--out</c>: used exactly as given, checked on the pool under the budget before the journal is
        /// read, a temporary file in the target's folder renamed into place, never a folder created. No importer cap
        /// trims it: the caps are the import's, and this verb writes what the store holds.
        /// </summary>
        public static async Task<AppCommandResponse> NoteExport(AppCommandInvocation invocation, string workingDir,
            Func<QuickNotesService?> provider, IScreenshotFileSystem fs, TimeSpan budget, Func<string, bool> isRemote,
            Func<string, string> T, CancellationToken token)
        {
            try
            {
                string target = invocation.Option("out") ?? "";
                bool overwrite = invocation.Flag("overwrite");

                QuickNotesService? service = provider();
                if (service == null) return ModuleOff(T);

                // The path is checked before the journal is read: a caller learns his mistake before anything of the notes is touched.
                OutCheck check = await ResolveOut(target, workingDir, overwrite, fs, budget, isRemote, T, token);
                if (check.Plan == null)
                    return check.Refusal
                           ?? (token.IsCancellationRequested
                               ? TimedOut(T)
                               : OutOfReach(target, workingDir, isRemote, T, wrote: false));
                OutputPlan plan = check.Plan;

                AppCommandResponse? notReady = await Ready(service, T, token);
                if (notReady != null) return notReady;
                if (token.IsCancellationRequested) return TimedOut(T);

                // The one copy the owner thread hands to the pool; the list is the store's standard order, newest first.
                var notes = new List<QuickNote>(service.Notes);
                object? outcome = await RunBudgeted(() => (object)WriteExport(notes, plan, fs, token), budget, token);
                if (outcome == null) return token.IsCancellationRequested
                    ? TimedOut(T)
                    : OutOfReach(target, workingDir, isRemote, T, wrote: true);
                switch ((ExportWriteOutcome)outcome)
                {
                    case ExportWriteOutcome.Saved:
                        var data = new Dictionary<string, object>
                        {
                            ["path"] = plan.Path,
                            ["notes"] = notes.Count,
                        };
                        return AppCommandResponse.Done(Localization.Format(T, "Exported {0} notes.", notes.Count), data);
                    case ExportWriteOutcome.Exists:
                        return AppCommandResponse.Refused(AppCommandReasons.Exists, AppCommandExit.NotReady,
                            T("A file with this name already exists. Choose another name, or add --overwrite to replace it."));
                    case ExportWriteOutcome.DiskFull:
                        return AppCommandResponse.Failed(AppCommandReasons.DiskFull, AppCommandExit.NoSpace,
                            T("The disk is full. The export was not written."));
                    case ExportWriteOutcome.FolderMissing:
                        return AppCommandResponse.Failed(AppCommandReasons.TargetMissing, AppCommandExit.NotReady,
                            T("The folder in --out does not exist. CyrFlip does not create it."));
                    default:
                        return AppCommandResponse.Failed(AppCommandReasons.WriteFailed, AppCommandExit.Failed,
                            T("The file could not be written. Nothing was exported."));
                }
            }
            catch (Exception ex)
            {
                return Crashed("note export", ex, T);
            }
        }

        /// <summary>
        /// The whole-or-not-at-all write (S0110's own discipline, S0023's own container): the complete text goes into a
        /// temporary file in the target's folder, the file takes its final name in one step, and the temporary is removed
        /// on every path but success. A file that appeared after the plan was made survives untouched.
        /// </summary>
        private static ExportWriteOutcome WriteExport(List<QuickNote> notes, OutputPlan plan, IScreenshotFileSystem fs,
            CancellationToken token)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(CyrFlipExchangeWriter.ToText(DateTime.UtcNow, notes, null));
            string full = plan.Path;
            string folder = Path.GetDirectoryName(full) ?? ".";
            string temp = Path.Combine(folder, "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            bool moved = false;
            try
            {
                fs.WriteAllBytes(temp, bytes);
                // Nothing is renamed after the bound: the caller has been told "not proven" by then.
                if (token.IsCancellationRequested) return ExportWriteOutcome.WriteFailed;
                if (plan.Overwrite) fs.MoveReplace(temp, full);
                else if (fs.FileExists(full) || !fs.MoveNoReplace(temp, full)) return ExportWriteOutcome.Exists;
                moved = true;
                return ExportWriteOutcome.Saved;
            }
            catch (Exception ex)
            {
                if (ex is DirectoryNotFoundException) return ExportWriteOutcome.FolderMissing;
                if (ex is IOException io)
                {
                    int win32 = io.HResult & 0xFFFF;
                    if (win32 == 39 || win32 == 112) return ExportWriteOutcome.DiskFull;
                    if (win32 == 3) return ExportWriteOutcome.FolderMissing;
                }
                return ExportWriteOutcome.WriteFailed;
            }
            finally
            {
                if (!moved)
                {
                    // Best effort: a leftover dot-file in the caller's folder is the cost the failure path tries not to leave.
                    try { fs.DeleteFile(temp); } catch (Exception) { }
                }
            }
        }

        /// <summary>The plan for <c>--out</c>, or the answer that says why there is none.</summary>
        private sealed class OutCheck
        {
            public OutputPlan? Plan { get; set; }
            public AppCommandResponse? Refusal { get; set; }
        }

        /// <summary>
        /// The plan for <c>--out</c>, worked out on the pool under the budget: the path may meet a share that does not
        /// answer, and the thread that asks must never be the one the keyboard hook shares. No answer in the budget is
        /// the caller's to hear (<see cref="OutOfReach"/>), never an assumption the target was fine.
        /// </summary>
        private static async Task<OutCheck> ResolveOut(string target, string workingDir, bool overwrite,
            IScreenshotFileSystem fs, TimeSpan budget, Func<string, bool> isRemote, Func<string, string> T, CancellationToken token)
        {
            OutputResolution? resolution = await RunBudgeted(
                () => OutputPlan.ResolveFile(target, workingDir, overwrite, fs), budget, token);
            if (resolution == null) return new OutCheck();
            if (resolution.Plan != null) return new OutCheck { Plan = resolution.Plan };
            switch (resolution.Problem)
            {
                case OutputProblem.MissingFolder:
                    return new OutCheck
                    {
                        Refusal = AppCommandResponse.Failed(AppCommandReasons.TargetMissing, AppCommandExit.NotReady,
                            T("The folder in --out does not exist. CyrFlip does not create it.")),
                    };
                case OutputProblem.Exists:
                    return new OutCheck
                    {
                        Refusal = AppCommandResponse.Refused(AppCommandReasons.Exists, AppCommandExit.NotReady,
                            T("A file with this name already exists. Choose another name, or add --overwrite to replace it.")),
                    };
                case OutputProblem.NotAFile:
                    return new OutCheck
                    {
                        Refusal = AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                            T("--out must be the file to write: any name Windows accepts, with or without an extension - not a folder and not a path ending in a backslash.")),
                    };
                default:
                    return new OutCheck
                    {
                        Refusal = AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                            T("--out is not a path Windows accepts here: no device names, no control characters, none of < > \" | * ?, no name ending in a space or a dot (or with a space before its extension), a colon only after the drive letter.")),
                    };
            }
        }

        /// <summary>
        /// A path that neither answered nor resolved in the budget. A share that did not answer is the network's own
        /// verdict (15); a local path that could not be checked or written is the write's own failure (1) - this verb's
        /// exit table has no 14, and it is not the importer's or the journal's.
        /// </summary>
        private static AppCommandResponse OutOfReach(string target, string workingDir, Func<string, bool> isRemote,
            Func<string, string> T, bool wrote)
        {
            if (RemoteTarget(target, workingDir, isRemote))
                return AppCommandResponse.Failed(AppCommandReasons.Network, AppCommandExit.Network,
                    T("The share in --out did not answer in time. Nothing was exported."));
            return AppCommandResponse.Failed(AppCommandReasons.WriteFailed, AppCommandExit.Failed,
                wrote ? T("The file could not be written. Nothing was exported.")
                      : T("The folder in --out could not be checked. Nothing was exported."));
        }

        /// <summary>A path the caller named is remote by its own text, or - for a relative one - by the working directory it is taken from.</summary>
        private static bool RemoteTarget(string target, string workingDir, Func<string, bool> isRemote)
        {
            if (isRemote(target)) return true;
            bool rooted;
            try { rooted = Path.IsPathRooted(target); }
            catch (ArgumentException) { rooted = true; } // an illegal character: the path is the caller's own text, not the directory's
            return !rooted && isRemote(workingDir);
        }

        /// <summary>Work on the pool under the budget; null when it neither answered in time nor finished. Its fault is never surfaced later.</summary>
        private static async Task<T?> RunBudgeted<T>(Func<T> work, TimeSpan budget, CancellationToken token) where T : class
        {
            Task<T> task = Task.Run(work);
            Task winner;
            using (CancellationTokenSource stopTimer = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                winner = await Task.WhenAny(task, Task.Delay(budget, stopTimer.Token));
                stopTimer.Cancel();
            }
            if (winner != task)
            {
                _ = task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                return null;
            }
            if (task.Status != TaskStatus.RanToCompletion)
            {
                _ = task.Exception;
                return null;
            }
            return (T?)task.Result;
        }

        // ------------------------------------------------------------------ shared

        /// <summary>
        /// Waits - under the token, never blocking this thread - for the journal's replay, then answers what stops a verb
        /// from going on: the bound passed (2 <c>timeout</c>), the service was retired meanwhile (the module was switched
        /// off), the journal could not be read (1 <c>store-failed</c>). Null means go on. Internal because the selection
        /// family's note verb runs the same wait on the same service (ticket S0129).
        /// </summary>
        internal static async Task<AppCommandResponse?> Ready(QuickNotesService service, Func<string, string> T, CancellationToken token)
        {
            Task load = service.LoadAsync();
            if (!load.IsCompleted)
            {
                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (token.Register(() => cancelled.TrySetResult(true)))
                {
                    if (await Task.WhenAny(load, cancelled.Task) != load) return TimedOut(T);
                }
            }
            if (!service.IsLoaded) return ModuleOff(T);
            if (service.ReadFailure != null)
                return AppCommandResponse.Failed(AppCommandReasons.StoreFailed, AppCommandExit.Failed,
                    T("Could not read the quick notes journal. Saving is disabled until restart."));
            return null;
        }

        private static AppCommandResponse ModuleOff(Func<string, string> T)
            => AppCommandVerbs.ModuleOff(T("The quick notes are switched off. Turn them on in the settings of CyrFlip."));

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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CyrFlip
{
    /// <summary>
    /// One visible top-level window as the naming options see it (ticket S0129): the handle, the process it belongs to
    /// and its title. The host's list holds only windows with a title - an untitled window cannot be named by a title,
    /// and a script that means one names the program's other, titled window.
    /// </summary>
    internal sealed class SelectionWindow
    {
        public SelectionWindow(IntPtr window, uint pid, string title) { Window = window; Pid = pid; Title = title; }
        public IntPtr Window { get; }
        public uint Pid { get; }
        public string Title { get; }
    }

    /// <summary>
    /// The seam between the selection verbs and the machines they ride (ticket S0129): the windows there are to name,
    /// the raise that brings one to the foreground, the flip pipeline the two edit verbs run, the capture half the read
    /// verbs take, and the translation service the translate verb shares with <c>translate text</c>. The production host
    /// is the running copy's adapter; a test supplies a fake, so the whole answer table is a unit test with no window,
    /// no keyboard and no clipboard.
    /// </summary>
    internal interface ISelectionCommandsHost
    {
        /// <summary>The visible titled top-level windows open right now, in Windows' Z order: what the naming options match against.</summary>
        IReadOnlyList<SelectionWindow> TopLevelWindows();
        /// <summary>
        /// Bring the named window to the foreground and verify it: false when Windows would not give it the focus (settled).
        /// The capture's synthesized Ctrl+C and the paste's Ctrl+V both work on the foreground window only.
        /// </summary>
        bool BringToFront(IntPtr window);
        /// <summary>The keyboard layout live in the window, as a language code; "" when Windows reports none.</summary>
        string LayoutCodeOf(IntPtr window);
        /// <summary>The running copy's own UI language, which the <c>ui</c> token resolves against.</summary>
        string UiLanguage { get; }
        /// <summary>The installed keyboard layouts in Windows' own order: what a layout value is resolved against.</summary>
        IReadOnlyList<LayoutEntry> InstalledLayouts();
        /// <summary>The stored preference of the conversion table: whether a key that is punctuation in both layouts is swapped.</summary>
        bool StoredConvertSymbols { get; }
        /// <summary>The stored preference of the case flip: whether the physical CapsLock key is brought into line afterwards.</summary>
        bool StoredFlipCapsLock { get; }
        /// <summary>Claims the one clipboard guard the chords share, with the history suppression bracketed inside; false when another operation runs.</summary>
        bool BeginClipboardWork();
        /// <summary>Ends the history suppression and releases the claim, whatever the outcome was.</summary>
        void EndClipboardWork();
        /// <summary>Convert the foreground selection by an explicit layout pair - the flip pipeline's own entry (no auto-reverse).</summary>
        ClipboardHandler.FlipResult ConvertSelection(string fromKlid, string toKlid, bool convertSymbols);
        /// <summary>Flip the case of the foreground selection - the flip pipeline's own entry.</summary>
        ClipboardHandler.FlipResult FlipCaseSelection(bool syncCapsAfter);
        /// <summary>The capture half on its own: back up, take the selection, hand the clipboard straight back.</summary>
        ClipboardHandler.CaptureResult TakeSelection(out string text, int maxChars);
        /// <summary>Claims the one command-translation slot the two translate verbs share: false when a model call is in flight.</summary>
        bool BeginCommandTranslation();
        /// <summary>Releases the claim of <see cref="BeginCommandTranslation"/>, whatever the outcome was.</summary>
        void EndCommandTranslation();
        /// <summary>The model call - the same no-sink service path <c>translate text</c> uses, under the caller's token.</summary>
        Task<TranslationResult> TranslateAsync(string text, string targetCode, CancellationToken ct);
    }

    /// <summary>
    /// The selection family (ticket S0129, APP-COMMANDS 0.3, the research rows C34 to C37): four verbs that act on the
    /// selection of a window another program owns, named by <c>--window-pid</c> and/or <c>--window-title</c> (APP-COMMANDS
    /// item Q). The window is brought to the foreground first - the raise the context menu's focus hand-back uses - and one
    /// that will not take it answers <c>window-not-foreground</c>; then the verb rides the engines the chords already own:
    /// the flip pipeline for <c>selection convert</c> and <c>selection flip-case</c>, the capture half and the translation
    /// service for <c>selection translate</c>, the capture half and the notes service for <c>selection note</c>.
    ///
    /// <para>Order inside each verb: the arguments (10) - the strict parser has already refused an unknown option and a
    /// missing <c>--from</c>/<c>--to</c> - then the module where the verb has one (11 <c>module-off</c>, the S0121 order:
    /// the base's rule-13 gate has already answered 12), then the clipboard claim (the base's <c>busy</c> shape, no queue),
    /// then the window (none matched: 11 <c>target-missing</c>; several: 10 <c>bad-argument</c>), then the raise
    /// (<c>window-not-foreground</c>), then the work and its answer. The two read verbs release the clipboard claim before
    /// their slow half - the translator's own phase-A discipline, so a model call or a note write never holds the clipboard.</para>
    ///
    /// <para>The logs keep their disciplines: <c>app-commands.log</c> holds the verb and the outcome only (rule 16 - never a
    /// pid the caller typed is fine, never a title, never a selection character), and <c>translate.log</c> gains the selection
    /// command line beside the chord's and the text verb's own - the target code and the outcome, never a character of the text.</para>
    /// </summary>
    internal static class AppCommandsSelection
    {
        // ------------------------------------------------------------------ the window the caller names

        /// <summary>
        /// The window the naming options pick out of <paramref name="windows"/>, or <see cref="IntPtr.Zero"/> with the refusal set
        /// (item Q): an empty title, no match (11 <c>target-missing</c>), more than one match (10 <c>bad-argument</c>, and the
        /// message says how to be more exact). A pid and a title given together narrow each other - the one way to name one
        /// window of a program that has several.
        /// </summary>
        internal static IntPtr Named(IReadOnlyList<SelectionWindow> windows, AppCommandInvocation invocation,
            Func<string, string> T, out AppCommandResponse? refusal)
        {
            refusal = null;
            string? pidText = invocation.Option("window-pid");
            string? titleText = invocation.Option("window-title");
            if (pidText == null && titleText == null)
            {
                refusal = Usage(T("Name the window with --window-pid, --window-title, or both."));
                return IntPtr.Zero;
            }
            uint pid = 0;
            if (pidText != null
                && (!uint.TryParse(pidText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out pid)))
            {
                refusal = Usage(T("The value of --window-pid is not a whole number. Give the number the program reports as its process id."));
                return IntPtr.Zero;
            }
            if (titleText != null && titleText.Trim().Length == 0)
            {
                refusal = Usage(T("Give at least one word of the window's title with --window-title."));
                return IntPtr.Zero;
            }

            var matches = new List<SelectionWindow>();
            foreach (SelectionWindow candidate in windows)
            {
                if (pidText != null && candidate.Pid != pid) continue;
                if (titleText != null && candidate.Title.IndexOf(titleText.Trim(), StringComparison.OrdinalIgnoreCase) < 0) continue;
                matches.Add(candidate);
            }
            if (matches.Count == 0)
            {
                refusal = AppCommandResponse.Failed(AppCommandReasons.TargetMissing, AppCommandExit.NotReady,
                    T("No open window matches. Check the pid, or give a longer part of the window's title."));
                return IntPtr.Zero;
            }
            if (matches.Count > 1)
            {
                refusal = Usage(T("More than one open window matches. Give the pid and a longer part of the title together, so only one window is meant."));
                return IntPtr.Zero;
            }
            return matches[0].Window;
        }

        /// <summary>
        /// The named window, brought to the foreground and verified; <see cref="IntPtr.Zero"/> with the refusal set. The claim
        /// of <see cref="ISelectionCommandsHost.BeginClipboardWork"/> is the caller's - it must already be held, so a verb that
        /// meets a busy guard answers before it touches a window.
        /// </summary>
        private static IntPtr FocusedTarget(ISelectionCommandsHost host, AppCommandInvocation invocation,
            Func<string, string> T, out AppCommandResponse? refusal)
        {
            IntPtr window = Named(host.TopLevelWindows(), invocation, T, out refusal);
            if (refusal != null) return IntPtr.Zero;
            if (!host.BringToFront(window))
            {
                refusal = AppCommandResponse.Failed(AppCommandReasons.WindowNotForeground, AppCommandExit.NotReady,
                    T("The window did not come to the front, so CyrFlip cannot read or write in it. Click once in that window, then run the command again."));
                return IntPtr.Zero;
            }
            return window;
        }

        // ------------------------------------------------------------------ selection convert

        /// <summary>
        /// <c>selection convert --from &lt;code|klid&gt; --to &lt;code|klid&gt;</c> (row C34): the flip pipeline over the named
        /// window's selection, with the direction the caller named - never the chord's auto-reverse - and the layout values
        /// resolved by <c>text convert</c>'s own grammar (its sentences, its exits).
        /// </summary>
        public static Task<AppCommandResponse> SelectionConvert(AppCommandInvocation invocation, ISelectionCommandsHost host,
            Func<string, string> T, CancellationToken token)
        {
            try
            {
                var catalog = new LayoutCatalog(host.InstalledLayouts());
                string? fromKlid = AppCommandsLayoutText.Side(invocation, "from", catalog, T, out bool fromInstalled, out AppCommandResponse? bad);
                if (fromKlid == null) return Done(bad!);
                string? toKlid = AppCommandsLayoutText.Side(invocation, "to", catalog, T, out bool toInstalled, out bad);
                if (toKlid == null) return Done(bad!);
                if (string.Equals(fromKlid, toKlid, StringComparison.OrdinalIgnoreCase))
                    return Done(Usage(T("The two layouts are the same. Name two different layouts.")));
                if (!(fromInstalled && toInstalled) && !KeyboardLayoutConverter.IsBuiltInPair(fromKlid, toKlid))
                    return Done(NotInstalledHere(invocation.Option(fromInstalled ? "to" : "from") ?? "", T));

                if (!host.BeginClipboardWork())
                    return Done(ClipboardBusy(T));
                try
                {
                    if (FocusedTarget(host, invocation, T, out bad) == IntPtr.Zero)
                        return Done(bad!);
                    if (token.IsCancellationRequested) return Done(TimedOut(T));
                    ClipboardHandler.FlipResult result = host.ConvertSelection(fromKlid, toKlid, host.StoredConvertSymbols);
                    return Done(ChangedAnswer(result, T("Converted the selection."),
                        T("The selection already read the same; nothing was converted."), T));
                }
                finally
                {
                    host.EndClipboardWork();
                }
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                return Done(Crashed("selection convert", ex, T));
            }
        }

        // ------------------------------------------------------------------ selection flip-case

        /// <summary>
        /// <c>selection flip-case</c> (row C35): the flip pipeline's own case flip over the named window's selection, with the
        /// stored CapsLock preference applied exactly as the chord applies it - an absolute state, not a blind toggle.
        /// </summary>
        public static Task<AppCommandResponse> SelectionFlipCase(AppCommandInvocation invocation, ISelectionCommandsHost host,
            Func<string, string> T, CancellationToken token)
        {
            try
            {
                if (!host.BeginClipboardWork())
                    return Done(ClipboardBusy(T));
                try
                {
                    if (FocusedTarget(host, invocation, T, out AppCommandResponse? bad) == IntPtr.Zero)
                        return Done(bad!);
                    if (token.IsCancellationRequested) return Done(TimedOut(T));
                    ClipboardHandler.FlipResult result = host.FlipCaseSelection(host.StoredFlipCapsLock);
                    return Done(ChangedAnswer(result, T("Flipped the case of the selection."),
                        T("Nothing in the selection had a letter case to flip."), T));
                }
                finally
                {
                    host.EndClipboardWork();
                }
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                return Done(Crashed("selection flip-case", ex, T));
            }
        }

        // ------------------------------------------------------------------ selection note

        /// <summary>
        /// <c>selection note</c> (row C37): the capture half over the named window's selection, then the quick notes service's
        /// own <c>note add</c> write. The service is resolved by the caller on the UI thread (it owns the tray and the window
        /// list state); null means the module is off. The clipboard claim brackets the capture only - the translator's own
        /// phase-A discipline - so the store write never holds the clipboard. The capture copy is suppressed from the history
        /// (the scaffolding rule): the suppression rides the claim.
        /// </summary>
        public static async Task<AppCommandResponse> SelectionNote(AppCommandInvocation invocation, ISelectionCommandsHost host,
            QuickNotesService? service, Func<string, string> T, CancellationToken token)
        {
            try
            {
                if (service == null)
                    return AppCommandVerbs.ModuleOff(T("The quick notes are switched off. Turn them on in the settings of CyrFlip."));
                string text;
                if (!host.BeginClipboardWork())
                    return ClipboardBusy(T);
                try
                {
                    if (FocusedTarget(host, invocation, T, out AppCommandResponse? bad) == IntPtr.Zero)
                        return bad!;
                    ClipboardHandler.CaptureResult captured = host.TakeSelection(out text, QuickNote.MaxBytes);
                    if (captured != ClipboardHandler.CaptureResult.Captured || text.Length == 0)
                        return CaptureAnswer(captured, QuickNote.MaxBytes, T);
                }
                finally
                {
                    host.EndClipboardWork();
                }

                // The store's own wait, the same one note add runs: the journal's replay under the token.
                AppCommandResponse? notReady = await AppCommandsNotes.Ready(service, T, token).ConfigureAwait(false);
                if (notReady != null) return notReady;
                if (token.IsCancellationRequested) return TimedOut(T);
                switch (service.TryAdd(null, text, out Guid id))
                {
                    case QuickNoteAddResult.Added:
                        return AppCommandResponse.Done(T("Added the note."),
                            new Dictionary<string, object> { ["noteId"] = id.ToString("D") });
                    case QuickNoteAddResult.Failed:
                        return AppCommandResponse.Failed(AppCommandReasons.StoreFailed, AppCommandExit.Failed,
                            T("The note could not be written. It was not saved."));
                    default:
                        return AppCommandVerbs.ModuleOff(T("The quick notes are switched off. Turn them on in the settings of CyrFlip."));
                }
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                return Crashed("selection note", ex, T);
            }
        }

        // ------------------------------------------------------------------ selection translate

        /// <summary>
        /// <c>selection translate --to &lt;language|ui|active&gt;</c> (row C36): the capture half over the named window's
        /// selection, then the model call on the same no-sink service path <c>translate text</c> uses. The <c>active</c> token is
        /// honest on this verb and resolved from the named window's own live layout <see cref="ISelectionCommandsHost.LayoutCodeOf"/>;
        /// the input is cut at the service's own cap with the cut answered; the model claim is shared with <c>translate text</c>,
        /// so one model call of either spelling runs at a time.
        /// </summary>
        public static async Task<AppCommandResponse> SelectionTranslate(AppCommandInvocation invocation, ISelectionCommandsHost? host,
            Func<string, string> T, CancellationToken token)
        {
            try
            {
                string? to = invocation.Option("to");
                if (to == null || to.Trim().Length == 0)
                    return Usage(T("Give the target language with --to: a language code, ui, or active."));
                if (host == null)
                    return AppCommandVerbs.ModuleOff(T("The translator is switched off. Turn it on in the settings of CyrFlip."));

                string text;
                IntPtr targetWindow;
                if (!host.BeginClipboardWork())
                    return ClipboardBusy(T);
                try
                {
                    targetWindow = FocusedTarget(host, invocation, T, out AppCommandResponse? bad);
                    if (bad != null) return bad;
                    ClipboardHandler.CaptureResult captured = host.TakeSelection(out text, ClipboardHandler.MaxFlipChars);
                    if (captured != ClipboardHandler.CaptureResult.Captured || text.Length == 0)
                        return CaptureAnswer(captured, ClipboardHandler.MaxFlipChars, T);
                }
                finally
                {
                    host.EndClipboardWork();
                }

                if (token.IsCancellationRequested) return TimedOut(T);
                string targetCode = TranslationLanguages.Resolve(to, host.UiLanguage, host.LayoutCodeOf(targetWindow));
                string input = TranslationService.Truncate(text, TranslationService.MaxChars, out bool truncated);
                TranslateLog.Log(SelectionCommandLine(targetCode, truncated));

                TranslationResult result;
                if (!host.BeginCommandTranslation())
                    return AppCommandResponse.BusyNow(
                        T("Another command translation is still running. Wait for its answer, then ask again."));
                try
                {
                    result = await host.TranslateAsync(input, targetCode, token).ConfigureAwait(false);
                }
                finally
                {
                    host.EndCommandTranslation();
                }
                TranslateLog.Log(AppCommandsTranslate.ResultLine(result));
                return AnswerTranslation(result, truncated, T);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                return Crashed("selection translate", ex, T);
            }
        }

        // ------------------------------------------------------------------ the translate.log lines

        /// <summary>
        /// The selection command's own line of <c>translate.log</c>, beside the chord's and the text verb's: the target code and
        /// the cut - never a character of the text (the module's own rule since its first day).
        /// </summary>
        internal static string SelectionCommandLine(string targetCode, bool truncated)
            => "selection command: into " + targetCode
               + (truncated ? ", input cut at " + TranslationService.MaxChars + " chars" : "");

        // ------------------------------------------------------------------ shared answers

        /// <summary>The answer of the two edit verbs: the pipeline's outcome into the family's own words and data.</summary>
        private static AppCommandResponse ChangedAnswer(ClipboardHandler.FlipResult result, string doneMessage,
            string noChangeMessage, Func<string, string> T)
        {
            switch (result)
            {
                case ClipboardHandler.FlipResult.Flipped:
                    return AppCommandResponse.Done(doneMessage, new Dictionary<string, object> { ["changed"] = true });
                case ClipboardHandler.FlipResult.NoChange:
                    return AppCommandResponse.Done(noChangeMessage, new Dictionary<string, object> { ["changed"] = false });
                default:
                    return CaptureAnswer(ToCaptureResult(result), ClipboardHandler.MaxFlipChars, T);
            }
        }

        /// <summary>The outcome of a capture the whole family shares: nothing selected, over the cap, focus lost, failed.</summary>
        private static AppCommandResponse CaptureAnswer(ClipboardHandler.CaptureResult captured, int maxChars,
            Func<string, string> T)
        {
            switch (captured)
            {
                case ClipboardHandler.CaptureResult.NoSelection:
                    return AppCommandResponse.Failed(AppCommandReasons.NoSelection, AppCommandExit.NotReady,
                        T("The window has nothing selected."));
                case ClipboardHandler.CaptureResult.TooLarge:
                    return AppCommandResponse.Refused(AppCommandReasons.TooLarge, AppCommandExit.Usage,
                        Localization.Format(T, "The selection is longer than the limit of {0} characters, so it was not read.", maxChars));
                case ClipboardHandler.CaptureResult.Cancelled:
                    return AppCommandResponse.Failed(AppCommandReasons.Error, AppCommandExit.Failed,
                        T("The window lost the focus while CyrFlip was working, so the command stopped. Run it again."));
                default:
                    return AppCommandResponse.Failed(AppCommandReasons.Error, AppCommandExit.Failed,
                        T("The selection could not be read. Another program may be holding the clipboard; try again."));
            }
        }

        /// <summary>The translate outcome table - the one <c>translate text</c> answers with (its own sentences), plus the bound check.</summary>
        private static AppCommandResponse AnswerTranslation(TranslationResult result, bool truncated, Func<string, string> T)
        {
            switch (result.Status)
            {
                case TranslationStatus.Ok:
                    string message = T("The translation is ready.");
                    if (result.Partial)
                        message += " " + T("The model stopped early, so the translation is partial.");
                    if (truncated)
                        message += " " + Localization.Format(T,
                            "The text was longer than {0} characters, so the translation covers the first {0}.",
                            TranslationService.MaxChars);
                    return Bounded(message, new Dictionary<string, object>
                    {
                        ["text"] = result.Text,
                        ["model"] = result.Model,
                        ["partial"] = result.Partial,
                        ["truncated"] = truncated,
                    }, T);

                case TranslationStatus.NotInstalled:
                    return AppCommandResponse.Failed(AppCommandReasons.Network, AppCommandExit.Network,
                        T("The translation server is not installed on this computer."));

                case TranslationStatus.NotRunning:
                    return AppCommandResponse.Failed(AppCommandReasons.Network, AppCommandExit.Network,
                        T("The translation server is not running, and starting it automatically is switched off in the settings of CyrFlip."));

                case TranslationStatus.StartFailed:
                    return AppCommandResponse.Failed(AppCommandReasons.Network, AppCommandExit.Network,
                        T("The translation server could not be started."));

                case TranslationStatus.Unreachable:
                    return AppCommandResponse.Failed(AppCommandReasons.Network, AppCommandExit.Network,
                        Localization.Format(T, "The translation server at {0} did not answer.", result.Server));

                case TranslationStatus.NoModel:
                    return AppCommandResponse.Failed(AppCommandReasons.NoModel, AppCommandExit.NotReady,
                        T("The server answered, but no model is installed on it. The settings of CyrFlip say how to get one."));

                case TranslationStatus.Timeout:
                case TranslationStatus.Cancelled:
                    return AppCommandResponse.Failed(AppCommandReasons.Timeout, AppCommandExit.NotProven,
                        T("CyrFlip did not answer in time. It may still be working."));

                case TranslationStatus.ServerError:
                    return AppCommandResponse.Failed(AppCommandReasons.Error, AppCommandExit.Failed,
                        T("The translation server refused the request."));

                default:
                    return AppCommandResponse.Failed(AppCommandReasons.Error, AppCommandExit.Failed,
                        T("The model could not translate this text."));
            }
        }

        /// <summary>
        /// The answer must fit the one response line the caller reads: a translation of a text the service itself caps always does,
        /// and a runaway model is answered rather than cut (the <c>response-too-large</c> token of amendment M, S0117's precedent).
        /// </summary>
        private static AppCommandResponse Bounded(string message, Dictionary<string, object> data, Func<string, string> T)
        {
            AppCommandResponse response = AppCommandResponse.Done(message, data);
            response.RequestId = new string('0', 36);
            int bytes = Encoding.UTF8.GetByteCount(AppCommandProtocol.Serialize(response)) + 1;
            response.RequestId = "";
            return bytes <= AppCommandLine.MaxRequestBytes
                ? response
                : AppCommandResponse.Failed("response-too-large", AppCommandExit.Failed,
                    T("The answer is too large. Translate a shorter piece of text."));
        }

        /// <summary>The refusal of a taken clipboard guard: the base's <c>busy</c> shape, the chord's own sentence.</summary>
        private static AppCommandResponse ClipboardBusy(Func<string, string> T)
            => AppCommandResponse.BusyNow(T("Another clipboard operation has not finished yet. Try again in a second."));

        private static AppCommandResponse TimedOut(Func<string, string> T)
            => AppCommandResponse.Failed(AppCommandReasons.Timeout, AppCommandExit.NotProven,
                T("CyrFlip did not answer in time. It may still be working."));

        private static AppCommandResponse NotInstalledHere(string raw, Func<string, string> T)
            => AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                Localization.Format(T, "The keyboard layout \"{0}\" is not installed on this PC, so the text cannot be converted from or to it.", Show(raw)));

        /// <summary>A layout value as a message repeats it: short, one line (the <c>text convert</c> shape).</summary>
        private static string Show(string value)
        {
            int max = AppCommandsLayoutText.ValueChars;
            var shown = new StringBuilder();
            foreach (char c in value.Length <= max ? value : value.Substring(0, max))
                shown.Append(char.IsControl(c) ? ' ' : c);
            if (value.Length > max) shown.Append("..");
            return shown.ToString();
        }

        /// <summary>The pipeline's paste-having outcomes onto the capture table the read verbs answer.</summary>
        private static ClipboardHandler.CaptureResult ToCaptureResult(ClipboardHandler.FlipResult result)
        {
            switch (result)
            {
                case ClipboardHandler.FlipResult.Cancelled: return ClipboardHandler.CaptureResult.Cancelled;
                case ClipboardHandler.FlipResult.Failed: return ClipboardHandler.CaptureResult.Failed;
                case ClipboardHandler.FlipResult.TooLarge: return ClipboardHandler.CaptureResult.TooLarge;
                case ClipboardHandler.FlipResult.NoSelection: return ClipboardHandler.CaptureResult.NoSelection;
                default: return ClipboardHandler.CaptureResult.Captured;
            }
        }

        private static AppCommandResponse Usage(string message)
            => AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage, message);

        /// <summary>A fault would otherwise fault the caller's wait: the caller always gets an answer. Only the type is logged.</summary>
        private static AppCommandResponse Crashed(string verb, Exception ex, Func<string, string> T)
        {
            LauncherLog.Log("Command: " + verb + " failed: " + ex.GetType().Name);
            return AppCommandResponse.Failed(AppCommandReasons.Error, AppCommandExit.Failed,
                T("The command failed. The log bundle on the About page has the details."));
        }

        private static Task<AppCommandResponse> Done(AppCommandResponse response) => Task.FromResult(response);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CyrFlip
{
    /// <summary>
    /// What the send's <c>--file</c> needs of the file system: whether the named file exists, how long it is and when
    /// it was last written. Null means the file system did not answer (a share cut off, the budget out) - never
    /// "missing": a file that is not there answers <see cref="Exists"/> false. The app answers with <see cref="System.IO"/>
    /// on the pool under the verb's budget; the tests fake the delegate.
    /// </summary>
    internal sealed class ExchangeSourceStat
    {
        public bool Exists;
        public long Length;
        public DateTime LastWriteUtc;
    }

    /// <summary>The one seam of <c>--file</c>: <paramref name="path"/> to its existence, length and last-write stamp, or null when nothing answered.</summary>
    internal delegate ExchangeSourceStat? ExchangeSourceStatOf(string path);

    /// <summary>
    /// A file the receive copies from (the received-files folder) into the target's temporary. The app answers with a
    /// buffered stream copy - a file may be as large as the exchange cap, so it is never held in memory; the tests
    /// fake the delegate.
    /// </summary>
    internal delegate void ExchangeFileCopy(string from, string to);

    /// <summary>What <c>ResolveSendSource</c> decided: a refusal, a built payload, or neither (the wave-1 clipboard source).</summary>
    internal sealed class SendSource
    {
        public AppCommandResponse? Refusal;
        public ExchangeClipboardPayload? Payload;
        /// <summary>No <c>--file</c> and no <c>--text</c>: the verb keeps its wave-1 behaviour byte for byte.</summary>
        public bool Wave1;
    }

    /// <summary>What became of writing a received value into the caller's file.</summary>
    internal enum ReceivedWriteOutcome
    {
        Saved,
        /// <summary>A file in the way and <c>--overwrite</c> was not given: 11 <c>exists</c>.</summary>
        Exists,
        /// <summary>Win32 39 or 112, the volume full: exit 13.</summary>
        DiskFull,
        /// <summary>The target's folder vanished between the plan and the write: 11 <c>target-missing</c>.</summary>
        FolderMissing,
        /// <summary>Anything else a write can meet: 1 <c>write-failed</c>.</summary>
        WriteFailed,
    }

    /// <summary>
    /// The send and get options of ticket S0121 (rows C32 and C33) on the wave-1 exchange verbs:
    /// <c>clipboard send --file <path></c> / <c>--text</c> builds the module's own payload from a named file or a
    /// given text instead of reading the clipboard, and <c>clipboard get --out</c> receives the incoming value
    /// straight into the caller's file through the <c>--out</c> resolver and write discipline S0108 shipped
    /// (<see cref="OutputPlan.ResolveFile"/>, ticket S0120's file target). Pure over the parsed invocation and small
    /// delegates - the file probe, the file copy and the saver's file-system seam - so the tests fake no network and
    /// touch no disk; the running copy supplies the pool, the budget and the real delegates. Nothing here puts a
    /// caller's path, text or a served file name into a message, an exception text or a log line.
    /// </summary>
    internal static class AppCommandsClipboardExchange
    {
        // ------------------------------------------------------------------ the send source

        /// <summary>
        /// Resolves <c>--file</c> / <c>--text</c> into the module's own payload, or the answer that says why it did
        /// not. Exactly one of the two options may be given (both is exit 10); neither is the wave-1 verb unchanged.
        /// A file's existence, length and stamp are answered under the budget - a dead share must not eat the verb's
        /// bound - and the payload carries the stamp the module's own source-changed check compares. The payload cap
        /// is the module's own (<see cref="ExchangeClipboardService.DefaultClipboardBytes"/>), named in the answer and
        /// never the path; the text is normalized exactly as the clipboard text path and bounded by the parser's
        /// 32 KiB argument cap before this runs.
        /// </summary>
        public static async Task<SendSource> ResolveSendSource(AppCommandInvocation invocation, string workingDir,
            TimeSpan budget, ExchangeSourceStatOf statOf, Func<string, bool> isRemote,
            Func<string, string> T, CancellationToken token)
        {
            string? file = invocation.Option("file");
            string? text = invocation.Option("text");
            if (file != null && text != null)
                return new SendSource
                {
                    Refusal = AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                        T("Give --file or --text, not both.")),
                };
            if (file != null) return await FileSource(file, workingDir, budget, statOf, isRemote, T, token);
            if (text != null) return TextSource(text, T);
            return new SendSource { Wave1 = true };
        }

        private static SendSource TextSource(string text, Func<string, string> T)
        {
            if (text.Length == 0)
                return new SendSource
                {
                    Refusal = AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                        T("The text needs at least one character.")),
                };
            byte[] utf8 = Encoding.UTF8.GetBytes(ExchangeClipboardSource.NormalizeLineEnds(text));
            return new SendSource
            {
                Payload = new ExchangeClipboardPayload
                {
                    Kind = ExchangeClipboardPayload.Text,
                    Bytes = utf8,
                    ClearSize = utf8.LongLength,
                },
            };
        }

        private static async Task<SendSource> FileSource(string path, string workingDir, TimeSpan budget,
            ExchangeSourceStatOf statOf, Func<string, bool> isRemote, Func<string, string> T, CancellationToken token)
        {
            string full = FullSourcePath(path, workingDir);
            ExchangeSourceStat? stat = await RunBudgeted(() => statOf(full), budget, token);
            if (stat == null)
                return new SendSource
                {
                    Refusal = token.IsCancellationRequested
                        ? TimedOut(T)
                        : RemoteTarget(path, workingDir, isRemote)
                            ? AppCommandResponse.Failed(AppCommandReasons.Network, AppCommandExit.Network,
                                T("The share in --file did not answer in time. Nothing was sent."))
                            : AppCommandResponse.Failed(AppCommandReasons.WriteFailed, AppCommandExit.Failed,
                                T("The file in --file could not be checked. Nothing was sent.")),
                };
            if (!stat.Exists || stat.Length < 1)
                return new SendSource
                {
                    // A file the module cannot carry has nothing to send; the message never names the path.
                    Refusal = AppCommandResponse.Failed(AppCommandReasons.TargetMissing, AppCommandExit.NotReady,
                        stat.Exists ? T("The file in --file has nothing to send.") : T("The file in --file does not exist. Nothing was sent.")),
                };
            if (stat.Length > ExchangeClipboardService.DefaultClipboardBytes)
                return new SendSource
                {
                    Refusal = AppCommandResponse.Refused(AppCommandReasons.TooLarge, AppCommandExit.Usage,
                        Localization.Format(T, "The file is over the limit of {0} bytes. Nothing was sent.",
                            ExchangeClipboardService.DefaultClipboardBytes)),
                };
            return new SendSource
            {
                Payload = new ExchangeClipboardPayload
                {
                    Kind = ExchangeClipboardPayload.File,
                    ClearSize = stat.Length,
                    FilePath = full,
                    // The wire name and the type are the module's own rules for a file the clipboard names.
                    Name = ExchangeClipboardSource.WireName(Path.GetFileName(full)),
                    MimeType = MimeTypes.Guess(Path.GetExtension(full)),
                    FileLastWriteUtcTicks = stat.LastWriteUtc.Ticks,
                },
            };
        }

        /// <summary>The caller's path against his own working directory; a path Windows refuses is left for the probe to say it cannot check.</summary>
        private static string FullSourcePath(string path, string workingDir)
        {
            try
            {
                string combined = Path.IsPathRooted(path) ? path : Path.Combine(workingDir ?? "", path);
                return Path.GetFullPath(combined);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return path;
            }
        }

        /// <summary>A path the caller named is remote by its own text, or - for a relative one - by the working directory it is taken from (S0120's own rule).</summary>
        internal static bool RemoteTarget(string target, string workingDir, Func<string, bool> isRemote)
        {
            if (isRemote(target)) return true;
            bool rooted;
            try { rooted = Path.IsPathRooted(target); }
            catch (ArgumentException) { rooted = true; } // an illegal character: the path is the caller's own text, not the directory's
            return !rooted && isRemote(workingDir);
        }

        // ------------------------------------------------------------------ the get --out target

        /// <summary>
        /// The plan for <c>--out</c>, worked out on the pool under the budget (a share in the target's folder may not
        /// answer), or the answer that says why there is none - S0120's own mapping of <see cref="OutputPlan.ResolveFile"/>'s
        /// problems. The plan is made before the fetch starts, so the caller learns his mistake with nothing received.
        /// </summary>
        public static async Task<(OutputPlan? Plan, AppCommandResponse? Refusal)> ResolveOut(string target,
            string workingDir, bool overwrite, IScreenshotFileSystem fs, TimeSpan budget,
            Func<string, bool> isRemote, Func<string, string> T, CancellationToken token)
        {
            OutputResolution? resolution = await RunBudgeted(() => OutputPlan.ResolveFile(target, workingDir, overwrite, fs), budget, token);
            if (resolution == null)
                return (null,
                    token.IsCancellationRequested ? TimedOut(T)
                        : RemoteTarget(target, workingDir, isRemote)
                            ? AppCommandResponse.Failed(AppCommandReasons.Network, AppCommandExit.Network,
                                T("The share in --out did not answer in time. Nothing was received."))
                            : AppCommandResponse.Failed(AppCommandReasons.WriteFailed, AppCommandExit.Failed,
                                T("The folder in --out could not be checked. Nothing was received.")));
            if (resolution.Plan != null) return (resolution.Plan, null);
            switch (resolution.Problem)
            {
                case OutputProblem.MissingFolder:
                    return (null, AppCommandResponse.Failed(AppCommandReasons.TargetMissing, AppCommandExit.NotReady,
                        T("The folder in --out does not exist. CyrFlip does not create it.")));
                case OutputProblem.Exists:
                    return (null, AppCommandResponse.Refused(AppCommandReasons.Exists, AppCommandExit.NotReady,
                        T("A file with this name already exists. Choose another name, or add --overwrite to replace it.")));
                case OutputProblem.NotAFile:
                    return (null, AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                        T("--out must be the file to write: any name Windows accepts, with or without an extension - not a folder and not a path ending in a backslash.")));
                default:
                    return (null, AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                        T("--out is not a path Windows accepts here: no device names, no control characters, none of < > \" | * ?, no name ending in a space or a dot (or with a space before its extension), a colon only after the drive letter.")));
            }
        }

        // ------------------------------------------------------------------ the received write

        /// <summary>
        /// Writes a received value into the caller's file: a text value as UTF-8 bytes, a picture as its bytes, a file
        /// copied from the received-files folder - all through the whole-or-not-at-all discipline
        /// (<see cref="WriteReceivedValue"/>: a temporary in the target's folder, then a rename), so the file appears
        /// whole or not at all. The clipboard is not touched - the point of <c>--out</c> is that a scheduled receive
        /// never hijacks what a person is working on.
        /// </summary>
        public static ReceivedWriteOutcome WriteReceivedValue(ExchangeReceivedValue value, OutputPlan plan,
            IScreenshotFileSystem fs, ExchangeFileCopy copy, CancellationToken token)
        {
            string full = plan.Path;
            string folder = Path.GetDirectoryName(full) ?? ".";
            string temp = Path.Combine(folder, "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            bool moved = false;
            try
            {
                if (value.Kind == ExchangeClipboardPayload.File && value.FilePath.Length > 0)
                    copy(value.FilePath, temp);
                else if (value.Bytes != null)
                    fs.WriteAllBytes(temp, value.Bytes);
                else
                    return ReceivedWriteOutcome.WriteFailed;
                // Nothing is renamed after the bound: the caller has been told "not proven" by then.
                if (token.IsCancellationRequested) return ReceivedWriteOutcome.WriteFailed;
                if (plan.Overwrite) fs.MoveReplace(temp, full);
                else if (fs.FileExists(full) || !fs.MoveNoReplace(temp, full)) return ReceivedWriteOutcome.Exists;
                moved = true;
                return ReceivedWriteOutcome.Saved;
            }
            catch (Exception ex)
            {
                if (ex is DirectoryNotFoundException) return ReceivedWriteOutcome.FolderMissing;
                if (ex is IOException io)
                {
                    int win32 = io.HResult & 0xFFFF;
                    if (win32 == 39 || win32 == 112) return ReceivedWriteOutcome.DiskFull;
                    if (win32 == 3) return ReceivedWriteOutcome.FolderMissing;
                }
                return ReceivedWriteOutcome.WriteFailed;
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

        /// <summary>The answer to a finished write - the module's own members (<c>kind</c>, <c>sizeBytes</c>, <c>savedTo</c>) on success, the family's exit table otherwise.</summary>
        public static AppCommandResponse OutcomeAnswer(ReceivedWriteOutcome outcome, OutputPlan plan,
            ExchangeReceivedValue value, Func<string, string> T)
        {
            switch (outcome)
            {
                case ReceivedWriteOutcome.Saved:
                    return AppCommandResponse.Done(T("Received into the file."),
                        new Dictionary<string, object>
                        {
                            ["kind"] = value.Kind,
                            ["sizeBytes"] = value.ClearSize,
                            ["savedTo"] = plan.Path,
                        });
                case ReceivedWriteOutcome.Exists:
                    return AppCommandResponse.Refused(AppCommandReasons.Exists, AppCommandExit.NotReady,
                        T("A file with this name already exists. Choose another name, or add --overwrite to replace it."));
                case ReceivedWriteOutcome.DiskFull:
                    return AppCommandResponse.Failed(AppCommandReasons.DiskFull, AppCommandExit.NoSpace,
                        T("The disk is full. The receive was not written."));
                case ReceivedWriteOutcome.FolderMissing:
                    return AppCommandResponse.Failed(AppCommandReasons.TargetMissing, AppCommandExit.NotReady,
                        T("The folder in --out does not exist. CyrFlip does not create it."));
                default:
                    return AppCommandResponse.Failed(AppCommandReasons.WriteFailed, AppCommandExit.Failed,
                        T("The file could not be written. Nothing was received."));
            }
        }

        /// <summary>
        /// The answer to a fetch that did not bring a value: the empty slot everywhere is 11 <c>target-missing</c> -
        /// the thing the verb names does not exist - and every other failure is 1 <c>write-failed</c> (S0120's own
        /// reading: the verb's exit table has no 14), said without naming an account or a device; the tray balloon
        /// the caller at the desk still sees carries those words.
        /// </summary>
        public static AppCommandResponse FetchFailureAnswer(string code, Func<string, string> T)
        {
            if (string.Equals(code, ExchangeCodes.ClipboardEmpty, StringComparison.Ordinal))
                return AppCommandResponse.Failed(AppCommandReasons.TargetMissing, AppCommandExit.NotReady,
                    T("There is nothing on the server to receive."));
            return AppCommandResponse.Failed(AppCommandReasons.WriteFailed, AppCommandExit.Failed,
                T("The value was not received. Nothing was written."));
        }

        // ------------------------------------------------------------------ the pool delegates

        /// <summary>The app's own <c>--file</c> probe: one look, on whatever thread the budget runs it on.</summary>
        internal static ExchangeSourceStat? StatOf(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return new ExchangeSourceStat { Exists = false };
                return new ExchangeSourceStat { Exists = true, Length = info.Length, LastWriteUtc = info.LastWriteTimeUtc };
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException
                                       || ex is ArgumentException || ex is NotSupportedException)
            {
                return null;
            }
        }

        /// <summary>The app's own file copy: buffered, streamed - a received file may be as large as the exchange cap, so it is never held in memory.</summary>
        internal static void CopyFile(string from, string to)
        {
            using Stream source = new FileStream(from, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.SequentialScan);
            using Stream target = new FileStream(to, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.SequentialScan);
            var buffer = new byte[81920];
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                target.Write(buffer, 0, read);
        }

        // ------------------------------------------------------------------ shared

        /// <summary>Work on the pool under the budget; null when it neither answered in time nor finished. Its fault is never surfaced later.</summary>
        private static async Task<T?> RunBudgeted<T>(Func<T> work, TimeSpan budget, CancellationToken token) where T : class?
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

        private static AppCommandResponse TimedOut(Func<string, string> T)
            => AppCommandResponse.Failed(AppCommandReasons.Timeout, AppCommandExit.NotProven,
                T("CyrFlip did not answer in time. It may still be working."));
    }
}

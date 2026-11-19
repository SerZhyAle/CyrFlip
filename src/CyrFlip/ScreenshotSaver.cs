using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CyrFlip
{
    /// <summary>
    /// "Also save the capture to a folder" (ticket S0026, section 5.4). Name, folder, collisions and
    /// the fallback are not CyrFlip's to choose - they are the shared contract <c>CAPTURE-OUTPUT</c>
    /// (kind <c>screenshot</c>), and every rule below cites it. A disagreement is an amendment in the
    /// catalog, never a local variation.
    ///
    /// <para>The file system sits behind <see cref="IScreenshotFileSystem"/> and the known folders
    /// behind two delegates, so <c>ScreenshotSaverTests</c> hold the naming, collision and fallback
    /// rules without a disk.</para>
    /// </summary>
    internal sealed class ScreenshotSaver
    {
        /// <summary>CAPTURE-OUTPUT rule 1: kind <c>screenshot</c>, prefix <c>screenshot</c>, PNG.</summary>
        internal const string Prefix = "screenshot";
        internal const string Extension = ".png";

        /// <summary>How long one folder may take before it counts as unreachable (a share gone offline).</summary>
        internal static readonly TimeSpan FolderBudget = TimeSpan.FromSeconds(10);

        /// <summary>Ordinals tried before a folder is given up on - a folder with 10 000 captures in one second is not a folder.</summary>
        private const int MaxOrdinal = 10000;

        // FOLDERID_Screenshots and FOLDERID_Downloads.
        private static readonly Guid ScreenshotsFolderId = new Guid("b7bede81-df94-4682-a7d8-57a52620b86f");
        private static readonly Guid DownloadsFolderId = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        private const uint KF_FLAG_DONT_VERIFY = 0x00004000;

        private readonly IScreenshotFileSystem _fs;
        private readonly Func<string?> _screenshotsFolder;
        private readonly Func<string?> _downloadsFolder;
        private readonly TimeSpan _budget;

        public ScreenshotSaver()
            : this(new DiskFileSystem(), () => KnownFolder(ScreenshotsFolderId, "Screenshots"),
                   () => KnownFolder(DownloadsFolderId, null), FolderBudget)
        {
        }

        internal ScreenshotSaver(IScreenshotFileSystem fs, Func<string?> screenshotsFolder,
            Func<string?> downloadsFolder, TimeSpan budget)
        {
            _fs = fs;
            _screenshotsFolder = screenshotsFolder;
            _downloadsFolder = downloadsFolder;
            _budget = budget;
        }

        /// <summary>What happened to one save.</summary>
        internal enum Outcome
        {
            /// <summary>Written into the folder the settings show.</summary>
            Saved,
            /// <summary>Written, but into a fallback folder - the user is told where (CAPTURE-OUTPUT rule 11).</summary>
            SavedToFallback,
            /// <summary>No folder of the chain could be written; the capture is on the clipboard only.</summary>
            Failed,
        }

        internal sealed class Result
        {
            public Outcome Outcome { get; set; }
            /// <summary>The folder the file landed in (the balloon's input); null on failure.</summary>
            public string? Folder { get; set; }
            public string? FilePath { get; set; }
            /// <summary>Why the folder(s) failed - the exception type, never a file name.</summary>
            public string Reason { get; set; } = "";
        }

        /// <summary>
        /// The automatic stem for a capture that started at <paramref name="started"/> (local time):
        /// <c>screenshot_yyMMdd_HHmmss</c> with ASCII digits whatever the UI culture (CAPTURE-OUTPUT rule 3).
        /// </summary>
        internal static string Stem(DateTime started)
            => Prefix + "_" + started.ToString("yyMMdd", CultureInfo.InvariantCulture)
               + "_" + started.ToString("HHmmss", CultureInfo.InvariantCulture);

        /// <summary><c>stem.png</c>, then <c>stem (2).png</c>, <c>(3)</c>.. (CAPTURE-OUTPUT rule 5).</summary>
        internal static string FileName(string stem, int ordinal)
            => ordinal <= 1
                ? stem + Extension
                : stem + " (" + ordinal.ToString(CultureInfo.InvariantCulture) + ")" + Extension;

        /// <summary>The folder the settings show: the user's choice, else the Screenshots known folder.</summary>
        internal string? DisplayFolder(string configured)
        {
            string? folder = string.IsNullOrWhiteSpace(configured) ? _screenshotsFolder() : configured;
            return string.IsNullOrWhiteSpace(folder) ? null : Clean(folder!);
        }

        /// <summary>Trimmed, without a trailing separator - except a drive root, where "E:" would mean "the current folder of E:".</summary>
        private static string Clean(string folder)
        {
            string trimmed = folder.Trim();
            while (trimmed.Length > 3 && (trimmed.EndsWith("\\", StringComparison.Ordinal) || trimmed.EndsWith("/", StringComparison.Ordinal)))
                trimmed = trimmed.Substring(0, trimmed.Length - 1);
            return trimmed;
        }

        /// <summary>
        /// The destinations in order (CAPTURE-OUTPUT rules 9 and 10): the chosen folder, the default
        /// Screenshots folder, then Downloads - and nothing else. Duplicates are dropped.
        /// </summary>
        internal List<string> Chain(string configured)
        {
            var chain = new List<string>(3);
            void Add(string? folder)
            {
                if (string.IsNullOrWhiteSpace(folder)) return;
                string trimmed = Clean(folder!);
                foreach (string existing in chain)
                    if (string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase))
                        return;
                chain.Add(trimmed);
            }
            Add(configured);
            Add(_screenshotsFolder());
            Add(_downloadsFolder());
            return chain;
        }

        /// <summary>
        /// Write <paramref name="png"/> - the very bytes the clipboard got - for a capture that started
        /// at <paramref name="started"/>. Runs on a background thread; never throws.
        /// </summary>
        public Result Save(byte[] png, DateTime started, string configured)
        {
            // CAPTURE-OUTPUT rule 16: the capture time inside the file equals the time in its name -
            // the tIME chunk carries the same local second the stem was formed from (PngTime).
            byte[] payload = PngTime.Inject(png, started);
            string stem = Stem(started);
            string? shown = DisplayFolder(configured);
            var reasons = new List<string>();
            foreach (string folder in Chain(configured))
            {
                string? path = TryFolderWithinBudget(folder, stem, payload, out string reason);
                if (path != null)
                {
                    bool asShown = shown != null && string.Equals(folder, shown, StringComparison.OrdinalIgnoreCase);
                    return new Result
                    {
                        Outcome = asShown ? Outcome.Saved : Outcome.SavedToFallback,
                        Folder = folder,
                        FilePath = path,
                        Reason = string.Join("; ", reasons),
                    };
                }
                reasons.Add(reason);
            }
            return new Result { Outcome = Outcome.Failed, Reason = string.Join("; ", reasons) };
        }

        /// <summary>
        /// One folder, under the time budget: a share gone offline can stall an SMB call for far longer
        /// than anybody waits for a screenshot. An attempt that overran is told to stand down, so it
        /// deletes its temporary file instead of finishing the rename behind the fallback's back.
        /// </summary>
        private string? TryFolderWithinBudget(string folder, string stem, byte[] png, out string reason)
        {
            var abandon = new CancellationTokenSource();
            string failure = "";
            Task<string?> attempt = Task.Run(() =>
            {
                try { return TryFolder(folder, stem, png, abandon.Token); }
                catch (Exception ex) { failure = ex.GetType().Name; return null; }
            });
            bool finished;
            try { finished = attempt.Wait(_budget); }
            catch { finished = true; }
            if (!finished)
            {
                abandon.Cancel();
                reason = "Timeout";
                return null;
            }
            reason = failure.Length > 0 ? failure : "NoName";
            return attempt.Status == TaskStatus.RanToCompletion ? attempt.Result : null;
        }

        /// <summary>
        /// Create the folder if needed (the first save, never earlier), write a temporary file beside
        /// the final name, then rename it without replacing (CAPTURE-OUTPUT rules 5, 6 and 12). A lost
        /// race moves on to the next ordinal; the temporary file never survives.
        /// </summary>
        private string? TryFolder(string folder, string stem, byte[] png, CancellationToken abandon)
        {
            if (!_fs.DirectoryExists(folder)) _fs.CreateDirectory(folder);
            return WriteUnique(folder, stem, png, abandon);
        }

        /// <summary>
        /// The temporary-file-then-rename core shared by the automatic save and a command's folder mode: the folder is
        /// there already (created by <see cref="TryFolder"/>, or checked by <see cref="OutputPlan"/>), the first free
        /// ordinal wins and the temporary file never survives.
        /// </summary>
        private string? WriteUnique(string folder, string stem, byte[] png, CancellationToken abandon)
        {
            string temp = Path.Combine(folder, "~" + stem + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
            try
            {
                _fs.WriteAllBytes(temp, png);
                for (int ordinal = 1; ordinal <= MaxOrdinal; ordinal++)
                {
                    if (abandon.IsCancellationRequested) return null;
                    string target = Path.Combine(folder, FileName(stem, ordinal));
                    if (_fs.FileExists(target)) continue;
                    if (_fs.MoveNoReplace(temp, target))
                    {
                        temp = "";
                        return target;
                    }
                    // Somebody took the name between the check and the rename: the next ordinal.
                }
                return null;
            }
            finally
            {
                if (temp.Length > 0)
                {
                    try { _fs.DeleteFile(temp); } catch { /* best effort */ }
                }
            }
        }

        /// <summary>What became of one caller-named save (<see cref="SaveTo"/>).</summary>
        internal enum SaveToOutcome
        {
            Saved,
            /// <summary>A file is in the way and the plan did not say to replace it - found again at write time.</summary>
            Exists,
            /// <summary>The volume is full (Win32 39 or 112).</summary>
            DiskFull,
            /// <summary>The folder is not there - it is never created for a command.</summary>
            FolderMissing,
            Failed,
            /// <summary>The budget ran out (a share that does not answer); the attempt was told to stand down.</summary>
            Timeout,
        }

        internal sealed class SaveToResult
        {
            public SaveToOutcome Outcome { get; set; }
            /// <summary>The file actually written - in folder mode the automatic name with its ordinal.</summary>
            public string? FilePath { get; set; }
            /// <summary>The exception type of a failure, never a file name.</summary>
            public string Reason { get; set; } = "";
        }

        /// <summary>The file system this saver writes through - what a command's <see cref="OutputPlan"/> is resolved against.</summary>
        internal IScreenshotFileSystem FileSystem => _fs;

        /// <summary>How long one write may take before it counts as unreachable.</summary>
        internal TimeSpan Budget => _budget;

        /// <summary>
        /// Write <paramref name="png"/> where a command's <paramref name="plan"/> says, and nowhere else (ticket S0110,
        /// <c>APP-COMMANDS</c> rule 9): no fallback chain, no folder created, nothing about the capture folder or its
        /// settings. The <c>tIME</c> chunk of the freeze instant goes in for both modes. A named file is the temporary
        /// file renamed over (<paramref name="plan"/>'s <c>Overwrite</c>) or without replacing - whole or not at all; a folder
        /// gets the rule 3 name and rule 5's ordinal. Runs on a pool thread under <see cref="Budget"/>; never throws.
        /// </summary>
        internal SaveToResult SaveTo(byte[] png, DateTime started, OutputPlan plan)
        {
            byte[] payload = PngTime.Inject(png, started);
            var abandon = new CancellationTokenSource();
            Task<SaveToResult> attempt = Task.Run(() => WriteCallerNamed(payload, started, plan, abandon.Token));
            bool finished;
            try { finished = attempt.Wait(_budget); }
            catch (Exception) { finished = true; } // a fault is read from the task below
            if (!finished)
            {
                abandon.Cancel();
                return new SaveToResult { Outcome = SaveToOutcome.Timeout, Reason = "Timeout" };
            }
            if (attempt.Status == TaskStatus.RanToCompletion) return attempt.Result;
            Exception? fault = attempt.Exception?.GetBaseException();
            return new SaveToResult { Outcome = SaveToOutcome.Failed, Reason = fault != null ? fault.GetType().Name : "Failed" };
        }

        private SaveToResult WriteCallerNamed(byte[] payload, DateTime started, OutputPlan plan, CancellationToken abandon)
        {
            try
            {
                if (plan.Mode == OutputMode.Folder)
                {
                    string? written = WriteUnique(plan.Path, Stem(started), payload, abandon);
                    if (written != null) return Saved(written);
                    return abandon.IsCancellationRequested
                        ? new SaveToResult { Outcome = SaveToOutcome.Timeout, Reason = "Timeout" }
                        : new SaveToResult { Outcome = SaveToOutcome.Failed, Reason = "NoName" };
                }

                string folder = Path.GetDirectoryName(plan.Path) ?? "";
                string temp = Path.Combine(folder, "~" + Stem(started) + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
                try
                {
                    _fs.WriteAllBytes(temp, payload);
                    if (abandon.IsCancellationRequested) return new SaveToResult { Outcome = SaveToOutcome.Timeout, Reason = "Timeout" };
                    if (plan.Overwrite)
                    {
                        _fs.MoveReplace(temp, plan.Path);
                    }
                    else
                    {
                        // The name was free when the plan was made; a file that appeared meanwhile survives untouched.
                        if (_fs.FileExists(plan.Path) || !_fs.MoveNoReplace(temp, plan.Path))
                            return new SaveToResult { Outcome = SaveToOutcome.Exists };
                    }
                    temp = "";
                    return Saved(plan.Path);
                }
                finally
                {
                    if (temp.Length > 0)
                    {
                        try { _fs.DeleteFile(temp); } catch (Exception) { /* best effort: the next listing shows a stray ~ file at worst */ }
                    }
                }
            }
            catch (Exception ex)
            {
                return Describe(ex);
            }
        }

        private static SaveToResult Saved(string path) => new SaveToResult { Outcome = SaveToOutcome.Saved, FilePath = path };

        /// <summary>An exception to a save outcome: a missing folder, a full volume (Win32 39 or 112), anything else.</summary>
        private static SaveToResult Describe(Exception ex)
        {
            SaveToOutcome outcome = SaveToOutcome.Failed;
            if (ex is DirectoryNotFoundException) outcome = SaveToOutcome.FolderMissing;
            else if (ex is IOException)
            {
                int win32 = ex.HResult & 0xFFFF;
                if (win32 == 39 || win32 == 112) outcome = SaveToOutcome.DiskFull;
                else if (win32 == 3) outcome = SaveToOutcome.FolderMissing;
            }
            return new SaveToResult { Outcome = outcome, Reason = ex.GetType().Name };
        }

        /// <summary>A known folder's path, not created and not verified (the first save creates it).</summary>
        private static string? KnownFolder(Guid id, string? picturesChild)
        {
            try
            {
                if (WindowInterop.SHGetKnownFolderPath(id, KF_FLAG_DONT_VERIFY, IntPtr.Zero, out IntPtr ptr) == 0)
                {
                    try
                    {
                        string? path = Marshal.PtrToStringUni(ptr);
                        if (!string.IsNullOrEmpty(path)) return path;
                    }
                    finally { Marshal.FreeCoTaskMem(ptr); }
                }
            }
            catch { /* older shell: fall through */ }

            return picturesChild != null
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), picturesChild)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        private sealed class DiskFileSystem : IScreenshotFileSystem
        {
            public bool DirectoryExists(string path) => Directory.Exists(path);
            public void CreateDirectory(string path) => Directory.CreateDirectory(path);
            public bool FileExists(string path) => File.Exists(path) || Directory.Exists(path);
            public void WriteAllBytes(string path, byte[] bytes)
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }
            public bool MoveNoReplace(string from, string to)
            {
                if (WindowInterop.MoveFileEx(from, to, WindowInterop.MOVEFILE_WRITE_THROUGH)) return true;
                int error = Marshal.GetLastWin32Error();
                const int ERROR_FILE_EXISTS = 80, ERROR_ALREADY_EXISTS = 183;
                if (error == ERROR_FILE_EXISTS || error == ERROR_ALREADY_EXISTS) return false;
                throw new IOException("MoveFileEx failed", error);
            }
            public bool MoveReplace(string from, string to)
            {
                // One rename over the target, written through: the old file stays whole until the new one has replaced it.
                if (WindowInterop.MoveFileEx(from, to, WindowInterop.MOVEFILE_REPLACE_EXISTING | WindowInterop.MOVEFILE_WRITE_THROUGH))
                    return true;
                throw new IOException("MoveFileEx failed", Marshal.GetLastWin32Error());
            }
            public void DeleteFile(string path) => File.Delete(path);
        }
    }

    /// <summary>The file operations <see cref="ScreenshotSaver"/> needs - the tests' seam.</summary>
    internal interface IScreenshotFileSystem
    {
        bool DirectoryExists(string path);
        void CreateDirectory(string path);
        /// <summary>True when anything - a file or a folder - already has this name.</summary>
        bool FileExists(string path);
        /// <summary>Creates a new file (never replaces one) holding exactly <paramref name="bytes"/>.</summary>
        void WriteAllBytes(string path, byte[] bytes);
        /// <summary>Renames; false when the target exists (a lost race), throws on any other failure.</summary>
        bool MoveNoReplace(string from, string to);
        /// <summary>Renames over an existing target in one step (ticket S0110, <c>--overwrite</c>); true when done, throws on any failure.</summary>
        bool MoveReplace(string from, string to);
        void DeleteFile(string path);
    }
}

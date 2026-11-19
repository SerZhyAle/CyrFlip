using System;
using System.IO;
using System.Security;

namespace CyrFlip
{
    /// <summary>What a caller-named <c>--out</c> is: one file, or a folder the file is named in.</summary>
    internal enum OutputMode
    {
        /// <summary>A file name ending in <c>.png</c>: written exactly there.</summary>
        File,
        /// <summary>An existing folder, or a path ending in a separator: the file gets the CAPTURE-OUTPUT rule 3 name inside it.</summary>
        Folder,
    }

    /// <summary>Why an <c>--out</c> is not usable.</summary>
    internal enum OutputProblem
    {
        None,
        /// <summary>A path Windows refuses (device namespace, reserved name, name ending in a space or a dot, control or forbidden characters, a stray colon), an empty or an over-long one, or a relative one with no working directory.</summary>
        BadPath,
        /// <summary>A name that is not a <c>.png</c> file name and not a folder.</summary>
        NotPng,
        /// <summary>A path shaped as a folder (a trailing separator, a bare root) where a verb that takes one file was asked (S0120): never a folder the caller did not name.</summary>
        NotAFile,
        /// <summary>The folder the file would go into does not exist; it is never created.</summary>
        MissingFolder,
        /// <summary>A file (or a folder with that name) is in the way and <c>--overwrite</c> was not given.</summary>
        Exists,
    }

    /// <summary>
    /// Where the picture of a screenshot command goes (ticket S0110, <c>APP-COMMANDS</c> 0.2 rule 9 and item P): a path a
    /// program names is not a name a person typed, so it is used <b>exactly as given</b> - no character replaced, no
    /// ordinal added to a file name (CyrFlip's dated exception against <c>CAPTURE-OUTPUT</c> rules 5 and 7). A folder gets the
    /// rule 3 automatic name. Pure: the file system is the saver's seam, there is no Win32, no registry and no
    /// configuration here.
    /// </summary>
    internal sealed class OutputPlan
    {
        /// <summary>Windows' own bound for a path; the parser's 32 KiB argument bound refuses a longer one first.</summary>
        internal const int MaxPathChars = 32767;

        private OutputPlan(OutputMode mode, string path, bool overwrite)
        {
            Mode = mode; Path = path; Overwrite = overwrite;
        }

        public OutputMode Mode { get; }
        /// <summary>The full path of the file (<see cref="OutputMode.File"/>) or of the folder (<see cref="OutputMode.Folder"/>).</summary>
        public string Path { get; }
        /// <summary>Replace an existing file. Never true in folder mode: an automatic name is never replaced.</summary>
        public bool Overwrite { get; }

        private static readonly char[] Forbidden = { '<', '>', '"', '|', '*', '?' };

        private static readonly string[] Reserved =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        /// <summary>
        /// Turns <paramref name="outArg"/> into a plan, or says which rule it broke. Order: empty or over-long, the device
        /// namespace, the characters and the colon, then the path against <paramref name="workingDir"/> (a relative path is
        /// the caller's), then folder or file: a trailing separator is a folder, a name ending in <c>.png</c> is a file, any
        /// other existing directory is a folder, and the rest is not a <c>.png</c> file name. The folder a file goes into
        /// must exist; a file in the way is refused unless <paramref name="overwrite"/>.
        /// </summary>
        public static OutputResolution Resolve(string outArg, string workingDir, bool overwrite, IScreenshotFileSystem fs)
        {
            if (string.IsNullOrEmpty(outArg) || outArg.Length > MaxPathChars)
                return OutputResolution.Refuse(OutputProblem.BadPath);
            string norm = outArg.Replace('/', '\\');
            if (norm.StartsWith(@"\\?\", StringComparison.Ordinal) || norm.StartsWith(@"\\.\", StringComparison.Ordinal))
                return OutputResolution.Refuse(OutputProblem.BadPath);
            if (!ColonsAreTheDrive(norm)) return OutputResolution.Refuse(OutputProblem.BadPath);
            foreach (string segment in norm.Split('\\'))
                if (SegmentIsBad(segment)) return OutputResolution.Refuse(OutputProblem.BadPath);

            string full;
            try
            {
                string combined = norm;
                if (!IsFullyQualified(norm))
                {
                    string dir = (workingDir ?? "").Replace('/', '\\');
                    if (!IsFullyQualified(dir)) return OutputResolution.Refuse(OutputProblem.BadPath);
                    // A root-relative path (\x\a.png) is the working directory's drive or share, not the process's.
                    combined = norm.StartsWith("\\", StringComparison.Ordinal)
                        ? System.IO.Path.Combine(System.IO.Path.GetPathRoot(dir) ?? "", norm.TrimStart('\\'))
                        : System.IO.Path.Combine(dir, norm);
                }
                full = System.IO.Path.GetFullPath(combined);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException
                                       || ex is PathTooLongException || ex is SecurityException)
            {
                return OutputResolution.Refuse(OutputProblem.BadPath);
            }
            if (full.StartsWith(@"\\?\", StringComparison.Ordinal) || full.StartsWith(@"\\.\", StringComparison.Ordinal))
                return OutputResolution.Refuse(OutputProblem.BadPath);

            if (norm.EndsWith("\\", StringComparison.Ordinal))
            {
                string folder = TrimSeparators(full);
                return fs.DirectoryExists(folder)
                    ? OutputResolution.Accept(new OutputPlan(OutputMode.Folder, folder, false))
                    : OutputResolution.Refuse(OutputProblem.MissingFolder);
            }

            string name = System.IO.Path.GetFileName(full);
            if (name.Length > ScreenshotSaver.Extension.Length
                && name.EndsWith(ScreenshotSaver.Extension, StringComparison.OrdinalIgnoreCase))
            {
                string? parent = System.IO.Path.GetDirectoryName(full);
                if (parent == null || !fs.DirectoryExists(parent)) return OutputResolution.Refuse(OutputProblem.MissingFolder);
                // A folder named like the file is in the way whatever --overwrite says.
                if (fs.DirectoryExists(full)) return OutputResolution.Refuse(OutputProblem.Exists);
                if (fs.FileExists(full) && !overwrite) return OutputResolution.Refuse(OutputProblem.Exists);
                return OutputResolution.Accept(new OutputPlan(OutputMode.File, full, overwrite));
            }
            return fs.DirectoryExists(full)
                ? OutputResolution.Accept(new OutputPlan(OutputMode.Folder, TrimSeparators(full), false))
                : OutputResolution.Refuse(OutputProblem.NotPng);
        }

        /// <summary>
        /// Turns <paramref name="outArg"/> into a plan for a verb that writes <b>one named file</b> (ticket S0120,
        /// <c>note export</c>): the same name rules and the same working-directory rule as <see cref="Resolve"/>, but
        /// the path must be a file - no extension is required, because the format says what it is, and no folder is
        /// named for the caller, because he has no automatic name to be given (a path ending in a separator, or a bare
        /// root, is <see cref="OutputProblem.NotAFile"/>). An existing directory at the exact target is a thing in the
        /// way whatever <c>--overwrite</c> says; the folder the file goes into must exist and is never created.
        /// </summary>
        public static OutputResolution ResolveFile(string outArg, string workingDir, bool overwrite, IScreenshotFileSystem fs)
        {
            if (string.IsNullOrEmpty(outArg) || outArg.Length > MaxPathChars)
                return OutputResolution.Refuse(OutputProblem.BadPath);
            string norm = outArg.Replace('/', '\\');
            if (norm.StartsWith(@"\\?\", StringComparison.Ordinal) || norm.StartsWith(@"\\.\", StringComparison.Ordinal))
                return OutputResolution.Refuse(OutputProblem.BadPath);
            if (!ColonsAreTheDrive(norm)) return OutputResolution.Refuse(OutputProblem.BadPath);
            foreach (string segment in norm.Split('\\'))
                if (SegmentIsBad(segment)) return OutputResolution.Refuse(OutputProblem.BadPath);

            string full;
            try
            {
                string combined = norm;
                if (!IsFullyQualified(norm))
                {
                    string dir = (workingDir ?? "").Replace('/', '\\');
                    if (!IsFullyQualified(dir)) return OutputResolution.Refuse(OutputProblem.BadPath);
                    combined = norm.StartsWith("\\", StringComparison.Ordinal)
                        ? System.IO.Path.Combine(System.IO.Path.GetPathRoot(dir) ?? "", norm.TrimStart('\\'))
                        : System.IO.Path.Combine(dir, norm);
                }
                full = System.IO.Path.GetFullPath(combined);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException
                                       || ex is PathTooLongException || ex is SecurityException)
            {
                return OutputResolution.Refuse(OutputProblem.BadPath);
            }
            if (full.StartsWith(@"\\?\", StringComparison.Ordinal) || full.StartsWith(@"\\.\", StringComparison.Ordinal))
                return OutputResolution.Refuse(OutputProblem.BadPath);

            // A folder shape is not a file name however it ends; a separator a caller typed is his answer, not ours to reinterpret.
            if (norm.EndsWith("\\", StringComparison.Ordinal) || System.IO.Path.GetFileName(full).Length == 0)
                return OutputResolution.Refuse(OutputProblem.NotAFile);

            string? parent = System.IO.Path.GetDirectoryName(full);
            if (parent == null || !fs.DirectoryExists(parent)) return OutputResolution.Refuse(OutputProblem.MissingFolder);
            // A folder named like the file is in the way whatever --overwrite says.
            if (fs.DirectoryExists(full)) return OutputResolution.Refuse(OutputProblem.Exists);
            if (fs.FileExists(full) && !overwrite) return OutputResolution.Refuse(OutputProblem.Exists);
            return OutputResolution.Accept(new OutputPlan(OutputMode.File, full, overwrite));
        }

        private static bool IsLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        /// <summary><c>X:\..</c> or <c>\\server\..</c>: a path no process's current directory can change the meaning of.</summary>
        private static bool IsFullyQualified(string path)
            => (path.Length >= 3 && IsLetter(path[0]) && path[1] == ':' && path[2] == '\\')
               || path.StartsWith(@"\\", StringComparison.Ordinal);

        /// <summary>A colon is allowed only right after the drive letter and before a separator (<c>C:\</c>); <c>C:a.png</c> would be the current folder of C:.</summary>
        private static bool ColonsAreTheDrive(string norm)
        {
            for (int i = 0; i < norm.Length; i++)
            {
                if (norm[i] != ':') continue;
                if (i != 1 || !IsLetter(norm[0]) || norm.Length < 3 || norm[2] != '\\') return false;
            }
            return true;
        }

        private static bool SegmentIsBad(string segment)
        {
            // Empty segments (the lead of \\server, a doubled separator) and . / .. are the path's business, not a name's.
            if (segment.Length == 0 || segment == "." || segment == "..") return false;
            foreach (char c in segment)
                if (c < ' ' || Array.IndexOf(Forbidden, c) >= 0) return true;
            char last = segment[segment.Length - 1];
            if (last == ' ' || last == '.') return true;
            // A space right before the extension (a .png) is refused as well: the stem would end in a space.
            int lastDot = segment.LastIndexOf('.');
            if (lastDot > 0 && segment[lastDot - 1] == ' ') return true;
            int dot = segment.IndexOf('.');
            string stem = (dot >= 0 ? segment.Substring(0, dot) : segment).TrimEnd(' ');
            foreach (string device in Reserved)
                if (stem.Equals(device, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Without a trailing separator - except a root, where <c>E:</c> would mean the current folder of E:.</summary>
        private static string TrimSeparators(string path)
        {
            string root = System.IO.Path.GetPathRoot(path) ?? "";
            string trimmed = path;
            while (trimmed.Length > root.Length && trimmed.EndsWith("\\", StringComparison.Ordinal))
                trimmed = trimmed.Substring(0, trimmed.Length - 1);
            return trimmed;
        }
    }

    /// <summary>The answer of <see cref="OutputPlan.Resolve"/>: a plan, or the problem with its reason, status and exit.</summary>
    internal sealed class OutputResolution
    {
        private OutputResolution(OutputPlan? plan, OutputProblem problem)
        {
            Plan = plan; Problem = problem;
        }

        public OutputPlan? Plan { get; }
        public OutputProblem Problem { get; }

        internal static OutputResolution Accept(OutputPlan plan) => new OutputResolution(plan, OutputProblem.None);
        internal static OutputResolution Refuse(OutputProblem problem) => new OutputResolution(null, problem);

        /// <summary>The reason token (APP-COMMANDS rule 12) of the problem; empty when there is none.</summary>
        public string Reason
        {
            get
            {
                switch (Problem)
                {
                    case OutputProblem.BadPath:
                    case OutputProblem.NotPng:
                    case OutputProblem.NotAFile: return AppCommandReasons.BadArgument;
                    case OutputProblem.MissingFolder: return AppCommandReasons.TargetMissing;
                    case OutputProblem.Exists: return AppCommandReasons.Exists;
                    default: return "";
                }
            }
        }

        /// <summary><c>refused</c> for a value the caller must change, <c>failed</c> for a thing that does not exist (rule 12).</summary>
        public string Status => Problem == OutputProblem.MissingFolder ? "failed" : "refused";

        public int ExitCode
        {
            get
            {
                switch (Problem)
                {
                    case OutputProblem.BadPath:
                    case OutputProblem.NotPng:
                    case OutputProblem.NotAFile: return AppCommandExit.Usage;
                    case OutputProblem.None: return AppCommandExit.Done;
                    default: return AppCommandExit.NotReady;
                }
            }
        }
    }
}

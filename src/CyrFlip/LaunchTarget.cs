using System;
using System.IO;

namespace CyrFlip
{
    /// <summary>What kind of thing the selection turned out to be - it decides how loudly we ask.</summary>
    internal enum LaunchKind
    {
        /// <summary>A web address (or mailto:) - the shell hands it to the browser or mail client.</summary>
        Url,
        /// <summary>An existing file or folder that opens in whatever is associated with it.</summary>
        Document,
        /// <summary>An existing file that <b>runs code</b>, or anything on a network share.</summary>
        Program,
    }

    /// <summary>A selection that can be opened, as resolved by <see cref="LaunchTargets.TryParse(string, out LaunchTarget, LaunchProbes)"/>.</summary>
    internal sealed class LaunchTarget
    {
        public LaunchTarget(string target, string display, LaunchKind kind, bool remote = false)
        {
            Target = target; Display = display; Kind = kind; IsRemote = remote;
        }

        /// <summary>Exactly what is handed to the shell.</summary>
        public string Target { get; }
        /// <summary>The same thing shortened for a menu caption.</summary>
        public string Display { get; }
        public LaunchKind Kind { get; }
        /// <summary>A path on another machine - classified from its text, never probed.</summary>
        public bool IsRemote { get; }
    }

    /// <summary>
    /// The four questions <see cref="LaunchTargets"/> has to ask the machine, each injectable so the
    /// whole matrix is unit-tested without a disk, a network or the registry. Only
    /// <see cref="Exists"/> and <see cref="RealName"/> touch a file system, and both are asked about
    /// <b>local</b> paths only - a remote candidate is classified from its text and never probed.
    /// </summary>
    internal sealed class LaunchProbes
    {
        /// <summary>"Is there such a file or folder." Never called with a remote path.</summary>
        public Func<string, bool> Exists { get; set; } = DefaultExists;

        /// <summary>"Is this drive letter a network drive." Answered from the local mount table.</summary>
        public Func<char, bool> IsRemoteDrive { get; set; } = DefaultIsRemoteDrive;

        /// <summary>The on-disk name of an existing path's last segment, or null to keep the text's.</summary>
        public Func<string, string?> RealName { get; set; } = DefaultRealName;

        /// <summary>Windows' own "this association runs code" list, beside our fixed one.</summary>
        public Func<string, bool> IsDangerousExtension { get; set; } = DefaultIsDangerousExtension;

        /// <summary>The directory a root-relative path (<c>\x</c>) resolves against.</summary>
        public Func<string> CurrentDirectory { get; set; } = DefaultCurrentDirectory;

        public static LaunchProbes Real() => new LaunchProbes();

        private static bool DefaultExists(string path)
        {
            try { return File.Exists(path) || Directory.Exists(path); }
            catch { return false; }
        }

        private static bool DefaultIsRemoteDrive(char letter)
        {
            try { return WindowInterop.GetDriveType(letter + @":\") == WindowInterop.DRIVE_REMOTE; }
            catch { return true; } // cannot tell: treat it as someone else's, i.e. confirm it
        }

        private static string? DefaultRealName(string path)
        {
            try
            {
                IntPtr find = WindowInterop.FindFirstFile(path, out WindowInterop.WIN32_FIND_DATA data);
                if (find == WindowInterop.INVALID_HANDLE_VALUE) return null;
                WindowInterop.FindClose(find);
                return string.IsNullOrEmpty(data.cFileName) ? null : data.cFileName;
            }
            catch { return null; }
        }

        private static bool DefaultIsDangerousExtension(string extension)
        {
            try { return WindowInterop.AssocIsDangerous(extension); }
            catch { return false; } // our own list still applies
        }

        private static string DefaultCurrentDirectory()
        {
            try { return Directory.GetCurrentDirectory(); }
            catch { return ""; }
        }
    }

    /// <summary>
    /// "The user selected a link - offer to open it." Turns a piece of selected text into something
    /// the shell can open, or refuses.
    ///
    /// <b>It refuses far more than it accepts, on purpose.</b> The text comes from whatever window
    /// the pointer happened to be over - a web page, a chat message, a log - so it is untrusted
    /// input that the user is one click away from handing to <c>ShellExecute</c>. Only three shapes
    /// are accepted: an absolute URL in a whitelisted scheme, a bare host that is unmistakably a web
    /// address, and a path that <b>exists on this machine right now</b>. Everything else - a command
    /// line, an unknown scheme (<c>javascript:</c>, <c>shell:</c>, <c>ms-settings:</c>), a filename
    /// that merely looks plausible - is not a launch target, and the menu item then does not appear
    /// at all rather than appearing and failing.
    ///
    /// Anything that runs code (or lives on a network share, where the file is someone else's) comes
    /// back as <see cref="LaunchKind.Program"/>, which is what the caller confirms with the user
    /// before starting it.
    ///
    /// <b>A remote path is never probed</b> (ticket S0008, LS-1). Asking "does <c>\\host\share\x</c>
    /// exist" makes Windows open an SMB session to a host the author of the text chose, with the
    /// user's credentials, and blocks for as long as that host takes not to answer - all to draw a
    /// menu. So UNC paths, <c>file://host/..</c> and mapped network drives are recognised from their
    /// text and the drive's type alone, and come back as <see cref="LaunchKind.Program"/> without an
    /// existence check: the confirmation, which shows the full path, is what stands between them and
    /// the shell. The parse itself runs on <see cref="SelectionProbe"/>'s worker, never on the UI
    /// thread that the low-level hooks share.
    ///
    /// Pure but for the probes it is handed, so the whole matrix is unit-tested without a disk.
    /// </summary>
    internal static class LaunchTargets
    {
        /// <summary>Longest selection still considered; a link is not a paragraph.</summary>
        public const int MaxLength = 2048;

        /// <summary>How many words of a long selection are examined before giving up.</summary>
        public const int MaxWords = 2000;

        /// <summary>How much of the target the menu caption shows before eliding.</summary>
        public const int MaxDisplayChars = 48;

        /// <summary>Schemes the shell may be handed. Everything else is refused by name.</summary>
        private static readonly string[] AllowedSchemes = { "http", "https", "ftp", "ftps", "mailto" };

        /// <summary>
        /// Extensions that run code rather than open in a viewer. Not a security boundary - the
        /// confirmation is - just the list of things worth asking about. Windows' own list
        /// (<c>AssocIsDangerous</c>) is consulted beside it; the second half of this one is what
        /// ShellExecute can still start, install, mount or hand to a script host on a machine whose
        /// list does not name it (ticket S0008, LS-3).
        /// </summary>
        private static readonly string[] ExecutableExtensions =
        {
            ".exe", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".msi", ".msp", ".scr", ".pif", ".cpl",
            ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".reg", ".lnk", ".url", ".jar",
            ".py", ".sh", ".msc", ".gadget", ".inf",
            ".appref-ms", ".application", ".chm", ".rdp", ".diagcab", ".settingcontent-ms",
            ".library-ms", ".search-ms", ".searchconnector-ms", ".wsc", ".sct", ".pyw", ".xbap",
            ".appx", ".appxbundle", ".msix", ".msixbundle", ".appinstaller", ".iso", ".img",
            ".vhd", ".vhdx", ".ps1xml", ".psd1",
        };

        /// <summary>
        /// The top-level domains a bare <c>host.tld</c> selection is accepted for. A whitelist rather
        /// than "two to twenty-four letters", because that rule turns <c>readme.md</c>,
        /// <c>notes.txt</c> and <c>setup.exe</c> into web addresses.
        /// </summary>
        private static readonly string[] KnownTlds =
        {
            "com", "org", "net", "edu", "gov", "mil", "int", "info", "biz", "name", "pro",
            "io", "co", "ai", "dev", "app", "me", "tv", "cc", "xyz", "site", "online", "shop",
            "tech", "cloud", "store", "blog", "wiki", "news", "email", "link", "live", "space",
            "ru", "ua", "by", "kz", "uk", "de", "fr", "it", "es", "pl", "pt", "nl", "be", "ch",
            "at", "cz", "se", "no", "fi", "dk", "ie", "gr", "ro", "bg", "hu", "tr", "il", "in",
            "cn", "jp", "kr", "br", "mx", "ar", "ca", "au", "nz", "za", "eu", "us",
        };

        private static readonly char[] TrailingJunk = { '.', ',', ';', ':', '!', '?', ')', ']', '»', '"', '\'', '>', '…' };

        /// <summary>What separates one word of a selection from the next - whitespace only.</summary>
        private static readonly char[] WordSeparators = { ' ', '\t', '\r', '\n', '\u00A0' };

        /// <summary>
        /// Resolve <paramref name="selection"/> into something openable.
        /// <paramref name="exists"/> answers "is there such a file or folder"; it is injected so the
        /// rules can be tested without a disk, and defaults to the real one. The other probes are the
        /// real ones.
        /// </summary>
        public static bool TryParse(string? selection, out LaunchTarget? target, Func<string, bool>? exists = null)
        {
            LaunchProbes probes = LaunchProbes.Real();
            if (exists != null) probes.Exists = exists;
            return TryParse(selection, out target, probes);
        }

        /// <summary>Resolve <paramref name="selection"/> with every probe supplied by the caller.</summary>
        public static bool TryParse(string? selection, out LaunchTarget? target, LaunchProbes probes)
        {
            target = null;
            if (string.IsNullOrWhiteSpace(selection)) return false;

            // Only the whole-text pass is length-limited - a target longer than this is not a target.
            // The word pass below still runs on a long selection, because a link inside a page of
            // text is exactly the case the user selects by dragging.
            string? text = selection!.Length <= MaxLength ? Normalize(selection) : null;
            LaunchTarget? whole = null;
            if (text != null)
            {
                whole = AsPath(text, probes) ?? AsUrl(text);
                // A remote path is never probed, so the existence check that throws "C:\x.txt for
                // details" out and lets the word pass find C:\x.txt does not exist for it - the whole
                // sentence would be the target (S0034 LS2-4). Such a text goes to the word pass first,
                // and is the target itself only when no word of it is.
                if (whole != null && !(whole.IsRemote && LooksLikeProseAfterPath(text)))
                {
                    target = whole;
                    return true;
                }
            }

            target = FromWords(selection, probes) ?? whole;
            return target != null;
        }

        /// <summary>
        /// True when the whitespace in a path-shaped text reads as prose after the path rather than
        /// as spaces inside its names: the last segment has whitespace and does not end in an
        /// extension. <c>\\nas\My Videos\a.mp4</c> and <c>\\nas\s\My File.docx</c> are one path each;
        /// <c>\\server\share\x.txt for details</c> and <c>Z:\plan.docx is final</c> are not.
        /// </summary>
        internal static bool LooksLikeProseAfterPath(string text)
        {
            if (text.IndexOfAny(WordSeparators) < 0) return false;
            string last = text.Substring(text.LastIndexOfAny(PathSeparators) + 1);
            if (last.IndexOfAny(WordSeparators) < 0) return false; // the spaces are in folder names

            int dot = last.LastIndexOf('.');
            if (dot <= 0 || dot == last.Length - 1 || last.Length - dot - 1 > MaxExtensionChars) return true;
            for (int i = dot + 1; i < last.Length; i++)
                if (!char.IsLetterOrDigit(last[i])) return true;
            return false;
        }

        private static readonly char[] PathSeparators = { '\\', '/' };
        private const int MaxExtensionChars = 5;

        /// <summary>
        /// The selection was not a target as a whole - look for one word inside it that is.
        ///
        /// This is not leniency for its own sake: a selection drawn with the mouse routinely picks up
        /// the space or the word next to the link, a sentence is quoted around it, and in Chromium the
        /// accessibility stack hands back the selection of one node, so the text can arrive with its
        /// neighbours attached. The user still sees exactly what will open - the menu caption names
        /// the target - and the first candidate wins, so a selection with two links opens the one the
        /// caption shows. A multi-word path ("C:\Program Files\...") is not found here, only by the
        /// whole-text pass above, which is the right way round: splitting on spaces cannot rebuild it.
        /// </summary>
        private static LaunchTarget? FromWords(string selection, LaunchProbes probes)
        {
            string[] words = selection.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2) return null; // one word was already tried as the whole text

            int examined = 0;
            foreach (string word in words)
            {
                if (++examined > MaxWords) break; // a menu must open now, not after a page of prose
                string? candidate = Normalize(word);
                if (candidate == null) continue;
                LaunchTarget? target = AsPath(candidate, probes) ?? AsUrl(candidate);
                if (target != null) return target;
            }
            return null;
        }

        /// <summary>
        /// Trim the selection down to the candidate: one line, no control characters, without the
        /// brackets and sentence punctuation that come along when a link is selected inside prose.
        ///
        /// A bidirectional override or mark refuses the candidate outright (S0008 LS-5): it is
        /// invisible, <see cref="char.IsControl"/> does not catch it (category Cf), and it lets
        /// <c>invoice&lt;RLO&gt;fdp.exe</c> read as <c>invoiceexe.pdf</c> in the confirmation that is
        /// supposed to show what will run.
        /// </summary>
        internal static string? Normalize(string? selection)
        {
            if (string.IsNullOrWhiteSpace(selection)) return null;
            string text = selection!.Trim();
            if (text.Length > MaxLength) return null;

            foreach (char c in text)
                if (char.IsControl(c) || IsBidiControl(c)) return null; // a newline means this is prose, not a target

            // One layer of wrapping is enough: links arrive as <a>, "a", «a» or (a), never nested.
            if (text.Length >= 2)
            {
                char first = text[0], last = text[text.Length - 1];
                if ((first == '<' && last == '>') || (first == '"' && last == '"')
                    || (first == '\'' && last == '\'') || (first == '«' && last == '»')
                    || (first == '(' && last == ')') || (first == '[' && last == ']'))
                    text = text.Substring(1, text.Length - 2).Trim();
            }

            text = text.TrimEnd(TrailingJunk).Trim();
            return text.Length == 0 ? null : text;
        }

        /// <summary>A control character or a bidi control anywhere in <paramref name="text"/>.</summary>
        internal static bool HasHiddenCharacters(string text)
        {
            foreach (char c in text)
                if (char.IsControl(c) || IsBidiControl(c)) return true;
            return false;
        }

        /// <summary>The invisible characters that reorder what the eye reads (LRM/RLM, embeddings, overrides, isolates, ALM).</summary>
        internal static bool IsBidiControl(char c)
            => c == '\u200E' || c == '\u200F' || c == '\u061C'
               || (c >= '\u202A' && c <= '\u202E')
               || (c >= '\u2066' && c <= '\u2069');

        /// <summary>
        /// A path, but only one that is there - or one that is someone else's, which is confirmed
        /// rather than probed. "Exists" is what separates a real local target from a word that happens
        /// to contain a backslash, and it is also why a selection is never turned into a command line.
        /// </summary>
        private static LaunchTarget? AsPath(string text, LaunchProbes probes)
        {
            string path = text;
            if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var uri = new Uri(path);
                    if (!uri.IsFile) return null;
                    path = uri.LocalPath; // file://host/share/x -> \\host\share\x, classified below
                }
                catch { return null; }
            }

            if (path.IndexOf('%') >= 0)
            {
                try { path = Environment.ExpandEnvironmentVariables(path); }
                catch { /* keep the literal text */ }
            }

            // Normalize saw the text before LocalPath unescaped it and before the variables were
            // expanded: file://host/s/invoice%E2%80%AEfdp.exe only becomes a right-to-left override
            // here, and would reach the caption and the confirmation spoofed (S0034 LS2-1).
            if (HasHiddenCharacters(path)) return null;

            if (path.Length < 3) return null;
            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
            // A wildcard is not a path, and FindFirstFile would read it as a pattern.
            if (path.IndexOf('*') >= 0 || (path.IndexOf('?') >= 0 && !path.StartsWith(@"\\?\", StringComparison.Ordinal)))
                return null;

            // Classify from the text alone, before any call that could touch a file system.
            PathPlace place = Classify(path, probes, out string canonicalRemote);
            switch (place)
            {
                case PathPlace.NotAPath:
                    return null;
                case PathPlace.Remote:
                    return HasHiddenCharacters(canonicalRemote)
                        ? null
                        : new LaunchTarget(canonicalRemote, Elide(canonicalRemote), LaunchKind.Program, remote: true);
            }

            // Local from here on: the canonical form is what Windows will open, so it is what is
            // probed, classified and shown (LS-3) - "x.exe. ." opens x.exe and must be asked about.
            string full;
            try { full = TrimTrailingDotsAndSpaces(Path.GetFullPath(path)); }
            catch { return null; }

            if (!probes.Exists(full)) return null;

            string? real = null;
            if (!IsRoot(full))
            {
                try { real = probes.RealName(full); }
                catch { real = null; }
            }
            if (!string.IsNullOrEmpty(real))
            {
                string? directory = Path.GetDirectoryName(full);
                if (directory != null) full = Path.Combine(directory, real);
            }
            // The name on disk is what the confirmation shows - a real file called so as to read
            // backwards is refused like the text that spells one.
            if (HasHiddenCharacters(full)) return null;

            string extension;
            try { extension = Path.GetExtension(full) ?? ""; }
            catch { extension = ""; }

            LaunchKind kind = IsCodeExtension(extension, probes) ? LaunchKind.Program : LaunchKind.Document;
            return new LaunchTarget(full, Elide(full), kind);
        }

        private enum PathPlace { NotAPath, Local, Remote }

        /// <summary>
        /// Where a rooted path lives, decided without touching it: a UNC name (in any slash
        /// direction, or as <c>\\?\UNC\</c>) and a drive letter the mount table calls
        /// <c>DRIVE_REMOTE</c> are remote; a root-relative <c>\x</c> is wherever the current directory
        /// is. The Win32 device namespace (<c>\\?\C:\</c>, <c>\\.\..</c>) is not a launch target at all.
        /// </summary>
        private static PathPlace Classify(string path, LaunchProbes probes, out string canonicalRemote)
        {
            canonicalRemote = "";
            string p = path.Replace('/', '\\');

            if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                p = @"\\" + p.Substring(8);
            else if (p.StartsWith(@"\\?\", StringComparison.Ordinal) || p.StartsWith(@"\\.\", StringComparison.Ordinal)
                     || p.StartsWith(@"\??\", StringComparison.Ordinal))
                return PathPlace.NotAPath;

            if (p.StartsWith(@"\\", StringComparison.Ordinal))
            {
                // \\server\share at the least - "\\n" in a code sample is not a share.
                string[] parts = p.Substring(2).Split('\\');
                if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0) return PathPlace.NotAPath;
                canonicalRemote = TrimTrailingDotsAndSpaces(p);
                return PathPlace.Remote;
            }

            if (p.Length >= 2 && p[1] == ':' && IsAsciiLetter(p[0]))
            {
                if (!probes.IsRemoteDrive(char.ToUpperInvariant(p[0]))) return PathPlace.Local;
                canonicalRemote = TrimTrailingDotsAndSpaces(p);
                return PathPlace.Remote;
            }

            if (p.StartsWith(@"\", StringComparison.Ordinal))
            {
                // Root-relative: on the current directory's drive, which may itself be a share.
                string current = probes.CurrentDirectory() ?? "";
                if (current.StartsWith(@"\\", StringComparison.Ordinal))
                {
                    // The root of \\server\share\folder is \\server\share.
                    string[] parts = current.Substring(2).Split('\\');
                    if (parts.Length < 2) return PathPlace.NotAPath;
                    return RemoteOn(@"\\" + parts[0] + @"\" + parts[1], p, out canonicalRemote);
                }
                if (current.Length >= 2 && current[1] == ':' && IsAsciiLetter(current[0])
                    && probes.IsRemoteDrive(char.ToUpperInvariant(current[0])))
                    return RemoteOn(current.Substring(0, 2), p, out canonicalRemote);
                return current.Length == 0 ? PathPlace.NotAPath : PathPlace.Local;
            }

            return PathPlace.NotAPath; // IsPathRooted said yes to something we do not recognise
        }

        /// <summary>
        /// Whether <paramref name="path"/> lives on another machine, decided from its text and the
        /// local mount table alone - the same classifier the selection launch uses, shared with the
        /// scenario launcher (ticket S0030 HT-1): a UNC name in either slash direction or as
        /// <c>\\?\UNC\</c>, <c>file://host/..</c>, a <c>DRIVE_REMOTE</c> letter, or a root-relative
        /// path on a network current directory. Probing such a path opens an SMB session and waits out
        /// its timeout, so a caller on the UI thread takes it "as written" instead. A relative path or
        /// anything that is not a path at all is not remote.
        /// </summary>
        internal static bool IsRemotePath(string? path, LaunchProbes? probes = null)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string p = path!.Trim().Trim('"');
            if (p.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var uri = new Uri(p);
                    if (!uri.IsFile) return false;
                    p = uri.LocalPath; // file://host/share/x -> \\host\share\x
                }
                catch { return false; }
            }
            if (p.IndexOf('%') >= 0)
            {
                try { p = Environment.ExpandEnvironmentVariables(p); }
                catch { /* keep the literal text */ }
            }

            bool rooted;
            try { rooted = Path.IsPathRooted(p); }
            catch { return false; }
            if (!rooted) return false;
            return Classify(p, probes ?? LaunchProbes.Real(), out _) == PathPlace.Remote;
        }

        private static PathPlace RemoteOn(string root, string rootRelative, out string canonicalRemote)
        {
            canonicalRemote = TrimTrailingDotsAndSpaces(root.TrimEnd('\\') + rootRelative);
            return PathPlace.Remote;
        }

        private static bool IsAsciiLetter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

        private static bool IsRoot(string path)
        {
            try { return string.Equals(Path.GetPathRoot(path), path, StringComparison.OrdinalIgnoreCase); }
            catch { return true; }
        }

        /// <summary>
        /// What Win32 does to the last segment of every path it opens - and what the extension
        /// check must therefore see: <c>x.exe. .</c> is <c>x.exe</c>.
        /// </summary>
        internal static string TrimTrailingDotsAndSpaces(string path)
        {
            string trimmed = path.TrimEnd('.', ' ');
            // "C:\.." resolves before we get here; never trim a root or a segment down to nothing.
            if (trimmed.Length == 0 || trimmed.EndsWith(@"\", StringComparison.Ordinal) || trimmed.EndsWith(":", StringComparison.Ordinal))
                return path;
            return trimmed;
        }

        private static bool IsCodeExtension(string extension, LaunchProbes probes)
        {
            if (IsExecutableExtension(extension)) return true;
            if (string.IsNullOrEmpty(extension)) return false;
            try { return probes.IsDangerousExtension(extension); }
            catch { return false; }
        }

        private static LaunchTarget? AsUrl(string text)
        {
            if (text.IndexOf(' ') >= 0) return null; // a URL with a space in it is prose around a URL

            // A scheme we allow is taken as given. One we do not is *not* a refusal on its own:
            // "example.com:8080/x" parses as a URI whose scheme is "example.com", so the bare-host
            // rule below still gets its say - and "javascript:alert(1)" fails there too.
            if (Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) && uri != null && IsAllowedScheme(uri.Scheme))
                return new LaunchTarget(uri.AbsoluteUri, ElideUrl(text), LaunchKind.Url);

            // No scheme: accept it only when the host itself says "web address".
            string host = text;
            int slash = host.IndexOf('/');
            if (slash >= 0) host = host.Substring(0, slash);
            int at = host.IndexOf('@');
            if (at >= 0) return null; // an e-mail address is not something to open without mailto:
            int colon = host.IndexOf(':');
            if (colon >= 0) host = host.Substring(0, colon);

            if (!IsWebHost(host)) return null;
            string url = "https://" + text;
            return Uri.TryCreate(url, UriKind.Absolute, out Uri? guessed) && guessed != null
                ? new LaunchTarget(guessed.AbsoluteUri, ElideUrl(text), LaunchKind.Url)
                : null;
        }

        internal static bool IsAllowedScheme(string? scheme)
        {
            if (string.IsNullOrEmpty(scheme)) return false;
            foreach (string allowed in AllowedSchemes)
                if (string.Equals(scheme, allowed, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static bool IsExecutableExtension(string? extension)
        {
            if (string.IsNullOrEmpty(extension)) return false;
            foreach (string known in ExecutableExtensions)
                if (string.Equals(extension, known, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>"www.x" or "x.tld" where tld is one we recognise - see <see cref="KnownTlds"/>.</summary>
        internal static bool IsWebHost(string host)
        {
            if (host.Length < 4 || host.Length > 253) return false;
            if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) return host.Length > 4 && host.IndexOf('.', 4) > 4;

            int dot = host.LastIndexOf('.');
            if (dot <= 0 || dot == host.Length - 1) return false;
            string tld = host.Substring(dot + 1);
            foreach (char c in host)
                if (!char.IsLetterOrDigit(c) && c != '.' && c != '-') return false;

            foreach (string known in KnownTlds)
                if (string.Equals(tld, known, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Shorten for a menu caption, keeping both ends - the drive and the last segment.</summary>
        internal static string Elide(string text)
        {
            if (text.Length <= MaxDisplayChars) return text;
            int head = (MaxDisplayChars - 2) / 2;
            int tail = MaxDisplayChars - 2 - head;
            return text.Substring(0, head) + ".." + text.Substring(text.Length - tail);
        }

        /// <summary>
        /// Shorten a URL for a caption <b>without ever cutting into its host</b> (S0008 LS-5): the
        /// registrable domain is the one part of a link that says whose it is, and a middle elision
        /// of <c>login.bank.example.attacker.test/..</c> can drop exactly the part that matters. The
        /// scheme and the whole authority stay; only the path gives up characters, from its middle.
        /// A host longer than the caption is shown whole - a long caption is better than a wrong one.
        /// </summary>
        internal static string ElideUrl(string text)
        {
            if (text.Length <= MaxDisplayChars) return text;
            int sep = text.IndexOf("://", StringComparison.Ordinal);
            int start = sep >= 0 ? sep + 3 : 0;
            int end = text.IndexOfAny(new[] { '/', '?', '#' }, start);
            if (end < 0) return text; // the authority is all there is
            string head = text.Substring(0, end);
            int room = MaxDisplayChars - 2 - head.Length;
            if (room <= 0) return head + "..";
            return head + ".." + text.Substring(text.Length - room);
        }
    }
}

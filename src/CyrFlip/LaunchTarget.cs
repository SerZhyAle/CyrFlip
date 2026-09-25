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

    /// <summary>A selection that can be opened, as resolved by <see cref="LaunchTargets.TryParse"/>.</summary>
    internal sealed class LaunchTarget
    {
        public LaunchTarget(string target, string display, LaunchKind kind)
        {
            Target = target; Display = display; Kind = kind;
        }

        /// <summary>Exactly what is handed to the shell.</summary>
        public string Target { get; }
        /// <summary>The same thing shortened for a menu caption.</summary>
        public string Display { get; }
        public LaunchKind Kind { get; }
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
    /// Pure but for the two probes it is handed, so the whole matrix is unit-tested without a disk.
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
        /// confirmation is - just the list of things worth asking about.
        /// </summary>
        private static readonly string[] ExecutableExtensions =
        {
            ".exe", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".msi", ".msp", ".scr", ".pif", ".cpl",
            ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".reg", ".lnk", ".url", ".jar",
            ".py", ".sh", ".msc", ".gadget", ".inf",
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
        /// rules can be tested without a disk, and defaults to the real one.
        /// </summary>
        public static bool TryParse(string? selection, out LaunchTarget? target, Func<string, bool>? exists = null)
        {
            target = null;
            if (string.IsNullOrWhiteSpace(selection)) return false;

            exists = exists ?? DefaultExists;

            // Only the whole-text pass is length-limited - a target longer than this is not a target.
            // The word pass below still runs on a long selection, because a link inside a page of
            // text is exactly the case the user selects by dragging.
            string? text = selection!.Length <= MaxLength ? Normalize(selection) : null;
            if (text != null)
            {
                target = AsPath(text, exists) ?? AsUrl(text);
                if (target != null) return true;
            }

            target = FromWords(selection, exists);
            return target != null;
        }

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
        private static LaunchTarget? FromWords(string selection, Func<string, bool> exists)
        {
            string[] words = selection.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2) return null; // one word was already tried as the whole text

            int examined = 0;
            foreach (string word in words)
            {
                if (++examined > MaxWords) break; // a menu must open now, not after a page of prose
                string? candidate = Normalize(word);
                if (candidate == null) continue;
                LaunchTarget? target = AsPath(candidate, exists) ?? AsUrl(candidate);
                if (target != null) return target;
            }
            return null;
        }

        /// <summary>
        /// Trim the selection down to the candidate: one line, no control characters, without the
        /// brackets and sentence punctuation that come along when a link is selected inside prose.
        /// </summary>
        internal static string? Normalize(string? selection)
        {
            if (string.IsNullOrWhiteSpace(selection)) return null;
            string text = selection!.Trim();
            if (text.Length > MaxLength) return null;

            foreach (char c in text)
                if (char.IsControl(c)) return null; // a newline means this is prose, not a target

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

        /// <summary>
        /// A path, but only one that is there. "Exists" is the whole test: it is what separates a
        /// real target from a word that happens to contain a backslash, and it is also why a
        /// selection is never turned into a command line.
        /// </summary>
        private static LaunchTarget? AsPath(string text, Func<string, bool> exists)
        {
            string path = text;
            if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var uri = new Uri(path);
                    if (!uri.IsFile) return null;
                    path = uri.LocalPath;
                }
                catch { return null; }
            }

            if (path.IndexOf('%') >= 0)
            {
                try { path = Environment.ExpandEnvironmentVariables(path); }
                catch { /* keep the literal text */ }
            }

            if (path.Length < 3) return null;
            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;

            bool rooted;
            try { rooted = Path.IsPathRooted(path); }
            catch { return null; }
            if (!rooted) return null;

            if (!exists(path)) return null;

            bool remote = path.StartsWith(@"\\", StringComparison.Ordinal);
            string extension;
            try { extension = Path.GetExtension(path) ?? ""; }
            catch { extension = ""; }

            LaunchKind kind = remote || IsExecutableExtension(extension) ? LaunchKind.Program : LaunchKind.Document;
            return new LaunchTarget(path, Elide(path), kind);
        }

        private static LaunchTarget? AsUrl(string text)
        {
            if (text.IndexOf(' ') >= 0) return null; // a URL with a space in it is prose around a URL

            // A scheme we allow is taken as given. One we do not is *not* a refusal on its own:
            // "example.com:8080/x" parses as a URI whose scheme is "example.com", so the bare-host
            // rule below still gets its say - and "javascript:alert(1)" fails there too.
            if (Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) && uri != null && IsAllowedScheme(uri.Scheme))
                return new LaunchTarget(uri.AbsoluteUri, Elide(text), LaunchKind.Url);

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
                ? new LaunchTarget(guessed.AbsoluteUri, Elide(text), LaunchKind.Url)
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

        /// <summary>Shorten for a menu caption, keeping both ends - the host and the last segment.</summary>
        internal static string Elide(string text)
        {
            if (text.Length <= MaxDisplayChars) return text;
            int head = (MaxDisplayChars - 2) / 2;
            int tail = MaxDisplayChars - 2 - head;
            return text.Substring(0, head) + ".." + text.Substring(text.Length - tail);
        }

        private static bool DefaultExists(string path)
        {
            try { return File.Exists(path) || Directory.Exists(path); }
            catch { return false; }
        }
    }
}

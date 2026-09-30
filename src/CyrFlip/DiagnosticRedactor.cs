using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CyrFlip
{
    /// <summary>
    /// What the log bundle does to every text it carries before the user is asked to send it
    /// (<c>DIAGNOSTIC-REPORT</c> rule 3, ticket S0040): a personal directory in a path becomes a
    /// placeholder, and the userinfo of a URL is dropped.
    ///
    /// <list type="bullet">
    /// <item>CyrFlip's own data folders become <c>&lt;APP_DATA&gt;</c>, the account's profile
    /// <c>&lt;USER&gt;</c> - the longest root first, either separator, a doubled backslash (a JSON
    /// value in the registry dump) too, case-insensitive, and only as a whole path segment, so
    /// <c>C:\Users\ann</c> never eats the front of <c>C:\Users\anna</c>. The tail of the path stays:
    /// it is the diagnostic part.</item>
    /// <item>Any other <c>X:\Users\&lt;name&gt;</c> - another account, an 8.3 short name, a profile the
    /// roots did not spell the same way - becomes <c>&lt;USER&gt;</c> as well. The shared profiles
    /// (<c>Public</c>, <c>Default</c>, <c>All Users</c>, <c>Default User</c>) name nobody and stay.</item>
    /// <item><c>scheme://user:password@host</c> loses <c>user:password@</c> - an Ollama endpoint behind
    /// a proxy is the one place a CyrFlip setting could carry one.</item>
    /// </list>
    ///
    /// <para>It is a filter over text, not a guarantee: an account name in a path of another shape
    /// (<c>D:\ann\projects</c>) is not recognised, which is why the pre-send dialog still says that
    /// paths may name the account and still lets the user open the archive first.</para>
    /// </summary>
    internal sealed class DiagnosticRedactor
    {
        public const string AppDataToken = "<APP_DATA>";
        public const string UserToken = "<USER>";

        private static readonly Regex AnyProfile = new Regex(
            @"(?<![A-Za-z0-9])[A-Za-z]:(?:\\{1,2}|/)Users(?:\\{1,2}|/)(?!(?:Public|Default|All Users|Default User)(?![^\\/\r\n""'<>|*?:]))[^\\/\r\n""'<>|*?:]+",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex UrlUserInfo = new Regex(
            @"(?<scheme>\b[A-Za-z][A-Za-z0-9+.\-]*://)[^/\s@""'<>]+@",
            RegexOptions.CultureInvariant);

        /// <summary>The generic half only - no machine roots. What a test (or a caller with no roots) gets.</summary>
        public static readonly DiagnosticRedactor Generic = new DiagnosticRedactor(new KeyValuePair<string, string>[0]);

        private readonly List<KeyValuePair<Regex, string>> _roots;

        /// <param name="roots">Directory → placeholder. Order does not matter: the longest is applied first.</param>
        public DiagnosticRedactor(IEnumerable<KeyValuePair<string, string>> roots)
        {
            _roots = roots
                .Where(r => !string.IsNullOrWhiteSpace(r.Key))
                .Select(r => new KeyValuePair<string, string>(r.Key.TrimEnd('\\', '/'), r.Value))
                .Where(r => r.Key.Length >= 3) // never a bare drive root
                .OrderByDescending(r => r.Key.Length)
                .Select(r => new KeyValuePair<Regex, string>(RootPattern(r.Key), r.Value))
                .ToList();
        }

        /// <summary>
        /// This machine's roots: the per-user data folder (logs, notes), the scenario store's
        /// <c>%APPDATA%\CyrFlip</c>, the package's own folder when packaged, and the profile.
        /// </summary>
        public static DiagnosticRedactor ForThisMachine()
        {
            var roots = new List<KeyValuePair<string, string>>();
            void Add(Func<string> read, string token)
            {
                try
                {
                    string path = read();
                    if (!string.IsNullOrWhiteSpace(path)) roots.Add(new KeyValuePair<string, string>(path, token));
                }
                catch { /* a root we cannot read is a root the generic pattern still covers */ }
            }
            Add(() => DataFolder.Current, AppDataToken);
            Add(() => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CyrFlip"), AppDataToken);
            Add(() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), UserToken);
            return new DiagnosticRedactor(roots);
        }

        public string Redact(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string result = text;
            foreach (KeyValuePair<Regex, string> root in _roots)
                result = root.Key.Replace(result, root.Value);
            result = AnyProfile.Replace(result, UserToken);
            result = UrlUserInfo.Replace(result, m => m.Groups["scheme"].Value);
            return result;
        }

        /// <summary>UTF-8 in, UTF-8 out; the very same array when nothing changed.</summary>
        public byte[] Redact(byte[] utf8)
        {
            string text = Encoding.UTF8.GetString(utf8);
            string redacted = Redact(text);
            return ReferenceEquals(redacted, text) || redacted == text ? utf8 : Encoding.UTF8.GetBytes(redacted);
        }

        /// <summary>
        /// A root as a pattern: every separator matches <c>\</c>, <c>\\</c> or <c>/</c>, and the match
        /// must end a path segment - the next character is a separator, a quote, whitespace or the end.
        /// </summary>
        private static Regex RootPattern(string root)
        {
            var sb = new StringBuilder("(?<![A-Za-z0-9])");
            foreach (char c in root)
                sb.Append(c == '\\' || c == '/' ? @"(?:\\{1,2}|/)" : Regex.Escape(c.ToString()));
            sb.Append(@"(?![^\\/\s""'<>|*?:])");
            return new Regex(sb.ToString(), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CyrFlip
{
    /// <summary>
    /// What the log bundle does to every text it carries before the user is asked to send it
    /// (<c>DIAGNOSTIC-REPORT</c> rule 3 and its sections 7 and 8 C, tickets S0040 and S0043): a personal
    /// directory in a path becomes a placeholder, and a secret is replaced by <c>[REDACTED]</c> wherever
    /// it sits - by its value's shape, never by the name of the file or setting it came from.
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
    /// a proxy is the one place a CyrFlip setting could carry one. A password that itself holds
    /// <c>/</c>, <c>?</c> or <c>#</c> makes the address unparsable; it is then cut as text, up to the last
    /// <c>@</c>, never returned verbatim (section 7 item 3).</item>
    /// <item>The secret shapes of section 7 and the by-value rule of section 8 C: a secret query
    /// parameter (<c>token</c>, <c>X-Amz-Signature</c>, <c>hdnts</c>.. - also after an HTML-escaped
    /// <c>&amp;amp;</c>), the two account segments of <c>/live|movie|series/user/pass/id</c>, a
    /// <c>name=value</c> or <c>name: value</c> pair whose name is a credential (a connection string's
    /// <c>Password=</c>), a JSON field whose key is a credential - whatever key the blob sits under -, an
    /// <c>Authorization</c> or <c>Cookie</c> header, a <c>Bearer</c> token and a PEM private key block.
    /// The value goes, the name stays.</item>
    /// </list>
    ///
    /// <para>Redaction runs line by line under a time budget: a line that cannot be redacted in time is
    /// replaced by <c>[Diag] PATH REDACTION TIMEOUT | dropped_line_bytes=&lt;n&gt;</c> - never passed
    /// through, and never at the cost of its neighbours (section 7).</para>
    ///
    /// <para>It is a filter over text, not a guarantee: an account name in a path of another shape
    /// (<c>D:\ann\projects</c>) is not recognised, which is why the pre-send dialog still says that
    /// paths may name the account and still lets the user open the archive first.</para>
    /// </summary>
    internal sealed class DiagnosticRedactor
    {
        public const string AppDataToken = "<APP_DATA>";
        public const string UserToken = "<USER>";
        public const string SecretToken = "[REDACTED]";

        /// <summary>The time one regular expression may spend on one line before the line is dropped.</summary>
        private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(250);

        private static Regex Pattern(string pattern, RegexOptions extra = RegexOptions.None)
            => new Regex(pattern, RegexOptions.CultureInvariant | extra, Budget);

        private static readonly Regex AnyProfile = Pattern(
            @"(?<![A-Za-z0-9])[A-Za-z]:(?:\\{1,2}|/)Users(?:\\{1,2}|/)(?!(?:Public|Default|All Users|Default User)(?![^\\/\r\n""'<>|*?:]))[^\\/\r\n""'<>|*?:]+",
            RegexOptions.IgnoreCase);

        private static readonly Regex UrlUserInfo = Pattern(
            @"(?<scheme>\b[A-Za-z][A-Za-z0-9+.\-]*://)[^/\s@""'<>]+@");

        /// <summary>
        /// <c>scheme://user:pa/ss@host/..</c>: the userinfo looks like <c>name:secret</c> and a <c>/</c>, <c>?</c>
        /// or <c>#</c> in the secret ends the authority early, so the first pattern cannot see the <c>@</c>.
        /// Everything up to the last <c>@</c> of the token goes. A host:port followed by a path that holds
        /// an <c>@</c> is cut too - an over-redaction, which is the safe side of the error.
        /// </summary>
        private static readonly Regex UrlUserInfoWithSeparators = Pattern(
            @"(?<scheme>\b[A-Za-z][A-Za-z0-9+.\-]*://)[^/?#\s@""'<>]*:[^/?#\s@""'<>]*[/?#][^\s""'<>]*@");

        private static readonly Regex CredentialInPath = Pattern(
            @"/(?<kind>live|movie|series)/[^/\s""'<>?#]+/[^/\s""'<>?#]+/(?<id>[^/\s""'<>?#]+)",
            RegexOptions.IgnoreCase);

        /// <summary>A PEM private key block, to its END line - or, if the text was cut mid-block, to the end of the text.</summary>
        private static readonly Regex PrivateKeyBlock = Pattern(
            @"-----BEGIN [A-Z0-9 ]*PRIVATE KEY(?: BLOCK)?-----.*?(?:-----END [A-Z0-9 ]*PRIVATE KEY(?: BLOCK)?-----|\z)",
            RegexOptions.Singleline);

        private static readonly Regex Header = Pattern(
            @"(?<![A-Za-z0-9-])(?<name>(?:Proxy-)?Authorization|Set-Cookie|Cookie)(?<sep>\s*:\s*)[^\r\n]+",
            RegexOptions.IgnoreCase);

        private static readonly Regex Bearer = Pattern(
            @"\bBearer\s+[A-Za-z0-9._~+/=\-]{8,}", RegexOptions.IgnoreCase);

        private const string SecretNames =
            @"password|passwd|pwd|pass|passphrase|pin|access[_-]?pin|secret|(?:client|app|api|shared)[_-]?secret"
            + @"|api[_-]?key|access[_-]?key|private[_-]?key|(?:access|refresh|auth|bearer|session|id)[_-]?token|token"
            + @"|salt|credentials?|auth|signature|x-amz-(?:signature|credential|security-token)|wmsAuthSign|hdnts|hdnea|policy|key-pair-id";

        /// <summary>
        /// <c>name=value</c> and <c>name: value</c> for a credential's name - a query parameter, a connection
        /// string's pair, a settings line. The name is a whole word (<c>ExecutionPolicy</c> is not
        /// <c>Policy</c>), the value runs to a separator or a quote.
        /// </summary>
        private static readonly Regex SecretPair = Pattern(
            @"(?<![A-Za-z0-9])(?<name>" + SecretNames + @")(?<sep>\s*[=:]\s*)(?<value>\[REDACTED\]|""[^""\r\n]*""|'[^'\r\n]*'|[^\s;&""'<>,}\]]+)",
            RegexOptions.IgnoreCase);

        /// <summary>A JSON field: its key decides, whatever the key of the blob it sits in is called.</summary>
        private static readonly Regex JsonField = Pattern(
            @"(?<head>""(?<key>[^""\\\r\n]+)""\s*:\s*)(?<value>""(?:[^""\\\r\n]|\\.)*""|[^\s,}\]""]+)");

        private static readonly Regex JsonSecretKey = Pattern(
            @"password|passwd|pwd|passphrase|secret|token|api[_-]?key|private[_-]?key|credential|authorization|signature|salt$|(?:^|[a-z])pin$|auth$",
            RegexOptions.IgnoreCase);

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
            string blocks;
            try { blocks = PrivateKeyBlock.Replace(text, SecretToken); }
            catch (RegexMatchTimeoutException) { return TimeoutMarker(Encoding.UTF8.GetByteCount(text)); }
            return RedactLines(blocks, RedactLine);
        }

        /// <summary>UTF-8 in, UTF-8 out; the very same array when nothing changed.</summary>
        public byte[] Redact(byte[] utf8)
        {
            string text = Encoding.UTF8.GetString(utf8);
            string redacted = Redact(text);
            return ReferenceEquals(redacted, text) || redacted == text ? utf8 : Encoding.UTF8.GetBytes(redacted);
        }

        /// <summary>The marker a line that ran out of time is replaced by (<c>DIAGNOSTIC-REPORT</c> section 7).</summary>
        internal static string TimeoutMarker(int droppedBytes)
            => "[Diag] PATH REDACTION TIMEOUT | dropped_line_bytes=" + droppedBytes;

        /// <summary>
        /// Applies <paramref name="redactLine"/> to every line on its own. A line whose redaction throws
        /// <see cref="RegexMatchTimeoutException"/> is replaced by <see cref="TimeoutMarker"/> and only that
        /// line: it is never passed through unredacted, and it does not cost the lines around it. Line
        /// endings (LF, CRLF, or none after the last line) are kept as they were.
        /// </summary>
        internal static string RedactLines(string text, Func<string, string> redactLine)
        {
            var result = new StringBuilder(text.Length);
            int start = 0;
            while (start < text.Length)
            {
                int newline = text.IndexOf('\n', start);
                int end = newline < 0 ? text.Length : newline;
                bool carriageReturn = end > start && text[end - 1] == '\r';
                string body = text.Substring(start, end - start - (carriageReturn ? 1 : 0));
                string redacted;
                try { redacted = redactLine(body); }
                catch (RegexMatchTimeoutException) { redacted = TimeoutMarker(Encoding.UTF8.GetByteCount(body)); }
                result.Append(redacted);
                if (carriageReturn) result.Append('\r');
                if (newline >= 0) result.Append('\n');
                start = end + 1;
            }
            return result.ToString();
        }

        private string RedactLine(string line)
        {
            if (line.Length == 0) return line;
            string result = line;
            foreach (KeyValuePair<Regex, string> root in _roots)
                result = root.Key.Replace(result, root.Value);
            result = AnyProfile.Replace(result, UserToken);

            result = UrlUserInfo.Replace(result, m => m.Groups["scheme"].Value);
            result = UrlUserInfoWithSeparators.Replace(result, m => m.Groups["scheme"].Value);
            result = CredentialInPath.Replace(result, m => "/" + m.Groups["kind"].Value + "/" + SecretToken + "/" + SecretToken + "/" + m.Groups["id"].Value);

            result = Header.Replace(result, m => m.Groups["name"].Value + m.Groups["sep"].Value + SecretToken);
            result = Bearer.Replace(result, "Bearer " + SecretToken);
            result = JsonField.Replace(result, m => JsonSecretKey.IsMatch(m.Groups["key"].Value)
                ? m.Groups["head"].Value + "\"" + SecretToken + "\""
                : m.Value);
            result = SecretPair.Replace(result, m => IsAlreadyRedacted(m.Groups["value"].Value)
                ? m.Value
                : m.Groups["name"].Value + m.Groups["sep"].Value + SecretToken);
            return result;
        }

        private static bool IsAlreadyRedacted(string value) => value.Contains(SecretToken);

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
            return Pattern(sb.ToString(), RegexOptions.IgnoreCase);
        }
    }
}

using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace CyrFlip
{
    /// <summary>Why an address was refused - the editor words it, the code never discovers it at run time.</summary>
    internal enum AddressIssue
    {
        None,
        Empty,
        UnknownKind,
        BadHost,
        BadPort,
        BadUrl,
        BadPath,
        BadDrive,
        BadFingerprint,
        BadStatusCodes,
    }

    /// <summary>
    /// Address validation per kind (ticket S0069 section 5, "addresses are validated before save"). Pure: no
    /// DNS lookup, no connection, no disk. The recognizer, the editor and the run-time re-check of an imported
    /// or hand-edited list all go through here, so they cannot disagree about what a valid address is.
    /// </summary>
    internal static class TrafficLightAddress
    {
        public const int MaxAddressLength = 2048;

        public static AddressIssue Validate(TrafficLightTarget target)
        {
            AddressIssue issue = Validate(target.Kind, target.Address, target.Fingerprint);
            if (issue != AddressIssue.None) return issue;
            if (target.Kind == TrafficLightKinds.Http && !TrafficLightStatusCodes.TryParse(target.AcceptedStatusCodes, out _))
                return AddressIssue.BadStatusCodes;
            return AddressIssue.None;
        }

        public static AddressIssue Validate(string? kind, string? address, string? fingerprint)
        {
            if (!TrafficLightKinds.IsKnown(kind)) return AddressIssue.UnknownKind;
            string text = (address ?? "").Trim();
            if (text.Length == 0) return AddressIssue.Empty;
            if (text.Length > MaxAddressLength) return AddressIssue.BadPath;

            string fp = (fingerprint ?? "").Trim();
            if (kind != TrafficLightKinds.Exchange && fp.Length > 0) return AddressIssue.BadFingerprint;
            if (kind == TrafficLightKinds.Exchange && fp.Length > 0 && !IsValidFingerprint(fp)) return AddressIssue.BadFingerprint;

            switch (kind)
            {
                case TrafficLightKinds.Ping:
                    return IsValidHost(text) ? AddressIssue.None : AddressIssue.BadHost;
                case TrafficLightKinds.Tcp:
                case TrafficLightKinds.Sftp:
                case TrafficLightKinds.Exchange:
                    if (!TryParseHostPort(text, out string host, out int port))
                    {
                        // No port, a port that is not 1-65535, or a bare IPv6 literal (which needs its brackets to
                        // carry one) are port problems; everything else the host part says for itself.
                        return text.IndexOf(':') < 0 || IsIpv6Literal(text) || LooksLikePortProblem(text)
                            ? AddressIssue.BadPort : AddressIssue.BadHost;
                    }
                    return IsValidHost(host) && port >= 1 && port <= 65535 ? AddressIssue.None : AddressIssue.BadHost;
                case TrafficLightKinds.Http:
                    return IsValidHttpUrl(text) ? AddressIssue.None : AddressIssue.BadUrl;
                case TrafficLightKinds.Folder:
                    return IsValidFolderPath(text) ? AddressIssue.None : AddressIssue.BadPath;
                case TrafficLightKinds.Drive:
                    return IsDriveLetter(text) ? AddressIssue.None : AddressIssue.BadDrive;
                default:
                    return AddressIssue.UnknownKind;
            }
        }

        // "host:" with nothing after it, or a port that is not a number, is a port problem and not a host one.
        private static bool LooksLikePortProblem(string text)
        {
            int colon = text.LastIndexOf(':');
            if (colon <= 0 || text.IndexOf(']') > colon) return false;
            string host = text.Substring(0, colon);
            return IsValidHost(host.TrimStart('[').TrimEnd(']'));
        }

        // ---- hosts ----

        /// <summary>An IPv4 or IPv6 literal (no zone) or a host name of the usual RFC 1123 shape.</summary>
        public static bool IsValidHost(string? host)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;
            string h = host!.Trim();
            if (h.Length > 253) return false;
            if (h.IndexOf(':') >= 0) return IsIpv6Literal(h);
            if (IsIpv4(h)) return true;
            return IsHostName(h);
        }

        /// <summary>A dotted quad: exactly four groups of one to three digits, each at most 255.</summary>
        public static bool IsIpv4(string text)
        {
            string[] parts = text.Split('.');
            if (parts.Length != 4) return false;
            foreach (string part in parts)
            {
                if (part.Length == 0 || part.Length > 3) return false;
                foreach (char c in part) if (c < '0' || c > '9') return false;
                if (int.Parse(part, CultureInfo.InvariantCulture) > 255) return false;
            }
            return true;
        }

        /// <summary>An IPv6 literal without a zone: it contains a colon and parses as IPv6.</summary>
        public static bool IsIpv6Literal(string text)
        {
            // No zone, no brackets and no port: .NET would read "[::1]:80" as the address and drop the rest.
            if (text.IndexOf(':') < 0 || text.IndexOfAny(new[] { '%', '[', ']', '/', ' ' }) >= 0) return false;
            return IPAddress.TryParse(text, out IPAddress? parsed) && parsed != null
                && parsed.AddressFamily == AddressFamily.InterNetworkV6;
        }

        // Letters (any script - a Windows machine may be named in Cyrillic), digits, hyphen and underscore in
        // labels of 1-63 characters; a label never starts or ends with a hyphen; a name made only of digits
        // and dots is an address that failed to be one, not a name.
        private static bool IsHostName(string text)
        {
            if (text.EndsWith(".", StringComparison.Ordinal) || text.StartsWith(".", StringComparison.Ordinal)) return false;
            bool onlyDigitsAndDots = true;
            foreach (char c in text)
                if ((c < '0' || c > '9') && c != '.') { onlyDigitsAndDots = false; break; }
            if (onlyDigitsAndDots) return false;

            foreach (string label in text.Split('.'))
            {
                if (label.Length == 0 || label.Length > 63) return false;
                if (label[0] == '-' || label[label.Length - 1] == '-') return false;
                foreach (char c in label)
                    if (!char.IsLetterOrDigit(c) && c != '-' && c != '_') return false;
            }
            return true;
        }

        /// <summary>
        /// Splits <c>host:port</c>, <c>[v6]:port</c>. False when there is no port, it is not 1-65535 digits, or the
        /// text is a bare IPv6 literal (which needs its brackets to carry a port).
        /// </summary>
        public static bool TryParseHostPort(string? text, out string host, out int port)
        {
            host = ""; port = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text!.Trim();
            string portText;
            if (t[0] == '[')
            {
                int close = t.IndexOf(']');
                if (close < 0 || close + 1 >= t.Length || t[close + 1] != ':') return false;
                host = t.Substring(1, close - 1);
                portText = t.Substring(close + 2);
                if (!IsIpv6Literal(host)) return false;
            }
            else
            {
                int colon = t.LastIndexOf(':');
                if (colon <= 0 || t.IndexOf(':') != colon) return false; // none, or a bare IPv6 literal
                host = t.Substring(0, colon);
                portText = t.Substring(colon + 1);
            }
            if (portText.Length == 0 || portText.Length > 5) return false;
            foreach (char c in portText) if (c < '0' || c > '9') return false;
            port = int.Parse(portText, CultureInfo.InvariantCulture);
            return port >= 1 && port <= 65535 && host.Length > 0;
        }

        /// <summary><c>host:port</c> in the form the stored address takes (IPv6 in brackets).</summary>
        public static string JoinHostPort(string host, int port)
            => (host.IndexOf(':') >= 0 && host[0] != '[' ? "[" + host + "]" : host) + ":" + port.ToString(CultureInfo.InvariantCulture);

        // ---- the other kinds ----

        /// <summary>An absolute http or https URL with a valid host, no credentials, no space.</summary>
        public static bool IsValidHttpUrl(string text)
        {
            if (text.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0) return false;
            if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || uri == null) return false;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
            if (uri.UserInfo.Length > 0) return false; // no credentials, ever (section 2)
            string host = uri.Host;
            if (host.Length > 0 && host[0] == '[') host = host.Trim('[', ']');
            return IsValidHost(host);
        }

        /// <summary>A drive-letter path or a UNC path, no wildcard and no device namespace.</summary>
        public static bool IsValidFolderPath(string text)
        {
            if (text.IndexOfAny(new[] { '<', '>', '"', '|', '?', '*', '\0' }) >= 0) return false;
            bool drivePath = text.Length >= 3 && IsAsciiLetter(text[0]) && text[1] == ':' && (text[2] == '\\' || text[2] == '/');
            bool uncPath = text.Length >= 5 && (text.StartsWith(@"\\", StringComparison.Ordinal) || text.StartsWith("//", StringComparison.Ordinal));
            if (!drivePath && !uncPath) return false;
            if (uncPath)
            {
                string[] parts = text.Substring(2).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !IsValidHost(parts[0])) return false;
            }
            try { return Path.IsPathRooted(text); }
            catch { return false; }
        }

        /// <summary>A single drive letter and a colon, the form a drive target is stored in (D18).</summary>
        public static bool IsDriveLetter(string text)
            => text.Length == 2 && IsAsciiLetter(text[0]) && text[1] == ':';

        /// <summary><c>E:</c>, <c>e:\</c> and <c>E:/</c> are one drive; anything else is returned untouched.</summary>
        public static string NormalizeDrive(string text)
        {
            string t = text.Trim();
            if (t.Length >= 2 && IsAsciiLetter(t[0]) && t[1] == ':' && (t.Length == 2 || (t.Length == 3 && (t[2] == '\\' || t[2] == '/'))))
                return char.ToUpperInvariant(t[0]) + ":";
            return text;
        }

        private static bool IsAsciiLetter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

        /// <summary>
        /// <c>SHA256:</c> and exactly 43 characters of the standard base64 alphabet without padding, the last one
        /// carrying no stray bits (the <c>FMSEXCH1</c> <c>fp</c> rule: a 32-byte hash is 43 characters and two
        /// spare bits, which must be zero).
        /// </summary>
        public static bool IsValidFingerprint(string? text)
        {
            const string prefix = "SHA256:";
            if (text == null || text.Length != prefix.Length + 43) return false;
            if (!text.StartsWith(prefix, StringComparison.Ordinal)) return false;
            for (int i = prefix.Length; i < text.Length; i++)
            {
                char c = text[i];
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '+' || c == '/';
                if (!ok) return false;
            }
            // 43 characters carry 258 bits for a 256-bit hash: the last character's low two bits must be zero.
            return Base64Value(text[text.Length - 1]) % 4 == 0;
        }

        private static int Base64Value(char c)
        {
            if (c >= 'A' && c <= 'Z') return c - 'A';
            if (c >= 'a' && c <= 'z') return c - 'a' + 26;
            if (c >= '0' && c <= '9') return c - '0' + 52;
            return c == '+' ? 62 : 63;
        }

        /// <summary>The host part of a target's address, for display and for the machine-name checks.</summary>
        public static string HostOf(string? kind, string? address)
        {
            string text = (address ?? "").Trim();
            switch (kind)
            {
                case TrafficLightKinds.Tcp:
                case TrafficLightKinds.Sftp:
                case TrafficLightKinds.Exchange:
                    return TryParseHostPort(text, out string host, out _) ? host : text;
                case TrafficLightKinds.Http:
                    return Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) && uri != null ? uri.Host : text;
                default:
                    return text;
            }
        }
    }
}

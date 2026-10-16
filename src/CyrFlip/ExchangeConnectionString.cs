using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CyrFlip
{
    /// <summary>
    /// The result of reading a contract string (<c>FMSEXCH1</c>, <c>FMSSFTP1/2</c>). A refusal carries the
    /// contract's own reason word, in the vocabulary of <c>vectors/connection-string-v1.json</c>.
    /// </summary>
    internal sealed class ExchangeConnectionParse
    {
        public bool Ok;
        public string Reason = "";
        /// <summary>What a not-a-string text resembles: <c>share-code</c> or <c>url</c>, else empty.</summary>
        public string Looks = "";
        public string Host = "";
        public int Port;
        /// <summary>The pin of the server's leaf certificate, or empty (a CA-signed deployment writes none).</summary>
        public string Fingerprint = "";
        public string Name = "";
        /// <summary>True when the string carried a <c>u</c> login hint - read, validated and dropped (D21).</summary>
        public bool LoginDropped;
        /// <summary>The login hint when explicitly preserved for account enrollment; empty by default (dropped).</summary>
        public string LoginHint = "";
    }

    /// <summary>
    /// The reading half of the <c>FMSEXCH1</c> connection string (<c>DEVICE-EXCHANGE</c> section 5.5; ticket
    /// S0069 D21). CyrFlip is a consumer only: it reads <c>x</c> (host and port), <c>fp</c> (the pin) and
    /// <c>n</c> (a label hint), validates <c>u</c> and drops it, and keeps nothing of the raw text.
    ///
    /// <para>Strict by contract: a string that breaks any rule is refused <b>whole</b>, nothing is partially
    /// read, and an unknown key is ignored whatever its value.</para>
    /// </summary>
    internal static class ExchangeConnectionString
    {
        public const string Prefix = "FMSEXCH1:";
        /// <summary>The port an exchange server listens on unless its operator chose another (7.1: one port).</summary>
        public const int DefaultPort = 22022;
        public const int MaxText = 512;
        public const int MaxName = 64;

        public static ExchangeConnectionParse Parse(string? text) => Parse(text, false);

        public static ExchangeConnectionParse Parse(string? text, bool keepLoginHint)
        {
            var result = new ExchangeConnectionParse();
            string t = text ?? "";
            if (t.Length > 0 && t[0] == '﻿') t = t.Substring(1);
            t = t.Trim();
            if (t.Length == 0) return NotAString(result, t);

            // The prefix: FMSEXCH, one digit, a colon. A later major is "update", a zero is not ours at all.
            if (!t.StartsWith("FMSEXCH", StringComparison.Ordinal) || t.Length < 9 || t[8] != ':'
                || t[7] < '0' || t[7] > '9')
                return NotAString(result, t);
            if (t[7] == '0') return NotAString(result, t);
            if (t[7] != '1') return Refuse(result, "newer-major");

            if (t.Length > MaxText) return Refuse(result, "too-long");
            foreach (char c in t)
                if (char.IsWhiteSpace(c)) return Refuse(result, "whitespace");
            foreach (char c in t)
                if (c < 0x21 || c > 0x7E) return Refuse(result, "not-ascii");

            string body = t.Substring(Prefix.Length);
            if (body.Length == 0) return Refuse(result, "missing-x");

            string? x = null, fp = null, n = null, u = null;
            foreach (string field in body.Split('&'))
            {
                if (field.Length == 0) return Refuse(result, "malformed-field");
                int eq = field.IndexOf('=');
                if (eq < 0) return Refuse(result, "malformed-field");
                string key = field.Substring(0, eq);
                if (key.Length == 0) return Refuse(result, "malformed-field");
                if (key != "x" && key != "fp" && key != "n" && key != "u") continue; // unknown: ignored, whatever its value

                if (!FormUrlText.TryDecode(field.Substring(eq + 1), out string? value, out string decodeReason))
                    return Refuse(result, decodeReason);
                switch (key)
                {
                    case "x": if (x != null) return Refuse(result, "repeated-key"); x = value; break;
                    case "fp": if (fp != null) return Refuse(result, "repeated-key"); fp = value; break;
                    case "n": if (n != null) return Refuse(result, "repeated-key"); n = value; break;
                    default: if (u != null) return Refuse(result, "repeated-key"); u = value; break;
                }
            }

            if (x == null) return Refuse(result, "missing-x");
            if (!ContractEndpoint.TryParse(x, out string host, out int port)) return Refuse(result, "bad-x");
            if (fp != null && !TrafficLightAddress.IsValidFingerprint(fp)) return Refuse(result, "bad-fp");
            if (n != null && !IsValidName(n)) return Refuse(result, "bad-n");
            if (u != null && !IsValidLogin(u)) return Refuse(result, "bad-u");

            result.Ok = true;
            result.Host = host;
            result.Port = port;
            result.Fingerprint = fp ?? "";
            result.Name = n ?? "";
            result.LoginDropped = u != null;
            if (keepLoginHint) result.LoginHint = u ?? "";
            return result;
        }

        private static ExchangeConnectionParse Refuse(ExchangeConnectionParse result, string reason)
        {
            result.Ok = false;
            result.Reason = reason;
            return result;
        }

        private static ExchangeConnectionParse NotAString(ExchangeConnectionParse result, string text)
        {
            result.Ok = false;
            result.Reason = "not-a-string";
            if (text.StartsWith("FMSSFTP", StringComparison.Ordinal)) result.Looks = "share-code";
            else if (text.IndexOf("://", StringComparison.Ordinal) > 0) result.Looks = "url";
            return result;
        }

        /// <summary>At most 64 Unicode scalar values, at least one, no control character (category Cc).</summary>
        private static bool IsValidName(string name)
        {
            if (name.Length == 0) return false;
            int scalars = 0;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsControl(c)) return false;
                if (char.IsHighSurrogate(c) && i + 1 < name.Length && char.IsLowSurrogate(name[i + 1])) i++;
                scalars++;
            }
            return scalars <= MaxName;
        }

        /// <summary>The login form of 5.1: 3-64 of <c>a-z 0-9 . _ -</c>; an upper-case input is tolerated.</summary>
        private static bool IsValidLogin(string login)
        {
            string lower = login.ToLowerInvariant();
            if (lower.Length < 3 || lower.Length > 64) return false;
            foreach (char c in lower)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';
                if (!ok) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// The <c>host:port</c> endpoint of the exchange contracts (the <c>x</c> of <c>FMSEXCH1</c> and of
    /// <c>FMSSFTP2</c>): a DNS name of ASCII letters, digits and hyphen, an IPv4 dotted quad, or an IPv6 literal
    /// in brackets with no zone; a port of one to five digits, 1-65535.
    /// </summary>
    internal static class ContractEndpoint
    {
        public static bool TryParse(string text, out string host, out int port)
        {
            host = ""; port = 0;
            if (text.Length == 0) return false;
            string hostText, portText;
            if (text[0] == '[')
            {
                int close = text.IndexOf(']');
                if (close < 0 || close + 1 >= text.Length || text[close + 1] != ':') return false;
                hostText = text.Substring(1, close - 1);
                portText = text.Substring(close + 2);
                // A literal in brackets must be IPv6: an IPv4 address or a name there is refused.
                if (hostText.IndexOf(':') < 0 || hostText.IndexOf('%') >= 0) return false;
                if (!IPAddress.TryParse(hostText, out IPAddress? address) || address == null
                    || address.AddressFamily != AddressFamily.InterNetworkV6) return false;
                hostText = address.ToString();
            }
            else
            {
                int colon = text.LastIndexOf(':');
                if (colon <= 0 || text.IndexOf(':') != colon) return false; // none, or a bare IPv6 literal
                hostText = text.Substring(0, colon);
                portText = text.Substring(colon + 1);
                if (!IsDnsNameOrIpv4(hostText)) return false;
                hostText = hostText.ToLowerInvariant();
            }
            if (portText.Length == 0 || portText.Length > 5) return false;
            foreach (char c in portText) if (c < '0' || c > '9') return false;
            int value = int.Parse(portText, CultureInfo.InvariantCulture);
            if (value < 1 || value > 65535) return false;
            host = hostText;
            port = value;
            return true;
        }

        /// <summary>A name of digits and dots is read as IPv4 or refused (the contract's own words).</summary>
        private static bool IsDnsNameOrIpv4(string text)
        {
            if (text.Length == 0 || text.Length > 253) return false;
            bool onlyDigitsAndDots = true;
            foreach (char c in text)
                if ((c < '0' || c > '9') && c != '.') { onlyDigitsAndDots = false; break; }
            if (onlyDigitsAndDots) return TrafficLightAddress.IsIpv4(text);

            foreach (string label in text.Split('.'))
            {
                if (label.Length == 0 || label.Length > 63) return false;
                if (label[0] == '-' || label[label.Length - 1] == '-') return false;
                foreach (char c in label)
                {
                    bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-';
                    if (!ok) return false;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// Decoding of an <c>application/x-www-form-urlencoded</c> value over UTF-8, as the exchange contracts
    /// write it: <c>%XX</c> with hex of either case, <c>+</c> as a space, and nothing else is changed.
    /// </summary>
    internal static class FormUrlText
    {
        /// <summary>
        /// False with <c>bad-escape</c> for a <c>%</c> that is not followed by two hex digits and
        /// <c>bad-utf8</c> for decoded bytes that are not UTF-8.
        /// </summary>
        public static bool TryDecode(string raw, out string? value, out string reason)
        {
            value = null; reason = "";
            var bytes = new List<byte>(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '%')
                {
                    if (i + 2 >= raw.Length) { reason = "bad-escape"; return false; }
                    int high = Hex(raw[i + 1]), low = Hex(raw[i + 2]);
                    if (high < 0 || low < 0) { reason = "bad-escape"; return false; }
                    bytes.Add((byte)(high * 16 + low));
                    i += 2;
                }
                else if (c == '+') bytes.Add((byte)' ');
                else if (c < 0x80) bytes.Add((byte)c);
                else { reason = "not-ascii"; return false; }
            }
            try
            {
                value = new UTF8Encoding(false, true).GetString(bytes.ToArray());
                return true;
            }
            catch (ArgumentException)
            {
                reason = "bad-utf8";
                return false;
            }
        }

        private static int Hex(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }
    }
}

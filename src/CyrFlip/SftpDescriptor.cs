using System;
using System.Collections.Generic;
using System.Globalization;

namespace CyrFlip
{
    /// <summary>The result of reading an <c>FMSSFTP1</c> / <c>FMSSFTP2</c> share code.</summary>
    internal sealed class SftpDescriptorParse
    {
        public bool Ok;
        public string Reason = "";
        /// <summary>1 or 2, the prefix digit.</summary>
        public int Version;
        /// <summary>The <c>h</c> hosts, in the code's order; all on <see cref="Port"/>.</summary>
        public List<string> Hosts = new List<string>();
        public int Port;
        /// <summary>The exchange endpoint <c>x</c> of a v2 code (host and port), or empty and 0.</summary>
        public string ExchangeHost = "";
        public int ExchangePort;
        /// <summary>True when the code carried a login or a password - read, validated and dropped (D21).</summary>
        public bool SecretsDropped;
    }

    /// <summary>
    /// The reading half of the share descriptor (<c>ANYWHERE-ACCESS</c> section 4.1; ticket S0069 D21). CyrFlip
    /// reads <c>h</c>, <c>p</c> and, in a v2 code, <c>x</c>; the login <c>u</c>, the password <c>pw</c>, the
    /// share's host-key pin <c>fp</c> and the share id <c>sid</c> are validated - a code that breaks a rule is
    /// refused whole - and dropped on the spot. A <c>FMSSFTP1</c> code is read by the v1 rules only: v2 keys
    /// inside it are ignored (conformance vectors <c>descriptor-v2.json</c>).
    /// </summary>
    internal static class SftpDescriptor
    {
        public const int MaxHosts = 16;
        public const int MaxText = 2048;

        public static SftpDescriptorParse Parse(string? text)
        {
            var result = new SftpDescriptorParse();
            string t = text ?? "";
            if (t.Length > 0 && t[0] == '﻿') t = t.Substring(1);
            t = t.Trim();

            int version;
            if (t.StartsWith("FMSSFTP1:", StringComparison.Ordinal)) version = 1;
            else if (t.StartsWith("FMSSFTP2:", StringComparison.Ordinal)) version = 2;
            else { result.Reason = "not-a-descriptor"; return result; }
            result.Version = version;
            if (t.Length > MaxText) return Refuse(result, "too-long");
            foreach (char c in t)
                if (char.IsWhiteSpace(c)) return Refuse(result, "whitespace");

            string body = t.Substring("FMSSFTP1:".Length);
            if (body.Length == 0) return Refuse(result, "missing-h");

            string? h = null, p = null, u = null, pw = null, fp = null, x = null, sid = null, drv = null;
            foreach (string field in body.Split('&'))
            {
                int eq = field.IndexOf('=');
                if (eq <= 0) return Refuse(result, "malformed-field");
                string key = field.Substring(0, eq);
                string value = field.Substring(eq + 1);
                switch (key)
                {
                    case "h": if (h != null) return Refuse(result, "repeated-key"); h = value; break;
                    case "p": if (p != null) return Refuse(result, "repeated-key"); p = value; break;
                    case "u": if (u != null) return Refuse(result, "repeated-key"); u = value; break;
                    case "pw": if (pw != null) return Refuse(result, "repeated-key"); pw = value; break;
                    case "fp": if (fp != null) return Refuse(result, "repeated-key"); fp = value; break;
                    // v2 keys: read only by a v2 code, ignored by a v1 reader whatever they hold
                    case "x": if (version == 2) { if (x != null) return Refuse(result, "repeated-key"); x = value; } break;
                    case "sid": if (version == 2) { if (sid != null) return Refuse(result, "repeated-key"); sid = value; } break;
                    case "drv": if (version == 2) { if (drv != null) return Refuse(result, "repeated-key"); drv = value; } break;
                }
            }

            // The v1 field set: h, p, u and fp are required; pw is absent in key-login mode.
            if (h == null || h.Length == 0) return Refuse(result, "missing-h");
            if (p == null) return Refuse(result, "missing-p");
            if (u == null) return Refuse(result, "missing-u");
            if (fp == null) return Refuse(result, "missing-fp");

            foreach (string rawHost in h.Split(','))
            {
                if (!FormUrlText.TryDecode(rawHost, out string? host, out _) || host == null
                    || !TrafficLightAddress.IsValidHost(host))
                    return Refuse(result, "bad-h");
                result.Hosts.Add(host.ToLowerInvariant());
                if (result.Hosts.Count > MaxHosts) return Refuse(result, "bad-h");
            }

            if (!int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || p.Length > 5
                || port < 1 || port > 65535)
                return Refuse(result, "bad-p");
            result.Port = port;

            if (!FormUrlText.TryDecode(u, out string? login, out _) || string.IsNullOrEmpty(login)) return Refuse(result, "bad-u");
            if (pw != null && !FormUrlText.TryDecode(pw, out _, out _)) return Refuse(result, "bad-pw");
            if (!FormUrlText.TryDecode(fp, out string? pin, out _) || !TrafficLightAddress.IsValidFingerprint(pin))
                return Refuse(result, "bad-fp");

            if (version == 2)
            {
                if ((x == null) != (sid == null)) return Refuse(result, "x-and-sid");
                if (x != null)
                {
                    if (!FormUrlText.TryDecode(x, out string? endpoint, out _) || endpoint == null
                        || !ContractEndpoint.TryParse(endpoint, out string exchangeHost, out int exchangePort))
                        return Refuse(result, "bad-x");
                    if (!IsShareId(sid!)) return Refuse(result, "bad-sid");
                    result.ExchangeHost = exchangeHost;
                    result.ExchangePort = exchangePort;
                }
                if (drv != null && drv != "1") return Refuse(result, "bad-drv");
            }

            result.Ok = true;
            result.SecretsDropped = true; // a login is always there: u is required
            return result;
        }

        private static SftpDescriptorParse Refuse(SftpDescriptorParse result, string reason)
        {
            result.Ok = false;
            result.Reason = reason;
            result.Hosts.Clear();
            result.ExchangeHost = "";
            result.ExchangePort = 0;
            return result;
        }

        /// <summary>
        /// The share id of section 6.2: exactly 22 characters of base64url, no padding, decoding to 16 bytes
        /// with the four spare bits zero.
        /// </summary>
        private static bool IsShareId(string text)
        {
            if (text.Length != 22) return false;
            foreach (char c in text)
            {
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!ok) return false;
            }
            char last = text[21];
            int value = last >= 'A' && last <= 'Z' ? last - 'A'
                : last >= 'a' && last <= 'z' ? last - 'a' + 26
                : last >= '0' && last <= '9' ? last - '0' + 52
                : last == '-' ? 62 : 63;
            return value % 16 == 0;
        }
    }
}

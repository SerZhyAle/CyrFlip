using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;

namespace CyrFlip
{
    /// <summary>What a typed line was worked out to be (the words the UI line under the field uses).</summary>
    internal enum RecognizedAs
    {
        Drive,
        LocalFolder,
        NetworkFolder,
        /// <summary><c>\\host</c> with no share: a Windows machine, probed as ping then tcp 445 when added.</summary>
        Machine,
        IpAddress,
        HostName,
        Website,
        HostPort,
        Sftp,
        Exchange,
    }

    /// <summary>One target the recognizer worked out - not yet a stored target.</summary>
    internal sealed class TargetCandidate
    {
        public string Kind = "";
        public string Address = "";
        public string Fingerprint = "";
        public string Label = "";
        public RecognizedAs As;
        /// <summary>True for <c>\\host</c> alone: adding it tries <c>ping</c>, then <c>tcp</c> 445, and no other port (D24).</summary>
        public bool IsMachineName;
    }

    internal enum LineStatus
    {
        Recognized,
        /// <summary>A blank line.</summary>
        Empty,
        /// <summary>Not one of the shapes - the details panel opens with the kind chooser empty.</summary>
        NotRecognized,
        /// <summary>A contract string that breaks its own grammar - refused whole, with the contract's reason.</summary>
        Refused,
        /// <summary>A <c>.fmscfg</c> document: recognized and refused unread, it carries a login (section 5).</summary>
        FmsCfgFile,
    }

    /// <summary>The verdict on one pasted line.</summary>
    internal sealed class RecognizedLine
    {
        /// <summary>
        /// The line as typed - <b>only</b> for a <see cref="LineStatus.NotRecognized"/> line, so the details panel can
        /// start from it. Every other verdict leaves it empty: a recognized or refused line has said what it is, and a
        /// link or a contract string may carry a login (D21) that must not outlive the parse. A line of the contract
        /// strings' family (<c>FMSEXCH*</c>, <c>FMSSFTP*</c>, <c>FMSCFG*</c>) that this reader does not know is left empty too.
        /// </summary>
        public string Text = "";
        public LineStatus Status;
        /// <summary>The contract's reason word when <see cref="LineStatus.Refused"/>.</summary>
        public string Reason = "";
        public List<TargetCandidate> Candidates = new List<TargetCandidate>();
        /// <summary>A login, password or PIN was in the text and was not kept (D21).</summary>
        public bool SecretDropped;
        /// <summary>Set when commas were read as dots: the address as it was read (<c>192.168.1.100</c>).</summary>
        public string CommaReadAs = "";

        public bool IsRecognized => Status == LineStatus.Recognized && Candidates.Count > 0;
    }

    /// <summary>The verdict on a whole pasted text, one <see cref="RecognizedLine"/> per non-empty line.</summary>
    internal sealed class RecognitionResult
    {
        public List<RecognizedLine> Lines = new List<RecognizedLine>();
        /// <summary>More than <see cref="TargetRecognizer.MaxLines"/> lines were pasted; the rest were not read.</summary>
        public bool Truncated;
        public int TotalLines;
    }

    /// <summary>
    /// "What is this text?" - the one-field smart add of ticket S0069 D24. <b>Pure and offline</b>: no DNS
    /// lookup, no connection, no disk. The first rule that matches wins, in the order of the specification's
    /// table; slashes of either direction are accepted in a UNC path. Everything a contract format could carry
    /// as a secret is dropped at parse time and the raw text is never stored or logged.
    /// </summary>
    internal static class TargetRecognizer
    {
        /// <summary>The most lines one paste adds at once.</summary>
        public const int MaxLines = 50;

        public static RecognitionResult Recognize(string? text)
        {
            var result = new RecognitionResult();
            string whole = StripBom(text ?? "").Trim();
            if (whole.Length == 0) return result;

            // A .fmscfg document is JSON and spans lines: judge it before the text is split into candidates.
            if (whole[0] == '{' && whole[whole.Length - 1] == '}')
            {
                result.Lines.Add(new RecognizedLine { Text = "", Status = LineStatus.FmsCfgFile, SecretDropped = true });
                result.TotalLines = 1;
                return result;
            }

            string[] lines = whole.Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                result.TotalLines++;
                if (result.Lines.Count >= MaxLines) { result.Truncated = true; continue; }
                result.Lines.Add(RecognizeLine(line));
            }
            return result;
        }

        /// <summary>One line, in the order of the specification's table.</summary>
        public static RecognizedLine RecognizeLine(string? raw)
        {
            string text = Unquote(StripBom(raw ?? "").Trim());
            var line = new RecognizedLine();
            if (text.Length == 0) { line.Status = LineStatus.Empty; return line; }

            // The contract strings first: they are unmistakable and each is refused whole when broken.
            if (text.StartsWith("FMSEXCH", StringComparison.Ordinal))
            {
                ExchangeConnectionParse parsed = ExchangeConnectionString.Parse(text);
                if (parsed.Ok)
                {
                    line.Status = LineStatus.Recognized;
                    line.SecretDropped = parsed.LoginDropped;
                    line.Candidates.Add(new TargetCandidate
                    {
                        Kind = TrafficLightKinds.Exchange,
                        Address = TrafficLightAddress.JoinHostPort(parsed.Host, parsed.Port),
                        Fingerprint = parsed.Fingerprint,
                        Label = Cap(parsed.Name.Length > 0 ? parsed.Name : parsed.Host),
                        As = RecognizedAs.Exchange,
                    });
                    return line;
                }
                if (parsed.Reason != "not-a-string") { line.Status = LineStatus.Refused; line.Reason = parsed.Reason; return line; }
            }

            if (text.StartsWith("FMSSFTP", StringComparison.Ordinal))
            {
                SftpDescriptorParse parsed = SftpDescriptor.Parse(text);
                if (parsed.Ok)
                {
                    line.Status = LineStatus.Recognized;
                    line.SecretDropped = parsed.SecretsDropped;
                    foreach (string host in parsed.Hosts)
                        line.Candidates.Add(new TargetCandidate
                        {
                            Kind = TrafficLightKinds.Sftp,
                            Address = TrafficLightAddress.JoinHostPort(host, parsed.Port),
                            Label = Cap(host),
                            As = RecognizedAs.Sftp,
                        });
                    if (parsed.ExchangePort > 0)
                        line.Candidates.Add(new TargetCandidate
                        {
                            Kind = TrafficLightKinds.Exchange,
                            Address = TrafficLightAddress.JoinHostPort(parsed.ExchangeHost, parsed.ExchangePort),
                            Label = Cap(parsed.ExchangeHost),
                            As = RecognizedAs.Exchange,
                        });
                    return line;
                }
                if (parsed.Reason != "not-a-descriptor") { line.Status = LineStatus.Refused; line.Reason = parsed.Reason; return line; }
            }

            if (TrySftpUrl(text, line)) return line;
            if (TryHttpUrl(text, line)) return line;
            if (TryFileUrl(text, line)) return line;
            if (TryDrive(text, line)) return line;
            if (TryLocalFolder(text, line)) return line;
            if (TryUnc(text, line)) return line;
            if (TryIpv4(text, line)) return line;
            if (TryIpv6(text, line)) return line;
            if (TryHostPort(text, line)) return line;
            if (TryWebsite(text, line)) return line;
            if (TryHostName(text, line)) return line;

            line.Status = LineStatus.NotRecognized;
            if (!InContractFamily(text)) line.Text = text;
            return line;
        }

        /// <summary>
        /// A text that begins like one of the product family's connection strings or files, whatever its version:
        /// those carry a login, so a version this reader does not know is not echoed into any field of ours.
        /// </summary>
        private static bool InContractFamily(string text)
            => text.StartsWith("FMSEXCH", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("FMSSFTP", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("FMSCFG", StringComparison.OrdinalIgnoreCase);

        // ---- the rules ----

        private static bool TrySftpUrl(string text, RecognizedLine line)
        {
            if (!text.StartsWith("sftp://", StringComparison.OrdinalIgnoreCase)) return false;
            if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || uri == null) return false;
            string host = uri.Host;
            if (host.Length > 0 && host[0] == '[') host = host.Trim('[', ']');
            int port = uri.Port > 0 ? uri.Port : 22;
            if (!TrafficLightAddress.IsValidHost(host) || port < 1 || port > 65535) return false;
            line.Status = LineStatus.Recognized;
            line.SecretDropped = uri.UserInfo.Length > 0;
            line.Candidates.Add(new TargetCandidate
            {
                Kind = TrafficLightKinds.Sftp,
                Address = TrafficLightAddress.JoinHostPort(host, port),
                Label = Cap(host),
                As = RecognizedAs.Sftp,
            });
            return true;
        }

        private static bool TryHttpUrl(string text, RecognizedLine line)
        {
            if (!text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
            if (text.IndexOfAny(new[] { ' ', '\t' }) >= 0) return false;
            if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || uri == null) return false;

            string address = text;
            if (uri.UserInfo.Length > 0)
            {
                // A link with a login in it: the login is dropped, the address without it is what is watched.
                var builder = new UriBuilder(uri) { UserName = "", Password = "" };
                address = builder.Uri.ToString();
                line.SecretDropped = true;
            }
            if (!TrafficLightAddress.IsValidHttpUrl(address)) { line.SecretDropped = false; return false; }

            line.Status = LineStatus.Recognized;
            line.Candidates.Add(new TargetCandidate
            {
                Kind = TrafficLightKinds.Http,
                Address = address,
                Label = Cap(WithoutWww(uri.Host)),
                As = RecognizedAs.Website,
            });
            return true;
        }

        private static bool TryFileUrl(string text, RecognizedLine line)
        {
            if (!text.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return false;
            if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || uri == null || !uri.IsFile) return false;
            string path = uri.LocalPath;
            return TryLocalFolder(path, line) || TryUnc(path, line);
        }

        // D:\  D:  D:/  - a drive, kept as the letter and a colon (D18)
        private static bool TryDrive(string text, RecognizedLine line)
        {
            string normalized = TrafficLightAddress.NormalizeDrive(text);
            if (!TrafficLightAddress.IsDriveLetter(normalized)) return false;
            line.Status = LineStatus.Recognized;
            line.Candidates.Add(new TargetCandidate
            {
                Kind = TrafficLightKinds.Drive, Address = normalized, Label = normalized, As = RecognizedAs.Drive,
            });
            return true;
        }

        private static bool TryLocalFolder(string text, RecognizedLine line)
        {
            if (text.Length < 4 || !IsAsciiLetter(text[0]) || text[1] != ':' || (text[2] != '\\' && text[2] != '/')) return false;
            string path = TrimTrailingSeparators(text.Replace('/', '\\'));
            if (!TrafficLightAddress.IsValidFolderPath(path)) return false;
            line.Status = LineStatus.Recognized;
            line.Candidates.Add(new TargetCandidate
            {
                Kind = TrafficLightKinds.Folder, Address = path, Label = Cap(LastSegment(path)), As = RecognizedAs.LocalFolder,
            });
            return true;
        }

        // \\SZAMEDIA  //mycomp  -> a machine;  \\SZAMEDIA\COMMON  //nas/share/sub -> a network folder
        private static bool TryUnc(string text, RecognizedLine line)
        {
            string path = text.Replace('/', '\\');
            if (!path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
            // \\?\ and \\.\ are the device namespace: not a target at all.
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal)) return false;

            string[] parts = path.Substring(2).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !TrafficLightAddress.IsValidHost(parts[0])) return false;

            if (parts.Length == 1)
            {
                line.Status = LineStatus.Recognized;
                line.Candidates.Add(new TargetCandidate
                {
                    Kind = TrafficLightKinds.Ping, Address = parts[0], Label = Cap(parts[0]),
                    As = RecognizedAs.Machine, IsMachineName = true,
                });
                return true;
            }

            string unc = @"\\" + string.Join(@"\", parts);
            if (!TrafficLightAddress.IsValidFolderPath(unc)) return false;
            line.Status = LineStatus.Recognized;
            line.Candidates.Add(new TargetCandidate
            {
                Kind = TrafficLightKinds.Folder, Address = unc, Label = Cap(parts[parts.Length - 1]),
                As = RecognizedAs.NetworkFolder,
            });
            return true;
        }

        // 8.8.8.8  192.168.1.100  192,168,1,100 - four groups of 1-3 digits joined by dots or commas
        private static bool TryIpv4(string text, RecognizedLine line)
        {
            if (text.Length < 7 || text.Length > 15) return false;
            bool hasComma = false;
            foreach (char c in text)
            {
                if (c == ',') hasComma = true;
                else if (c != '.' && (c < '0' || c > '9')) return false;
            }
            string dotted = text.Replace(',', '.');
            if (!TrafficLightAddress.IsIpv4(dotted)) return false;

            line.Status = LineStatus.Recognized;
            if (hasComma) line.CommaReadAs = dotted;
            line.Candidates.Add(new TargetCandidate
            {
                Kind = TrafficLightKinds.Ping, Address = dotted, Label = dotted, As = RecognizedAs.IpAddress,
            });
            return true;
        }

        // fe80::1  [::1]
        private static bool TryIpv6(string text, RecognizedLine line)
        {
            string t = text;
            if (t.Length > 2 && t[0] == '[' && t[t.Length - 1] == ']') t = t.Substring(1, t.Length - 2);
            if (!TrafficLightAddress.IsIpv6Literal(t)) return false;
            line.Status = LineStatus.Recognized;
            line.Candidates.Add(new TargetCandidate
            {
                Kind = TrafficLightKinds.Ping, Address = t, Label = Cap(t), As = RecognizedAs.IpAddress,
            });
            return true;
        }

        // nas:445  192.168.1.5:3389  [::1]:22
        private static bool TryHostPort(string text, RecognizedLine line)
        {
            if (text.IndexOf('/') >= 0 || text.IndexOf('\\') >= 0) return false;
            if (!TrafficLightAddress.TryParseHostPort(text, out string host, out int port)) return false;
            if (!TrafficLightAddress.IsValidHost(host)) return false;
            string address = TrafficLightAddress.JoinHostPort(host, port);
            line.Status = LineStatus.Recognized;
            line.Candidates.Add(new TargetCandidate
            {
                Kind = TrafficLightKinds.Tcp, Address = address, Label = Cap(address), As = RecognizedAs.HostPort,
            });
            return true;
        }

        // google.com  www.google.com  google.com/path  -> a website, https:// in front
        private static bool TryWebsite(string text, RecognizedLine line)
        {
            if (text.IndexOfAny(new[] { ' ', '\t', '@', '\\' }) >= 0) return false;
            string host = text;
            int slash = host.IndexOf('/');
            if (slash >= 0) host = host.Substring(0, slash);
            int colon = host.IndexOf(':');
            if (colon >= 0) host = host.Substring(0, colon);
            if (!LaunchTargets.IsWebHost(host)) return false;
            string url = "https://" + text;
            if (!TrafficLightAddress.IsValidHttpUrl(url)) return false;
            line.Status = LineStatus.Recognized;
            line.Candidates.Add(new TargetCandidate
            {
                Kind = TrafficLightKinds.Http, Address = url, Label = Cap(WithoutWww(host)), As = RecognizedAs.Website,
            });
            return true;
        }

        // SZAMEDIA  router  nas.local - a name that is not a website: ping
        private static bool TryHostName(string text, RecognizedLine line)
        {
            if (text.IndexOfAny(new[] { ' ', '\t', '/', '\\', ':', '@' }) >= 0) return false;
            if (!TrafficLightAddress.IsValidHost(text)) return false;
            line.Status = LineStatus.Recognized;
            line.Candidates.Add(new TargetCandidate
            {
                Kind = TrafficLightKinds.Ping, Address = text, Label = Cap(text), As = RecognizedAs.HostName,
            });
            return true;
        }

        // ---- small helpers ----

        /// <summary>The label a kind and address suggest (used when the editor changes the kind).</summary>
        public static string SuggestLabel(string? kind, string? address)
        {
            string text = (address ?? "").Trim();
            switch (kind)
            {
                case TrafficLightKinds.Folder: return Cap(LastSegment(text));
                case TrafficLightKinds.Http: return Cap(WithoutWww(TrafficLightAddress.HostOf(kind, text)));
                case TrafficLightKinds.Sftp:
                case TrafficLightKinds.Exchange: return Cap(TrafficLightAddress.HostOf(kind, text));
                default: return Cap(text);
            }
        }

        private static string LastSegment(string path)
        {
            string trimmed = path.TrimEnd('\\', '/');
            int cut = trimmed.LastIndexOfAny(new[] { '\\', '/' });
            string segment = cut >= 0 ? trimmed.Substring(cut + 1) : trimmed;
            // "C:" is the segment of a root path: read it as the drive
            return segment.Length == 0 ? trimmed : segment;
        }

        private static string TrimTrailingSeparators(string path)
        {
            // Keep a root's own separator: "C:\" stays, "C:\Data\" becomes "C:\Data".
            string trimmed = path.TrimEnd('\\');
            return trimmed.Length <= 2 ? path : trimmed;
        }

        private static string WithoutWww(string host)
            => host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) && host.Length > 4 ? host.Substring(4) : host;

        private static string Cap(string label)
            => label.Length <= TrafficLightTargetList.MaxLabelLength ? label : label.Substring(0, TrafficLightTargetList.MaxLabelLength);

        private static string StripBom(string text) => text.Length > 0 && text[0] == '\uFEFF' ? text.Substring(1) : text;

        private static string Unquote(string text)
        {
            if (text.Length >= 2)
            {
                char first = text[0], last = text[text.Length - 1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\'') || (first == '“' && last == '”'))
                    return text.Substring(1, text.Length - 2).Trim();
            }
            return text;
        }

        private static bool IsAsciiLetter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
    }
}

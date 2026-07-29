using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>What the ladder in <see cref="MailSender.Send"/> actually managed to do.</summary>
    internal enum MailOutcome
    {
        /// <summary>MAPI opened the compose window with the archive attached and the user closed it by sending.</summary>
        Sent,
        /// <summary>MAPI opened the window and the user abandoned the message - a finished scenario, not a failure.</summary>
        Aborted,
        /// <summary>No MAPI: a mailto: message was opened and the archive revealed in Explorer for dragging in.</summary>
        MailtoOpened,
        /// <summary>Nothing could be opened - the caller shows the path and the address so the user can do it by hand.</summary>
        Manual,
    }

    /// <summary>The message CyrFlip prepares for the author. English on purpose - see <see cref="MailSender.Compose"/>.</summary>
    internal sealed class SupportMail
    {
        public string To = "";
        public string Subject = "";
        /// <summary>Full body, used by MAPI, where length costs nothing.</summary>
        public string Body = "";
        /// <summary>Shorter body for the mailto: fallback, where the whole URL has a practical length limit.</summary>
        public string ShortBody = "";
        public string AttachmentPath = "";
        public string AttachmentName = "";
    }

    /// <summary>
    /// The seam that separates "which step of the ladder do we take" from "does this machine have a
    /// mail client" - so the order of the steps and the reaction to every MAPI return code are unit
    /// tested without a live Outlook.
    /// </summary>
    internal interface IMailTransport
    {
        /// <summary>Simple MAPI with an attachment; returns a MAPI code (<c>MAPI_E_FAILURE</c> when unusable at all).</summary>
        uint SendWithAttachment(SupportMail mail);

        /// <summary>Open a URL through the shell. False = there is no handler registered for it.</summary>
        bool OpenUrl(string url);

        /// <summary>Show the file in Explorer, selected, so attaching it by hand is one drag.</summary>
        void Reveal(string path);
    }

    /// <summary>
    /// Hands the log archive to the user's own mail client. CyrFlip opens no socket here and sends
    /// nothing itself: the transport is the user's mail program, and the Send button is theirs.
    ///
    /// The shape of this class is dictated by one fact: <b>mailto: cannot carry an attachment.</b>
    /// RFC 2368 has no field for it and the non-standard <c>attach=</c> is deliberately ignored by
    /// every modern client. Attachments need Simple MAPI, which classic Outlook and Thunderbird
    /// register but "new Outlook" and webmail do not. Hence a ladder rather than one attempt, with
    /// the archive already written to disk before the first rung, so "no mail client" can never mean
    /// "no logs".
    /// </summary>
    internal static class MailSender
    {
        /// <summary>
        /// The published support address - the one already in the site footer in all 13 languages and
        /// in the privacy policy. Changing it means changing those too, or there would be a support
        /// channel only the program knows about.
        /// </summary>
        public const string AuthorAddress = "sza@ukr.net";

        /// <summary>
        /// Practical ceiling for a mailto: URL: the shell hands it to the protocol handler as a
        /// command line, and long ones get cut mid-escape. The body is trimmed to fit rather than
        /// risking a truncated URL.
        /// </summary>
        public const int MaxMailtoLength = 2000;

        /// <summary>
        /// Subject and body are <b>English regardless of the UI language</b>: this artefact is
        /// addressed to the author, and the subject stays readable in a mail list. The UI language
        /// code travels in the subject instead - it says which language to answer in.
        /// </summary>
        public static SupportMail Compose(string archivePath, string version, string uiLanguageCode, string system)
        {
            string name = Path.GetFileName(archivePath);
            var body = new StringBuilder();
            body.AppendLine("CyrFlip diagnostic logs are attached (" + name + ").");
            body.AppendLine();
            body.AppendLine("Please describe what went wrong here (any language is fine):");
            body.AppendLine();
            body.AppendLine();
            body.AppendLine("---");
            body.AppendLine("Sent from CyrFlip Settings > About > Send logs to the author.");
            body.AppendLine("The archive contains CyrFlip's own diagnostic logs and a configuration report.");
            body.AppendLine("It does not contain clipboard history.");

            var shortBody = new StringBuilder();
            shortBody.AppendLine("Please describe what went wrong here (any language is fine):");
            shortBody.AppendLine();
            shortBody.AppendLine();
            shortBody.AppendLine("Attach this archive before sending:");
            shortBody.AppendLine(archivePath);

            return new SupportMail
            {
                To = AuthorAddress,
                Subject = "CyrFlip " + version + " logs - " + system + " - " + uiLanguageCode,
                Body = body.ToString(),
                ShortBody = shortBody.ToString(),
                AttachmentPath = archivePath,
                AttachmentName = name,
            };
        }

        /// <summary>"Windows 10.0.26200 x64" - short enough for a subject line, precise enough to matter.</summary>
        public static string DescribeSystem()
        {
            try
            {
                return "Windows " + Environment.OSVersion.Version.Major + "." + Environment.OSVersion.Version.Minor
                    + "." + Environment.OSVersion.Version.Build + (Environment.Is64BitOperatingSystem ? " x64" : " x86");
            }
            catch { return "Windows"; }
        }

        /// <summary>
        /// The three rungs, in order. Note that <c>MAPI_USER_ABORT</c> stops here: the user just
        /// closed the compose window, and opening a second message at someone who abandoned the
        /// first one is not a fallback, it is a nuisance.
        /// </summary>
        public static MailOutcome Send(SupportMail mail, IMailTransport transport)
        {
            uint code = transport.SendWithAttachment(mail);
            if (code == MAPI_SUCCESS_SUCCESS) return MailOutcome.Sent;
            if (code == MAPI_USER_ABORT) return MailOutcome.Aborted;

            if (transport.OpenUrl(BuildMailto(mail)))
            {
                // The message carries the path; Explorer showing the file selected turns "attach it"
                // into one drag.
                transport.Reveal(mail.AttachmentPath);
                return MailOutcome.MailtoOpened;
            }
            return MailOutcome.Manual;
        }

        /// <summary>
        /// RFC 2368: recipient in the path, subject and body as percent-encoded query values. The
        /// body is trimmed - never the subject - when the whole URL would exceed
        /// <see cref="MaxMailtoLength"/>.
        /// </summary>
        public static string BuildMailto(SupportMail mail)
        {
            // The address itself is left literal: '@' is legal in a mailto: path, the address is our
            // own ASCII constant, and a handler that percent-decodes poorly is a real thing.
            string head = "mailto:" + mail.To
                + "?subject=" + Uri.EscapeDataString(mail.Subject) + "&body=";
            string body = mail.ShortBody;
            string url = head + Uri.EscapeDataString(body);
            while (url.Length > MaxMailtoLength && body.Length > 0)
            {
                // Percent-encoding expands, so cut generously and re-measure rather than compute.
                body = body.Substring(0, Math.Max(0, body.Length - Math.Max(16, (url.Length - MaxMailtoLength) / 2)));
                url = head + Uri.EscapeDataString(body);
            }
            return url;
        }

        /// <summary>The live transport: Simple MAPI, then the shell.</summary>
        internal sealed class WindowsMailTransport : IMailTransport
        {
            private readonly IntPtr _owner;

            /// <param name="owner">Window the MAPI compose dialog belongs to; <c>IntPtr.Zero</c> is fine.</param>
            public WindowsMailTransport(IntPtr owner) => _owner = owner;

            public uint SendWithAttachment(SupportMail mail)
            {
                string? path = AnsiSafePath(mail.AttachmentPath);
                if (path == null) return MAPI_E_FAILURE;   // non-ASCII path and no 8.3 form: next rung

                IntPtr recips = IntPtr.Zero, files = IntPtr.Zero;
                try
                {
                    var recipient = new MapiRecipDesc
                    {
                        ulRecipClass = MAPI_TO,
                        lpszName = mail.To,
                        lpszAddress = "SMTP:" + mail.To,
                    };
                    recips = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(MapiRecipDesc)));
                    Marshal.StructureToPtr(recipient, recips, false);

                    var attachment = new MapiFileDesc
                    {
                        nPosition = uint.MaxValue,          // append after the note text
                        lpszPathName = path,
                        lpszFileName = mail.AttachmentName,
                    };
                    files = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(MapiFileDesc)));
                    Marshal.StructureToPtr(attachment, files, false);

                    var message = new MapiMessage
                    {
                        lpszSubject = mail.Subject,
                        lpszNoteText = mail.Body,
                        nRecipCount = 1,
                        lpRecips = recips,
                        nFileCount = 1,
                        lpFiles = files,
                    };
                    return MAPISendMail(IntPtr.Zero, _owner, ref message, MAPI_DIALOG | MAPI_LOGON_UI, 0);
                }
                catch (Exception)
                {
                    // No mapi32.dll, no registered client, a bitness mismatch: all the same answer -
                    // this rung is unavailable, take the next one.
                    return MAPI_E_FAILURE;
                }
                finally
                {
                    if (recips != IntPtr.Zero)
                    {
                        Marshal.DestroyStructure(recips, typeof(MapiRecipDesc));
                        Marshal.FreeHGlobal(recips);
                    }
                    if (files != IntPtr.Zero)
                    {
                        Marshal.DestroyStructure(files, typeof(MapiFileDesc));
                        Marshal.FreeHGlobal(files);
                    }
                }
            }

            public bool OpenUrl(string url)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    return true;
                }
                catch (Win32Exception) { return false; }   // no handler for mailto:
                catch (Exception) { return false; }
            }

            public void Reveal(string path)
            {
                try
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"")
                    { UseShellExecute = true });
                }
                catch { /* the dialog still shows the path */ }
            }
        }

        /// <summary>
        /// Simple MAPI is ANSI, and the archive lives under a path that contains the Windows account
        /// name - which is Cyrillic or CJK often enough to matter. An ASCII path is passed through; a
        /// non-ASCII one is converted to its 8.3 short form, and if the volume has 8.3 names disabled
        /// this returns null so the caller drops to the mailto: rung instead of sending mojibake.
        /// </summary>
        public static string? AnsiSafePath(string path)
        {
            if (IsAscii(path)) return path;
            try
            {
                var buffer = new StringBuilder(600);
                uint length = GetShortPathName(path, buffer, (uint)buffer.Capacity);
                if (length == 0 || length > buffer.Capacity) return null;
                string shortPath = buffer.ToString();
                return IsAscii(shortPath) ? shortPath : null;
            }
            catch { return null; }
        }

        public static bool IsAscii(string value)
        {
            foreach (char c in value) if (c > 127) return false;
            return true;
        }

        /// <summary>Two-letter UI language code for the subject line ("ru", "en", ..).</summary>
        public static string LanguageCode(string uiLanguage)
        {
            try { return Localization.Codes[Localization.IndexOf(uiLanguage)]; }
            catch { return CultureInfo.InvariantCulture.TwoLetterISOLanguageName; }
        }
    }
}

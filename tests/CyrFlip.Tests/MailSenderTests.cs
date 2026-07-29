using System;
using System.Collections.Generic;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The ladder that exists because <b>mailto: cannot carry an attachment</b>. Every rung and every
    /// MAPI return code is checked through a fake transport - no test may reach a real mail client,
    /// and the one code that is easy to get wrong (a user who closed the compose window) has its own
    /// test, because treating it as a failure would open a second message at them.
    /// </summary>
    public class MailSenderTests
    {
        private sealed class FakeTransport : IMailTransport
        {
            public uint MapiCode = WindowInterop.MAPI_SUCCESS_SUCCESS;
            public bool UrlOpens = true;
            public readonly List<string> Calls = new List<string>();
            public string? OpenedUrl;

            public uint SendWithAttachment(SupportMail mail)
            {
                Calls.Add("mapi");
                return MapiCode;
            }

            public bool OpenUrl(string url)
            {
                Calls.Add("mailto");
                OpenedUrl = url;
                return UrlOpens;
            }

            public void Reveal(string path) => Calls.Add("reveal");
        }

        private static SupportMail Mail() =>
            MailSender.Compose(@"C:\Users\u\AppData\Local\CyrFlip\reports\CyrFlip-logs-26.7.29.2340-20260729-2340.zip",
                "26.7.29.2340", "ru", "Windows 10.0.26200 x64");

        [Fact]
        public void MapiSuccessStopsAtTheFirstRung()
        {
            var transport = new FakeTransport { MapiCode = WindowInterop.MAPI_SUCCESS_SUCCESS };

            Assert.Equal(MailOutcome.Sent, MailSender.Send(Mail(), transport));
            Assert.Equal(new[] { "mapi" }, transport.Calls.ToArray());
        }

        /// <summary>
        /// The user opened the message and decided not to send it. That is the scenario finishing, not
        /// the transport failing - a mailto: fallback here would be a nuisance, not a rescue.
        /// </summary>
        [Fact]
        public void AUserWhoAbandonsTheMessageIsNotOfferedASecondOne()
        {
            var transport = new FakeTransport { MapiCode = WindowInterop.MAPI_USER_ABORT };

            Assert.Equal(MailOutcome.Aborted, MailSender.Send(Mail(), transport));
            Assert.Equal(new[] { "mapi" }, transport.Calls.ToArray());
        }

        [Fact]
        public void NoMapiFallsBackToMailtoAndRevealsTheArchive()
        {
            var transport = new FakeTransport { MapiCode = WindowInterop.MAPI_E_FAILURE };

            Assert.Equal(MailOutcome.MailtoOpened, MailSender.Send(Mail(), transport));
            Assert.Equal(new[] { "mapi", "mailto", "reveal" }, transport.Calls.ToArray());
        }

        [Fact]
        public void NoMailtoHandlerLeavesItToTheUser()
        {
            var transport = new FakeTransport { MapiCode = WindowInterop.MAPI_E_FAILURE, UrlOpens = false };

            Assert.Equal(MailOutcome.Manual, MailSender.Send(Mail(), transport));
            Assert.Equal(new[] { "mapi", "mailto" }, transport.Calls.ToArray());   // nothing revealed
        }

        [Fact]
        public void TheMailtoUrlIsRfc2368Shaped()
        {
            string url = MailSender.BuildMailto(Mail());

            // The address stays literal - '@' is legal in a mailto: path and handlers vary in how
            // well they decode. Everything after it is percent-encoded.
            Assert.StartsWith("mailto:" + MailSender.AuthorAddress + "?subject=", url);
            Assert.Contains("&body=", url);
            Assert.DoesNotContain(" ", url);
            Assert.DoesNotContain("\n", url);
            Assert.Contains("%20", url);
        }

        [Fact]
        public void AnOverlongBodyIsTrimmedButTheSubjectSurvives()
        {
            SupportMail mail = Mail();
            mail.ShortBody = new string('x', 8000);

            string url = MailSender.BuildMailto(mail);

            Assert.True(url.Length <= MailSender.MaxMailtoLength,
                "mailto: URL is " + url.Length + " chars");
            Assert.Contains(Uri.EscapeDataString(mail.Subject), url);
        }

        [Fact]
        public void TheMessageIsEnglishAndNamesTheArchive()
        {
            SupportMail mail = Mail();

            Assert.Equal(MailSender.AuthorAddress, mail.To);
            Assert.Equal("CyrFlip 26.7.29.2340 logs - Windows 10.0.26200 x64 - ru", mail.Subject);
            Assert.Contains("CyrFlip-logs-26.7.29.2340-20260729-2340.zip", mail.Body);
            Assert.Contains("does not contain clipboard history", mail.Body);
            // The fallback body has to carry the path, since the link cannot carry the file.
            Assert.Contains(mail.AttachmentPath, mail.ShortBody);
        }

        [Fact]
        public void AnAsciiPathIsPassedToMapiUnchanged()
        {
            const string path = @"C:\Users\u\CyrFlip-logs.zip";
            Assert.Equal(path, MailSender.AnsiSafePath(path));
        }

        [Fact]
        public void ANonAsciiPathIsNeverHandedToTheAnsiApiAsIs()
        {
            // Simple MAPI is ANSI; a Cyrillic account name would arrive as mojibake. Either the 8.3
            // form comes back (ASCII) or the answer is null and the caller drops a rung.
            string? safe = MailSender.AnsiSafePath(@"C:\Users\Серёжа\CyrFlip-logs.zip");
            Assert.True(safe == null || MailSender.IsAscii(safe), "non-ASCII path leaked to MAPI: " + safe);
        }

        [Fact]
        public void TheSubjectCarriesTheUiLanguageCode()
        {
            Assert.Equal("ru", MailSender.LanguageCode("Русский"));
            Assert.Equal("uk", MailSender.LanguageCode("Українська"));
            Assert.Equal("zh", MailSender.LanguageCode("中文"));
            // An unknown language falls back to English, exactly like the rest of the layer.
            Assert.Equal("en", MailSender.LanguageCode("Klingon"));
        }
    }
}

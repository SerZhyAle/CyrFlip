using System.Collections.Generic;
using System.Text;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// DIAGNOSTIC-REPORT rule 3 as the log bundle applies it (ticket S0040): which text becomes a
    /// placeholder, and - as much the point - which does not.
    /// </summary>
    public class DiagnosticRedactorTests
    {
        private static readonly DiagnosticRedactor Machine = new DiagnosticRedactor(new[]
        {
            new KeyValuePair<string, string>(@"C:\Users\ann", DiagnosticRedactor.UserToken),
            new KeyValuePair<string, string>(@"C:\Users\ann\AppData\Local\CyrFlip\", DiagnosticRedactor.AppDataToken),
        });

        [Theory]
        [InlineData(@"C:\Users\ann\AppData\Local\CyrFlip\layout.txt", @"<APP_DATA>\layout.txt")]
        [InlineData(@"c:\users\ANN\Documents\a.txt", @"<USER>\Documents\a.txt")]
        [InlineData(@"C:/Users/ann/Documents", @"<USER>/Documents")]
        [InlineData(@"{""p"":""C:\\Users\\ann\\x""}", @"{""p"":""<USER>\\x""}")]
        [InlineData(@"'C:\Users\ann'", @"'<USER>'")]
        public void TheMachineRootsBecomePlaceholdersLongestFirst(string input, string expected)
            => Assert.Equal(expected, Machine.Redact(input));

        /// <summary>A root is a whole path segment: ann's profile is not the front of anna's.</summary>
        [Fact]
        public void ARootNeverMatchesTheFrontOfALongerName()
            => Assert.Equal(@"<USER>\x", Machine.Redact(@"C:\Users\anna\x"));

        [Theory]
        [InlineData(@"D:\Users\bob\tools\app.exe", @"<USER>\tools\app.exe")]
        [InlineData(@"C:\Users\SERZH~1\AppData", @"<USER>\AppData")]
        [InlineData(@"C:\Users\Имя Фамилия\Desktop", @"<USER>\Desktop")]
        public void AnyOtherProfileBecomesUser(string input, string expected)
            => Assert.Equal(expected, DiagnosticRedactor.Generic.Redact(input));

        [Theory]
        [InlineData(@"C:\Users\Public\Documents")]
        [InlineData(@"C:\Users\Default\NTUSER.DAT")]
        [InlineData(@"C:\Program Files\CyrFlip\CyrFlip.exe")]
        [InlineData(@"C:\Users")]
        [InlineData("no path at all")]
        public void WhatNamesNobodyStays(string input)
            => Assert.Equal(input, DiagnosticRedactor.Generic.Redact(input));

        /// <summary>The shared-profile exemption is the whole segment, not a prefix: "Publicist" is somebody.</summary>
        [Fact]
        public void AnAccountThatMerelyStartsLikeASharedProfileIsRedacted()
            => Assert.Equal(@"<USER>\x", DiagnosticRedactor.Generic.Redact(@"C:\Users\Publicist\x"));

        [Theory]
        [InlineData("http://me:pw@localhost:11434/api", "http://localhost:11434/api")]
        [InlineData("https://token@example.com", "https://example.com")]
        [InlineData("http://localhost:11434", "http://localhost:11434")]
        [InlineData("mailto:sza@ukr.net", "mailto:sza@ukr.net")]
        public void AUrlLosesItsUserinfoOnly(string input, string expected)
            => Assert.Equal(expected, DiagnosticRedactor.Generic.Redact(input));

        [Fact]
        public void UnchangedBytesAreReturnedAsTheSameArray()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("nothing personal\n");
            Assert.Same(bytes, DiagnosticRedactor.Generic.Redact(bytes));
        }

        // ---- DIAGNOSTIC-REPORT 0.12: section 8 C (by value) and section 7 (the shapes) ----

        /// <summary>
        /// The catalog's specification vector (<c>reference/redaction-structured-value.txt</c>), the
        /// EXPECTED-FIELD form of each line: a structured value under a harmless key is parsed and the secret
        /// field replaced. A filter by the name of the key would keep every one of these lines.
        /// </summary>
        [Theory]
        [InlineData(@"folderParams={""folders"":[{""label"":""Family"",""accessPin"":""4821"",""slideshowInterval"":15}]}",
                    @"folderParams={""folders"":[{""label"":""Family"",""accessPin"":""[REDACTED]"",""slideshowInterval"":15}]}")]
        [InlineData("lastOpened=https://media.example.test/play?id=17&token=Zk93mQ&lang=en",
                    "lastOpened=https://media.example.test/play?id=17&token=[REDACTED]&lang=en")]
        [InlineData("source=Host=nas.example.test;Port=22;User=guest;Password=Hq7-pL2x;Timeout=15",
                    "source=Host=nas.example.test;Port=22;User=guest;Password=[REDACTED];Timeout=15")]
        public void AStructuredValueIsRedactedByItsContentWhateverKeyItSitsUnder(string input, string expected)
            => Assert.Equal(expected, DiagnosticRedactor.Generic.Redact(input));

        [Theory]
        [InlineData("https://cdn.example.test/v.mp4?X-Amz-Signature=abc123&x=1", "https://cdn.example.test/v.mp4?X-Amz-Signature=[REDACTED]&x=1")]
        [InlineData("https://cdn.example.test/v.mp4?x=1&X-AMZ-CREDENTIAL=AKIA%2F2026", "https://cdn.example.test/v.mp4?x=1&X-AMZ-CREDENTIAL=[REDACTED]")]
        [InlineData("https://cdn.example.test/v?hdnts=exp=1~acl=x&id=2", "https://cdn.example.test/v?hdnts=[REDACTED]&id=2")]
        [InlineData("https://cdn.example.test/v?wmsAuthSign=zzz", "https://cdn.example.test/v?wmsAuthSign=[REDACTED]")]
        [InlineData("https://cdn.example.test/v?Policy=eyJ&Key-Pair-Id=K2&id=3", "https://cdn.example.test/v?Policy=[REDACTED]&Key-Pair-Id=[REDACTED]&id=3")]
        [InlineData("https://api.example.test/x?api_key=k1&client_secret=s2&refresh_token=r3", "https://api.example.test/x?api_key=[REDACTED]&client_secret=[REDACTED]&refresh_token=[REDACTED]")]
        [InlineData("https://h.example.test/x?id=1&amp;token=abc&amp;lang=en", "https://h.example.test/x?id=1&amp;token=[REDACTED]&amp;lang=en")]
        [InlineData("https://h.example.test/x?PASSWORD=p&Pass=q&Auth=r", "https://h.example.test/x?PASSWORD=[REDACTED]&Pass=[REDACTED]&Auth=[REDACTED]")]
        public void ASecretQueryParameterKeepsItsNameAndLosesItsValue(string input, string expected)
            => Assert.Equal(expected, DiagnosticRedactor.Generic.Redact(input));

        [Fact]
        public void TheTwoAccountSegmentsOfAnXtreamPathAreReplacedAndTheIdStays()
            => Assert.Equal("url=http://panel.example:8080/live/[REDACTED]/[REDACTED]/12345.ts",
                DiagnosticRedactor.Generic.Redact("url=http://panel.example:8080/live/alice/s3cret/12345.ts"));

        [Theory]
        [InlineData("http://alice:pa/ss@host.example/x", "http://host.example/x")]
        [InlineData("http://alice:p?ss@host.example", "http://host.example")]
        [InlineData("http://alice:p#ss@host.example/", "http://host.example/")]
        [InlineData("see http://alice:pa/ss@host.example/x now", "see http://host.example/x now")]
        public void AnAddressThatAPasswordMakesUnparsableIsCutAsTextAndNeverReturnedVerbatim(string input, string expected)
            => Assert.Equal(expected, DiagnosticRedactor.Generic.Redact(input));

        [Theory]
        [InlineData("Authorization: Bearer abc.def.ghi", "Authorization: [REDACTED]")]
        [InlineData("proxy-authorization: Basic dXNlcjpwdw==", "proxy-authorization: [REDACTED]")]
        [InlineData("Cookie: sid=1; theme=dark", "Cookie: [REDACTED]")]
        [InlineData("sent with Bearer eyJhbGciOiJIUzI1NiJ9.payload.sig ok", "sent with Bearer [REDACTED] ok")]
        public void AnAuthHeaderAndABearerTokenAreRedacted(string input, string expected)
            => Assert.Equal(expected, DiagnosticRedactor.Generic.Redact(input));

        [Fact]
        public void APrivateKeyBlockGoesWholeAndOneCutMidBlockGoesToTheEnd()
        {
            string whole = "before\n-----BEGIN RSA PRIVATE KEY-----\nMIIEow\nIBAAKCAQ\n-----END RSA PRIVATE KEY-----\nafter\n";
            Assert.Equal("before\n[REDACTED]\nafter\n", DiagnosticRedactor.Generic.Redact(whole));

            // A log cut to its tail can begin or end inside the block: nothing after BEGIN may survive.
            string cut = "before\n-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBg\nkqhkiG9w0B";
            Assert.Equal("before\n[REDACTED]", DiagnosticRedactor.Generic.Redact(cut));
        }

        /// <summary>The other half of the rule: what is not a credential stays, or the report stops being a diagnostic.</summary>
        [Theory]
        [InlineData("ExecutionPolicy=Bypass")]
        [InlineData("model=aya-expanse:8b")]
        [InlineData(@"{""Hotkey"":""Ctrl+Shift+F12"",""Enabled"":true}")]
        [InlineData("pinned=3 passed=true tokens=120")]
        [InlineData("keyboard layout 00000419 (Russian)")]
        [InlineData("http://localhost:11434/api/tags")]
        [InlineData("https://example.com/@user/page")]
        [InlineData("Authenticode: valid")]
        public void WhatIsNotACredentialStays(string input)
            => Assert.Equal(input, DiagnosticRedactor.Generic.Redact(input));

        [Fact]
        public void LineEndingsSurviveWhateverIsRedactedOnTheLine()
        {
            string text = "a=1\r\npassword=hunter2\r\nlast line without a break\nno cr token=abc\n";
            Assert.Equal("a=1\r\npassword=[REDACTED]\r\nlast line without a break\nno cr token=[REDACTED]\n",
                DiagnosticRedactor.Generic.Redact(text));
        }

        [Fact]
        public void RedactingTwiceChangesNothingTheSecondTime()
        {
            string text = "x?token=abc&id=1\nPassword=Hq7;Timeout=15\n" + @"{""accessPin"":""4821""}" + "\nhttp://u:p/q@h/x\n";
            string once = DiagnosticRedactor.Generic.Redact(text);
            Assert.Equal(once, DiagnosticRedactor.Generic.Redact(once));
        }

        /// <summary>
        /// A line that runs out of time is dropped and says so, and only that line (section 7). The seam
        /// is <see cref="DiagnosticRedactor.RedactLines"/>: a real catastrophic pattern would make this test slow.
        /// </summary>
        [Fact]
        public void ALineThatTimesOutIsReplacedByTheMarkerAndItsNeighboursAreUntouched()
        {
            string redacted = DiagnosticRedactor.RedactLines("keep one\r\nBOOM secret text\nkeep two\n", line =>
            {
                if (line.Contains("BOOM")) throw new System.Text.RegularExpressions.RegexMatchTimeoutException();
                return line;
            });

            Assert.Equal("keep one\r\n[Diag] PATH REDACTION TIMEOUT | dropped_line_bytes=16\nkeep two\n", redacted);
            Assert.DoesNotContain("secret", redacted);
        }
    }
}

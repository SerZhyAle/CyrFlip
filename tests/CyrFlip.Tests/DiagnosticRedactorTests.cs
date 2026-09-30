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
    }
}

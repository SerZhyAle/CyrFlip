using System;
using System.ComponentModel;
using System.IO;
using System.Net.Sockets;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// S0065 E-8: <see cref="FailureCause"/> - the one sanctioned wrapper for an exception the user
    /// hears about - had no test, and every IOException read "Disk read or write error". The split
    /// by HResult (sharing violation, disk full, path too long), the classic classifications and the
    /// translating-function seam of E-5 (which is what keeps a launcher balloon from mixing two
    /// languages) are pinned here.
    /// </summary>
    public class FailureCauseTests
    {
        /// <summary>An IOException whose HResult the test picks - the setter is protected.</summary>
        private sealed class HrException : IOException
        {
            public HrException(int hr)
                : base(@"Access to the path 'C:\Users\SECRET-ANN\AppData\Local\CyrFlip\x.log' is denied.")
            {
                HResult = hr;
            }
        }

        [Fact]
        public void SharingViolationDiskFullAndPathTooLongHaveTheirOwnSentences()
        {
            Assert.Equal("The file is in use by another program.",
                FailureCause.Describe(new HrException(unchecked((int)0x80070020)), "English"));
            Assert.Equal("The disk is full - free some space.",
                FailureCause.Describe(new HrException(unchecked((int)0x80070070)), "English"));
            Assert.Equal("The path to the file is too long.",
                FailureCause.Describe(new HrException(unchecked((int)0x800700CE)), "English"));
        }

        [Fact]
        public void EveryOtherIOExceptionKeepsTheDiskSentence()
        {
            Assert.Equal("Disk read or write error.",
                FailureCause.Describe(new HrException(unchecked((int)0x80070003)), "English"));
            Assert.Equal("Disk read or write error.",
                FailureCause.Describe(new FileNotFoundException(), "English"));
            Assert.Equal("Disk read or write error.",
                FailureCause.Describe(new DirectoryNotFoundException(), "English"));
        }

        [Fact]
        public void TheClassicClassificationsKeepTheirSentences()
        {
            Assert.Equal("Access to file or folder is denied.",
                FailureCause.Describe(new UnauthorizedAccessException(), "English"));
            Assert.Equal("Network error or server is unavailable.",
                FailureCause.Describe(new SocketException(), "English"));
            Assert.Equal("Could not start process.",
                FailureCause.Describe(new Win32Exception(), "English"));
            Assert.Equal("An unexpected error occurred.",
                FailureCause.Describe(new InvalidOperationException(), "English"));
            Assert.Equal("An unexpected error occurred.",
                FailureCause.Describe(null, "English"));
        }

        [Fact]
        public void AnAggregateExceptionIsUnwrappedToItsBase()
        {
            var inner = new Win32Exception();
            Assert.Equal("Could not start process.",
                FailureCause.Describe(new AggregateException(inner), "English"));
        }

        /// <summary>
        /// E-5: the launcher used to call <c>Describe(ex, "Русский")</c> with the language literal, so an
        /// English balloon read "Could not start «x»: Не удалось запустить процесс." - the seam a window's
        /// T is must carry the classification instead.
        /// </summary>
        [Fact]
        public void TheTranslatingFunctionSeamCarriesEveryClassification()
        {
            Func<string, string> english = ru => Localization.Translate("English", ru);
            Assert.Equal("Could not start process.",
                FailureCause.Describe(new Win32Exception(), english));
            Assert.Equal("The file is in use by another program.",
                FailureCause.Describe(new HrException(unchecked((int)0x80070020)), english));
            Assert.Equal("An unexpected error occurred.", FailureCause.Describe(null, english));
        }

        [Fact]
        public void ANullSeamIsTheRussianKeyItself()
        {
            Assert.Equal("Could not start process.",
                FailureCause.Describe(new Win32Exception(), (Func<string, string>?)null));
        }

        /// <summary>APP-BEHAVIOUR rule 6, held by the one wrapper every user-visible exception goes through.</summary>
        [Fact]
        public void TheRawMessageNeverLeavesTheWrapper()
        {
            string described = FailureCause.Describe(
                new HrException(unchecked((int)0x80070020)), "English");
            Assert.DoesNotContain("SECRET-ANN", described);
            Assert.DoesNotContain("AppData", described);
            Assert.DoesNotContain("x.log", described);
        }

        [Fact]
        public void EverySentenceExistsInAllThirteenLanguages()
        {
            string[] keys =
            {
                "The file is in use by another program.",
                "The disk is full - free some space.",
                "The path to the file is too long.",
            };
            foreach (string key in keys)
            {
                Assert.Equal(key, Localization.Translate("English", key));
                foreach (string language in Localization.Names)
                    Assert.False(string.IsNullOrWhiteSpace(Localization.Translate(language, key)),
                        key + " has no translation for " + language);
            }
        }
    }
}

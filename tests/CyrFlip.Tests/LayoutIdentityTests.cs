using System;
using System.Collections.Generic;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The HKL → KLID decode behind the marker's colour. The values below are not invented: the two
    /// substitute shapes were read off this machine (LoadKeyboardLayout("00020409") came back as
    /// <c>F0010409</c>, "00010419" as <c>F0080419</c>, matching the <c>Layout Id</c> values 0001 and
    /// 0008 in the machine's layout store).
    ///
    /// <para>Getting this wrong is invisible in a build and almost invisible on screen - the marker
    /// still shows the right two letters, just in the wrong layout's shade - which is exactly why the
    /// registry lookup is a parameter here rather than something the decode does for itself.</para>
    /// </summary>
    public class LayoutIdentityTests
    {
        private static readonly Dictionary<int, string> Map = new Dictionary<int, string>
        {
            { 0x0001, "00020409" }, // United States-International
            { 0x0002, "00010409" }, // United States-Dvorak
            { 0x0008, "00010419" }, // Russian (Typewriter)
            { 0x00A8, "00020422" }, // Ukrainian (Enhanced)
        };

        [Theory]
        [InlineData(0x04090409u, "00000409")] // US: the high word repeats the language id
        [InlineData(0x04190419u, "00000419")] // Russian
        [InlineData(0x10091009u, "00001009")] // Canadian French - a primary layout of its own langid
        [InlineData(0x0000040Cu, "0000040C")] // high word 0: still the language's primary layout
        public void APrimaryLayoutIsTheLanguagesOwnKlid(uint hkl, string expected)
            => Assert.Equal(expected, LayoutIdentity.Resolve(hkl, Map));

        [Theory]
        [InlineData(0xF0010409u, "00020409")]
        [InlineData(0xF0020409u, "00010409")]
        [InlineData(0xF0080419u, "00010419")]
        [InlineData(0xF0A80422u, "00020422")]
        public void ASubstituteLayoutIsFoundByItsLayoutId(uint hkl, string expected)
            => Assert.Equal(expected, LayoutIdentity.Resolve(hkl, Map));

        /// <summary>
        /// A layout id we cannot place - a keyboard added by a display-language pack we have not
        /// re-read yet, or a registry we could not open at all - falls back to the language's primary
        /// layout rather than to nothing: the marker is about to draw that language either way, and a
        /// missing KLID would drop it to the neutral "other" colour, which would be a lie.
        /// </summary>
        [Fact]
        public void AnUnknownLayoutIdFallsBackToTheLanguagesPrimaryLayout()
        {
            Assert.Equal("00000409", LayoutIdentity.Resolve(0xF0FF0409u, Map));
            Assert.Equal("00000409", LayoutIdentity.Resolve(0xF0010409u, new Dictionary<int, string>()));
        }

        /// <summary>An IME rides an 0xE0xx handle and has no KLID of its own.</summary>
        [Fact]
        public void AnImeReadsAsItsLanguagesPrimaryLayout()
            => Assert.Equal("00000411", LayoutIdentity.Resolve(0xE0200411u, Map));

        /// <summary>
        /// The map is keyed by layout id alone, and those ids are only unique per language - so a hit
        /// whose KLID belongs to another language is refused rather than colouring, say, a Russian
        /// keyboard as a Ukrainian one.
        /// </summary>
        [Fact]
        public void ALayoutIdBelongingToAnotherLanguageIsRefused()
            => Assert.Equal("00000419", LayoutIdentity.Resolve(0xF0010419u, Map));

        /// <summary>
        /// Ticket S0030 HT-7: the registry is re-read only for a layout id the map does not have - an
        /// id that is there but belongs to another language would come back from a re-read unchanged.
        /// </summary>
        [Theory]
        [InlineData(0xF0010419u, false)] // id 0001 known, belongs to English: refused on purpose, nothing to re-read
        [InlineData(0xF0770409u, true)]  // id 0077 unknown: the map may predate a newly installed layout
        [InlineData(0x04090409u, false)] // a primary layout needs no map at all
        [InlineData(0xE0200411u, false)] // nor does an IME handle
        public void TheRegistryIsReReadOnlyForAnUnknownLayoutId(uint hkl, bool expected)
            => Assert.Equal(expected, LayoutIdentity.NeedsReread(hkl, Map));

        [Fact]
        public void AnHklWithNoLanguageYieldsNothing()
            => Assert.Equal("", LayoutIdentity.Resolve(0x00000000u, Map));

        private static readonly HashSet<string> Known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "00000409", "00000419", "00000C0C", "00001009", "00020409",
        };

        /// <summary>
        /// S0036 UI-3: Canadian French, KLID 00001009, in both shapes it arrives in. Installed as CyrFlip
        /// installs it (language = the KLID's own word) it is 10091009; added from Windows Settings under
        /// French (Canada) it is 10090C0C - the high word is the layout's KLID word. Either way it is the
        /// same layout, its shade and the table's code "FR" - never "EN", which is what its en-CA low
        /// word decodes to.
        /// </summary>
        [Theory]
        [InlineData(0x10091009u)]
        [InlineData(0x10090C0Cu)]
        public void CanadianFrenchIsOneLayoutWithOneCodeInBothShapes(uint hkl)
        {
            string klid = LayoutIdentity.Resolve(hkl, Map, Known);
            Assert.Equal("00001009", klid);
            Assert.Equal("FR", LayoutIdentity.CodeFor(hkl, klid));
        }

        /// <summary>...while a keyboard of one language under another stays the other's, as an alternate does.</summary>
        [Fact]
        public void AnotherLanguagesPrimaryKeyboardIsNotTakenAsTheLayout()
        {
            string klid = LayoutIdentity.Resolve(0x04090419u, Map, Known); // US keyboard under Russian
            Assert.Equal("00000419", klid);
            Assert.Equal("RU", LayoutIdentity.CodeFor(0x04090419u, klid));
        }

        /// <summary>A layout the machine's store does not list is not guessed at.</summary>
        [Fact]
        public void AnUnknownPrimaryKeyboardFallsBackAndAsksForAReread()
        {
            var sparse = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "00000C0C" };
            Assert.Equal("00000C0C", LayoutIdentity.Resolve(0x10090C0Cu, Map, sparse));
            Assert.True(LayoutIdentity.NeedsReread(0x10090C0Cu, Map, sparse));
            Assert.False(LayoutIdentity.NeedsReread(0x10090C0Cu, Map, Known));
        }

        [Theory]
        [InlineData(0x04090409u, "00000409", "EN")]
        [InlineData(0x04150415u, "00000415", "PL")] // not curated: the language decoded from the HKL
        public void TheCodeIsTheCuratedOneElseTheHklsLanguage(uint hkl, string klid, string expected)
            => Assert.Equal(expected, LayoutIdentity.CodeFor(hkl, klid));
    }
}

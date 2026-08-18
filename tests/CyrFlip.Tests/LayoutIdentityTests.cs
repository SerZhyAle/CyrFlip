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

        [Fact]
        public void AnHklWithNoLanguageYieldsNothing()
            => Assert.Equal("", LayoutIdentity.Resolve(0x00000000u, Map));
    }
}

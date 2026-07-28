using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The mouse chord behind the text context menu: token round trip, the fallbacks, and the
    /// exact-modifier rule the hook matches on.
    /// </summary>
    public class MouseChordTests
    {
        [Theory]
        [InlineData("Ctrl+RightClick")]
        [InlineData("Shift+RightClick")]
        [InlineData("Alt+RightClick")]
        [InlineData("MiddleClick")]
        [InlineData("Ctrl+MiddleClick")]
        [InlineData("Shift+MiddleClick")]
        public void EveryOfferedChoiceRoundTrips(string token)
        {
            Assert.True(MouseChord.TryParse(token, out MouseChord chord));
            Assert.Equal(token, chord.Token);
        }

        [Fact]
        public void OfferedChoicesAreExactlyTheParsableSet()
        {
            foreach (string token in MouseChord.Choices)
                Assert.True(MouseChord.TryParse(token, out _), token + " is offered but does not parse");
        }

        [Fact]
        public void DefaultIsCtrlRightClick()
        {
            MouseChord chord = MouseChord.Default;
            Assert.True(chord.Ctrl);
            Assert.False(chord.Shift);
            Assert.False(chord.Alt);
            Assert.Equal(MouseChordButton.Right, chord.Button);
            Assert.Equal("Ctrl+RightClick", chord.Token);
        }

        [Theory]
        [InlineData("ctrl+rightclick")]
        [InlineData("CTRL + RMB")]
        [InlineData("Ctrl+Right")]
        public void ParsingIsCaseAndSpellingTolerant(string token)
        {
            Assert.True(MouseChord.TryParse(token, out MouseChord chord));
            Assert.Equal("Ctrl+RightClick", chord.Token);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Ctrl+Shift")]     // no button at all
        [InlineData("Ctrl+Shift+F12")] // a keyboard chord, not a mouse one
        public void TokenWithoutAButtonIsRejected(string? token)
        {
            Assert.False(MouseChord.TryParse(token, out _));
            Assert.Equal(MouseChord.Default.Token, MouseChord.Parse(token).Token);
        }

        /// <summary>
        /// A bare right click would swallow every context menu in Windows, so a hand-edited registry
        /// value must not be able to produce one. The bare middle button is a legitimate choice.
        /// </summary>
        [Fact]
        public void BareRightClickIsRejectedButBareMiddleClickIsNot()
        {
            Assert.False(MouseChord.TryParse("RightClick", out _));
            Assert.Equal(MouseChord.Default.Token, MouseChord.Parse("RightClick").Token);

            Assert.True(MouseChord.TryParse("MiddleClick", out MouseChord middle));
            Assert.Equal(MouseChordButton.Middle, middle.Button);
            Assert.False(middle.Ctrl || middle.Shift || middle.Alt);
        }

        [Fact]
        public void MatchesRequiresExactlyTheConfiguredModifiers()
        {
            MouseChord ctrlRight = MouseChord.Parse("Ctrl+RightClick");

            Assert.True(ctrlRight.Matches(ctrl: true, shift: false, alt: false, win: false));
            Assert.False(ctrlRight.Matches(ctrl: false, shift: false, alt: false, win: false)); // too few
            Assert.False(ctrlRight.Matches(ctrl: true, shift: true, alt: false, win: false));   // too many
            Assert.False(ctrlRight.Matches(ctrl: true, shift: false, alt: true, win: false));
        }

        /// <summary>Win is never part of a mouse chord, so holding it means "not this chord".</summary>
        [Fact]
        public void WinKeyNeverMatches()
        {
            Assert.False(MouseChord.Parse("Ctrl+RightClick").Matches(true, false, false, win: true));
            Assert.False(MouseChord.Parse("MiddleClick").Matches(false, false, false, win: true));
        }

        [Fact]
        public void BareMiddleClickMatchesOnlyWithNoModifiers()
        {
            MouseChord middle = MouseChord.Parse("MiddleClick");
            Assert.True(middle.Matches(false, false, false, false));
            Assert.False(middle.Matches(true, false, false, false));
        }

        /// <summary>Modifiers stay Latin; only the button name goes through the translator.</summary>
        [Fact]
        public void DisplayTranslatesOnlyTheButtonName()
        {
            string shown = MouseChord.Parse("Ctrl+RightClick").Display(_ => "right mouse button");
            Assert.Equal("Ctrl + right mouse button", shown);

            Assert.Equal("Shift + middle mouse button",
                MouseChord.Parse("Shift+MiddleClick").Display(_ => "middle mouse button"));
        }

        [Fact]
        public void ButtonNameKeyIsTheRussianSourceString()
        {
            Assert.Equal(MouseChord.RightButtonName, MouseChord.Parse("Ctrl+RightClick").ButtonNameKey);
            Assert.Equal(MouseChord.MiddleButtonName, MouseChord.Parse("MiddleClick").ButtonNameKey);
        }
    }
}

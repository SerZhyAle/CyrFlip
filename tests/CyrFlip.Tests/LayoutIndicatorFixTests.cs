using System;
using System.Drawing;
using System.IO;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Ticket S0011 LI-1: the caret overlay asks other processes where their caret is only while
    /// somebody is typing or has just moved the focus.
    /// </summary>
    public class CaretQueryGateTests
    {
        private const int Never = -1_000_000;

        [Theory]
        [InlineData(1000, 1000, Never, true)]    // the key-down itself
        [InlineData(3999, 1000, Never, true)]    // 2999 ms after it
        [InlineData(4000, 1000, Never, false)]   // the window has closed
        [InlineData(2499, Never, 1000, true)]    // 1499 ms after a focus change
        [InlineData(2500, Never, 1000, false)]   // the focus window is shorter than the key window
        [InlineData(2500, 1000, 1000, true)]     // either window keeps it open
        [InlineData(100_000, Never, Never, false)] // reading a page, watching a video: nothing asked
        public void AsksOnlyWhileThereIsInputToFollow(int now, int lastKey, int lastFocus, bool expected)
            => Assert.Equal(expected, CaretQueryGate.ShouldQuery(now, lastKey, lastFocus));

        [Fact]
        public void SurvivesTheTickCounterWrapping()
        {
            int lastKey = int.MaxValue - 500;
            int now = unchecked(lastKey + 1000); // wrapped to a large negative number
            Assert.True(CaretQueryGate.ShouldQuery(now, lastKey, Never));
            Assert.False(CaretQueryGate.ShouldQuery(unchecked(lastKey + 5000), lastKey, Never));
        }

        [Fact]
        public void AStampInTheFutureOpensNothing()
            => Assert.False(CaretQueryGate.ShouldQuery(1000, 1500, 1500));

        [Fact]
        public void AKeyOpensTheGateAtOnce()
        {
            CaretQueryGate.NoteKey();
            Assert.True(CaretQueryGate.ShouldQueryNow());
        }
    }

    /// <summary>Ticket S0011 LI-7: the marker stays on the caret's monitor.</summary>
    public class CaretPlacementTests
    {
        private static readonly Rectangle Primary = new Rectangle(0, 0, 1920, 1080);
        private static readonly Rectangle Right = new Rectangle(1920, 0, 2560, 1440);
        private static readonly Size Badge = new Size(30, 20);

        [Fact]
        public void BelowRightOfTheCaretWhenThereIsRoom()
        {
            Point p = CaretPlacement.Place(new CaretRect(500, 300, 320), Badge, Primary);
            Assert.Equal(new Point(500 + CaretPlacement.GapX, 320 + CaretPlacement.GapY), p);
        }

        [Fact]
        public void AboveTheLineAtTheBottomEdge()
        {
            // The prompt on the last line of a maximized terminal.
            Point p = CaretPlacement.Place(new CaretRect(500, 1062, 1078), Badge, Primary);
            Assert.Equal(1062 - CaretPlacement.GapY - Badge.Height, p.Y);
            Assert.True(p.Y + Badge.Height <= 1062, "the marker covers the line being typed");
        }

        [Fact]
        public void LeftOfTheCaretAtTheRightEdge()
        {
            Point p = CaretPlacement.Place(new CaretRect(1910, 300, 320), Badge, Primary);
            Assert.Equal(1910 - CaretPlacement.GapX - Badge.Width, p.X);
        }

        [Fact]
        public void NeverAboveOrLeftOfTheMonitor()
        {
            Point p = CaretPlacement.Place(new CaretRect(-50, -40, -30), Badge, Primary);
            Assert.Equal(new Point(0, 0), p);
        }

        [Fact]
        public void BottomRightCornerUsesBothFallbacks()
        {
            Point p = CaretPlacement.Place(new CaretRect(1915, 1060, 1079), Badge, Primary);
            Assert.True(Primary.Contains(new Rectangle(p, Badge)));
            Assert.True(p.X + Badge.Width <= 1915 && p.Y + Badge.Height <= 1060);
        }

        [Fact]
        public void AtTheSeamOfTwoMonitorsTheMarkerStaysOnTheCaretsOne()
        {
            // The caret at the right edge of the left monitor: the marker would spill onto the right one.
            Point p = CaretPlacement.Place(new CaretRect(1915, 500, 520), Badge, Primary);
            Assert.True(p.X + Badge.Width <= Primary.Right);

            // The same caret on the right monitor's left edge, whose top is different.
            Point q = CaretPlacement.Place(new CaretRect(1921, 1420, 1438), Badge, Right);
            Assert.True(Right.Contains(new Rectangle(q, Badge)));
        }

        [Fact]
        public void TheMonitorProbeIsInsideTheCaret()
        {
            var caret = new CaretRect(1919, 100, 120);
            Point probe = CaretPlacement.MonitorProbe(caret);
            Assert.True(Primary.Contains(probe));
            Assert.InRange(probe.Y, caret.Top, caret.Bottom);
        }
    }

    /// <summary>Ticket S0011 LI-10: every caret path rejects a whole line and knows the text direction.</summary>
    public class CaretGeometryTests
    {
        [Theory]
        [InlineData(2, 18, false)]
        [InlineData(72, 18, false)]
        [InlineData(73, 18, true)]
        [InlineData(900, 20, true)]
        public void AWideBoxIsALineNotACaret(double width, double height, bool wholeLine)
            => Assert.Equal(wholeLine, CaretGeometry.IsWholeLine(width, height));

        [Fact]
        public void LeftToRightCaretIsTheLeftEdgeOfTheNextCharacter()
        {
            var next = new CharBox(110, 50, 10, 18);
            var previous = new CharBox(100, 50, 10, 18);
            Assert.Equal(110, CaretGeometry.XFromFollowing(next, previous));
            Assert.Equal(110, CaretGeometry.XFromFollowing(next, null));
        }

        [Fact]
        public void RightToLeftCaretIsTheRightEdgeOfTheNextCharacter()
        {
            // Arabic: the character before the caret (logically) is drawn to the right of the one after it.
            var next = new CharBox(100, 50, 10, 18);
            var previous = new CharBox(110, 50, 10, 18);
            Assert.Equal(110, CaretGeometry.XFromFollowing(next, previous));
        }

        [Fact]
        public void AtTheEndOfTheTextTheSidesSwapWithTheDirection()
        {
            var last = new CharBox(100, 50, 10, 18);
            Assert.Equal(110, CaretGeometry.XFromPreceding(last, new CharBox(90, 50, 10, 18)));  // LTR
            Assert.Equal(100, CaretGeometry.XFromPreceding(last, new CharBox(110, 50, 10, 18))); // RTL
            Assert.Equal(110, CaretGeometry.XFromPreceding(last, null));
        }

        [Fact]
        public void APreviousCharacterOnAnotherLineSaysNothingAboutDirection()
        {
            // The caret at the start of a wrapped line: the previous character ends the line above, to the right.
            var next = new CharBox(10, 70, 10, 18);
            var previous = new CharBox(600, 50, 10, 18);
            Assert.Equal(10, CaretGeometry.XFromFollowing(next, previous));
        }
    }

    /// <summary>Ticket S0011 LI-3: sizes follow the DPI and the pointer size.</summary>
    public class MarkerSizeTests
    {
        [Theory]
        [InlineData(24, 96, 24)]
        [InlineData(24, 144, 36)]
        [InlineData(24, 192, 48)]
        [InlineData(24, 240, 60)]
        [InlineData(18, 96, 18)]
        [InlineData(0, 96, 24)]   // unset: the default
        [InlineData(8, 96, 14)]   // never smaller than legible
        public void TheBadgeScalesWithTheMonitor(int baseSize, int dpi, int expected)
            => Assert.Equal(expected, MarkerSize.OverlayHeight(baseSize, dpi));

        [Theory]
        [InlineData(24, 96, 32, 24)]
        [InlineData(24, 192, 32, 48)]
        [InlineData(24, 96, 64, 48)]   // the pointer enlarged in the accessibility settings
        [InlineData(24, 192, 64, 96)]
        [InlineData(24, 96, 0, 24)]    // an unreadable pointer size is the standard one
        public void TheIBeamFollowsDpiAndThePointerSize(int baseSize, int dpi, int pointer, int expected)
            => Assert.Equal(expected, MarkerSize.CursorHeight(baseSize, dpi, pointer));

        [Theory]
        [InlineData(18, 0)]
        [InlineData(24, 1)]
        [InlineData(32, 2)]
        [InlineData(12, 0)]
        [InlineData(27, 1)]
        [InlineData(128, 2)]
        public void AnyStoredSizeFindsItsSliderStep(int stored, int step)
            => Assert.Equal(step, MarkerSize.NearestPreset(stored));

        [Fact]
        public void TheDefaultIsTheMiddleStep()
        {
            Assert.Equal(MarkerSize.DefaultBase, new AppConfig().CursorSize);
            Assert.Equal(MarkerSize.DefaultBase, MarkerSize.Presets[1]);
        }

        [Fact]
        public void TheOverlayBadgeDoublesAt200Percent()
        {
            using var overlay = new CaretOverlay(24);
            overlay.ApplyDpiForTest(96);
            Assert.Equal(24, overlay.BadgeHeight);
            overlay.ApplyDpiForTest(192);
            Assert.Equal(48, overlay.BadgeHeight);
        }
    }

    /// <summary>Ticket S0011 LI-5, LI-8, LI-9: the extension's side, read from its source (it has no runner).</summary>
    public class ExtensionIndicatorFixTests
    {
        private static readonly string ExtensionRoot = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "vscode-extension"));

        private static string Read(string relative) => File.ReadAllText(Path.Combine(ExtensionRoot, relative));

        [Fact]
        public void TheExtensionRunsOnTheUiSide()
            => Assert.Matches("\"extensionKind\"\\s*:\\s*\\[\\s*\"ui\"\\s*\\]", Read("package.json"));

        [Fact]
        public void OnlyTheUsersOwnEditsRenewTheClaim()
        {
            string source = Read(@"src\extension.ts");
            Assert.Contains("e.document === vscode.window.activeTextEditor?.document && e.contentChanges.length > 0", source);
            Assert.Contains("e.textEditor === vscode.window.activeTextEditor && e.kind !== undefined", source);
        }

        [Fact]
        public void AWindowDeletesOnlyItsOwnClaim()
        {
            string source = Read(@"src\extension.ts");
            Assert.Contains("${vscode.env.sessionId}", source);
            Assert.Contains("if (owner !== vscode.env.sessionId)", source);
        }
    }
}

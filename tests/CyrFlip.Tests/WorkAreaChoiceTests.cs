using System.Drawing;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Which monitor the settings window comes to when it is opened from a chord, the tray or the text
    /// menu (APP-SETTINGS rule 1, ticket S0060 P7-m): the one the user's own application is on, not
    /// blindly the one under the pointer.
    /// </summary>
    public class WorkAreaChoiceTests
    {
        private static readonly Rectangle Left = new Rectangle(0, 0, 1920, 1040);
        private static readonly Rectangle Right = new Rectangle(1920, 0, 2560, 1400);
        private static readonly Rectangle[] Both = { Left, Right };

        [Fact]
        public void TheApplicationsMonitorWinsOverThePointers()
        {
            // The user works on the right monitor; the tray icon and the pointer are on the left.
            Rectangle app = new Rectangle(2200, 100, 1600, 1000);
            Assert.Equal(Right, ScreenPlacement.ChooseWorkArea(app, new Point(900, 1060), Both));
        }

        [Fact]
        public void AWindowSpanningTwoMonitorsTakesTheOneItIsMostlyOn()
        {
            Rectangle app = new Rectangle(1500, 100, 1000, 800); // 420 px left of the seam, 580 right
            Assert.Equal(Right, ScreenPlacement.ChooseWorkArea(app, new Point(10, 10), Both));
            Rectangle mostlyLeft = new Rectangle(900, 100, 1100, 800); // 1020 left, 80 right
            Assert.Equal(Left, ScreenPlacement.ChooseWorkArea(mostlyLeft, new Point(3000, 10), Both));
        }

        [Fact]
        public void NoApplicationWindowFallsBackToThePointer()
        {
            Assert.Equal(Right, ScreenPlacement.ChooseWorkArea(null, new Point(2500, 300), Both));
            Assert.Equal(Left, ScreenPlacement.ChooseWorkArea(null, new Point(100, 300), Both));
        }

        [Fact]
        public void AMinimizedWindowReportedFarOffScreenIsNotAnAnchor()
        {
            Rectangle minimized = new Rectangle(-32000, -32000, 160, 28);
            Assert.Equal(Right, ScreenPlacement.ChooseWorkArea(minimized, new Point(2500, 300), Both));
        }

        [Fact]
        public void AnEmptyWindowRectIsNotAnAnchor()
        {
            Assert.Equal(Left, ScreenPlacement.ChooseWorkArea(new Rectangle(2500, 100, 0, 0), new Point(10, 10), Both));
        }

        [Fact]
        public void APointerOutsideEveryWorkAreaTakesTheNearestOne()
        {
            // On the taskbar: below the left monitor's work area, not inside any of them.
            Assert.Equal(Left, ScreenPlacement.ChooseWorkArea(null, new Point(500, 1060), Both));
            Assert.Equal(Right, ScreenPlacement.ChooseWorkArea(null, new Point(5000, 700), Both));
        }

        [Fact]
        public void NoWorkAreaAtAllIsAnEmptyAnswerNotAnException()
        {
            Assert.True(ScreenPlacement.ChooseWorkArea(new Rectangle(1, 1, 5, 5), new Point(0, 0), new Rectangle[0]).IsEmpty);
        }

        [Fact]
        public void CenteringPutsTheWindowInTheMiddleAndNeverOutsideTheArea()
        {
            Assert.Equal(new Rectangle(1920 + (2560 - 900) / 2, (1400 - 600) / 2, 900, 600),
                ScreenPlacement.CenterIn(new Size(900, 600), Right));

            // Larger than the area: shrunk to it and put at its corner.
            Assert.Equal(new Rectangle(0, 0, 1920, 1040), ScreenPlacement.CenterIn(new Size(3000, 2000), Left));
        }
    }
}

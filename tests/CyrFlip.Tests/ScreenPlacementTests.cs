using System.Drawing;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    public class ScreenPlacementTests
    {
        [Fact]
        public void BoundsInsideSingleScreenRemainUnchanged()
        {
            var screen = new Rectangle(0, 0, 1920, 1080);
            var bounds = new Rectangle(100, 100, 400, 300);

            var clamped = ScreenPlacement.Clamp(bounds, new[] { screen });

            Assert.Equal(bounds, clamped);
        }

        [Fact]
        public void BoundsPartiallyOutsideShiftedIntoWorkingArea()
        {
            var screen = new Rectangle(0, 0, 1920, 1080);
            var bounds = new Rectangle(1800, 900, 400, 300);

            var clamped = ScreenPlacement.Clamp(bounds, new[] { screen });

            Assert.Equal(new Rectangle(1520, 780, 400, 300), clamped);
            Assert.True(screen.Contains(clamped));
        }

        [Fact]
        public void BoundsPartiallyLeftAndTopShiftedIntoWorkingArea()
        {
            var screen = new Rectangle(0, 0, 1920, 1080);
            var bounds = new Rectangle(-50, -50, 400, 300);

            var clamped = ScreenPlacement.Clamp(bounds, new[] { screen });

            Assert.Equal(new Rectangle(0, 0, 400, 300), clamped);
        }

        [Fact]
        public void BoundsOnLostMonitorClampToNearestSurvivingScreen()
        {
            var primary = new Rectangle(0, 0, 1920, 1080);
            // Previously on a secondary monitor at (1920, 0, 1920, 1080) which disconnected
            var bounds = new Rectangle(2100, 200, 500, 400);

            var clamped = ScreenPlacement.Clamp(bounds, new[] { primary });

            Assert.Equal(new Rectangle(1420, 200, 500, 400), clamped);
            Assert.True(primary.Contains(clamped));
        }

        [Fact]
        public void BoundsLargerThanScreenClampedToScreenDimensions()
        {
            var screen = new Rectangle(0, 0, 1024, 768);
            var bounds = new Rectangle(50, 50, 1200, 900);

            var clamped = ScreenPlacement.Clamp(bounds, new[] { screen });

            Assert.Equal(new Rectangle(0, 0, 1024, 768), clamped);
        }

        [Fact]
        public void MultiScreenPicksScreenWithLargestOverlap()
        {
            var screen1 = new Rectangle(0, 0, 1920, 1080);
            var screen2 = new Rectangle(1920, 0, 1920, 1080);

            // 300 px width on screen2, 100 px width on screen1 -> target is screen2, clamped to X=1920
            var bounds = new Rectangle(1820, 200, 400, 300);

            var clamped = ScreenPlacement.Clamp(bounds, new[] { screen1, screen2 });

            Assert.Equal(new Rectangle(1920, 200, 400, 300), clamped);
            Assert.True(screen2.Contains(clamped));
        }

        [Fact]
        public void EmptyOrNullScreensReturnsOriginalBounds()
        {
            var bounds = new Rectangle(100, 100, 400, 300);

            Assert.Equal(bounds, ScreenPlacement.Clamp(bounds, null!));
            Assert.Equal(bounds, ScreenPlacement.Clamp(bounds, new Rectangle[0]));
        }
    }
}

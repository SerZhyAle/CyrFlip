using System;
using System.Linq;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Pure logic unit tests for companion surface topmost yield policy (S0094).
    /// </summary>
    public class TopmostYieldPolicyTests
    {
        [Theory]
        [InlineData(CompanionSurfaceKind.ClipboardHistory)]
        [InlineData(CompanionSurfaceKind.TranslationResult)]
        [InlineData(CompanionSurfaceKind.Symbols)]
        [InlineData(CompanionSurfaceKind.TrayHotkeys)]
        public void StandardCompanions_YieldTopmost_WhenDialogOrDropdownActive(CompanionSurfaceKind kind)
        {
            // Visible, no selection surface active -> stays topmost
            Assert.True(TopmostYieldPolicy.ShouldBeTopmost(kind, isVisible: true, isTrafficLightPinned: false, hasActiveSelectionSurface: false));

            // Visible, dialog or dropdown active -> yields topmost (false)
            Assert.False(TopmostYieldPolicy.ShouldBeTopmost(kind, isVisible: true, isTrafficLightPinned: false, hasActiveSelectionSurface: true));

            // Hidden satellite -> returns false (no topmost action on hidden window per Q2)
            Assert.False(TopmostYieldPolicy.ShouldBeTopmost(kind, isVisible: false, isTrafficLightPinned: false, hasActiveSelectionSurface: false));
            Assert.False(TopmostYieldPolicy.ShouldBeTopmost(kind, isVisible: false, isTrafficLightPinned: false, hasActiveSelectionSurface: true));
        }

        [Fact]
        public void TrafficLight_FollowsPinnedSetting_AndYieldsWhenSelectionActive()
        {
            CompanionSurfaceKind kind = CompanionSurfaceKind.TrafficLight;

            // Unpinned traffic light is never topmost
            Assert.False(TopmostYieldPolicy.ShouldBeTopmost(kind, isVisible: true, isTrafficLightPinned: false, hasActiveSelectionSurface: false));
            Assert.False(TopmostYieldPolicy.ShouldBeTopmost(kind, isVisible: true, isTrafficLightPinned: false, hasActiveSelectionSurface: true));

            // Pinned traffic light is topmost when no selection surface active
            Assert.True(TopmostYieldPolicy.ShouldBeTopmost(kind, isVisible: true, isTrafficLightPinned: true, hasActiveSelectionSurface: false));

            // Pinned traffic light yields topmost when selection surface active
            Assert.False(TopmostYieldPolicy.ShouldBeTopmost(kind, isVisible: true, isTrafficLightPinned: true, hasActiveSelectionSurface: true));
        }

        [Fact]
        public void SelectionSurfaceActive_CombinesDialogAndNativeDropdown()
        {
            Assert.False(TopmostYieldPolicy.IsSelectionSurfaceActive(isDialogActive: false, isNativeListDropped: false));
            Assert.True(TopmostYieldPolicy.IsSelectionSurfaceActive(isDialogActive: true, isNativeListDropped: false));
            Assert.True(TopmostYieldPolicy.IsSelectionSurfaceActive(isDialogActive: false, isNativeListDropped: true));
            Assert.True(TopmostYieldPolicy.IsSelectionSurfaceActive(isDialogActive: true, isNativeListDropped: true));
        }

        [Fact]
        public void Transitions_AreSymmetric()
        {
            foreach (CompanionSurfaceKind kind in Enum.GetValues(typeof(CompanionSurfaceKind)).Cast<CompanionSurfaceKind>())
            {
                foreach (bool isPinned in new[] { false, true })
                {
                    bool initial = TopmostYieldPolicy.ShouldBeTopmost(kind, isVisible: true, isTrafficLightPinned: isPinned, hasActiveSelectionSurface: false);

                    // Dialog opens
                    bool duringDialog = TopmostYieldPolicy.ShouldBeTopmost(kind, isVisible: true, isTrafficLightPinned: isPinned, hasActiveSelectionSurface: true);
                    Assert.False(duringDialog);

                    // Dialog closes
                    bool restored = TopmostYieldPolicy.ShouldBeTopmost(kind, isVisible: true, isTrafficLightPinned: isPinned, hasActiveSelectionSurface: false);
                    Assert.Equal(initial, restored);
                }
            }
        }

        [Fact]
        public void Registry_CoversAllDocumentedCompanions()
        {
            string[] expected = new[]
            {
                "ClipboardHistoryWindow",
                "TranslationResultWindow",
                "SymbolsWindow",
                "TrayHotkeysPopup",
                "TrafficLightWindow",
            };

            Assert.Equal(expected.OrderBy(x => x), CompanionSurfaceRegistry.CompanionTypeNames.OrderBy(x => x));
        }
    }
}

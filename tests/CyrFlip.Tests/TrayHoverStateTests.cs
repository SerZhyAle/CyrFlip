using System;
using System.Drawing;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    public class TrayHoverStateTests
    {
        [Fact]
        public void PassOverPointerDoesNotOpenPopup()
        {
            var sm = new TrayHoverStateMachine { HoverDwellTimeMs = 400, LeaveDistanceThresholdPx = 20 };
            bool shown = false;
            bool nativeHidden = false;
            bool nativeRestored = false;

            sm.ShowPopupRequested += _ => shown = true;
            sm.HideNativeTipRequested += () => nativeHidden = true;
            sm.RestoreNativeTipRequested += () => nativeRestored = true;

            // Step 1: mouse enters at (100, 100), time = 0
            sm.OnPointerMove(new Point(100, 100), 0);
            Assert.Equal(TrayHoverState.HoverDwellPending, sm.State);
            Assert.True(nativeHidden);
            Assert.False(shown);

            // Step 2: mouse leaves to (150, 150) before dwell expires, time = 100 ms
            sm.OnPointerMove(new Point(150, 150), 100);
            Assert.Equal(TrayHoverState.Idle, sm.State);
            Assert.False(shown, "Popup must not show for quick pass-over");
            Assert.True(nativeRestored, "Native tooltip text must be restored on leave");
        }

        [Fact]
        public void RestingForDwellTimeOpensPopup()
        {
            var sm = new TrayHoverStateMachine { HoverDwellTimeMs = 400, LeaveDistanceThresholdPx = 20 };
            Point? shownAt = null;

            sm.ShowPopupRequested += pt => shownAt = pt;

            sm.OnPointerMove(new Point(100, 100), 0);
            Assert.Null(shownAt);

            // Small jitter within threshold, dwell timer fires at 400 ms
            sm.OnPointerMove(new Point(102, 101), 200);
            sm.OnDwellTimerTick(400, new Point(102, 101));

            Assert.Equal(TrayHoverState.PopupVisible, sm.State);
            Assert.NotNull(shownAt);
            Assert.Equal(new Point(100, 100), shownAt.Value);
        }

        [Fact]
        public void MovingBeyondDistanceThresholdDismissesPopup()
        {
            var sm = new TrayHoverStateMachine { HoverDwellTimeMs = 400, LeaveDistanceThresholdPx = 20 };
            bool dismissed = false;
            bool nativeRestored = false;

            sm.DismissPopupRequested += () => dismissed = true;
            sm.RestoreNativeTipRequested += () => nativeRestored = true;

            sm.OnPointerMove(new Point(100, 100), 0);
            sm.OnDwellTimerTick(400, new Point(100, 100));
            Assert.Equal(TrayHoverState.PopupVisible, sm.State);

            // Move pointer far away
            sm.OnPointerMove(new Point(135, 100), 500);
            Assert.Equal(TrayHoverState.Idle, sm.State);
            Assert.True(dismissed);
            Assert.True(nativeRestored);
        }

        [Fact]
        public void UserActionDismissesPopupAndRestoresNativeTip()
        {
            var sm = new TrayHoverStateMachine { HoverDwellTimeMs = 400, LeaveDistanceThresholdPx = 20 };
            bool dismissed = false;
            bool nativeRestored = false;

            sm.DismissPopupRequested += () => dismissed = true;
            sm.RestoreNativeTipRequested += () => nativeRestored = true;

            sm.OnPointerMove(new Point(100, 100), 0);
            sm.OnDwellTimerTick(400, new Point(100, 100));
            Assert.Equal(TrayHoverState.PopupVisible, sm.State);

            sm.OnUserActionDismiss();
            Assert.Equal(TrayHoverState.Idle, sm.State);
            Assert.True(dismissed);
            Assert.True(nativeRestored);
        }

        [Fact]
        public void SecondHoverAfterCloseReopensPopup()
        {
            var sm = new TrayHoverStateMachine { HoverDwellTimeMs = 400, LeaveDistanceThresholdPx = 20 };
            int showCount = 0;
            int dismissCount = 0;

            sm.ShowPopupRequested += _ => showCount++;
            sm.DismissPopupRequested += () => dismissCount++;

            // Hover 1
            sm.OnPointerMove(new Point(100, 100), 0);
            sm.OnDwellTimerTick(400, new Point(100, 100));
            Assert.Equal(1, showCount);

            // Dismiss
            sm.OnUserActionDismiss();
            Assert.Equal(1, dismissCount);
            Assert.Equal(TrayHoverState.Idle, sm.State);

            // Hover 2
            sm.OnPointerMove(new Point(100, 100), 1000);
            sm.OnDwellTimerTick(1400, new Point(100, 100));
            Assert.Equal(2, showCount);
            Assert.Equal(TrayHoverState.PopupVisible, sm.State);
        }

        [Fact]
        public void KeyboardTriggerAutoDismissesAfter10Seconds()
        {
            var sm = new TrayHoverStateMachine { AutoDismissTimeoutMs = 10000 };
            bool shown = false;
            bool dismissed = false;

            sm.ShowPopupRequested += _ => shown = true;
            sm.DismissPopupRequested += () => dismissed = true;

            sm.TriggerFromKeyboard(new Point(100, 100), 1000);
            Assert.True(shown);
            Assert.True(sm.IsKeyboardInvoked);
            Assert.Equal(TrayHoverState.PopupVisible, sm.State);

            // Tick at 5 seconds -> still visible
            sm.OnDwellTimerTick(6000, new Point(100, 100));
            Assert.False(dismissed);
            Assert.Equal(TrayHoverState.PopupVisible, sm.State);

            // Tick at 11 seconds -> expired and dismissed
            sm.OnDwellTimerTick(11001, new Point(100, 100));
            Assert.True(dismissed);
            Assert.Equal(TrayHoverState.Idle, sm.State);
        }

        [Fact]
        public void RestingOnTheIconWhileMenuIsOpenShowsNoPopup()
        {
            // The shell keeps sending MouseMove over the icon while the tray menu is open (S0059 3.2).
            var sm = new TrayHoverStateMachine { HoverDwellTimeMs = 400, LeaveDistanceThresholdPx = 20 };
            bool shown = false;
            bool nativeHidden = false;

            sm.ShowPopupRequested += _ => shown = true;
            sm.HideNativeTipRequested += () => nativeHidden = true;

            sm.OnMenuOpened();
            sm.OnPointerMove(new Point(100, 100), 0);
            sm.OnDwellTimerTick(400, new Point(100, 100));

            Assert.False(shown, "Popup must not cover the open tray menu");
            Assert.False(nativeHidden, "Native tooltip must not be touched while the menu is open");
            Assert.Equal(TrayHoverState.Idle, sm.State);
        }

        [Fact]
        public void OpeningTheMenuDismissesAVisiblePopup()
        {
            var sm = new TrayHoverStateMachine { HoverDwellTimeMs = 400, LeaveDistanceThresholdPx = 20 };
            bool dismissed = false;
            bool nativeRestored = false;

            sm.DismissPopupRequested += () => dismissed = true;
            sm.RestoreNativeTipRequested += () => nativeRestored = true;

            sm.OnPointerMove(new Point(100, 100), 0);
            sm.OnDwellTimerTick(400, new Point(100, 100));
            Assert.Equal(TrayHoverState.PopupVisible, sm.State);

            sm.OnMenuOpened();

            Assert.True(sm.MenuOpen);
            Assert.Equal(TrayHoverState.Idle, sm.State);
            Assert.True(dismissed);
            Assert.True(nativeRestored);
        }

        [Fact]
        public void HoverResumesOnceTheMenuCloses()
        {
            var sm = new TrayHoverStateMachine { HoverDwellTimeMs = 400, LeaveDistanceThresholdPx = 20 };
            int showCount = 0;

            sm.ShowPopupRequested += _ => showCount++;

            sm.OnMenuOpened();
            sm.OnPointerMove(new Point(100, 100), 0);
            sm.OnDwellTimerTick(400, new Point(100, 100));
            Assert.Equal(0, showCount);

            sm.OnMenuClosed();
            Assert.False(sm.MenuOpen);

            sm.OnPointerMove(new Point(100, 100), 1000);
            sm.OnDwellTimerTick(1400, new Point(100, 100));
            Assert.Equal(1, showCount);
            Assert.Equal(TrayHoverState.PopupVisible, sm.State);
        }

        [Fact]
        public void KeyboardTriggerIsStillAllowedWhileMenuIsMarkedOpen()
        {
            // The "hotkeys overview" item runs while the menu is still marked open (Closed comes after
            // the click), so the keyboard route must not be swallowed by the menu guard.
            var sm = new TrayHoverStateMachine { AutoDismissTimeoutMs = 10000 };
            bool shown = false;

            sm.ShowPopupRequested += _ => shown = true;

            sm.OnMenuOpened();
            sm.TriggerFromKeyboard(new Point(100, 100), 1000);

            Assert.True(shown);
            Assert.True(sm.IsKeyboardInvoked);
            Assert.Equal(TrayHoverState.PopupVisible, sm.State);
        }
    }
}

using System;
using System.Drawing;

namespace CyrFlip
{
    /// <summary>State of the tray hover detection and popup lifecycle.</summary>
    internal enum TrayHoverState
    {
        Idle,
        HoverDwellPending,
        PopupVisible,
    }

    /// <summary>
    /// Pure state machine governing tray hover dwell, leave detection and popup dismissal (ticket S0059).
    /// </summary>
    internal sealed class TrayHoverStateMachine
    {
        public TrayHoverState State { get; private set; } = TrayHoverState.Idle;
        public Point HoverAnchor { get; private set; }
        public long HoverStartTimeMs { get; private set; }
        public long AutoDismissAtMs { get; private set; }
        public bool IsKeyboardInvoked { get; private set; }

        /// <summary>
        /// True while the tray's context menu is open (ticket S0059). The pointer is still over the icon
        /// then, so the shell keeps sending MouseMove; the hover must not re-arm underneath the menu.
        /// </summary>
        public bool MenuOpen { get; private set; }

        public int HoverDwellTimeMs { get; set; } = 400;
        public int LeaveDistanceThresholdPx { get; set; } = 28;
        public int AutoDismissTimeoutMs { get; set; } = 10000;

        public event Action<Point>? ShowPopupRequested;
        public event Action? DismissPopupRequested;
        public event Action? HideNativeTipRequested;
        public event Action? RestoreNativeTipRequested;

        public void OnPointerMove(Point cursorPosition, long nowMs)
        {
            if (MenuOpen) return;

            switch (State)
            {
                case TrayHoverState.Idle:
                    HoverAnchor = cursorPosition;
                    HoverStartTimeMs = nowMs;
                    IsKeyboardInvoked = false;
                    State = TrayHoverState.HoverDwellPending;
                    HideNativeTipRequested?.Invoke();
                    break;

                case TrayHoverState.HoverDwellPending:
                    if (IsOutsideRadius(HoverAnchor, cursorPosition, LeaveDistanceThresholdPx))
                    {
                        State = TrayHoverState.Idle;
                        RestoreNativeTipRequested?.Invoke();
                    }
                    else if (nowMs - HoverStartTimeMs >= HoverDwellTimeMs)
                    {
                        State = TrayHoverState.PopupVisible;
                        ShowPopupRequested?.Invoke(HoverAnchor);
                    }
                    break;

                case TrayHoverState.PopupVisible:
                    if (!IsKeyboardInvoked && IsOutsideRadius(HoverAnchor, cursorPosition, LeaveDistanceThresholdPx))
                    {
                        Dismiss();
                    }
                    break;
            }
        }

        public void OnDwellTimerTick(long nowMs, Point currentCursorPosition)
        {
            if (State == TrayHoverState.HoverDwellPending)
            {
                if (IsOutsideRadius(HoverAnchor, currentCursorPosition, LeaveDistanceThresholdPx))
                {
                    State = TrayHoverState.Idle;
                    RestoreNativeTipRequested?.Invoke();
                }
                else if (nowMs - HoverStartTimeMs >= HoverDwellTimeMs)
                {
                    State = TrayHoverState.PopupVisible;
                    ShowPopupRequested?.Invoke(HoverAnchor);
                }
            }
            else if (State == TrayHoverState.PopupVisible)
            {
                if (IsKeyboardInvoked)
                {
                    if (nowMs >= AutoDismissAtMs)
                        Dismiss();
                }
                else if (IsOutsideRadius(HoverAnchor, currentCursorPosition, LeaveDistanceThresholdPx))
                {
                    Dismiss();
                }
            }
        }

        public void TriggerFromKeyboard(Point anchorPosition, long nowMs)
        {
            HoverAnchor = anchorPosition;
            HoverStartTimeMs = nowMs;
            AutoDismissAtMs = nowMs + AutoDismissTimeoutMs;
            IsKeyboardInvoked = true;
            State = TrayHoverState.PopupVisible;
            HideNativeTipRequested?.Invoke();
            ShowPopupRequested?.Invoke(anchorPosition);
        }

        public void OnUserActionDismiss()
        {
            if (State != TrayHoverState.Idle)
                Dismiss();
        }

        /// <summary>
        /// The tray menu opened: any popup goes, and no hover starts until <see cref="OnMenuClosed"/>.
        /// A keyboard-invoked popup is still allowed (the menu item that opens it runs while the menu
        /// is still marked open).
        /// </summary>
        public void OnMenuOpened()
        {
            MenuOpen = true;
            OnUserActionDismiss();
        }

        public void OnMenuClosed()
        {
            MenuOpen = false;
        }

        public void Dismiss()
        {
            State = TrayHoverState.Idle;
            IsKeyboardInvoked = false;
            DismissPopupRequested?.Invoke();
            RestoreNativeTipRequested?.Invoke();
        }

        public static bool IsOutsideRadius(Point p1, Point p2, int radius)
        {
            int dx = p1.X - p2.X;
            int dy = p1.Y - p2.Y;
            return (dx * dx + dy * dy) > (radius * radius);
        }
    }
}

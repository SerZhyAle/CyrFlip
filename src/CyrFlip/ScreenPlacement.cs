using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace CyrFlip
{
    /// <summary>
    /// Pure geometry helpers for clamping window rectangles to surviving display working areas
    /// (APP-BEHAVIOUR rule 10). Unit-tested without a display device.
    /// </summary>
    internal static class ScreenPlacement
    {
        /// <summary>
        /// The work area a window opened from the tray, a chord or the text menu should come to
        /// (APP-SETTINGS rule 1; ticket S0060 P7-m): the monitor of the application the user was
        /// working in, because that is where their attention is - the pointer may be on the taskbar of
        /// another one, and a keyboard chord has no pointer position at all. The application's
        /// window picks the work area it overlaps most; a window that overlaps none (minimized windows
        /// are reported far off-screen), or no window, falls back to the work area under the pointer,
        /// else the nearest one. <see cref="Rectangle.Empty"/> when there is no work area at all.
        /// </summary>
        public static Rectangle ChooseWorkArea(Rectangle? appWindow, Point pointer, IReadOnlyList<Rectangle> workAreas)
        {
            if (workAreas == null || workAreas.Count == 0) return Rectangle.Empty;

            if (appWindow.HasValue && appWindow.Value.Width > 0 && appWindow.Value.Height > 0)
            {
                Rectangle best = Rectangle.Empty;
                long bestArea = 0;
                foreach (Rectangle area in workAreas)
                {
                    Rectangle overlap = Rectangle.Intersect(appWindow.Value, area);
                    long size = (long)overlap.Width * overlap.Height;
                    if (size > bestArea) { bestArea = size; best = area; }
                }
                if (bestArea > 0) return best;
            }

            foreach (Rectangle area in workAreas)
                if (area.Contains(pointer)) return area;

            Rectangle nearest = workAreas[0];
            long nearestDistance = long.MaxValue;
            foreach (Rectangle area in workAreas)
            {
                long dx = pointer.X < area.Left ? area.Left - pointer.X : pointer.X >= area.Right ? pointer.X - area.Right + 1 : 0;
                long dy = pointer.Y < area.Top ? area.Top - pointer.Y : pointer.Y >= area.Bottom ? pointer.Y - area.Bottom + 1 : 0;
                long distance = dx * dx + dy * dy;
                if (distance < nearestDistance) { nearestDistance = distance; nearest = area; }
            }
            return nearest;
        }

        /// <summary><paramref name="window"/> centred in <paramref name="workArea"/>, then clamped into it.</summary>
        public static Rectangle CenterIn(Size window, Rectangle workArea)
            => Clamp(new Rectangle(workArea.Left + Math.Max(0, (workArea.Width - window.Width) / 2),
                                   workArea.Top + Math.Max(0, (workArea.Height - window.Height) / 2),
                                   window.Width, window.Height),
                     new[] { workArea });

        public static Rectangle Clamp(Rectangle bounds, IEnumerable<Rectangle> screens)
        {
            if (screens == null) return bounds;
            Rectangle[] screenList = screens.ToArray();
            if (screenList.Length == 0) return bounds;

            // 1. Pick the screen with the largest intersection, or closest distance if off-screen.
            Rectangle target = screenList[0];
            int maxArea = 0;
            foreach (Rectangle screen in screenList)
            {
                Rectangle inter = Rectangle.Intersect(bounds, screen);
                int area = inter.Width * inter.Height;
                if (area > maxArea)
                {
                    maxArea = area;
                    target = screen;
                }
            }

            if (maxArea == 0)
            {
                // Off-screen: pick the screen closest to bounds center.
                Point center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
                double minDistance = double.MaxValue;
                foreach (Rectangle screen in screenList)
                {
                    Point sc = new Point(screen.X + screen.Width / 2, screen.Y + screen.Height / 2);
                    double dx = center.X - sc.X;
                    double dy = center.Y - sc.Y;
                    double dist = dx * dx + dy * dy;
                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        target = screen;
                    }
                }
            }

            // 2. Clamp into target working area.
            int width = Math.Min(bounds.Width, target.Width);
            int height = Math.Min(bounds.Height, target.Height);
            int x = (bounds.Width >= target.Width)
                ? target.Left
                : Math.Max(target.Left, Math.Min(bounds.X, target.Right - width));
            int y = (bounds.Height >= target.Height)
                ? target.Top
                : Math.Max(target.Top, Math.Min(bounds.Y, target.Bottom - height));

            return new Rectangle(x, y, width, height);
        }
    }
}

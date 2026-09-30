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

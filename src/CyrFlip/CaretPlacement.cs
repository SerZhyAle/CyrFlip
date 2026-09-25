using System;
using System.Drawing;

namespace CyrFlip
{
    /// <summary>
    /// Where the text caret is, in physical screen pixels: its x and the top and bottom of the line it
    /// sits on. Every caret source answers with this, and <see cref="CaretPlacement"/> alone decides
    /// where the marker goes from it - so "beside the caret, never covering the line, never off the
    /// monitor" is one pure function instead of four copies of <c>x + 2, y + 1</c>.
    /// </summary>
    internal readonly struct CaretRect : IEquatable<CaretRect>
    {
        public readonly int X;
        public readonly int Top;
        public readonly int Bottom;

        public CaretRect(int x, int top, int bottom)
        {
            X = x;
            Top = top;
            Bottom = Math.Max(top, bottom);
        }

        public int Height => Bottom - Top;

        public bool Equals(CaretRect other) => X == other.X && Top == other.Top && Bottom == other.Bottom;
        public override bool Equals(object? obj) => obj is CaretRect other && Equals(other);
        public override int GetHashCode() => (X * 397) ^ (Top * 31) ^ Bottom;
        public override string ToString() => $"x={X} top={Top} bottom={Bottom}";
    }

    /// <summary>
    /// The marker's position for a caret (ticket S0011 LI-7). The rule has always been "diagonally
    /// below-right of the caret, so it never covers the line being typed"; what it lacked was the edge
    /// of the monitor. A maximized terminal with the prompt on its last line, or a caret at the right
    /// edge, put the marker below the screen or onto the neighbouring monitor.
    /// </summary>
    internal static class CaretPlacement
    {
        /// <summary>Gap between the caret and the marker, horizontally.</summary>
        internal const int GapX = 2;

        /// <summary>Gap between the line and the marker, vertically.</summary>
        internal const int GapY = 1;

        /// <summary>
        /// Top-left of a <paramref name="marker"/>-sized badge for <paramref name="caret"/> on the
        /// monitor whose bounds are <paramref name="monitor"/>. Below-right by default; above the line
        /// when there is no room below, left of the caret when there is no room to the right, and
        /// clamped inside the monitor whatever happens.
        /// </summary>
        public static Point Place(CaretRect caret, Size marker, Rectangle monitor)
        {
            int x = caret.X + GapX;
            int y = caret.Bottom + GapY;

            if (x + marker.Width > monitor.Right)
                x = caret.X - GapX - marker.Width;
            if (y + marker.Height > monitor.Bottom)
                y = caret.Top - GapY - marker.Height;

            x = Clamp(x, monitor.Left, monitor.Right - marker.Width);
            y = Clamp(y, monitor.Top, monitor.Bottom - marker.Height);
            return new Point(x, y);
        }

        /// <summary>The point that decides which monitor the caret is on: inside the caret itself.</summary>
        public static Point MonitorProbe(CaretRect caret) => new Point(caret.X, caret.Top + caret.Height / 2);

        private static int Clamp(int value, int min, int max)
            => max < min ? min : value < min ? min : value > max ? max : value;
    }

    /// <summary>One character's box as a caret source reports it, in screen pixels.</summary>
    internal readonly struct CharBox
    {
        public readonly double Left;
        public readonly double Top;
        public readonly double Width;
        public readonly double Height;

        public CharBox(double left, double top, double width, double height)
        {
            Left = left; Top = top; Width = width; Height = height;
        }

        public double Right => Left + Width;
        public double Bottom => Top + Height;
        public double CenterX => Left + Width / 2;
    }

    /// <summary>
    /// The two facts about a caret source's rectangles that every path must agree on (ticket S0011
    /// LI-10): a rect wider than four times its height is a whole line or the text box, not a caret, and
    /// in right-to-left text the caret sits on the other side of the character it is measured from.
    /// Direction is read from the geometry itself - whether the character before the caret lies to the
    /// right of the one after it - because that works the same for UIA and IAccessible2 and needs no
    /// attribute that half the providers do not implement.
    /// </summary>
    internal static class CaretGeometry
    {
        /// <summary>True when the box is too wide to be a caret or a character (a line, the text area).</summary>
        public static bool IsWholeLine(double width, double height)
            => width > 4 * (height > 0 ? height : 16);

        /// <summary>
        /// The caret's x from the character that <b>follows</b> it (logical order), with the one that
        /// precedes it when known: left edge in left-to-right text, right edge in right-to-left text.
        /// </summary>
        public static double XFromFollowing(CharBox following, CharBox? preceding)
            => preceding is CharBox p && SameLine(p, following) && p.CenterX > following.CenterX
                ? following.Right
                : following.Left;

        /// <summary>
        /// The caret's x from the character that <b>precedes</b> it (the caret at the end of the text),
        /// with the one before that when known: right edge left-to-right, left edge right-to-left.
        /// </summary>
        public static double XFromPreceding(CharBox preceding, CharBox? beforeThat)
            => beforeThat is CharBox b && SameLine(b, preceding) && b.CenterX > preceding.CenterX
                ? preceding.Left
                : preceding.Right;

        /// <summary>Two boxes are on one line when they overlap vertically by at least half the shorter.</summary>
        internal static bool SameLine(CharBox a, CharBox b)
        {
            double overlap = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
            double shorter = Math.Min(a.Height, b.Height);
            return shorter > 0 && overlap >= shorter / 2;
        }

        /// <summary>The caret for a character box and the x the direction rule chose.</summary>
        public static CaretRect ToCaret(double x, CharBox line)
            => new CaretRect((int)Math.Round(x), (int)Math.Round(line.Top), (int)Math.Round(line.Bottom));
    }
}

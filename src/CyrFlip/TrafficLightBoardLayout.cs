using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>One thing the board draws: a group caption or a tile.</summary>
    internal sealed class BoardItem
    {
        public bool IsTile;
        /// <summary>Content coordinates: the board's own scroll offset is not in it.</summary>
        public Rectangle Bounds;
        /// <summary>Index into the tile list the layout was computed for; -1 for a caption.</summary>
        public int TileIndex = -1;
        public string Caption = "";
    }

    /// <summary>
    /// Where the board puts its tiles (ticket S0069 section 6): groups in the order of their first member,
    /// each tile of a group flowing left to right and wrapping after a chosen number of columns - the rows
    /// follow from the number of targets, so three columns and five targets are two rows at once - and a
    /// muted caption above a named group. Pure - the metrics come in as arguments (they are measured from
    /// the font and the DPI by the window, never pixel constants) - so the whole arrangement and the
    /// arrow-key walk are tested without a window.
    /// </summary>
    internal static class TrafficLightBoardLayout
    {
        /// <summary>The columns a width holds - the old width-driven wrap, kept for a caller that has no setting.</summary>
        public static int ColumnsForWidth(int width, Size tile, int gap)
            => Math.Max(1, (width - gap) / Math.Max(1, tile.Width + gap));

        /// <summary>
        /// The columns that are really used: the chosen number, but never more than the widest group has
        /// tiles - two targets on a three-column board are a two-tile row, not a row with a hole in it. An
        /// empty board keeps the chosen number (its message wants the width).
        /// </summary>
        public static int EffectiveColumns(IReadOnlyList<TrafficLightTile> tiles, int configured)
        {
            int columns = Math.Max(1, configured);
            if (tiles.Count == 0) return columns;
            var sizes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int widest = 1;
            foreach (TrafficLightTile tile in tiles)
            {
                string group = (tile.Target.Group ?? "").Trim();
                sizes.TryGetValue(group, out int n);
                sizes[group] = ++n;
                widest = Math.Max(widest, n);
            }
            return Math.Min(columns, widest);
        }

        /// <summary>The width of a grid of <paramref name="columns"/> tiles with a gap on every side.</summary>
        public static int GridWidth(int columns, int tileWidth, int gap)
            => gap + Math.Max(1, columns) * (tileWidth + gap);

        public static List<BoardItem> Compute(IReadOnlyList<TrafficLightTile> tiles, int width, Size tile, int gap,
            int captionHeight, bool rightToLeft, out int contentHeight)
            => Compute(tiles, width, ColumnsForWidth(width, tile, gap), tile, gap, captionHeight, rightToLeft, out contentHeight);

        public static List<BoardItem> Compute(IReadOnlyList<TrafficLightTile> tiles, int width, int columns, Size tile, int gap,
            int captionHeight, bool rightToLeft, out int contentHeight)
        {
            var items = new List<BoardItem>();
            columns = Math.Max(1, columns);
            int y = gap;

            // The group order of TrafficLightTargetList.Grouped, over the snapshot's own indexes.
            var order = new List<string>();
            var members = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < tiles.Count; i++)
            {
                string group = (tiles[i].Target.Group ?? "").Trim();
                if (!members.TryGetValue(group, out List<int>? list))
                {
                    list = new List<int>();
                    members[group] = list;
                    order.Add(group);
                }
                list.Add(i);
            }

            foreach (string group in order)
            {
                if (group.Length > 0)
                {
                    items.Add(new BoardItem
                    {
                        Caption = group,
                        Bounds = new Rectangle(gap, y, Math.Max(1, width - 2 * gap), captionHeight),
                    });
                    y += captionHeight + gap / 2;
                }

                List<int> indexes = members[group];
                for (int k = 0; k < indexes.Count; k++)
                {
                    int column = k % columns, row = k / columns;
                    int x = gap + column * (tile.Width + gap);
                    if (rightToLeft) x = width - x - tile.Width;
                    items.Add(new BoardItem
                    {
                        IsTile = true,
                        TileIndex = indexes[k],
                        Bounds = new Rectangle(x, y + row * (tile.Height + gap), tile.Width, tile.Height),
                    });
                }
                int rows = (indexes.Count + columns - 1) / columns;
                y += rows * (tile.Height + gap) + gap / 2;
            }

            contentHeight = y + gap / 2;
            return items;
        }

        /// <summary>The tile indexes in reading order (group by group, left to right).</summary>
        public static List<int> ReadingOrder(IReadOnlyList<BoardItem> items)
        {
            var order = new List<int>();
            foreach (BoardItem item in items)
                if (item.IsTile) order.Add(item.TileIndex);
            return order;
        }

        /// <summary>
        /// The tile an arrow key, Home or End moves to, or <paramref name="current"/> when it cannot move.
        /// Left and Right walk the reading order; Up and Down go to the nearest row above or below, to the tile
        /// whose centre is closest to the current one.
        /// </summary>
        public static int Navigate(IReadOnlyList<BoardItem> items, int current, Keys key)
        {
            List<int> order = ReadingOrder(items);
            if (order.Count == 0) return -1;
            int at = order.IndexOf(current);
            if (at < 0) return order[0];

            switch (key)
            {
                case Keys.Home: return order[0];
                case Keys.End: return order[order.Count - 1];
                case Keys.Right: return order[Math.Min(order.Count - 1, at + 1)];
                case Keys.Left: return order[Math.Max(0, at - 1)];
                case Keys.Up:
                case Keys.Down:
                {
                    BoardItem? from = Find(items, current);
                    if (from == null) return current;
                    int centre = from.Bounds.Left + from.Bounds.Width / 2;
                    bool down = key == Keys.Down;
                    int bestRow = down ? int.MaxValue : int.MinValue;
                    // The nearest row in the direction of travel...
                    foreach (BoardItem item in items)
                    {
                        if (!item.IsTile) continue;
                        int top = item.Bounds.Top;
                        if (down && top > from.Bounds.Top && top < bestRow) bestRow = top;
                        if (!down && top < from.Bounds.Top && top > bestRow) bestRow = top;
                    }
                    if (bestRow == int.MaxValue || bestRow == int.MinValue) return current;
                    // ...and in it, the tile whose centre is closest.
                    int best = current, bestDistance = int.MaxValue;
                    foreach (BoardItem item in items)
                    {
                        if (!item.IsTile || item.Bounds.Top != bestRow) continue;
                        int distance = Math.Abs(item.Bounds.Left + item.Bounds.Width / 2 - centre);
                        if (distance < bestDistance) { bestDistance = distance; best = item.TileIndex; }
                    }
                    return best;
                }
                default:
                    return current;
            }
        }

        public static BoardItem? Find(IReadOnlyList<BoardItem> items, int tileIndex)
        {
            foreach (BoardItem item in items)
                if (item.IsTile && item.TileIndex == tileIndex) return item;
            return null;
        }

        /// <summary>The tile under a point in content coordinates, or -1 (a gap, a caption, empty space).</summary>
        public static int HitTest(IReadOnlyList<BoardItem> items, Point contentPoint)
        {
            foreach (BoardItem item in items)
                if (item.IsTile && item.Bounds.Contains(contentPoint)) return item.TileIndex;
            return -1;
        }
    }
}

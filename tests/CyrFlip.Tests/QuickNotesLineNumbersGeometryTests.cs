using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;
using Xunit.Abstractions;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Ticket S0078, "geometry of the real HWND". The numbers are drawn by a sibling control, so the only
    /// honest proof that they stand opposite the rows they name is to ask the real edit control where its
    /// rows are and compare <b>pixels</b> - the ink of a number in the gutter against the ink of the first
    /// characters of the same row in the edit - and to compare the numbers against an oracle that knows
    /// nothing about <see cref="QuickNotesLineIndex"/>: it counts line breaks in the text by hand.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class QuickNotesLineNumbersGeometryTests
    {
        private readonly ITestOutputHelper _output;

        public QuickNotesLineNumbersGeometryTests(ITestOutputHelper output) => _output = output;

        private static void OnUiThread(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Exception("UI thread failed: " + failure, failure);
        }

        // ---- A hosted editor, the way the quick-notes window hosts it ----

        private sealed class Rig : IDisposable
        {
            public readonly Form Form;
            public readonly QuickNotesTextEditor Editor;
            public Font EditorFont;

            public Rig(int width = 520, int height = 420, float fontPoints = 10f, bool wrap = false, bool numbers = true,
                string family = "Consolas", bool mirrored = false)
            {
                Form = new Form { Width = width, Height = height, StartPosition = FormStartPosition.Manual, Location = new Point(20, 20) };
                if (mirrored)
                {
                    Form.RightToLeft = RightToLeft.Yes;
                    Form.RightToLeftLayout = true;
                }
                Editor = new QuickNotesTextEditor { Dock = DockStyle.Fill };
                EditorFont = new Font(family, fontPoints);
                Editor.TextBox.Font = EditorFont;
                Form.Controls.Add(Editor);
                Form.Show();
                Editor.ApplyWordWrap(wrap);
                Editor.ApplyLineNumbers(numbers);
                Pump();
            }

            public QuickNotesTextBox Box => Editor.TextBox;
            public QuickNotesLineGutter Gutter => Editor.Gutter;

            public void SetFont(string family, float points)
            {
                Font old = EditorFont;
                EditorFont = new Font(family, points);
                Box.Font = EditorFont;
                old.Dispose();
            }

            public void Pump() { Application.DoEvents(); }

            public void Dispose()
            {
                Form.Dispose();
                EditorFont.Dispose();
            }
        }

        /// <summary>A row of the edit control: its first character and its top in the edit's client area.</summary>
        private readonly struct Row
        {
            public Row(int index, int charIndex, int y) { Index = index; CharIndex = charIndex; Y = y; }
            public readonly int Index, CharIndex, Y;
        }

        private static IntPtr Send(Control c, uint msg, int w = 0, int l = 0)
            => WindowInterop.SendMessage(c.Handle, msg, (IntPtr)w, (IntPtr)l);

        private static int FirstVisibleRow(Control box) => (int)Send(box, WindowInterop.EM_GETFIRSTVISIBLELINE);

        private static WindowInterop.RECT WindowRect(Control c)
        {
            WindowInterop.GetWindowRect(c.Handle, out WindowInterop.RECT r);
            return r;
        }

        private static WindowInterop.RECT FormattingRect(Control box)
        {
            var rc = new WindowInterop.RECT();
            WindowInterop.SendMessageRect(box.Handle, WindowInterop.EM_GETRECT, IntPtr.Zero, ref rc);
            return rc;
        }

        /// <summary>The edit's client origin inside <paramref name="inner"/>'s own coordinates, by the screen.</summary>
        private static Point OriginIn(Control inner, Control box) => inner.PointToClient(box.PointToScreen(Point.Empty));

        private static int RowY(Control box, int charIndex)
        {
            int pos = (int)Send(box, WindowInterop.EM_POSFROMCHAR, charIndex);
            return (short)((pos >> 16) & 0xFFFF);
        }

        /// <summary>Visible rows of the real edit control, straight from its own messages.</summary>
        private static List<Row> VisibleRows(Control box)
        {
            var rows = new List<Row>();
            int length = ((TextBox)box).TextLength;
            int first = FirstVisibleRow(box);
            WindowInterop.RECT rc = FormattingRect(box);
            for (int r = first; ; r++)
            {
                int ci = (int)Send(box, WindowInterop.EM_LINEINDEX, r);
                if (ci < 0 || ci >= length) break;
                int y = RowY(box, ci);
                if (y >= rc.Bottom) break;
                rows.Add(new Row(r, ci, y));
            }
            return rows;
        }

        /// <summary>
        /// What the gutter has to say about each visible row, by counting line breaks in the text by hand -
        /// no index, no binary search. Null = no number for that row.
        /// </summary>
        private static List<(Row Row, string? Label)> Oracle(Rig rig)
        {
            string text = rig.Box.Text;
            var result = new List<(Row, string?)>();
            int last = -1;
            foreach (Row row in VisibleRows(rig.Box))
            {
                int line = 1;
                for (int i = 0; i < row.CharIndex; i++)
                {
                    if (text[i] == '\r')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                        line++;
                    }
                    else if (text[i] == '\n') line++;
                }
                bool isStart = row.CharIndex == 0 || text[row.CharIndex - 1] == '\n' || text[row.CharIndex - 1] == '\r';
                string? label = null;
                if (isStart) { label = line.ToString(); last = line; }
                else if (line != last) { label = line + " " + QuickNotesTextEditor.ContinuationMarker; last = line; }
                result.Add((row, label));
            }
            return result;
        }

        private static Bitmap Snap(Control c)
        {
            var bmp = new Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height));
            c.DrawToBitmap(bmp, new Rectangle(0, 0, c.Width, c.Height));
            return bmp;
        }

        private static int Distance(Color px, Color bg)
            => Math.Abs(px.R - bg.R) + Math.Abs(px.G - bg.G) + Math.Abs(px.B - bg.B);

        /// <summary>The strongest pixel in <paramref name="area"/>: what "full ink" means for this bitmap.</summary>
        private static int StrongestInk(Bitmap bmp, Rectangle area, Color bg)
        {
            int strongest = 0;
            for (int y = Math.Max(0, area.Top); y < Math.Min(bmp.Height, area.Bottom); y++)
                for (int x = Math.Max(0, area.Left); x < Math.Min(bmp.Width, area.Right); x++)
                    strongest = Math.Max(strongest, Distance(bmp.GetPixel(x, y), bg));
            return strongest;
        }

        /// <summary>
        /// First and last pixel row holding ink inside <paramref name="band"/>, or null. Ink is a pixel at
        /// least half as far from the background as the strongest one in the bitmap's text area, so grey
        /// numbers on a grey strip and black text on white are judged by the same rule.
        /// </summary>
        private static (int Top, int Bottom)? InkExtent(Bitmap bmp, Rectangle band, Color bg, int strongest)
        {
            int threshold = Math.Max(30, strongest / 2);
            int top = -1, bottom = -1;
            for (int y = Math.Max(0, band.Top); y < Math.Min(bmp.Height, band.Bottom); y++)
            {
                for (int x = Math.Max(0, band.Left); x < Math.Min(bmp.Width, band.Right); x++)
                {
                    if (Distance(bmp.GetPixel(x, y), bg) < threshold) continue;
                    if (top < 0) top = y;
                    bottom = y;
                    break;
                }
            }
            return top < 0 ? ((int, int)?)null : (top, bottom);
        }

        /// <summary>Lines that begin with their own number and continue in letters without ascenders or descenders.</summary>
        private static string NumberedDoc(int lines, Func<int, int> wordsPerLine)
            => string.Join("\r\n", Enumerable.Range(1, lines).Select(n => n + " " + string.Join(" ", Enumerable.Repeat("xxxx", wordsPerLine(n)))));

        private static int Wordy(int n) => n % 7 == 0 ? 40 : n % 3 == 0 ? 12 : 2;

        // ---- The assertions every scenario ends in ----

        /// <summary>
        /// The gutter is exactly what the oracle says, at exactly the rows the edit reports - checked as numbers
        /// and, when <paramref name="pixels"/>, as ink on the screen: every numbered row has its number's ink on
        /// the same pixel row as the first characters of the text, every unnumbered row has none.
        /// </summary>
        private void AssertAligned(Rig rig, string context, bool pixels = true, bool numberedText = true)
        {
            rig.Pump();
            var oracle = Oracle(rig);
            string text = rig.Box.Text;
            Assert.True(oracle.Count > 0, context + ": nothing visible to check.");

            Point origin = OriginIn(rig.Gutter, rig.Box);
            var expected = oracle.Where(o => o.Label != null).Select(o => (o.Label!, o.Row.Y + origin.Y)).ToList();
            var actual = rig.Editor.LastLabels.Select(l => (l.Text, l.Y)).ToList();
            Assert.True(expected.SequenceEqual(actual),
                context + ": expected " + string.Join(", ", expected) + " but the gutter painted " + string.Join(", ", actual));
            if (!pixels) return;

            using Bitmap boxShot = Snap(rig.Box);
            using Bitmap gutterShot = Snap(rig.Gutter);
            if (Environment.GetEnvironmentVariable("CYRFLIP_DUMP_SHOTS") is string dump && dump.Length > 0)
            {
                string stem = string.Concat(context.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
                boxShot.Save(System.IO.Path.Combine(dump, "box-" + stem + ".png"));
                gutterShot.Save(System.IO.Path.Combine(dump, "gutter-" + stem + ".png"));
            }
            WindowInterop.RECT boxWindow = WindowRect(rig.Box);
            Point boxClient = rig.Box.PointToScreen(Point.Empty);
            int clientLeft = boxClient.X - boxWindow.Left, clientTop = boxClient.Y - boxWindow.Top;
            // The background is the pixel in the edit's own empty margin (its formatting rectangle starts 4 px in):
            // the middle of the picture may well be a letter when the document fills the view.
            Color boxBg = boxShot.GetPixel(clientLeft + 1, clientTop + 1);
            Color gutterBg = gutterShot.GetPixel(2, 1);
            Rectangle clientArea = new Rectangle(clientLeft, clientTop, rig.Box.ClientSize.Width, rig.Box.ClientSize.Height);
            int lineHeight = oracle.Count > 1 ? oracle[1].Row.Y - oracle[0].Row.Y : 15;
            int boxStrongest = StrongestInk(boxShot, clientArea, boxBg);
            int gutterStrongest = StrongestInk(gutterShot, new Rectangle(4, 0, Math.Max(1, rig.Gutter.Width - 8), gutterShot.Height), gutterBg);

            foreach (var (row, label) in oracle)
            {
                int gy = origin.Y + row.Y;
                if (label == null)
                {
                    // Inset by two rows of pixels: a neighbour's marker glyph may reach a pixel into this row's cell.
                    var blank = InkExtent(gutterShot, new Rectangle(4, gy + 2, Math.Max(1, rig.Gutter.Width - 8), Math.Max(1, lineHeight - 4)), gutterBg, gutterStrongest);
                    Assert.True(blank == null, $"{context}: row {row.Index} is a continuation and must have no number, but has ink.");
                    continue;
                }

                var inGutter = InkExtent(gutterShot, new Rectangle(4, gy, Math.Max(1, rig.Gutter.Width - 8), lineHeight), gutterBg, gutterStrongest);
                Assert.True(inGutter != null, $"{context}: row {row.Index} should read '{label}' but shows nothing at y={gy}.");
                // A "↳" row's text is not the number, and neither is any row of text that does not begin with its own.
                if (!numberedText || label.Contains(QuickNotesTextEditor.ContinuationMarker)) continue;

                var inBox = InkExtent(boxShot, new Rectangle(clientLeft, clientTop + row.Y, clientArea.Width, lineHeight), boxBg, boxStrongest);
                if (inBox == null)
                {
                    // An empty line: nothing to compare with, but it still has its number.
                    Assert.True(text[row.CharIndex] == '\r' || text[row.CharIndex] == '\n', $"{context}: row {row.Index} has text but no ink.");
                    continue;
                }
                int editOffset = inBox!.Value.Top - clientTop - row.Y;
                int gutterOffset = inGutter!.Value.Top - gy;
                Assert.True(editOffset == gutterOffset,
                    $"{context}: row {row.Index} '{label}' (char {row.CharIndex}, y {row.Y}, band {clientTop + row.Y}+{lineHeight}, gutter y {gy}) - the text's ink {inBox.Value.Top}..{inBox.Value.Bottom} starts {editOffset}px into its row, the number's {inGutter.Value.Top}..{inGutter.Value.Bottom} {gutterOffset}px.");
                int boxHeight = inBox.Value.Bottom - inBox.Value.Top;
                // The bottom edge is anti-aliasing: ClearType in the edit, grey on grey here - a pixel row of slack.
                Assert.InRange(inGutter.Value.Bottom - inGutter.Value.Top, boxHeight - 1, boxHeight + 1);
            }
        }

        /// <summary>The cheap half, with nothing pumped in between: the gutter already shows what the edit shows.</summary>
        private void AssertGutterFollowsNow(Rig rig, string how)
        {
            Assert.True(FirstVisibleRow(rig.Box) == rig.Editor.PaintedFirstRow,
                $"{how}: the text is at row {FirstVisibleRow(rig.Box)} but the gutter was last painted for row {rig.Editor.PaintedFirstRow}.");
            Point origin = OriginIn(rig.Gutter, rig.Box);
            var expected = Oracle(rig).Where(o => o.Label != null).Select(o => (o.Label!, o.Row.Y + origin.Y)).ToList();
            var actual = rig.Editor.LastLabels.Select(l => (l.Text, l.Y)).ToList();
            Assert.True(expected.SequenceEqual(actual),
                $"{how}: expected {string.Join(", ", expected)} but the gutter shows {string.Join(", ", actual)}.");
        }

        // =========================================================================
        // The numbers stand opposite their rows - pixel for pixel
        // =========================================================================

        [Theory]
        [InlineData(10f, false)]
        [InlineData(10f, true)]
        [InlineData(15f, false)]   // what 150 % display scaling does to the editor's font
        [InlineData(20f, true)]    // 200 %
        public void EveryNumberStartsOnTheSamePixelRowAsItsLine(float points, bool wrap)
        {
            OnUiThread(() =>
            {
                using var rig = new Rig(wrap: wrap, fontPoints: points, width: 520, height: 420);
                rig.Box.Text = NumberedDoc(12, n => 1);
                AssertAligned(rig, $"{points} pt, wrap {wrap}");
            });
        }

        [Theory]
        [InlineData("Consolas", 10f)]            // the edit lays out 15 px rows, Font.Height says 16
        [InlineData("Segoe UI", 9f)]             // 15 against 16
        [InlineData("Microsoft YaHei UI", 9f)]   // 17 against 16 - the other direction
        [InlineData("Courier New", 11f)]
        public void RowHeightIsTheEditsNotTheFontsAndTheLastEmptyRowStandsOnIt(string family, float points)
        {
            OnUiThread(() =>
            {
                using var rig = new Rig(fontPoints: points, family: family);
                rig.Box.Text = "a\r\nb\r\nc";
                rig.Pump();
                var real = VisibleRows(rig.Box);
                int rowHeight = real[1].Y - real[0].Y;
                _output.WriteLine($"{family} {points} pt: edit row {rowHeight} px, Font.Height {rig.EditorFont.Height} px");

                // A final line break makes an empty last row, for which the edit gives no position at all.
                foreach (string text in new[] { "a\r\n", "a\r\nb\r\n", "\r\n", "\r\n\r\n\r\n" })
                {
                    rig.Box.Text = text;
                    rig.Pump();
                    var labels = rig.Editor.LastLabels;
                    Assert.Equal(text.Split(new[] { "\r\n" }, StringSplitOptions.None).Length, labels.Count);
                    for (int i = 1; i < labels.Count; i++)
                        Assert.True(labels[i].Y - labels[i - 1].Y == rowHeight,
                            $"{family}: row {i} of {text.Replace("\r\n", "\\r\\n")} is {labels[i].Y - labels[i - 1].Y}px below the previous, the edit's rows are {rowHeight}px.");
                }

                rig.Box.Text = "";
                rig.Pump();
                Assert.Equal("1", Assert.Single(rig.Editor.LastLabels).Text);
                Assert.Equal(OriginIn(rig.Gutter, rig.Box).Y + FormattingRect(rig.Box).Top, rig.Editor.LastLabels[0].Y);
            });
        }

        // =========================================================================
        // Examples of the ticket (item 5), on the real control
        // =========================================================================

        [Fact]
        public void TicketExampleOneTwoBlankBlankThreeFour()
        {
            OnUiThread(() =>
            {
                using var rig = new Rig(width: 230, height: 200, wrap: true);
                // The ticket's text, then enough lines after it that the view can scroll.
                string text = "Альфа\r\nБета очень длинная строка текста которая точно не помещается\r\n\r\nГамма"
                    + string.Concat(Enumerable.Range(5, 30).Select(n => "\r\nстрока " + n));
                rig.Box.Text = text;
                rig.Pump();

                int rowsOfLineTwo = (int)Send(rig.Box, WindowInterop.EM_GETLINECOUNT) - 3 - 30;
                Assert.True(rowsOfLineTwo >= 3, $"The rig must wrap line 2 over at least three rows, got {rowsOfLineTwo}.");

                var labels = rig.Editor.LastLabels;
                Assert.Equal(new[] { "1", "2", "3", "4" }, labels.Take(4).Select(l => l.Text).ToArray());
                int rowHeight = labels[1].Y - labels[0].Y;
                Assert.Equal(rowsOfLineTwo * rowHeight, labels[2].Y - labels[1].Y);   // the continuations are empty
                Assert.Equal(rowHeight, labels[3].Y - labels[2].Y);                   // line 3 is an empty line of one row

                // Scrolled one row into line 2: its first visible row reads "2 ↳".
                Send(rig.Box, WindowInterop.EM_LINESCROLL, 0, 2);
                rig.Pump();
                Assert.Equal("2 ↳", rig.Editor.LastLabels[0].Text);
                Assert.True(rig.Editor.LastLabels[0].Continuation);
                Assert.Equal(OriginIn(rig.Gutter, rig.Box).Y + FormattingRect(rig.Box).Top, rig.Editor.LastLabels[0].Y);
                Assert.Equal(new[] { "2 ↳", "3", "4" }, rig.Editor.LastLabels.Take(3).Select(l => l.Text).ToArray());
                AssertAligned(rig, "after scrolling into line 2", numberedText: false);
            });
        }

        [Fact]
        public void ALineSpanningSeveralScreensIsNamedOnceInTheMiddle()
        {
            OnUiThread(() =>
            {
                using var rig = new Rig(width: 300, height: 260, wrap: true);
                string longLine = string.Join(" ", Enumerable.Repeat("xxxx", 900));
                rig.Box.Text = "1 xxxx\r\n2 xxxx\r\n3 " + longLine + "\r\n4 xxxx\r\n5 xxxx";
                rig.Pump();
                int total = (int)Send(rig.Box, WindowInterop.EM_GETLINECOUNT);
                int visible = VisibleRows(rig.Box).Count;
                Assert.True(total > visible * 3, "Line 3 must be several screens long.");

                foreach (int scroll in new[] { 10, 5, 7, 9 })   // always inside line 3
                {
                    Send(rig.Box, WindowInterop.EM_LINESCROLL, 0, scroll);
                    rig.Pump();
                    Assert.Equal("3 ↳", Assert.Single(rig.Editor.LastLabels).Text);   // once, however far in
                    AssertAligned(rig, $"{scroll} rows further into line 3");
                }

                // Exactly at its first row it is a plain "3"; at the end the lines after it come into view.
                Send(rig.Box, WindowInterop.EM_LINESCROLL, 0, -1000);
                Send(rig.Box, WindowInterop.EM_LINESCROLL, 0, 2);
                rig.Pump();
                Assert.Equal("3", rig.Editor.LastLabels[0].Text);
                Send(rig.Box, WindowInterop.EM_LINESCROLL, 0, 100000);
                rig.Pump();
                Assert.Equal(new[] { "3 ↳", "4", "5" }, rig.Editor.LastLabels.Select(l => l.Text).ToArray());
                AssertAligned(rig, "at the end of the document");
            });
        }

        // =========================================================================
        // Scrolling: every way gives the same alignment, in the same turn
        // =========================================================================

        [Fact]
        public void EveryWayOfScrollingMovesTheNumbersWithTheTextBeforeTheNextMessage()
        {
            OnUiThread(() =>
            {
                using var rig = new Rig(width: 300, height: 340, wrap: true);
                rig.Box.Text = NumberedDoc(160, Wordy);
                rig.Pump();
                AssertGutterFollowsNow(rig, "initial");

                const int SB_LINEDOWN = 1, SB_PAGEUP = 2, SB_PAGEDOWN = 3, SB_THUMBPOSITION = 4, SB_BOTTOM = 7, SB_TOP = 6;
                Point inside = rig.Box.PointToScreen(new Point(30, 30));
                int pointer = (inside.Y << 16) | (inside.X & 0xFFFF);
                IntPtr wheelDown = (IntPtr)((long)unchecked((ushort)-120) << 16);
                IntPtr wheelUp = (IntPtr)((long)120 << 16);

                var ways = new List<(string Name, Action Do)>
                {
                    ("wheel over the text, down", () => WindowInterop.SendMessage(rig.Box.Handle, 0x020A, wheelDown, (IntPtr)pointer)),
                    ("wheel over the text, up", () => WindowInterop.SendMessage(rig.Box.Handle, 0x020A, wheelUp, (IntPtr)pointer)),
                    ("wheel over the gutter, down", () => WindowInterop.SendMessage(rig.Gutter.Handle, 0x020A, wheelDown, (IntPtr)pointer)),
                    ("wheel over the gutter, down again", () => WindowInterop.SendMessage(rig.Gutter.Handle, 0x020A, wheelDown, (IntPtr)pointer)),
                    ("scroll bar, line down", () => Send(rig.Box, 0x0115, SB_LINEDOWN)),
                    ("scroll bar, page down", () => Send(rig.Box, 0x0115, SB_PAGEDOWN)),
                    ("scroll bar, page up", () => Send(rig.Box, 0x0115, SB_PAGEUP)),
                    ("scroll bar, thumb to 40 %", () => Send(rig.Box, 0x0115, (60 << 16) | SB_THUMBPOSITION)),
                    ("Page Down key", () => Send(rig.Box, WindowInterop.WM_KEYDOWN, 0x22, 1)),
                    ("Page Up key", () => Send(rig.Box, WindowInterop.WM_KEYDOWN, 0x21, 1)),
                    ("Down arrow key", () => Send(rig.Box, WindowInterop.WM_KEYDOWN, 0x28, 1)),
                    ("EM_LINESCROLL forward", () => Send(rig.Box, WindowInterop.EM_LINESCROLL, 0, 9)),
                    ("EM_LINESCROLL back", () => Send(rig.Box, WindowInterop.EM_LINESCROLL, 0, -4)),
                    ("EM_SCROLL page down", () => Send(rig.Box, 0x00B5, SB_PAGEDOWN)),
                    ("caret moved by code, then scrolled into view", () => { rig.Box.SelectionStart = rig.Box.TextLength; rig.Box.ScrollToCaret(); }),
                    ("scroll bar, to the top", () => Send(rig.Box, 0x0115, SB_TOP)),
                    ("caret moved by code to the middle", () => { rig.Box.SelectionStart = rig.Box.TextLength / 2; rig.Box.ScrollToCaret(); }),
                    ("scroll bar, to the bottom", () => Send(rig.Box, 0x0115, SB_BOTTOM)),
                };

                int moved = 0, previous = FirstVisibleRow(rig.Box);
                foreach (var (name, act) in ways)
                {
                    act();
                    AssertGutterFollowsNow(rig, name);   // nothing pumped: not a frame behind
                    int now = FirstVisibleRow(rig.Box);
                    if (now != previous) moved++;
                    previous = now;
                }
                Assert.True(moved >= ways.Count - 4, $"The rig must really scroll; only {moved} of {ways.Count} ways moved the view.");
                AssertAligned(rig, "after every way of scrolling");
            });
        }

        [Fact]
        public void HorizontalScrollingMovesNothingInTheGutter()
        {
            OnUiThread(() =>
            {
                using var rig = new Rig(width: 300, height: 340, wrap: false);
                rig.Box.Text = NumberedDoc(40, n => n % 5 == 0 ? 120 : 3);
                rig.Pump();
                var before = rig.Editor.LastLabels.Select(l => (l.Text, l.Y)).ToList();
                Rectangle gutterBefore = rig.Gutter.Bounds;

                Send(rig.Box, WindowInterop.EM_LINESCROLL, 150, 0);
                rig.Pump();

                Assert.Equal(before, rig.Editor.LastLabels.Select(l => (l.Text, l.Y)).ToList());
                Assert.Equal(gutterBefore, rig.Gutter.Bounds);
                AssertAligned(rig, "scrolled sideways", pixels: false);   // the text itself has moved away from the digits
            });
        }

        [Fact]
        public void ARowCutOffAtTheBottomHasNoNumber()
        {
            OnUiThread(() =>
            {
                using var rig = new Rig(wrap: false);
                rig.Box.Text = NumberedDoc(80, n => 1);
                // Heights that leave every possible fraction of a row under the last whole one.
                for (int height = 300; height < 322; height++)
                {
                    rig.Form.Height = height;
                    rig.Pump();
                    int visible = VisibleRows(rig.Box).Count;
                    Assert.Equal(visible, rig.Editor.LastLabels.Count);
                    Assert.True((int)Send(rig.Box, WindowInterop.EM_LINEINDEX, FirstVisibleRow(rig.Box) + visible) >= 0,
                        "There must be a row beyond the visible ones for this to prove anything.");
                    foreach (GutterLabel label in rig.Editor.LastLabels)
                        Assert.True(label.Y - OriginIn(rig.Gutter, rig.Box).Y < FormattingRect(rig.Box).Bottom);
                }
            });
        }

        // =========================================================================
        // Everything that rebuilds the correspondence of rows to lines
        // =========================================================================

        [Fact]
        public void ResizingTheFontTheWrapAndTheOptionRebuildsTheCorrespondenceAtOnce()
        {
            OnUiThread(() =>
            {
                using var rig = new Rig(width: 460, height: 380, wrap: true);
                rig.Box.Text = NumberedDoc(60, Wordy);
                Send(rig.Box, WindowInterop.EM_LINESCROLL, 0, 25);
                AssertAligned(rig, "start");

                foreach (int width in new[] { 640, 330, 520, 280 })
                {
                    rig.Form.Width = width;
                    AssertAligned(rig, $"window {width} px wide");
                }

                rig.SetFont("Consolas", 14f);
                AssertAligned(rig, "font 14 pt");
                rig.SetFont("Consolas", 9f);
                AssertAligned(rig, "font 9 pt");

                rig.Editor.ApplyWordWrap(false);
                AssertAligned(rig, "wrap switched off");
                rig.Editor.ApplyWordWrap(true);
                AssertAligned(rig, "wrap switched on");

                rig.Editor.ApplyLineNumbers(false);
                rig.Pump();
                Assert.False(rig.Gutter.Visible);
                Assert.Equal(0, rig.Gutter.Width);
                rig.Editor.ApplyLineNumbers(true);
                AssertAligned(rig, "numbers switched back on");
            });
        }

        [Fact]
        public void MovingASplitterRewrapsAndTheNumbersFollow()
        {
            OnUiThread(() =>
            {
                using var form = new Form { Width = 800, Height = 420, StartPosition = FormStartPosition.Manual, Location = new Point(20, 20) };
                using var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 100 };
                using var editor = new QuickNotesTextEditor { Dock = DockStyle.Fill };
                using var font = new Font("Consolas", 10f);
                editor.TextBox.Font = font;
                split.Panel2.Controls.Add(editor);
                form.Controls.Add(split);
                form.Show();
                editor.ApplyWordWrap(true);
                editor.ApplyLineNumbers(true);
                editor.TextBox.Text = NumberedDoc(50, Wordy);
                Application.DoEvents();

                foreach (int distance in new[] { 100, 300, 180, 450 })
                {
                    split.SplitterDistance = distance;
                    Application.DoEvents();
                    // Every row that begins a line has a number, at the row's own Y; continuations have none.
                    Point origin = OriginIn(editor.Gutter, editor.TextBox);
                    string text = editor.TextBox.Text;
                    var expected = VisibleRows(editor.TextBox)
                        .Where(row => row.CharIndex == 0 || text[row.CharIndex - 1] == '\n')
                        .Select(row => row.Y + origin.Y).ToList();
                    Assert.True(expected.Count >= 3);
                    Assert.Equal(expected, editor.LastLabels.Select(l => l.Y).ToList());
                    Assert.Equal(editor.Gutter.Right, editor.TextBox.Left);
                }
            });
        }

        [Fact]
        public void NumbersGainTheirDigitsAtTenAndAHundredWithoutCuttingOneOff()
        {
            OnUiThread(() =>
            {
                using var rig = new Rig(width: 420, height: 380, wrap: true);
                int previousWidth = 0;
                foreach (int lines in new[] { 8, 9, 10, 11, 98, 99, 100, 101, 998, 999, 1000 })
                {
                    rig.Box.Text = NumberedDoc(lines, n => 1);
                    rig.Pump();
                    Send(rig.Box, WindowInterop.EM_LINESCROLL, 0, lines);   // to the end: the widest numbers are in view
                    AssertAligned(rig, $"{lines} lines");

                    int digits = lines.ToString().Length;
                    string widest = new string('8', digits) + " " + QuickNotesTextEditor.ContinuationMarker;
                    int needed = TextRenderer.MeasureText(widest, rig.Box.Font, new Size(int.MaxValue, int.MaxValue),
                        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
                    int pad = QuickNotesTextEditor.ScaledPadding(rig.Editor.DeviceDpi);
                    Assert.True(rig.Gutter.Width >= needed + 2 * pad + 1, $"{lines} lines: strip {rig.Gutter.Width}px, its widest label needs {needed + 2 * pad + 1}px.");
                    Assert.True(rig.Gutter.Width >= previousWidth, "A longer document never has a narrower strip.");
                    previousWidth = rig.Gutter.Width;
                    Assert.Equal(rig.Gutter.Right, rig.Box.Left);
                    Assert.Equal(rig.Editor.ClientSize.Width, rig.Box.Right);   // the strip and the text fill the editor, no overlap, no gap
                }
            });
        }

        // =========================================================================
        // Toggling: caret, selection, undo and the top of the view survive
        // =========================================================================

        [Fact]
        public void TogglingTheNumbersKeepsCaretSelectionUndoAndTheTopOfTheView()
        {
            OnUiThread(() =>
            {
                using var rig = new Rig(width: 400, height: 360, wrap: true, numbers: false);
                string original = NumberedDoc(120, Wordy);
                rig.Box.Text = original;
                rig.Pump();
                rig.Box.ClearUndo();

                // One edit on the undo stack - sent as EM_REPLACESEL with "can undo" set, because WinForms'
                // SelectedText and AppendText both clear the undo buffer - then a selection deep in the
                // document with the view scrolled a little above it.
                rig.Box.Select(rig.Box.TextLength, 0);
                IntPtr replacement = System.Runtime.InteropServices.Marshal.StringToHGlobalUni("EDITED");
                try { WindowInterop.SendMessage(rig.Box.Handle, 0x00C2, (IntPtr)1, replacement); }
                finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(replacement); }
                string edited = rig.Box.Text;
                Assert.EndsWith("EDITED", edited);
                int start = original.IndexOf("\r\n80 ", StringComparison.Ordinal) + 2;
                rig.Box.Select(start + 2, 4);
                rig.Box.ScrollToCaret();
                Send(rig.Box, WindowInterop.EM_LINESCROLL, 0, -3);
                rig.Pump();
                Assert.True(rig.Box.CanUndo);

                int selStart = rig.Box.SelectionStart, selLen = rig.Box.SelectionLength;
                int topLineBefore = LogicalLine(edited, TopChar(rig.Box));

                foreach (bool on in new[] { true, false, true })
                {
                    rig.Editor.ApplyLineNumbers(on);
                    rig.Pump();
                    Assert.Equal(edited, rig.Box.Text);
                    Assert.Equal(selStart, rig.Box.SelectionStart);
                    Assert.Equal(selLen, rig.Box.SelectionLength);
                    Assert.True(rig.Box.CanUndo, "The edit's undo history must survive the option.");
                    Assert.True(topLineBefore == LogicalLine(edited, TopChar(rig.Box)),
                        $"Numbers {(on ? "on" : "off")}: the view was at line {topLineBefore} and is now at line {LogicalLine(edited, TopChar(rig.Box))}.");
                }

                rig.Box.Undo();
                Assert.Equal(original, rig.Box.Text);   // the native Undo still reverts the edit made before the option was touched
                rig.Box.Undo();
                Assert.Equal(edited, rig.Box.Text);     // ...and its second press is Redo
            });
        }

        private static int TopChar(Control box) => (int)Send(box, WindowInterop.EM_LINEINDEX, FirstVisibleRow(box));

        /// <summary>The logical line (1-based) a character belongs to, counted by hand.</summary>
        private static int LogicalLine(string text, int charIndex)
        {
            int line = 1;
            for (int i = 0; i < Math.Min(charIndex, text.Length); i++)
            {
                if (text[i] == '\r') { if (i + 1 < text.Length && text[i + 1] == '\n') i++; line++; }
                else if (text[i] == '\n') line++;
            }
            return line;
        }

        [Fact]
        public void WrapSwitchKeepsSelectionAndTopLine()
        {
            OnUiThread(() =>
            {
                using var rig = new Rig(width: 400, height: 360, wrap: false);
                string text = NumberedDoc(150, Wordy);
                rig.Box.Text = text;
                int start = text.IndexOf("\r\n100 ", StringComparison.Ordinal) + 2;
                rig.Box.Select(start, 5);
                rig.Box.ScrollToCaret();
                Send(rig.Box, WindowInterop.EM_LINESCROLL, 0, -4);
                rig.Pump();
                int topLineBefore = LogicalLine(text, TopChar(rig.Box));
                Assert.True(topLineBefore > 50, $"The rig must start deep in the document, it is at line {topLineBefore}.");

                foreach (bool wrap in new[] { true, false, true })
                {
                    rig.Editor.ApplyWordWrap(wrap);
                    rig.Pump();
                    Assert.Equal(start, rig.Box.SelectionStart);
                    Assert.Equal(5, rig.Box.SelectionLength);
                    Assert.Equal(text, rig.Box.Text);
                    Assert.True(topLineBefore == LogicalLine(text, TopChar(rig.Box)),
                        $"Wrap {(wrap ? "on" : "off")}: the view was at line {topLineBefore} and is now at line {LogicalLine(text, TopChar(rig.Box))}.");
                    AssertAligned(rig, "wrap " + (wrap ? "on" : "off"));
                }
            });
        }

        [Fact]
        public void TheStripIsOnlyAGuide()
        {
            OnUiThread(() =>
            {
                using var rig = new Rig(wrap: true);
                rig.Box.Text = NumberedDoc(30, n => 2);
                rig.Form.ActiveControl = rig.Box;
                rig.Pump();
                Assert.False(rig.Gutter.TabStop);

                string before = rig.Box.Text;
                int selStart = rig.Box.SelectionStart, selLen = rig.Box.SelectionLength;
                // A click on the strip selects nothing, edits nothing and takes no focus.
                WindowInterop.SendMessage(rig.Gutter.Handle, 0x0201, (IntPtr)1, (IntPtr)(8 | (20 << 16)));
                WindowInterop.SendMessage(rig.Gutter.Handle, 0x0202, IntPtr.Zero, (IntPtr)(8 | (20 << 16)));
                WindowInterop.SendMessage(rig.Gutter.Handle, 0x0203, (IntPtr)1, (IntPtr)(8 | (20 << 16)));   // double click
                WindowInterop.SendMessage(rig.Gutter.Handle, 0x0102, (IntPtr)'x', (IntPtr)1);               // typing into it
                rig.Pump();
                Assert.Same(rig.Box, rig.Form.ActiveControl);
                Assert.Equal(before, rig.Box.Text);
                Assert.Equal(selStart, rig.Box.SelectionStart);
                Assert.Equal(selLen, rig.Box.SelectionLength);
                Assert.False(rig.Editor.Gutter.Focused);
            });
        }

        // =========================================================================
        // Right-to-left and the themes
        // =========================================================================

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TheStripIsPhysicallyOnTheLeftInAMirroredWindowToo(bool mirrored)
        {
            OnUiThread(() =>
            {
                using var rig = new Rig(width: 520, height: 380, wrap: true, mirrored: mirrored);
                rig.Box.Text = NumberedDoc(40, Wordy);
                rig.Pump();

                WindowInterop.RECT editor = WindowRect(rig.Editor), gutter = WindowRect(rig.Gutter), box = WindowRect(rig.Box);
                Assert.True(gutter.Left == editor.Left, $"The strip starts at screen x={gutter.Left}, the editor at {editor.Left} (mirrored window: {mirrored}).");
                Assert.True(gutter.Right == box.Left, "The text starts where the strip ends.");
                Assert.True(box.Right == editor.Right, "...and fills the rest.");
                Assert.Equal(mirrored ? RightToLeft.Yes : RightToLeft.No, rig.Form.RightToLeft);
                AssertAligned(rig, mirrored ? "mirrored window" : "plain window");

                // The divider is the strip's edge next to the text, the numbers hug it.
                using Bitmap shot = Snap(rig.Gutter);
                ThemePalette palette = ThemeApply.PaletteOf(rig.Editor) ?? ThemePalette.Light;
                Assert.Equal(palette.Border.ToArgb(), shot.GetPixel(shot.Width - 1, 5).ToArgb());
                Assert.Equal(palette.SurfaceSunken.ToArgb(), shot.GetPixel(0, 5).ToArgb());
            });
        }

        [Theory]
        [InlineData("light")]
        [InlineData("dark")]
        [InlineData("highcontrast")]
        public void EveryThemeGivesReadableNumbersAndABorder(string theme)
        {
            OnUiThread(() =>
            {
                using var rig = new Rig(width: 520, height: 380, wrap: true);
                ThemePalette palette = theme == "dark" ? ThemePalette.Dark : theme == "highcontrast" ? ThemePalette.HighContrast : ThemePalette.Light;
                rig.Box.Text = NumberedDoc(25, n => 1);
                ThemeApply.Apply(rig.Form, palette);
                rig.Editor.ApplyTheme(palette);
                rig.Pump();

                using Bitmap shot = Snap(rig.Gutter);
                Assert.Equal(palette.SurfaceSunken.ToArgb(), shot.GetPixel(1, 1).ToArgb());
                Assert.Equal(palette.Border.ToArgb(), shot.GetPixel(shot.Width - 1, 1).ToArgb());

                // The ink: the strongest pixel is nearer the theme's number colour than the strip's own colour,
                // and that colour holds the contrast the palette promises for it (4.5:1).
                Color ink = theme == "highcontrast" ? palette.TextPrimary : palette.TextMuted;
                Color best = palette.SurfaceSunken;
                for (int y = 0; y < shot.Height; y++)
                    for (int x = 2; x < shot.Width - 3; x++)
                        if (Distance(shot.GetPixel(x, y), palette.SurfaceSunken) > Distance(best, palette.SurfaceSunken)) best = shot.GetPixel(x, y);
                Assert.True(Distance(best, ink) < Distance(best, palette.SurfaceSunken), $"{theme}: the numbers are not drawn in the number colour.");
                // System colours are the palette in light and high contrast: a machine already in a contrast theme has its own.
                if (!SystemInformation.HighContrast)
                    Assert.True(ThemePalette.Contrast(ink, palette.SurfaceSunken) >= 4.5,
                        $"{theme}: {ink} on {palette.SurfaceSunken} is {ThemePalette.Contrast(ink, palette.SurfaceSunken):0.00}:1");
                AssertAligned(rig, theme + " theme");
            });
        }

        // =========================================================================
        // Cost on a note at the size limit
        // =========================================================================

        [Fact]
        public void ANoteAtTheSizeLimitIsCheapToScrollResizeAndEdit()
        {
            OnUiThread(() =>
            {
                // About QuickNote.MaxBytes of mixed text: lines of every length, a few that wrap over many rows.
                var sb = new System.Text.StringBuilder();
                var random = new Random(78);
                while (sb.Length < QuickNote.MaxBytes - 200)
                {
                    int words = random.Next(0, 4) == 0 ? random.Next(0, 3) : random.Next(3, 22);
                    sb.Append(string.Join(" ", Enumerable.Range(0, words).Select(_ => new string('x', random.Next(1, 9))))).Append("\r\n");
                }
                string text = sb.ToString();
                Assert.False(QuickNote.ExceedsLimit(text));

                foreach (bool wrap in new[] { true, false })
                {
                    using var rig = new Rig(width: 700, height: 520, wrap: wrap, numbers: true);
                    var clock = Stopwatch.StartNew();
                    rig.Box.Text = text;
                    rig.Pump();
                    long loaded = clock.ElapsedMilliseconds;
                    int builds = rig.Editor.IndexBuilds;
                    int lines = rig.Editor.LineIndex.LineCount;

                    // Scrolling: 400 steps of every kind. The document is never scanned again.
                    var scroll = new List<double>();
                    var random2 = new Random(5);
                    int rowsTotal = (int)Send(rig.Box, WindowInterop.EM_GETLINECOUNT);
                    for (int i = 0; i < 400; i++)
                    {
                        long t0 = Stopwatch.GetTimestamp();
                        if (i % 4 == 0) Send(rig.Box, 0x0115, 3);                                          // page down
                        else if (i % 4 == 1) Send(rig.Box, WindowInterop.EM_LINESCROLL, 0, random2.Next(-40, 40));
                        else if (i % 4 == 2) Send(rig.Box, 0x0115, (random2.Next(0, 100) << 16) | 4);       // thumb
                        else Send(rig.Box, 0x0115, 1);
                        rig.Pump();
                        scroll.Add((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
                    }
                    Assert.Equal(builds, rig.Editor.IndexBuilds);   // not one rebuild of the index while scrolling

                    // Resizing re-wraps the whole text inside the edit (that is the edit's own cost) and never the index.
                    var resize = new List<double>();
                    for (int i = 0; i < 12; i++)
                    {
                        long t0 = Stopwatch.GetTimestamp();
                        rig.Form.Width = i % 2 == 0 ? 520 + i * 12 : 900 - i * 12;
                        rig.Pump();
                        resize.Add((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
                    }
                    Assert.Equal(builds, rig.Editor.IndexBuilds);

                    // Typing at the end of the note: one rebuild per change, and the strip keeps up.
                    var typing = new List<double>();
                    rig.Box.SelectionStart = rig.Box.TextLength;
                    for (int i = 0; i < 40; i++)
                    {
                        long t0 = Stopwatch.GetTimestamp();
                        rig.Box.SelectedText = i % 5 == 4 ? "\r\n" : "y";
                        rig.Pump();
                        typing.Add((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
                    }
                    // One scan per change - the edit reports a line break being typed as two changes.
                    int typedBuilds = rig.Editor.IndexBuilds - builds;
                    Assert.InRange(typedBuilds, 40, 48 + 4);

                    string Report(string what, List<double> samples)
                    {
                        var sorted = samples.OrderBy(v => v).ToList();
                        return $"{what}: n={sorted.Count} median {sorted[sorted.Count / 2]:0.00} ms, p95 {sorted[(int)(sorted.Count * 0.95)]:0.00} ms, max {sorted[sorted.Count - 1]:0.00} ms";
                    }
                    _output.WriteLine($"[{(wrap ? "wrap on" : "wrap off")}] machine: {Environment.ProcessorCount} logical cores, {Environment.OSVersion.VersionString}, " +
                        $"{(Environment.Is64BitProcess ? "x64" : "x86")} process, CLR {Environment.Version}; " +
                        $"text {Encoding_Utf8(text)} bytes, {lines} logical lines, {rowsTotal} rows; load+index {loaded} ms");
                    _output.WriteLine("  " + Report("scroll step incl. gutter repaint", scroll));
                    _output.WriteLine("  " + Report("window resize", resize));
                    _output.WriteLine("  " + Report("one typed change incl. index rebuild and repaint", typing));

                    Assert.True(scroll.OrderBy(v => v).ToList()[(int)(scroll.Count * 0.95)] < 40, "A scroll step must not feel slow (p95 under 40 ms).");
                    Assert.True(typing.OrderBy(v => v).ToList()[(int)(typing.Count * 0.95)] < 150, "One typed change must stay well inside a frame budget of the hook (p95 under 150 ms).");
                }
            });
        }

        private static int Encoding_Utf8(string s) => System.Text.Encoding.UTF8.GetByteCount(s);
    }
}

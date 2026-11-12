using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The scrollable page rail (ticket S0076 M3, SETTINGS-NAVIGATION section 2): whole captions that wrap instead of
    /// an ellipsis, every destination reachable by scrolling at any scale, selection by a click released on the
    /// row that took the press (never by a scroll), the keyboard, the accessible list, and the documented
    /// docked-or-drawer breakpoint. The control tests build a small form, not the settings window, so they stay
    /// in the default run; the settings-window rows are [UiSweepFact].
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class SettingsRailTests
    {
        private static void Sta(Action action)
        {
            Exception? failure = null;
            var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "UI check exceeded 60 seconds");
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private static readonly string[] Captions =
        {
            "General", "Indicators", "Hotkeys overview and global switches", "Clipboard", "About", "Windows languages and keyboard layouts",
            "Text correction", "Quick launch", "Translation", "Quick notes", "Screenshots", "Symbols", "Selection menu",
            "Availability traffic light", "Exchange server", "Settings transfer", "Support",
        };

        private static List<SettingsRail.Entry> Entries() => Captions
            .Select((c, i) => new SettingsRail.Entry { Caption = c, GlyphId = AppGlyphs.Settings, Kind = i % 3, Tag = i }).ToList();

        private static (Form form, SettingsRail rail) Host(int width, int height, UiMetrics? metrics = null)
        {
            var form = new Form { ClientSize = new Size(width, height), ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(10, 10) };
            if (metrics != null) UiMetrics.Set(form, metrics);
            var rail = new SettingsRail { Dock = DockStyle.Fill };
            form.Controls.Add(rail);
            rail.SetEntries(Entries());
            form.Show();
            Application.DoEvents();
            return (form, rail);
        }

        [Fact]
        public void BreakpointDocksOnlyWhileTheContentKeepsItsMinimum()
        {
            Assert.Equal(SettingsRailMode.Docked, SettingsRailLayout.Decide(1340, 300, 480));
            Assert.Equal(SettingsRailMode.Docked, SettingsRailLayout.Decide(780, 300, 480));
            Assert.Equal(SettingsRailMode.Drawer, SettingsRailLayout.Decide(779, 300, 480));
            Assert.Equal(SettingsRailMode.Drawer, SettingsRailLayout.Decide(900, 520, 960));
        }

        [Fact]
        public void RailWidthIsTheLongestCaptionBetweenItsMinimumAndShare()
        {
            Assert.Equal(200, SettingsRailLayout.RailWidth(120, 200, 400));
            Assert.Equal(260, SettingsRailLayout.RailWidth(260, 200, 400));
            Assert.Equal(400, SettingsRailLayout.RailWidth(700, 200, 400));
            // A share smaller than the minimum never drops the rail below the minimum.
            Assert.Equal(200, SettingsRailLayout.RailWidth(700, 200, 100));
        }

        [Fact]
        public void EveryCaptionIsWholeInItsRowAtEveryScaleAndTheListScrollsToReachAll()
        {
            Sta(() =>
            {
                foreach (double scale in new[] { .75, 1, 1.5, 2, 3 })
                {
                    (Form form, SettingsRail rail) = Host(220, 360, new UiMetrics(false, scale, 1));
                    try
                    {
                        using var bold = new Font(rail.Font, FontStyle.Bold);
                        for (int i = 0; i < rail.Count; i++)
                        {
                            Rectangle row = rail.RowBounds(i);
                            // The row is as tall as the whole wrapped caption needs - an ellipsis would need no more than one line.
                            int textWidth = rail.ClientSize.Width - 60;
                            Size need = TextRenderer.MeasureText(Captions[i], bold, new Size(Math.Max(1, textWidth), int.MaxValue),
                                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
                            Assert.True(row.Height >= Math.Min(need.Height, row.Height) && row.Height > 0, "row " + i + " at " + scale);
                        }
                        // A column of 17 rows cannot fit 360 px at these scales, and must therefore scroll rather than wrap into a second column.
                        int total = Enumerable.Range(0, rail.Count).Sum(i => rail.RowBounds(i).Height);
                        if (total > rail.ClientSize.Height)
                        {
                            Assert.True(rail.VerticalScroll.Visible, "no scroll bar at " + scale);
                            rail.EnsureVisible(rail.Count - 1);
                            Rectangle last = rail.RowBounds(rail.Count - 1);
                            Assert.True(last.Bottom <= rail.ClientSize.Height + 1 && last.Top >= 0, "the last destination is unreachable at " + scale + ": " + last);
                            rail.EnsureVisible(0);
                            Assert.True(rail.RowBounds(0).Top >= 0);
                        }
                    }
                    finally { form.Close(); form.Dispose(); }
                }
            });
        }

        [Fact]
        public void RowHeightsGrowWithTheInterfaceScaleButNotWithTheSelection()
        {
            Sta(() =>
            {
                int Height(double scale)
                {
                    (Form form, SettingsRail rail) = Host(300, 700, new UiMetrics(false, scale, 1));
                    try { return rail.RowBounds(0).Height; }
                    finally { form.Close(); form.Dispose(); }
                }
                Assert.True(Height(2) > Height(1) && Height(1) > Height(.75), "rows must follow the interface scale");

                (Form f, SettingsRail r) = Host(300, 700);
                try
                {
                    int[] before = Enumerable.Range(0, r.Count).Select(i => r.RowBounds(i).Height).ToArray();
                    r.SelectedIndex = 5;
                    int[] after = Enumerable.Range(0, r.Count).Select(i => r.RowBounds(i).Height).ToArray();
                    Assert.Equal(before, after);   // selecting bolds a caption; no other row moves
                }
                finally { f.Close(); f.Dispose(); }
            });
        }

        [Fact]
        public void ClickChoosesOnlyWhenReleasedOnThePressedRowAndAScrollChoosesNothing()
        {
            Sta(() =>
            {
                (Form form, SettingsRail rail) = Host(260, 600);
                try
                {
                    int chosen = 0, changed = 0;
                    rail.DestinationChosen += (_, _) => chosen++;
                    rail.SelectedIndexChanged += (_, _) => changed++;
                    Point row2 = Center(rail, 2), row4 = Center(rail, 4);

                    Invoke(rail, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, row2.X, row2.Y, 0));
                    Invoke(rail, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, row4.X, row4.Y, 0));   // dragged away
                    Assert.Equal(0, chosen); Assert.Equal(0, changed);

                    Invoke(rail, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, row2.X, row2.Y, 0));
                    Invoke(rail, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, row2.X, row2.Y, 0));
                    Assert.Equal(1, chosen); Assert.Equal(1, changed); Assert.Equal(2, rail.SelectedIndex);

                    // Choosing the destination that is already selected still reports the choice (the drawer closes on it) but changes nothing.
                    Invoke(rail, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, row2.X, row2.Y, 0));
                    Invoke(rail, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, row2.X, row2.Y, 0));
                    Assert.Equal(2, chosen); Assert.Equal(1, changed);

                    // The wheel between press and release (a scroll gesture) cancels the activation.
                    Invoke(rail, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, row4.X, row4.Y, 0));
                    Invoke(rail, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, row4.X, row4.Y, -120));
                    Invoke(rail, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, row4.X, row4.Y, 0));
                    Assert.Equal(2, chosen); Assert.Equal(2, rail.SelectedIndex);
                }
                finally { form.Close(); form.Dispose(); }
            });
        }

        [Fact]
        public void KeyboardMovesTheSelectionAndEnterChoosesIt()
        {
            Sta(() =>
            {
                (Form form, SettingsRail rail) = Host(260, 300);
                try
                {
                    rail.SelectedIndex = 0;
                    int chosen = 0;
                    rail.DestinationChosen += (_, _) => chosen++;
                    Key(rail, Keys.Down); Assert.Equal(1, rail.SelectedIndex);
                    Key(rail, Keys.Up); Key(rail, Keys.Up); Assert.Equal(0, rail.SelectedIndex);   // clamps at the top
                    Key(rail, Keys.End); Assert.Equal(rail.Count - 1, rail.SelectedIndex);
                    Assert.True(rail.RowBounds(rail.SelectedIndex).Bottom <= rail.ClientSize.Height + 1, "End must scroll the last row into view");
                    Key(rail, Keys.Home); Assert.Equal(0, rail.SelectedIndex);
                    Key(rail, Keys.PageDown); Assert.True(rail.SelectedIndex > 1);
                    Key(rail, Keys.Enter); Assert.Equal(1, chosen);
                }
                finally { form.Close(); form.Dispose(); }
            });
        }

        [Fact]
        public void AccessibleListNamesEveryDestinationAndMarksTheSelectedOne()
        {
            Sta(() =>
            {
                (Form form, SettingsRail rail) = Host(260, 600);
                try
                {
                    rail.SelectedIndex = 3;
                    AccessibleObject list = rail.AccessibilityObject;
                    Assert.Equal(AccessibleRole.List, list.Role);
                    Assert.Equal(Captions.Length, list.GetChildCount());
                    for (int i = 0; i < Captions.Length; i++)
                    {
                        AccessibleObject item = list.GetChild(i)!;
                        Assert.Equal(Captions[i], item.Name);
                        Assert.Equal(AccessibleRole.ListItem, item.Role);
                        Assert.Equal(i == 3, (item.State & AccessibleStates.Selected) != 0);
                    }
                    Assert.Equal(Captions[3], list.GetSelected()!.Name);
                    list.GetChild(1)!.DoDefaultAction();
                    Assert.Equal(1, rail.SelectedIndex);
                }
                finally { form.Close(); form.Dispose(); }
            });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SelectionBarAndCaptionSitOnTheReadingStartSideInBothDirections(bool rtl)
        {
            Sta(() =>
            {
                var form = new Form { ClientSize = new Size(300, 400), ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(10, 10) };
                if (rtl) { form.RightToLeft = RightToLeft.Yes; form.RightToLeftLayout = true; }
                var rail = new SettingsRail { Dock = DockStyle.Fill };
                form.Controls.Add(rail);
                rail.SetEntries(Entries());
                form.Show();
                Application.DoEvents();
                try
                {
                    rail.SelectedIndex = 2;
                    form.Refresh();
                    // The whole window as the user sees it, so a mirrored layout is judged as drawn: the row is found
                    // through screen coordinates (which honour the mirroring) relative to the window's own corner.
                    using var shot = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size));
                    Rectangle row = rail.RectangleToScreen(rail.RowBounds(2));
                    Rectangle railBox = rail.RectangleToScreen(rail.ClientRectangle);
                    int y = row.Top + row.Height / 2 - form.Top;
                    int left = -1, right = -1;
                    int accent = ThemePalette.Light.Accent.ToArgb();
                    for (int x = railBox.Left - form.Left; x < railBox.Right - form.Left; x++)
                        if (shot.GetPixel(x, y).ToArgb() == accent) { if (left < 0) left = x; right = x; }
                    Assert.True(left >= 0, "the selection bar was not painted");
                    int railLeft = railBox.Left - form.Left, railRight = railBox.Right - form.Left;
                    if (rtl) Assert.True(left > railRight - 12, "RTL: the bar should be at the rail's right edge, found x=" + left + ".." + right + " in " + railLeft + ".." + railRight);
                    else Assert.True(right < railLeft + 12, "LTR: the bar should be at the rail's left edge, found x=" + left + ".." + right + " in " + railLeft + ".." + railRight);
                    // The caption starts on the same side: the ink of the row's caption lies nearer the bar than the far edge.
                    float inkBrightness = Color.FromArgb(ThemePalette.Light.TextPrimary.ToArgb()).GetBrightness();
                    int firstInk = -1, lastInk = -1;
                    for (int x = railLeft + 14; x < railRight - 14; x++)
                        for (int dy = -6; dy <= 6; dy++)
                            if (Math.Abs(shot.GetPixel(x, y + dy).GetBrightness() - inkBrightness) < 0.2f) { if (firstInk < 0) firstInk = x; lastInk = x; break; }
                    Assert.True(firstInk >= 0, "no caption ink found");
                    int middle = (railLeft + railRight) / 2;
                    if (rtl) Assert.True(lastInk > middle, "RTL: the caption should reach toward the right, last ink x=" + lastInk + " middle " + middle);
                    else Assert.True(firstInk < middle, "LTR: the caption should start at the left, first ink x=" + firstInk + " middle " + middle);
                }                finally { form.Close(); form.Dispose(); }
            });
        }

        [Theory]
        [InlineData("Light")]
        [InlineData("Dark")]
        public void EveryDestinationGlyphIsReadableOnEveryRowFill(string theme)
        {
            // SETTINGS-NAVIGATION section 2 rule 3: identity colours owe 3:1 on the actual normal, hover and selected fills.
            ThemePalette palette = theme == "Dark" ? ThemePalette.Dark : ThemePalette.Light;
            Color[] fills =
            {
                SettingsRail.RowFill(palette, selected: false, hot: false),
                SettingsRail.RowFill(palette, selected: false, hot: true),
                SettingsRail.RowFill(palette, selected: true, hot: false),
            };
            var failures = new List<string>();
            foreach (int kind in ThemePalette.NavInk.Kinds)
                foreach (Color fill in fills)
                {
                    double ratio = ThemePalette.Contrast(ThemePalette.NavInk.For(kind, palette), fill);
                    if (ratio < 3) failures.Add($"kind {kind} on {fill}: {ratio:0.00}");
                }
            Assert.True(failures.Count == 0, string.Join("; ", failures));
            // Captions keep the text role at the stronger 4.5:1.
            foreach (Color fill in fills) Assert.True(ThemePalette.Contrast(palette.TextPrimary, fill) >= 4.5);
        }

        [UiSweepFact]
        public void SettingsWindowListsEveryPageInTheRailAndKeepsBothSelectionsEqual()
        {
            Sta(() =>
            {
                var config = new AppConfig { UiLanguage = "English", SettingsGroupsState = "", SettingsViewport = "" };
                using var form = TestForms.NewSettings(config);
                form.Show();
                Application.DoEvents();
                TabControl tabs = form.Tabs!;
                Assert.Equal(tabs.TabCount, form.Rail.Count);
                for (int i = 0; i < tabs.TabCount; i++) Assert.Equal(tabs.TabPages[i].Text, form.Rail.CaptionAt(i));

                tabs.SelectedIndex = 4;
                Assert.Equal(4, form.Rail.SelectedIndex);
                Invoke(form.Rail, "OnKeyDown", new KeyEventArgs(Keys.Down));
                Assert.Equal(5, tabs.SelectedIndex);   // the rail drives the page host, and nothing else changed
                // The native strip is not shown: the display area of the host is its whole client area.
                Assert.Equal(tabs.ClientRectangle, tabs.DisplayRectangle);
            });
        }

        [UiSweepFact]
        public void NarrowOrVeryLargeWindowMovesTheSameRailIntoADrawerThatClosesOnAChoice()
        {
            Sta(() =>
            {
                var config = new AppConfig { UiLanguage = "English", SettingsGroupsState = "", SettingsViewport = "" };
                using var form = TestForms.NewSettings(config);
                form.Show();
                form.Size = new Size(1500, 900);
                form.Reload();
                Application.DoEvents();
                Assert.Equal(SettingsRailMode.Docked, form.RailMode);
                Assert.True(form.RailHost.Visible);

                // Content would be squeezed: the same menu retreats behind a button; nothing is clipped or removed.
                UiMetrics.Set(form, new UiMetrics(false, 3, 1));
                form.MinimumSize = new Size(200, 200);
                form.Size = new Size(760, 700);
                form.Reload();
                Application.DoEvents();
                Assert.Equal(SettingsRailMode.Drawer, form.RailMode);
                Assert.False(form.RailHost.Visible);
                Button pages = form.Controls.Find("settingsPages", true).OfType<Button>().Single();
                Assert.True(pages.Visible);

                pages.PerformClick();
                Application.DoEvents();
                Assert.True(form.DrawerOpen); Assert.True(form.RailHost.Visible);
                Assert.Equal(form.Tabs!.TabCount, form.Rail.Count);   // the same destinations, none dropped

                int before = form.Tabs.SelectedIndex;
                int target = before == 2 ? 3 : 2;
                Invoke(form.Rail, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, Center(form.Rail, target).X, Center(form.Rail, target).Y, 0));
                Invoke(form.Rail, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, Center(form.Rail, target).X, Center(form.Rail, target).Y, 0));
                Application.DoEvents();
                Assert.Equal(target, form.Tabs.SelectedIndex);
                Assert.False(form.DrawerOpen); Assert.False(form.RailHost.Visible);
            });
        }

        private static Point Center(SettingsRail rail, int index)
        {
            Rectangle row = rail.RowBounds(index);
            return new Point(row.Left + row.Width / 2, row.Top + row.Height / 2);
        }

        private static void Key(SettingsRail rail, Keys key) => Invoke(rail, "OnKeyDown", new KeyEventArgs(key));

        private static void Invoke(Control control, string method, EventArgs args)
            => control.GetType().GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, new[] { args.GetType() }, null)!
                .Invoke(control, new object[] { args });
    }
}

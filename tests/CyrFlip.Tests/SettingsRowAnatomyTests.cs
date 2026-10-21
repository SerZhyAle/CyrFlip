using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The row anatomy of the settings window (ticket S0060 P6, WINDOWS-UI section 4): a value row keeps its caption
    /// and its description in the leading column and its editors in the trailing column of the same row; a boolean
    /// row puts the description under the caption's own text; a command's description starts under the command's
    /// caption; the editors of one page stand on one edge; and no row leaves the page. Every gate runs on the real
    /// window, shown, in the source language, in a long one and in a right-to-left one.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class SettingsRowAnatomyTests
    {
        private static readonly string[] Languages = { "English", "Deutsch", "العربية" };

        internal static AppConfig Config(string language)
            => new AppConfig { UiLanguage = language, SettingsGroupsState = "", SettingsViewport = "", SettingsPageId = "", SettingsTab = 0 };

        /// <summary>The window built, shown and sized the way a default first run is, so every page can be laid out by selecting it.</summary>
        internal static SettingsForm Open(string language, AppConfig? config = null)
        {
            SettingsForm form = TestForms.NewSettings(config ?? Config(language));
            form.Show();
            form.Size = new Size(
                Math.Min(1340, SystemInformation.MaxWindowTrackSize.Width),
                Math.Min(900, SystemInformation.MaxWindowTrackSize.Height));
            Application.DoEvents();
            return form;
        }

        internal static void Select(SettingsForm form, TabPage page)
        {
            form.Tabs!.SelectedTab = page;
            Application.DoEvents();
        }

        internal static IEnumerable<SettingsForm.CaptionedRow> RowsOf(Control root)
        {
            foreach (Control child in root.Controls)
            {
                if (child is TableLayoutPanel { Tag: SettingsForm.CaptionedRow row }) yield return row;
                foreach (SettingsForm.CaptionedRow deeper in RowsOf(child)) yield return deeper;
            }
        }

        private static Rectangle Screen(Control control) => control.RectangleToScreen(control.ClientRectangle);

        // ---- WINDOWS-UI section 4, "Ordinary row" and "Boolean" ----

        [Fact]
        public void EveryCaptionedRowKeepsItsCaptionAndDescriptionInTheLeadingColumn()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                var problems = new List<string>();
                int rows = 0, booleans = 0;
                foreach (string language in Languages)
                {
                    using SettingsForm form = Open(language);
                    bool rtl = Localization.IsRightToLeft(language);
                    foreach (TabPage page in form.Tabs!.TabPages)
                    {
                        Select(form, page);
                        foreach (SettingsForm.CaptionedRow row in RowsOf(page).Where(r => r.Table.Visible))
                        {
                            rows++;
                            string name = $"[{language}] {page.Text}: \"{row.Caption.Text}\"";
                            Rectangle table = Screen(row.Table);
                            int lead = (int)row.Table.ColumnStyles[0].Width;
                            // The leading column is the reading-start one: the left in English, the right in Arabic.
                            Rectangle leading = rtl
                                ? Rectangle.FromLTRB(table.Right - lead, table.Top, table.Right, table.Bottom)
                                : Rectangle.FromLTRB(table.Left, table.Top, table.Left + lead, table.Bottom);
                            Rectangle caption = Screen(row.Caption), hint = Screen(row.Hint), editors = Screen(row.Editors);

                            if (row.Hint.Text.Trim().Length == 0) problems.Add(name + " has an empty description");
                            if (hint.Left < leading.Left - 1 || hint.Right > leading.Right + 1)
                                problems.Add($"{name}: the description {hint} leaves the leading column {leading}");
                            if (caption.Left < leading.Left - 1 || caption.Right > leading.Right + 1)
                                problems.Add($"{name}: the caption {caption} leaves the leading column {leading}");
                            // The editors are beside both, in the trailing column of the same row.
                            if (rtl ? editors.Right > leading.Left + 1 : editors.Left < leading.Right - 1)
                                problems.Add($"{name}: the editors {editors} are not in the trailing column (leading {leading})");
                            if (hint.Top < caption.Bottom - 1) problems.Add($"{name}: the description is not under the caption");

                            if (row.Caption is CheckBox box)
                            {
                                booleans++;
                                // The description stands under the caption's text, not under its box.
                                int inset = SettingsForm.CaptionTextInset(box);
                                int offset = rtl ? caption.Right - hint.Right : hint.Left - caption.Left;
                                if (offset < inset - 2) problems.Add($"{name}: the description starts {offset} px in, the caption's text {inset} px in");
                            }
                        }
                    }
                }
                Assert.True(rows >= 45, "only " + rows + " captioned rows were measured - the gate is looking at the wrong thing");
                Assert.True(booleans >= 20, "only " + booleans + " boolean rows with editors were measured");
                Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct().Take(30)));
            });
        }

        [Fact]
        public void TheEditorsOfOnePageStandOnOneEdge()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                var problems = new List<string>();
                int measured = 0;
                foreach (string language in Languages)
                {
                    using SettingsForm form = Open(language);
                    bool rtl = Localization.IsRightToLeft(language);
                    foreach (TabPage page in form.Tabs!.TabPages)
                    {
                        Select(form, page);
                        FlowLayoutPanel flow = (FlowLayoutPanel)page.Controls[0];
                        var edges = new List<(string Caption, int Edge)>();
                        foreach (SettingsForm.CaptionedRow row in RowsOf(page).Where(r => r.Table.Visible))
                        {
                            // The edge the editors start from: the left in English, the right in Arabic.
                            Point at = flow.PointToClient(row.Editors.PointToScreen(new Point(rtl ? row.Editors.Width : 0, 0)));
                            edges.Add((row.Caption.Text, at.X));
                        }
                        measured += edges.Count;
                        if (edges.Count > 1 && edges.Max(e => e.Edge) - edges.Min(e => e.Edge) > 1)
                            problems.Add($"[{language}] {page.Text}: editors start at " + string.Join(", ", edges.Select(e => e.Edge + " (" + e.Caption + ")")));
                    }
                }
                Assert.True(measured >= 45, "only " + measured + " rows were measured");
                Assert.True(problems.Count == 0, string.Join("\n", problems));
            });
        }

        [Fact]
        public void NoCaptionedRowLeavesItsPageWhenThePageCanHoldIt()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                var problems = new List<string>();
                int measured = 0;
                foreach (string language in Languages)
                {
                    using SettingsForm form = Open(language);
                    foreach (TabPage page in form.Tabs!.TabPages)
                    {
                        Select(form, page);
                        FlowLayoutPanel flow = (FlowLayoutPanel)page.Controls[0];
                        int pageWidth = flow.ClientSize.Width - flow.Padding.Horizontal;
                        foreach (SettingsForm.CaptionedRow row in RowsOf(page).Where(r => r.Table.Visible))
                        {
                            measured++;
                            Rectangle table = Screen(row.Table);
                            Rectangle page0 = Screen(flow);
                            // The row can fit only if its floor does: the leading column never goes under
                            // MinTextWidth, and the editors are what they are. A page narrower than that scrolls.
                            int floor = SettingsForm.MinTextWidth + row.Editors.GetPreferredSize(Size.Empty).Width + 40;
                            if (floor > pageWidth) continue;
                            if (table.Left < page0.Left - 1 || table.Right > page0.Right + 1)
                                problems.Add($"[{language}] {page.Text}: \"{row.Caption.Text}\" spans {table.Left}..{table.Right}, the page {page0.Left}..{page0.Right}");
                        }
                    }
                }
                Assert.True(measured >= 45, "only " + measured + " rows were measured");
                Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct().Take(30)));
            });
        }

        private static readonly (string Field, string Reason)[] ChordRows =
        {
            ("_caseEnabled", "the case fix"), ("_historyEnabled", "the history chord"), ("_textMenuHotkeyEnabled", "the text menu's keyboard chord"),
            ("_quickNotesHotkeyEnabled", "the quick notes chord"), ("_screenshotHotkeyEnabled", "the capture chord"),
            ("_screenshotFolderHotkeyEnabled", "the screenshots folder chord"), ("_symbolsHotkeyEnabled", "the symbols chord"),
            ("_tlHotkeyEnabled", "the traffic light chord"),
        };

        [Fact]
        public void EveryChordRowIsACaptionedBooleanWithTheChordAndItsButtonInTheEditorColumn()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                using SettingsForm form = TestForms.NewSettings(Config("English"));
                var problems = new List<string>();
                foreach ((string field, string what) in ChordRows)
                {
                    var box = (CheckBox)typeof(SettingsForm).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
                    SettingsForm.CaptionedRow? row = null;
                    for (Control? up = box.Parent; up != null && row == null; up = up.Parent)
                        if (up is TableLayoutPanel { Tag: SettingsForm.CaptionedRow found }) row = found;
                    if (row == null) { problems.Add(what + ": not in a captioned row"); continue; }
                    if (!ReferenceEquals(row.Caption, box)) problems.Add(what + ": the switch is not the row's caption");
                    if (!row.Editors.Controls.OfType<Label>().Any()) problems.Add(what + ": no chord label in the editor column");
                    if (!row.Editors.Controls.OfType<Button>().Any()) problems.Add(what + ": no Change button in the editor column");
                    if (row.Hint.Text.Length == 0) problems.Add(what + ": no description");
                    if (!form.HintPairs.Any(pair => ReferenceEquals(pair.Key, box) && ReferenceEquals(pair.Value, row.Hint)))
                        problems.Add(what + ": its description is not registered with the measured inset");
                    if (box.Parent == row.Editors) problems.Add(what + ": the switch stands in the editor column");
                }
                Assert.True(problems.Count == 0, string.Join("\n", problems));
            });
        }

        [Fact]
        public void TheScreenshotFolderIsACaptionedRowWithADescription()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                using SettingsForm form = TestForms.NewSettings(Config("English"));
                var box = (TextBox)typeof(SettingsForm).GetField("_screenshotFolderBox", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
                SettingsForm.CaptionedRow? row = null;
                for (Control? up = box.Parent; up != null && row == null; up = up.Parent)
                    if (up is TableLayoutPanel { Tag: SettingsForm.CaptionedRow found }) row = found;
                Assert.NotNull(row);
                Assert.Equal("Folder for screen region captures", row!.Caption.Text);
                Assert.Contains("Screenshots", Localization.Translate("English", row.Hint.Text));
                Assert.True(RegroupingHarness.Walk(row.Editors).Contains(box), "the box is not in the editor column");
                Assert.True(RegroupingHarness.Walk(row.Editors).OfType<Button>().Count() == 2, "Choose and Open belong to the editor column");
            });
        }

        // ---- WINDOWS-UI section 4, "Commands": a command is not a value row; its description starts under its caption ----

        [Fact]
        public void ACommandsDescriptionStartsUnderTheCommandsCaptionText()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                var problems = new List<string>();
                int measured = 0;
                foreach (string language in Languages)
                {
                    using SettingsForm form = Open(language);
                    bool rtl = Localization.IsRightToLeft(language);
                    foreach (TabPage page in form.Tabs!.TabPages)
                    {
                        Select(form, page);
                        foreach (KeyValuePair<Control, Label> pair in form.HintPairs)
                        {
                            if (pair.Key is not Button button || !OwnPage(button, page) || !button.Visible || !pair.Value.Visible) continue;
                            if (!ButtonGlyphs.IsAttached(button)) continue;
                            measured++;
                            Rectangle face = Screen(button), hint = Screen(pair.Value);
                            using var graphics = button.CreateGraphics();
                            Rectangle glyph = ButtonGlyphs.GlyphBox(button, graphics);
                            int gap = (int)Math.Round(ButtonGlyphs.GapLogical * ButtonGlyphs.Dpi(button) / 96.0);
                            // Where the caption's text starts: after the glyph and its gap - or before them, mirrored.
                            int text = rtl ? face.Left + glyph.Left - gap : face.Left + glyph.Right + gap;
                            int actual = rtl ? hint.Right : hint.Left;
                            if (Math.Abs(actual - text) > 32)
                                problems.Add($"[{language}] {page.Text}: \"{button.Text}\" - its text starts at {text}, the description at {actual}");
                        }
                    }
                }
                Assert.True(measured >= 8, "only " + measured + " command rows were measured");
                Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct().Take(20)));
            });
        }

        private static bool OwnPage(Control control, TabPage page)
        {
            for (Control? up = control; up != null; up = up.Parent)
                if (ReferenceEquals(up, page)) return true;
            return false;
        }

        // ---- WINDOWS-UI section 5: the command metric ----

        [Fact]
        public void EveryCommandButtonHasTheSharedHeightAndPaddingAndTheirGapIsTheMetric()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                var problems = new List<string>();
                int buttons = 0, neighbours = 0;
                foreach (string language in Languages)
                {
                    using SettingsForm form = Open(language);
                    bool rtl = Localization.IsRightToLeft(language);
                    foreach (TabPage page in form.Tabs!.TabPages)
                    {
                        Select(form, page);
                        foreach (Control control in RegroupingHarness.Walk(page))
                        {
                            if (control is not Button button || !button.Visible || !ButtonGlyphs.IsAttached(button)) continue;
                            buttons++;
                            double scale = ButtonGlyphs.Dpi(button) / 96.0;
                            int floor = (int)Math.Round(ButtonGlyphs.CommandHeightLogical * scale);
                            string name = $"[{language}] {page.Text}: \"{button.Text}\"";
                            if (button.Height < floor) problems.Add($"{name} is {button.Height} px high, the metric {floor}");
                            if (button.Text.Length > 0)
                            {
                                int side = (int)Math.Round(ButtonGlyphs.CommandPaddingLogical * scale);
                                int near = rtl ? button.Padding.Right : button.Padding.Left, far = rtl ? button.Padding.Left : button.Padding.Right;
                                if (far != side) problems.Add($"{name} has {far} px of padding on its far side, the metric {side}");
                                if (near < side + ButtonGlyphs.GlyphLogical) problems.Add($"{name} reserves {near} px for its glyph side");
                            }
                        }

                        // Buttons standing side by side in a strip: one height, and the gap is the metric.
                        foreach (Control host in RegroupingHarness.Walk(page).Prepend(page).Where(c => c is FlowLayoutPanel))
                        {
                            Button[] strip = host.Controls.OfType<Button>().Where(b => b.Visible && ButtonGlyphs.IsAttached(b)).ToArray();
                            for (int i = 0; i + 1 < strip.Length; i++)
                            {
                                Rectangle a = Screen(strip[i]), b = Screen(strip[i + 1]);
                                if (Math.Abs((a.Top + a.Bottom) - (b.Top + b.Bottom)) > 2) continue;   // wrapped to another line
                                neighbours++;
                                if (a.Height != b.Height) problems.Add($"[{language}] {page.Text}: \"{strip[i].Text}\" is {a.Height} px, \"{strip[i + 1].Text}\" {b.Height}");
                                int gap = rtl ? a.Left - b.Right : b.Left - a.Right;
                                int expected = (int)Math.Round(ButtonGlyphs.CommandGapLogical * ButtonGlyphs.Dpi(strip[i]) / 96.0);
                                if (Math.Abs(gap - expected) > 1)
                                    problems.Add($"[{language}] {page.Text}: \"{strip[i].Text}\" and \"{strip[i + 1].Text}\" are {gap} px apart, the metric {expected}");
                            }
                        }
                    }
                }
                Assert.True(buttons >= 60, "only " + buttons + " command buttons were measured");
                Assert.True(neighbours >= 10, "only " + neighbours + " neighbouring pairs were measured");
                Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct().Take(30)));
            });
        }

        [Fact]
        public void TheGlyphOnlyArrowsAreSquaresOfTheCommandHeight()
        {
            // The scaled value is the window's DeviceDpi, which on a test thread is the process DPI: what a monitor
            // move changes is held by the message-path gate of SettingsDpiGeometryTests, and the live move is OWED.
            RegroupingHarness.OnUiThread(() =>
            {
                using SettingsForm form = Open("English");
                var arrows = RegroupingHarness.Walk(form).OfType<Button>()
                    .Where(b => b.Text.Length == 0 && b.Image != null && !b.AutoSize).ToList();
                Assert.True(arrows.Count >= 6, "only " + arrows.Count + " arrows were found");
                int edge = ControlDpi.Scale(form.DeviceDpi, ButtonGlyphs.CommandHeightLogical);
                Assert.True(edge >= 32);
                foreach (Button arrow in arrows)
                    Assert.True(arrow.Width == edge && arrow.Height == edge, $"\"{arrow.AccessibleName}\" is {arrow.Width}x{arrow.Height}, the metric {edge}");
            });
        }

        // ---- WINDOWS-UI section 4: a drop-down is as wide as its longest entry, and stands level with its neighbours ----

        [UiSweepFact]
        public void NoDropDownIsNarrowerThanItsWidestEntryAndSitsOnItsRowsCentreLine()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                var problems = new List<string>();
                int combos = 0, levelled = 0;
                foreach (string language in Localization.Names)
                {
                    using SettingsForm form = Open(language);
                    foreach (TabPage page in form.Tabs!.TabPages)
                    {
                        Select(form, page);
                        foreach (ComboBox combo in RegroupingHarness.Walk(page).OfType<ComboBox>())
                        {
                            // The width rule is about a fixed list; an editable box (the translator's model name) takes
                            // whatever is typed into it, but it still stands beside its button on the same centre line.
                            if (combo.DropDownStyle == ComboBoxStyle.DropDownList && combo.Items.Count > 0)
                            {
                                combos++;
                                int widest = combo.Items.Cast<object>().Max(item => TextRenderer.MeasureText(item.ToString() ?? "", combo.Font).Width);
                                int needed = widest + SystemInformation.VerticalScrollBarWidth;
                                if (combo.Width < needed) problems.Add($"[{language}] {page.Text}: a drop-down is {combo.Width} px, its widest entry needs {needed}");
                            }

                            // Level with whatever shares its strip - the buttons of the row are 32 px, the list as high
                            // as its font - so the pair stands on one centre line.
                            if (combo.Parent is FlowLayoutPanel strip && combo.Visible)
                            {
                                foreach (Button neighbour in strip.Controls.OfType<Button>().Where(b => b.Visible))
                                {
                                    levelled++;
                                    Rectangle a = Screen(combo), b = Screen(neighbour);
                                    int off = Math.Abs((a.Top + a.Bottom) / 2 - (b.Top + b.Bottom) / 2);
                                    if (off > 1) problems.Add($"[{language}] {page.Text}: a drop-down and \"{neighbour.Text}\" are {off} px off one centre line");
                                }
                            }
                        }
                    }
                }
                Assert.True(combos >= 5 * Localization.Names.Length / 2, "only " + combos + " drop-downs were measured");
                Assert.True(levelled >= 1, "no drop-down shares a strip with a command - the centre-line gate saw nothing");
                Assert.True(problems.Count == 0, string.Join("\n", problems.Distinct().Take(30)));
            });
        }

        // ---- S0060 P7 (c): the scenario table's headers are measured, not literals ----

        [UiSweepFact]
        public void NoLauncherColumnHeaderIsClippedInAnyLanguageAtOneAndOneAndAHalfTimesTheFont()
        {
            // A 150 % display makes the same font one and a half times as large; the pixel measurement that would
            // follow a real monitor is the window's DeviceDpi, which a test thread cannot move - the font is what
            // the headers' measurement reads, and the live monitor move stays OWED.
            RegroupingHarness.OnUiThread(() =>
            {
                var problems = new List<string>();
                int measured = 0;
                foreach (string language in Localization.Names)
                {
                    AppConfig config = Config(language);
                    config.EnableScenarioLauncher = true;
                    using SettingsForm form = TestForms.NewSettings(config);
                    var list = (ListView)typeof(SettingsForm).GetField("_launcherList", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
                    Font original = form.Font;
                    foreach (float factor in new[] { 1f, 1.5f })
                    {
                        using var scaled = new Font(original.FontFamily, original.SizeInPoints * factor);
                        form.Font = scaled;
                        // A different switch value makes the table rebuild itself in the new font (its stamp changed).
                        config.EnableScenarioLauncher = !config.EnableScenarioLauncher;
                        form.Reload();
                        string[] heads = form.LauncherColumnHeads();
                        Assert.Equal(heads.Length, list.Columns.Count);
                        for (int i = 0; i < heads.Length; i++)
                        {
                            measured++;
                            int needed = TextRenderer.MeasureText(heads[i], list.Font).Width + 12;
                            if (list.Columns[i].Width < needed)
                                problems.Add($"[{language} x{factor}] column \"{heads[i]}\" is {list.Columns[i].Width} px, its header needs {needed}");
                        }
                        form.Font = original;
                    }
                }
                Assert.True(measured >= 6 * Localization.Names.Length * 2);
                Assert.True(problems.Count == 0, string.Join("\n", problems));
            });
        }

        [Fact]
        public void TheLauncherColumnsAreNotLiteralsAnyMore()
        {
            string body = MethodBody(SettingsSource(), "private void ReloadLauncherRows()");
            Assert.DoesNotMatch(new Regex(@"Columns\.Add\([^;]*Column\(\d+\)"), body);
            Assert.Contains("MeasureLauncherColumns(", body);
        }

        // ---- S0060 M14: the search says where a result lives, and finds only what can be changed or run ----

        [Fact]
        public void EachSearchResultCarriesItsPageAndGroupBesideItsCaption()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                using SettingsForm form = TestForms.NewSettings(Config("English"));
                List<SettingsForm.SettingsSearchHit> index = form.BuildSearchIndex();
                Assert.True(index.Count > 40);
                Assert.True(index.Any(hit => hit.GroupCaption.Length > 0), "no grouped result - the gate sees only flat pages");
                foreach (SettingsForm.SettingsSearchHit hit in index)
                {
                    Assert.StartsWith(hit.PageTitle, hit.Context);
                    if (hit.GroupCaption.Length > 0) Assert.EndsWith(" / " + hit.GroupCaption, hit.Context);
                    else Assert.Equal(hit.PageTitle, hit.Context);
                    // What a screen reader reads for the list item: the caption, then where it lives.
                    Assert.Equal(hit.Caption + " - " + hit.Context, hit.ToString());
                }

                using var dialog = new SettingsForm.SettingsSearchDialog(form);
                ListBox results = dialog.ResultsForTest;
                Assert.Equal(DrawMode.OwnerDrawFixed, results.DrawMode);
                Assert.True(results.ItemHeight >= form.Font.Height * 2, "a two-line result needs two lines of height");
                Assert.True(results.Items.Count > 40);
                Assert.True(results.Items.Cast<object>().All(item => item is SettingsForm.SettingsSearchHit));
                Assert.Contains(results.Items.Cast<object>(), item => (item.ToString() ?? "").Contains(" / "));
            });
        }

        [Fact]
        public void TheIndexHoldsOnlyRowsWithAnEditorOrACommand()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                using SettingsForm form = TestForms.NewSettings(Config("English"));
                List<SettingsForm.SettingsSearchHit> index = form.BuildSearchIndex();
                var prose = new List<string>();
                var proseControls = new HashSet<Control>();
                foreach (TabPage page in form.Tabs!.TabPages)
                    foreach (Label label in ((FlowLayoutPanel)page.Controls[0]).Controls.OfType<Label>())
                    {
                        if (label.Text.Length > 0) prose.Add(label.Text);
                        proseControls.Add(label);
                    }
                foreach (SettingsForm.SettingsSearchHit hit in index)
                {
                    Assert.True(SettingsForm.CarriesEditorOrCommand(hit.Row), $"'{hit.Caption}' ({hit.PageTitle}) has no editor and no command");
                    Assert.False(proseControls.Contains(hit.Row), "a page title or a purpose line is not a setting: " + hit.Caption);
                    foreach (Label note in form.DependencyNotes)
                        Assert.False(ReferenceEquals(hit.Row, note) || RegroupingHarness.Walk(hit.Row).Contains(note), "a dependency note is a result");
                }
                // The guard: the old index took every label, so the pages' own prose was findable.
                Assert.Contains(prose, text => text.Length > 0);
                Assert.False(SettingsForm.CarriesEditorOrCommand(new Label { Text = "A sentence" }));
                Assert.True(SettingsForm.CarriesEditorOrCommand(new FlowLayoutPanel { Controls = { new Label(), new CheckBox() } }));
            });
        }

        [Fact]
        public void TheSecondLineOfAResultStaysReadableInEveryPalette()
        {
            foreach (ThemePalette palette in new[] { ThemePalette.Light, ThemePalette.Dark, ThemePalette.HighContrast })
            {
                foreach (Color face in new[] { palette.Control, palette.SurfaceWindow, palette.SurfaceRaised })
                {
                    Color ink = SettingsForm.SettingsSearchDialog.ContextInk(palette, palette.TextPrimary, face, selected: false);
                    Assert.True(ThemePalette.Contrast(ink, face) >= 4.5,
                        $"{palette}: the context line holds {ThemePalette.Contrast(ink, face):F2}:1 on {face}");
                }
                // The selected row's second line is its own ink: the highlight would not hold a muted grey.
                Assert.Equal(palette.AccentInk, SettingsForm.SettingsSearchDialog.ContextInk(palette, palette.AccentInk, palette.Accent, selected: true));
            }
        }

        // ---- S0060 P7 (j), (e): asking before the backup, no silent failure ----

        private static string SettingsSource()
            => File.ReadAllText(Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "CyrFlip", "SettingsForm.cs")));

        /// <summary>The text of one method of SettingsForm.cs: from its signature to the next member at the same indent.</summary>
        internal static string MethodBody(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, "no method '" + signature + "' in SettingsForm.cs");
            Match next = new Regex(@"^        (private|public|internal|protected)[^=;]*\(", RegexOptions.Multiline)
                .Match(source, start + signature.Length);
            return source.Substring(start, (next.Success ? next.Index : source.Length) - start);
        }

        [Fact]
        public void RemovingAnOrphanedShortcutAsksBeforeTheBackupIsTaken()
        {
            string body = MethodBody(SettingsSource(), "private void RemoveOrphanHotkey(int id)");
            int ask = body.IndexOf("ConfirmDialog.Show", StringComparison.Ordinal);
            int backup = body.IndexOf("EnsureSystemBackups()", StringComparison.Ordinal);
            Assert.True(ask >= 0 && backup >= 0 && ask < backup,
                "a Cancel must not have written the backup: the question has to come first (S0060 U-L1)");
        }

        [Fact]
        public void NoShortcutOrAddressFailureIsSwallowedSilently()
        {
            string source = SettingsSource();
            var swallowed = new Regex(@"catch\s*(\([^)]*\))?\s*\{\s*(/\*.*?\*/\s*)?\}", RegexOptions.Singleline);
            foreach (string signature in new[] { "private void SetShortcut(", "private void OpenAddress(", "private LinkLabel Link(" })
            {
                string body = MethodBody(source, signature);
                Assert.False(swallowed.IsMatch(body), signature + " swallows a failure with an empty catch (S0060 e)");
            }
            // And what it does instead names the cause and offers the actions.
            Assert.Contains("Fail(", MethodBody(source, "private void SetShortcut("));
            Assert.Contains("Fail(", MethodBody(source, "private void OpenAddress("));
            Assert.Contains("OpenAddress(url)", MethodBody(source, "private LinkLabel Link("));
        }

        // ---- S0060 U-L6: a field being typed in is not overwritten by a Reload ----

        [Fact]
        public void AReloadDoesNotTypeOverATranslatorFieldTheUserIsEditing()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                var config = Config("English");
                config.TranslateEndpoint = "http://stored:11434";
                config.TranslateModel = "stored-model";
                using SettingsForm form = TestForms.NewSettings(config);
                var endpoint = (TextBox)typeof(SettingsForm).GetField("_translateEndpoint", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
                var model = (ComboBox)typeof(SettingsForm).GetField("_translateModel", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
                Assert.Equal("http://stored:11434", endpoint.Text);
                Assert.Equal("stored-model", model.Text);

                // The user types: the config still holds the old value until Leave.
                endpoint.Text = "http://typed:11434";
                model.Text = "typed-model";
                config.TranslateEndpoint = "http://changed-elsewhere:11434";   // a background result, an import, another page
                form.Reload();
                Assert.Equal("http://typed:11434", endpoint.Text);
                Assert.Equal("typed-model", model.Text);

                // Once committed (Leave), the field is the config's again and follows it.
                typeof(SettingsForm).GetMethod("CommitTranslateText", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null);
                Assert.Equal("http://typed:11434", config.TranslateEndpoint);
                config.TranslateEndpoint = "http://elsewhere-again:11434";
                form.Reload();
                Assert.Equal("http://elsewhere-again:11434", endpoint.Text);
            });
        }

        [Fact]
        public void AFocusedTranslatorFieldIsTheUsersToo()
        {
            Assert.False(SettingsForm.IsBeingEdited(new TextBox(), uncommitted: false));
            Assert.True(SettingsForm.IsBeingEdited(new TextBox(), uncommitted: true));
        }

        // ---- S0060 M11: Escape ----

        [Fact]
        public void EscapeLeavesTheWindowUnlessADropDownIsOpen()
        {
            Assert.True(SettingsForm.EscapeLeavesWindow(null));
            Assert.True(SettingsForm.EscapeLeavesWindow(new TextBox()));
            Assert.True(SettingsForm.EscapeLeavesWindow(new Button()));
            Assert.True(SettingsForm.EscapeLeavesWindow(new ComboBox()));   // closed
        }

        [Fact]
        public void AnOpenDropDownTakesTheEscapeAndTheWindowStaysThenLeavesOnTheNext()
        {
            RegroupingHarness.OnUiThread(() =>
            {
                using SettingsForm form = Open("English");
                var combo = (ComboBox)typeof(SettingsForm).GetField("_themeCombo", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
                Select(form, RegroupingHarness.PageOf(form, SettingsPageKey.General));
                combo.Focus();
                Application.DoEvents();
                MethodInfo processCmdKey = typeof(SettingsForm).GetMethod("ProcessCmdKey", BindingFlags.NonPublic | BindingFlags.Instance)!;

                combo.DroppedDown = true;
                Assert.True(combo.DroppedDown, "the list did not open - the test cannot say anything");
                Assert.False(SettingsForm.EscapeLeavesWindow(form.ActiveControl));
                var message = new Message();
                object[] args = { message, Keys.Escape };
                processCmdKey.Invoke(form, args);
                Assert.True(form.Visible, "Escape over an open drop-down hid the window");

                combo.DroppedDown = false;
                Assert.True(SettingsForm.EscapeLeavesWindow(form.ActiveControl));
                processCmdKey.Invoke(form, new object[] { message, Keys.Escape });
                Assert.False(form.Visible, "Escape with nothing open did not leave the window");
            });
        }

        [Fact]
        public void TheWindowsEscapeDecisionLivesInOneMethod()
        {
            // Escape is read in ProcessCmdKey and nowhere else of the window itself: the one other place SettingsForm.cs
            // names the key is the search dialog's own exit, and the traffic-light add field's cancel of its own check
            // lives in SettingsForm.TrafficLight.cs and never closes anything. No CancelButton assignment either, which
            // would be a second route to Close.
            string source = SettingsSource();
            Assert.Equal(2, Regex.Matches(source, @"Keys\.Escape").Count);   // SettingsForm.ProcessCmdKey, SettingsSearchDialog.ProcessCmdKey
            string window = MethodBody(source, "\n        protected override bool ProcessCmdKey(");
            Assert.Contains("keyData == Keys.Escape", window);
            Assert.Contains("EscapeLeavesWindow(ActiveControl)", window);
            Assert.DoesNotMatch(new Regex(@"CancelButton\s*="), source);
        }
    }
}

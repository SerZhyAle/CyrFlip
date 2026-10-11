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
    /// The WINDOWS-UI unification (ticket S0048): collapsible groups, remembered context, discovery,
    /// the numeric editor metrics and the mixed nav/header fills. Everything here observes the outside
    /// behaviour the contract names - what a toggle changes, what a restore brings back, what UI
    /// Automation is told - and the pure halves (state strings, anchor math, match rule) without a
    /// window at all.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class SettingsUiConformanceTests : IDisposable
    {
        private readonly List<IDisposable> _owned = new List<IDisposable>();
        private readonly List<string> _problems = new List<string>();

        public void Dispose()
        {
            foreach (IDisposable disposable in _owned) disposable.Dispose();
            Assert.True(_problems.Count == 0, string.Join("\n", _problems));
        }

        private static AppConfig NewConfig() => new AppConfig { SettingsGroupsState = "", SettingsViewport = "" };

        private SettingsForm NewSettings(AppConfig config)
        {
            var form = TestForms.NewSettings(config, withExchange: false);
            _owned.Add(form);
            return form;
        }

        // ---- CollapsibleGroup: collapse changes visibility only (WINDOWS-UI section 3 rules 1-2) ----

        [Fact]
        public void CollapsedGroupHidesItsRowsAndKeepsTheirValues()
        {
            OnUiThread(() =>
            {
                var group = new CollapsibleGroup("test/group", "Группа");
                var box = new CheckBox { Text = "Строка", Checked = true };
                group.Body.Controls.Add(box);
                var host = new Form { Controls = { group } };
                _owned.Add(host);
                host.Show();

                Assert.True(group.Expanded);
                Assert.True(box.Visible);
                group.Expanded = false;
                Assert.False(group.Expanded);
                Assert.False(group.Body.Visible);
                Assert.False(box.Visible);
                // Visibility alone: the value the user left is untouched by collapsing.
                Assert.True(box.Checked);
                group.Expanded = true;
                Assert.True(box.Checked);
                Assert.True(box.Visible);
            });
        }

        [Fact]
        public void CollapsingHandsFocusToTheHeader()
        {
            OnUiThread(() =>
            {
                var group = new CollapsibleGroup("test/focus", "Группа");
                var box = new CheckBox { Text = "Строка" };
                group.Body.Controls.Add(box);
                var host = new Form { Controls = { group } };
                _owned.Add(host);
                host.Show();
                box.Focus();
                Assert.True(group.Body.ContainsFocus);

                group.Expanded = false;
                Assert.False(group.Body.Visible);
                // The focus went to the header before the body went away (WINDOWS-UI section 3 rule 2).
                Assert.True(group.ContainsFocus);
                Assert.False(group.Body.ContainsFocus);
            });
        }

        [Fact]
        public void HeaderExposesExpandedStateAndGlyph()
        {
            OnUiThread(() =>
            {
                var group = new CollapsibleGroup("test/state", "Группа");
                var host = new Form { Controls = { group } };
                _owned.Add(host);
                host.Show();

                Control header = group.Controls[0];
                Assert.True(header is Button, "the first child of a group is its header button, got " + header.GetType().Name);
                AccessibleObject accessible = header.AccessibilityObject;
                Assert.True(accessible.State.HasFlag(AccessibleStates.Expanded), "state: " + accessible.State);
                Assert.NotNull(((Button)header).Image);                       // nav.collapse drawn (open group)
                Assert.Equal("Группа", accessible.Name);
                group.Expanded = false;
                Assert.True(accessible.State.HasFlag(AccessibleStates.Collapsed), "state after collapse: " + accessible.State);
                Assert.False(accessible.State.HasFlag(AccessibleStates.Expanded));
                Assert.NotNull(((Button)header).Image);                       // nav.expand drawn (closed group)
            });
        }

        [Fact]
        public void ExpansionSurvivesTheStateStringRoundTrip()
        {
            OnUiThread(() =>
            {
                var config = NewConfig();
                var form = NewSettings(config);
                form.Show();

                // Flip a known group closed and take the form's own serialization.
                CollapsibleGroup? layouts = form.Groups.FirstOrDefault(g => g.Id == "languages/layouts");
                Assert.NotNull(layouts);
                layouts!.Expanded = false;
                string state = form.GroupStateString();
                Assert.Contains("languages/layouts=0", state);

                // A fresh form reading that state comes back with the group collapsed; an unknown
                // id in the state is ignored (a retired group leaves nothing behind).
                var config2 = NewConfig();
                config2.SettingsGroupsState = state + ";retired/group=0";
                var form2 = NewSettings(config2);
                form2.Show();
                CollapsibleGroup? restored = form2.Groups.FirstOrDefault(g => g.Id == "languages/layouts");
                Assert.NotNull(restored);
                Assert.False(restored!.Expanded);
                CollapsibleGroup? neverMentioned = form2.Groups.First(g => g.Id != "languages/layouts");
                Assert.True(neverMentioned.Expanded, "group " + neverMentioned.Id + " came back collapsed; saved state: '" + config2.SettingsGroupsState + "'");
            });
        }

        // ---- Pure state-string halves ----

        [Fact]
        public void GroupStateParserIsStrictAndLosslessForWellFormedRows()
        {
            var states = SettingsForm.ParseGroupState("a=1;b=0;c=x;;d=");
            Assert.Equal(2, states.Count);
            Assert.True(states["a"]);
            Assert.False(states["b"]);

            string sanitized = AppConfig.SanitizeGroupState("a=1;junk;b=0;way-too-long-id-" + new string('x', 100) + "=1;c=2");
            Assert.Equal("a=1;b=0", sanitized);
            Assert.Equal(64, AppConfig.SanitizeGroupState(string.Join(";", Enumerable.Range(0, 100).Select(i => "g" + i + "=1"))).Split(';').Length);
        }

        [Fact]
        public void ViewportRowsRoundTripAndReplacePerPage()
        {
            string raw = SettingsForm.UpsertViewportRow("", SettingsPageKey.Hotkeys, "g:graphics/capture", 120);
            raw = SettingsForm.UpsertViewportRow(raw, SettingsPageKey.General, "r:4", 30);
            var (anchor, offset) = SettingsForm.ReadViewportRow(raw, SettingsPageKey.Hotkeys);
            Assert.Equal("g:graphics/capture", anchor);
            Assert.Equal(120, offset);
            raw = SettingsForm.UpsertViewportRow(raw, SettingsPageKey.Hotkeys, "r:1", 55);
            Assert.Equal(("r:1", 55), SettingsForm.ReadViewportRow(raw, SettingsPageKey.Hotkeys));
            Assert.Equal(("r:4", 30), SettingsForm.ReadViewportRow(raw, SettingsPageKey.General));
            Assert.Equal((null, 0), SettingsForm.ReadViewportRow(raw, SettingsPageKey.Launcher));
            // A page is written by its key: a row names its page, not its position in a list that is reordered.
            Assert.StartsWith("General|r:4|30", raw.Split(';').Single(row => row.StartsWith("General")));

            string safe = AppConfig.SanitizeViewport("0|g:x|10;junk;2||7;3|r:5|999999999;-1|y|1;8|r:1|-20");
            Assert.Equal("0|g:x|10;8|r:1|-20", safe);
        }

        // ---- Viewport anchor selection over a laid-out page ----

        [Fact]
        public void ViewportAnchorFollowsTheDeepestGroupPassed()
        {
            OnUiThread(() =>
            {
                var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
                var group1 = new CollapsibleGroup("page/first", "Первая");
                var group2 = new CollapsibleGroup("page/second", "Вторая");
                foreach (Control child in new Control[]
                {
                    new Label { Text = "top", AutoSize = true, Height = 20 }, group1,
                    new Label { Text = "row", AutoSize = true, Height = 20 }, group2,
                    new Label { Text = "tail", AutoSize = true, Height = 20 },
                }) flow.Controls.Add(child);
                var host = new Form { Controls = { flow } };
                _owned.Add(host);
                host.Show();
                flow.PerformLayout();

                Control[] children = flow.Controls.Cast<Control>().ToArray();
                Assert.Equal(children[1], flow.Controls[1]);
                int firstTop = group1.Top, secondTop = group2.Top;
                Assert.True(secondTop > firstTop);

                // At the top of the page: the first group (or the row above it).
                Assert.Equal(("g:page/first", firstTop), SettingsForm.ViewportAnchor(flow, firstTop));
                // Past the second group's top: the second group is the anchor, not the first.
                (string id, int top) = SettingsForm.ViewportAnchor(flow, secondTop + 5);
                Assert.Equal("g:page/second", id);
                Assert.Equal(secondTop, top);
                // Nothing passed yet: the topmost row, addressed by ordinal.
                (string flatId, _) = SettingsForm.ViewportAnchor(flow, 10);
                Assert.Equal("r:0", flatId);
            });
        }

        [Fact]
        public void ViewportAnchorAndRestoreHoldOnAScrolledPage()
        {
            // A scrolled panel moves its children: their Top is relative to the visible area. The
            // anchor and the restore must work in content coordinates, or a page scrolled anywhere
            // but the top comes back somewhere else.
            OnUiThread(() =>
            {
                var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Dock = DockStyle.Fill };
                var groups = new List<CollapsibleGroup>();
                for (int i = 0; i < 12; i++)
                {
                    var group = new CollapsibleGroup("page/g" + i, "Группа " + i);
                    group.Body.Controls.Add(new Label { Text = "row", AutoSize = false, Size = new Size(200, 120) });
                    groups.Add(group);
                    flow.Controls.Add(group);
                }
                var host = new Form { ClientSize = new Size(400, 300), Controls = { flow } };
                _owned.Add(host);
                host.Show();
                flow.PerformLayout();
                Application.DoEvents();

                int[] contentTops = groups.Select(g => SettingsForm.ContentTop(flow, g)).ToArray();
                int target = contentTops[6] + 30;
                SettingsForm.ScrollPage(flow, target);
                int scroll = -flow.AutoScrollPosition.Y;
                Assert.True(scroll > contentTops[5], $"the page did not scroll (at {scroll}, wanted {target})");
                // Content positions do not move with the scroll.
                Assert.Equal(contentTops, groups.Select(g => SettingsForm.ContentTop(flow, g)).ToArray());

                (string id, int top) = SettingsForm.ViewportAnchor(flow, scroll);
                int expected = Enumerable.Range(0, groups.Count).Last(i => contentTops[i] <= scroll);
                Assert.Equal("g:page/g" + expected, id);
                Assert.Equal(contentTops[expected], top);

                // Back to the top, then to the anchor plus the captured offset: the same place.
                int offset = scroll - top;
                SettingsForm.ScrollPage(flow, 0);
                Assert.Equal(0, -flow.AutoScrollPosition.Y);
                SettingsForm.ScrollPage(flow, SettingsForm.ContentTop(flow, groups[expected]) + offset);
                Assert.Equal(scroll, -flow.AutoScrollPosition.Y);
            });
        }

        // ---- Discovery (WINDOWS-UI section 3 rules 4-5) ----

        [Fact]
        public void SearchIndexCoversEveryPageAndMatchesDescriptions()
        {
            OnUiThread(() =>
            {
                var form = NewSettings(NewConfig());
                form.Show();
                List<SettingsForm.SettingsSearchHit> index = form.BuildSearchIndex();
                Assert.True(index.Count > 40, "the settings inventory shrank below its known size");
                Assert.Equal(form.Tabs!.TabPages.Count, index.Select(hit => hit.Page).Distinct().Count());

                // Language-independent by design: the terms come from the live controls, whatever
                // language the window is showing (this machine may not be the source language).
                Control deferRdp = FindByName(form, "deferRdp") ?? throw new InvalidOperationException("deferRdp not found");
                SettingsForm.SettingsSearchHit? rdpHit = index.FirstOrDefault(hit => ReferenceEquals(hit.Focus, deferRdp));
                Assert.True(rdpHit != null, "the deferRdp row is not discoverable");
                Assert.True(rdpHit!.Texts.Contains(deferRdp.Text), "the hit does not carry its caption");
                Assert.Equal(form.Tabs.TabPages.Cast<TabPage>().Single(p => Equals(p.Tag, SettingsPageKey.Hotkeys)).Text, rdpHit.PageTitle);

                // A term inside a description (not just the caption) finds the row.
                string descriptionWord = rdpHit.Texts.Split(' ').LastOrDefault(word => word.Length > 4 && !deferRdp.Text.Contains(word)) ?? "";
                Assert.True(descriptionWord.Length > 4, "no description word to search for");
                Assert.Contains(index, hit => hit.Texts.Contains(descriptionWord));

                // Reveal lands on the hit's page with its group expanded.
                SettingsForm.SettingsSearchHit? groupedHit = index.First(hit => hit.GroupCaption.Length > 0 && hit.Focus is CheckBox);
                form.Tabs.SelectedIndex = 0;
                CollapsibleGroup? owner = null;
                for (Control? parent = groupedHit.Row.Parent; parent != null; parent = parent.Parent)
                    if (parent is CollapsibleGroup group) { owner = group; break; }
                Assert.NotNull(owner);
                owner!.Expanded = false;
                form.RevealSetting(groupedHit);
                Assert.Equal(groupedHit.Page, form.Tabs.SelectedTab);
                Assert.True(owner.Expanded, "reveal did not expand the owning group");
            });
        }

        [Fact]
        public void SearchDialogHasAQueryBoxThatFiltersAndCaptionsOnlyResults()
        {
            OnUiThread(() =>
            {
                var form = NewSettings(NewConfig());
                form.Show();
                var dialog = new SettingsForm.SettingsSearchDialog(form);
                _owned.Add(dialog);
                dialog.Show(form);

                // The query box is in the dialog's tree (it once was built but never added).
                TextBox query = dialog.QueryForTest;
                Assert.Same(dialog, query.FindForm());
                Assert.True(query.Visible && query.Width > 0, "the query box is not laid out");

                ListBox results = dialog.ResultsForTest;
                int all = results.Items.Count;
                Assert.True(all > 40, "an empty query lists every setting");

                List<SettingsForm.SettingsSearchHit> index = form.BuildSearchIndex();
                SettingsForm.SettingsSearchHit hit = index.First(h => h.Focus is CheckBox && h.Texts.Length > h.Caption.Length);
                // A result shows the row's caption, not caption and description run together.
                Assert.Equal(SettingsForm.OneLine(((CheckBox)hit.Focus).Text), hit.Caption);

                query.Text = hit.Texts.Substring(hit.Caption.Length).Trim().Split(' ').First(w => w.Length > 4);
                Assert.InRange(results.Items.Count, 1, all - 1);
                Assert.True(results.Items.Cast<string>().All(item => !item.Contains("\n")));
            });
        }

        [Fact]
        public void SearchMatchRequiresEveryTerm()
        {
            Assert.True(SettingsForm.SearchMatchesForTest("Перевести буфер обмена", "буфер перевести"));
            Assert.True(SettingsForm.SearchMatchesForTest("Синхронизировать CapsLock", "capslock"));
            Assert.False(SettingsForm.SearchMatchesForTest("Синхронизировать CapsLock", "capslock буфер"));
            Assert.True(SettingsForm.SearchMatchesForTest("Anything", ""));   // empty query matches everything
        }

        // ---- Numeric editor metrics (WINDOWS-UI section 4) ----

        [Fact]
        public void NumericEditorTakesLogicalMetrics()
        {
            OnUiThread(() =>
            {
                var editor = new NumericUpDown { Minimum = -1, Maximum = 600, Width = 70 };
                var host = new Form { Controls = { editor } };
                _owned.Add(host);
                host.Show();
                SettingsNumeric.Apply(editor);

                Assert.True(editor.Width >= SettingsNumeric.Scale(editor, SettingsNumeric.EditorWidth),
                    $"numeric editor {editor.Width}px is below the {SettingsNumeric.EditorWidth} logical floor");
                Assert.Equal(GraphicsUnit.Pixel, editor.Font.Unit);
                Assert.True(editor.Font.Size >= SettingsNumeric.DigitFontPixels - 1,
                    $"digit font {editor.Font.Size}px is below the {SettingsNumeric.DigitFontPixels}px baseline");
                Control? spins = SettingsNumeric.SpinButtonsForTest(editor);
                if (spins != null)
                    Assert.True(spins.Width >= SettingsNumeric.Scale(editor, SettingsNumeric.SpinWidth),
                        $"spin halves {spins.Width}px are below the {SettingsNumeric.SpinWidth} logical floor");
            });
        }

        // ---- Mixed fills: readable text on the mixed surfaces in all three palettes ----

        [Fact]
        public void MixedFillsKeepTextReadableInEveryPalette()
        {
            foreach (ThemePalette palette in new[] { ThemePalette.Light, ThemePalette.Dark, ThemePalette.HighContrast })
            {
                Color selected = ThemePalette.Mix(palette.SurfaceWindow, palette.Accent, 0.18f);
                Color hovered = ThemePalette.Mix(palette.SurfaceWindow, palette.Accent, 0.09f);
                Color band = ThemePalette.Mix(palette.SurfaceWindow, palette.Accent, CollapsibleGroup.BandAmount);
                Assert.True(ThemePalette.Contrast(palette.TextPrimary, selected) >= 4.5,
                    $"{palette}: selected nav fill holds only {ThemePalette.Contrast(palette.TextPrimary, selected):F2}:1");
                Assert.True(ThemePalette.Contrast(palette.TextPrimary, hovered) >= 4.5,
                    $"{palette}: hovered nav fill holds only {ThemePalette.Contrast(palette.TextPrimary, hovered):F2}:1");
                Assert.True(ThemePalette.Contrast(palette.TextPrimary, band) >= 4.5,
                    $"{palette}: group header band holds only {ThemePalette.Contrast(palette.TextPrimary, band):F2}:1");
            }
            Assert.Equal(Color.Red.ToArgb(), ThemePalette.Mix(Color.Red, Color.Blue, 0f).ToArgb());
            Assert.Equal(Color.FromArgb(255, 0, 0, 255).ToArgb(), ThemePalette.Mix(Color.Red, Color.FromArgb(255, 0, 0, 255), 1f).ToArgb());
        }

        // ---- The theme round trip covers the new surfaces (WINDOWS-UI section 6.2) ----

        [Fact]
        public void LightAfterDarkRestoresGroupBandsAndHints()
        {
            OnUiThread(() =>
            {
                var form = NewSettings(NewConfig());
                form.Show();
                CollapsibleGroup group = form.Groups.First();
                var header = (Button)group.Controls[0];

                Apply(form, ThemePalette.Dark);
                Color darkBand = header.BackColor;
                Assert.Equal(ThemePalette.Mix(ThemePalette.Dark.SurfaceWindow, ThemePalette.Dark.Accent, CollapsibleGroup.BandAmount), darkBand);

                Apply(form, ThemePalette.Light);
                Assert.Equal(ThemePalette.Mix(ThemePalette.Light.SurfaceWindow, ThemePalette.Light.Accent, CollapsibleGroup.BandAmount), header.BackColor);
                Assert.NotEqual(darkBand, header.BackColor);

                // And one more cycle, dark again: the re-mix wins, never a stale light value.
                Apply(form, ThemePalette.Dark);
                Assert.Equal(darkBand, header.BackColor);
            });
        }

        private static void Apply(Form form, ThemePalette palette)
        {
            ThemeApply.Apply(form, palette);
            (form as ThemedForm)?.RaiseThemeApplied(palette);
        }

        // ---- Boolean hint inset (WINDOWS-UI section 4) ----

        [Fact]
        public void HintInsetMatchesTheCaptionGlyphWidth()
        {
            OnUiThread(() =>
            {
                var box = new CheckBox { Text = "Строка" };
                var host = new Form { Controls = { box } };
                _owned.Add(host);
                host.Show();
                int inset = SettingsForm.CaptionTextInset(box);
                Assert.InRange(inset, 12, 60);
                // The hint sits under the caption text, never to the left of it.
                Assert.True(inset > 0);
            });
        }

        private static Control? FindByName(Control root, string name)
        {
            if (root.Name == name) return root;
            foreach (Control child in root.Controls)
            {
                Control? found = FindByName(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private void OnUiThread(Action body)
        {
            var thread = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { _problems.Add("EXCEPTION: " + ex); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }
    }
}

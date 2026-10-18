using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    [Collection(SharedGdiCollection.Name)]
    public class UnifiedUiTests
    {
        private static void Sta(Action action)
        {
            Exception? failure = null;
            var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "UI check exceeded 30 seconds");
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        [Theory]
        [InlineData(false, .75, 24)]
        [InlineData(true, .75, 36)]
        [InlineData(false, 1, 32)]
        [InlineData(true, 1, 48)]
        [InlineData(false, 3, 96)]
        public void ProfileHasBoundedTargetsAndIndependentText(bool touch, double scale, int target)
        {
            var metrics = new UiMetrics(touch, scale, 2);
            Assert.Equal(target, metrics.Target);
            Assert.Equal(new UiMetrics(touch, scale, 1).Glyph, metrics.Glyph);
            Assert.Equal(32, new UiMetrics(false, double.NaN, 0).Target);
            Assert.Equal(32, new UiMetrics(false, 0, double.PositiveInfinity).Target);
        }

        private sealed class TestOption : BooleanOptionCheckBox
        {
            internal void Tap() { OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 5, 5, 0)); OnClick(EventArgs.Empty); OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, 5, 5, 0)); }
            internal void Drag() { OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 5, 5, 0)); OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, 5, 80, 0)); OnClick(EventArgs.Empty); OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, 5, 80, 0)); }
            internal void Keyboard() { OnKeyDown(new KeyEventArgs(Keys.Space)); OnClick(EventArgs.Empty); }
        }

        [Fact]
        public void BooleanDragCancelsAndCaptionTapAndKeyboardToggleOnce()
        {
            Sta(() =>
            {
                using var box = new TestOption { Text = "Keep the screen on" };
                int changes = 0;
                box.CheckedChanged += (_, _) => changes++;
                box.Drag(); Assert.False(box.Checked); Assert.Equal(0, changes);
                box.Tap(); Assert.True(box.Checked); Assert.Equal(1, changes);
                box.Keyboard(); Assert.False(box.Checked); Assert.Equal(2, changes);
                box.Enabled = false; box.Tap(); Assert.Equal(2, changes);
                Assert.True(box.GetPreferredSize(Size.Empty).Height >= 32);
                Assert.True(box.CaptionInset >= 28);
            });
        }

        [Fact]
        public void BooleanPaintsBackgroundOverUnclearedSurface()
        {
            Sta(() =>
            {
                using var bmp = new Bitmap(200, 40);
                using (var g = Graphics.FromImage(bmp)) g.Clear(Color.Red);
                using var box = new BooleanOptionCheckBox { Text = "Option", Size = new Size(200, 40), BackColor = Color.Blue };
                using (var g = Graphics.FromImage(bmp))
                {
                    typeof(Control).GetMethod("OnPaint", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .Invoke(box, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, 200, 40)) });
                }
                Color pixel = bmp.GetPixel(180, 20);
                Assert.Equal(Color.Blue.ToArgb(), pixel.ToArgb());
            });
        }

        [Fact]
        public void DisclosureHasCurrentStateGeometryInBothDirectionsAndPreservesBody()
        {
            Sta(() =>
            {
                var rect = new Rectangle(0, 0, 16, 16);
                PointF[] down = CollapsibleGroup.StateTriangle(rect, true, false);
                Assert.True(down[2].Y > down[0].Y);
                Assert.Equal(down, CollapsibleGroup.StateTriangle(rect, true, true));
                Assert.True(CollapsibleGroup.StateTriangle(rect, false, false)[1].X > 8);
                Assert.True(CollapsibleGroup.StateTriangle(rect, false, true)[1].X < 8);
                using var group = new CollapsibleGroup("test/topic", "Topic");
                var value = new TextBox { Text = "unsaved" };
                group.Body.Controls.Add(value);
                group.FitWidth(310);
                group.Expanded = false;
                Assert.False(group.Body.Visible);
                group.Expanded = true;
                Assert.Equal("unsaved", value.Text);
                Assert.Equal(310 - group.Body.Margin.Horizontal, group.Body.MinimumSize.Width);
                Assert.True(group.Controls[0].Font.Bold);
            });
        }

        [Fact]
        public void NarrowCommandPanelWrapsWithoutStretchingAndResetsRowHeights()
        {
            Sta(() =>
            {
                using var form = new Form { ClientSize = new Size(500, 350) };
                var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
                form.Controls.Add(row);
                foreach (string caption in new[] { "Copy", "A command with a long caption", "Close" })
                {
                    var button = new Button { Text = caption, AutoSize = true };
                    ButtonGlyphs.Attach(button, AppGlyphs.Copy); ButtonGlyphs.UseCommandMetric(button);
                    row.Controls.Add(button);
                }
                form.Show();
                CommandPanel.Fit(row, 160); row.PerformLayout();
                Button[] buttons = row.Controls.OfType<Button>().ToArray();
                Assert.True(buttons[1].Top > buttons[0].Top, "whole buttons should wrap: " + string.Join(";", buttons.Select(b => b.Bounds)));
                Assert.True(buttons[0].Width < 160, "Copy should fit content");
                Assert.All(buttons, b => Assert.True(b.Right <= 160, "overflow: " + b.Bounds));
                Assert.True(buttons[1].Height > buttons[0].Height, "multiline should grow: " + string.Join(";", buttons.Select(b => b.Bounds)));
                CommandPanel.Fit(row, 480); row.PerformLayout();
                Assert.All(buttons, b => Assert.True(b.Height < 60));
                form.Close();
            });
        }

        [Fact]
        public void AboutInventoryIsUniqueLocalizedAndHasAllResourceKinds()
        {
            AboutResources.Resource[] resources = AboutResources.Product.Concat(AboutResources.Dependencies).Concat(AboutResources.Related).ToArray();
            Assert.Equal(resources.Length, resources.Select(r => r.Id).Distinct().Count());
            foreach (string id in new[] { "site", "repository", "license", "privacy", "notices", "downloads", "contact", "guide", "ollama", "yt-dlp", "extension", "launcher" })
                Assert.Contains(resources, r => r.Id == id);
            foreach (AboutResources.Resource resource in resources)
            {
                Assert.True(Uri.TryCreate(resource.Url, UriKind.Absolute, out Uri? uri));
                Assert.Contains(uri!.Scheme, new[] { "https", "mailto" });
                Assert.True(Localization.All.Any(p => p.Key == resource.Caption && p.Value.All(v => !string.IsNullOrWhiteSpace(v))), "Missing authored locale: " + resource.Caption);
            }
        }

        [Fact]
        public void ConfirmationUsesSafeFirstReadingOrderWithoutChangingDefault()
        {
            Sta(() =>
            {
                using var dialog = new ConfirmDialog("English", "Delete the sample?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, true, false);
                Button safe = dialog.Buttons.Single(b => b.DialogResult == DialogResult.No);
                Button danger = dialog.Buttons.Single(b => b.DialogResult == DialogResult.Yes);
                Assert.Same(safe, dialog.AcceptButton);
                Assert.Same(safe, dialog.CancelButton);
                Assert.Same(safe, safe.Parent!.Controls[0]);
                Assert.Same(danger, safe.Parent.Controls[1]);
                Assert.Equal(ThemePalette.Light.TextPrimary, ButtonGlyphs.InkFor(ButtonLook.DismissingSafe, ThemePalette.Light, true));
            });
        }

        [Fact]
        public void ActualComponentsHaveReadableContrastAndCanGenerateEvidence()
        {
            Sta(() =>
            {
                string? output = Environment.GetEnvironmentVariable("CYRFLIP_UI_EVIDENCE");
                foreach (ThemePalette palette in new[] { ThemePalette.Light, ThemePalette.Dark, ThemePalette.HighContrast })
                foreach (var scenario in new[] {
                    (id: "ordinary", scale: 1.0, text: 1.0, language: "English", width: 520),
                    (id: "compact", scale: .75, text: 1.0, language: "English", width: 360),
                    (id: "very-large", scale: 3.0, text: 1.0, language: "Deutsch", width: 760),
                    (id: "large-text", scale: 1.0, text: 2.0, language: "Deutsch", width: 360),
                    (id: "rtl", scale: 1.0, text: 1.0, language: "العربية", width: 360) })
                {
                    using var font = new Font(Localization.FontFamily(scenario.language) ?? "Segoe UI", (float)(9 * scenario.scale * scenario.text));
                    using var form = new Form { ClientSize = new Size(scenario.width, 520), Text = "S0076 components", ShowInTaskbar = false, Font = font };
                    UiMetrics.Set(form, new UiMetrics(false, scenario.scale, scenario.text));
                    if (Localization.IsRightToLeft(scenario.language)) { form.RightToLeft = RightToLeft.Yes; form.RightToLeftLayout = true; }
                    var group = new CollapsibleGroup("sample", Localization.Translate(scenario.language, "Power and screen")) { Location = new Point(8, 8) };
                    group.Body.Controls.Add(new BooleanOptionCheckBox { Text = Localization.Translate(scenario.language, "Keep the screen on (like video)"), Checked = true,
                        MaximumSize = new Size(scenario.width - 55, 0) });
                    form.Controls.Add(group);
                    group.FitWidth(scenario.width - 30);
                    ThemeApply.Apply(form, palette); group.ApplyPalette(palette);
                    form.Show(); form.Refresh();
                    Color face = group.Controls[0].BackColor;
                    Assert.True(ThemePalette.Contrast(palette.TextPrimary, face) >= 4.5);
                    foreach (ButtonLook look in new[] { ButtonLook.Neutral, ButtonLook.Affirmative, ButtonLook.Dismissing })
                        Assert.True(ThemePalette.Contrast(ButtonGlyphs.InkFor(look, palette, true), palette.Control) >= 3);
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        Directory.CreateDirectory(output);
                        using var bitmap = new Bitmap(form.Width, form.Height);
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        string name = palette.Kind + "-" + scenario.id;
                        bitmap.Save(Path.Combine(output, name + ".png"));
                        File.WriteAllText(Path.Combine(output, name + ".txt"),
                            "BUTTONS 2.0; BOOLEAN-OPTION 2.0; COLLAPSIBLE-GROUP 1.1; UI-SIZING 1.0\n"
                            + "build=" + typeof(AppConfig).Assembly.GetName().Version + "; os=" + Environment.OSVersion
                            + "; runtime=" + Environment.Version + "; machine=" + Environment.MachineName
                            + "; locale=" + scenario.language + "; profile=pointer; interface=" + scenario.scale
                            + "; text=" + scenario.text + "; dpi=" + form.DeviceDpi
                            + "; header=" + group.Controls[0].Bounds + "; body=" + group.Body.Bounds);
                    }
                    form.Close();
                }
            });
        }
    }
}

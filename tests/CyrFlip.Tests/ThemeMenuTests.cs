using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The ToolStrip menus in dark (ticket S0020 section 8, test 5): the tray menu, the text context
    /// menu, the taskbar button's menu. What a colour table alone would get wrong is what is checked -
    /// the caption colours, and the statistics captions at the foot of the text context menu reading
    /// as muted rather than as disabled commands.
    /// </summary>
    public class ThemeMenuTests
    {
        [Fact]
        public void TheDarkRendererDrawsADarkMenuWithReadableCaptions()
        {
            var failures = new List<string>();
            RunSta(() =>
            {
                using var menu = new ContextMenuStrip();
                var command = new ToolStripMenuItem("Copy");
                var disabled = new ToolStripMenuItem("Paste") { Enabled = false };
                var caption = new ToolStripLabel("Selected: 3 lines");
                menu.Items.AddRange(new ToolStripItem[] { command, disabled, new ToolStripSeparator(), caption });

                ToolStripRenderer renderer = ThemeToolStripRenderer.For(ThemePalette.Dark);
                Assert.IsType<ThemeToolStripRenderer>(renderer);

                using var bitmap = new Bitmap(120, 40);
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    renderer.DrawToolStripBackground(new ToolStripRenderEventArgs(g, menu, new Rectangle(0, 0, 120, 40), ThemePalette.Dark.SurfaceRaised));
                    if (bitmap.GetPixel(60, 20).ToArgb() != ThemePalette.Dark.SurfaceRaised.ToArgb())
                        failures.Add("menu background " + bitmap.GetPixel(60, 20));

                    Color TextOf(ToolStripItem item)
                    {
                        var args = new ToolStripItemTextRenderEventArgs(g, item, item.Text, new Rectangle(0, 0, 100, 20),
                            SystemColors.ControlText, SystemFonts.MenuFont, TextFormatFlags.Default);
                        renderer.DrawItemText(args);
                        return args.TextColor;
                    }
                    if (TextOf(command) != ThemePalette.Dark.TextPrimary) failures.Add("command text");
                    if (TextOf(disabled) != ThemePalette.Dark.TextDisabled) failures.Add("disabled text");
                    if (TextOf(caption) != ThemePalette.Dark.TextMuted) failures.Add("statistics caption");
                }
            }, failures);
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        /// <summary>Light and high contrast keep the stock renderer - so both look exactly as they always did.</summary>
        [Fact]
        public void LightAndHighContrastKeepTheStockRenderer()
        {
            var failures = new List<string>();
            RunSta(() =>
            {
                if (ThemeToolStripRenderer.For(ThemePalette.Light) is ThemeToolStripRenderer) failures.Add("light");
                if (ThemeToolStripRenderer.For(ThemePalette.HighContrast) is ThemeToolStripRenderer) failures.Add("high contrast");
            }, failures);
            Assert.True(failures.Count == 0, "the dark renderer was handed out for: " + string.Join(", ", failures));
        }

        private static void RunSta(Action body, List<string> failures)
        {
            var thread = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { failures.Add("EXCEPTION: " + ex); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }
    }
}

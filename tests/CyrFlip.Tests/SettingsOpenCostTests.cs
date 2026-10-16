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
    /// What it costs to open the settings window for the first time. The window is built and shown on the
    /// thread both low-level hooks run on, so seconds spent here are seconds nothing answers - and a double
    /// click on the tray icon that looks like it did nothing gets repeated.
    ///
    /// <para>The defect these guard: every page panel's Resize used to re-clamp the text widths of <i>all</i>
    /// thirteen pages, and showing the window resizes every panel, so the first open measured the same
    /// captions a hundred and sixty times over - about four of the six seconds it took.</para>
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class SettingsOpenCostTests
    {
        [Fact]
        public void ShowingTheWindowClampsOnlyThePageThatIsShown()
        {
            OnUiThread(() =>
            {
                using SettingsForm form = TestForms.NewSettings(SettingsRowAnatomyTests.Config("English"));
                int pages = form.Tabs!.TabCount;
                Assert.True(pages >= 10, "the page list changed shape - the bound below no longer means anything");
                int built = form.PageClampCount;

                form.Show();
                Application.DoEvents();
                form.Size = new Size(1100, 760);
                Application.DoEvents();

                int onShow = form.PageClampCount - built;
                // One page is on screen. Before the fix this was pages x pages and more.
                Assert.True(onShow >= 1, "the page on screen was never clamped");
                Assert.True(onShow <= pages,
                    $"showing and resizing the window clamped {onShow} times for {pages} pages - a resize of one page's panel must not re-clamp the others");
            });
        }

        /// <summary>
        /// The other half of the same change: a page that was hidden while the window changed size is brought
        /// up to date when it is selected, so nothing is left at the width it was authored for.
        /// </summary>
        [Fact]
        public void APageHiddenDuringAResizeIsClampedWhenItIsSelected()
        {
            OnUiThread(() =>
            {
                using SettingsForm form = TestForms.NewSettings(SettingsRowAnatomyTests.Config("English"));
                form.Show();
                Application.DoEvents();
                // The narrowest window: the authored 890/930 px text caps are wider than any page.
                form.Size = form.MinimumSize;
                Application.DoEvents();

                var problems = new List<string>();
                foreach (TabPage page in form.Tabs!.TabPages)
                {
                    form.Tabs.SelectedTab = page;
                    Application.DoEvents();
                    var flow = (FlowLayoutPanel)page.Controls[0];
                    int available = flow.ClientSize.Width - flow.Padding.Horizontal;
                    int bound = Math.Max(SettingsForm.MinTextWidth, available);
                    foreach (Label label in Labels(flow))
                        if (label.MaximumSize.Width > bound)
                            problems.Add($"{page.Text}: \"{Shorten(label.Text)}\" may be {label.MaximumSize.Width}px wide on a page that has {available}px");
                }
                Assert.True(problems.Count == 0, string.Join("\n", problems));
            });
        }

        private static IEnumerable<Label> Labels(Control root)
        {
            foreach (Control child in root.Controls)
            {
                if (child is Label label && label.MaximumSize.Width > 0) yield return label;
                foreach (Label inner in Labels(child)) yield return inner;
            }
        }

        private static string Shorten(string text) => text.Length <= 40 ? text : text.Substring(0, 40) + "..";

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
            if (failure != null) throw new Exception("UI-thread failure", failure);
        }
    }

    /// <summary>The "Loading settings.." window that stands in while the first build blocks the UI thread.</summary>
    [Collection(SharedGdiCollection.Name)]
    public class SettingsLoadingNoticeTests
    {
        [Fact]
        public void TheSentenceIsTranslatedInEveryLanguage()
        {
            var untranslated = new List<string>();
            OnUiThread(() =>
            {
                foreach (string language in Localization.Names)
                {
                    using var notice = new SettingsLoadingNotice(language);
                    if (language != "English" && notice.MessageText == "Loading settings..")
                        untranslated.Add(language);
                    Assert.False(string.IsNullOrWhiteSpace(notice.MessageText), language);
                }
            });
            Assert.True(untranslated.Count == 0, "Not translated: " + string.Join(", ", untranslated));
        }

        [Fact]
        public void ItIsOnScreenWithoutTakingTheFocus()
        {
            OnUiThread(() =>
            {
                using var notice = new SettingsLoadingNotice("English");
                Rectangle work = Screen.PrimaryScreen!.WorkingArea;
                notice.ShowCentered(work);
                Assert.True(notice.Visible);
                Assert.True(work.Contains(notice.Bounds), "the notice is not inside the work area it was asked to centre in");
                Assert.False(notice.ShowInTaskbar);
                Assert.True(notice.TopMost);
                // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW: a click or a show never moves the focus off the user's editor.
                int exStyle = (int)GetWindowLongPtr(notice.Handle, -20);
                Assert.NotEqual(0, exStyle & 0x08000000);
                Assert.NotEqual(0, exStyle & 0x00000080);
            });
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

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
            if (failure != null) throw new Exception("UI-thread failure", failure);
        }
    }
}

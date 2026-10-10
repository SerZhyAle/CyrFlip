using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The pixel contract of the settings window (ticket S0060, phase P2): the window answers
    /// <c>WM_DPICHANGED</c> even though the app does not opt into WinForms' own DPI machinery
    /// (WINDOWS-UI section 5), the config stores the window rectangle in logical 96-DPI pixels
    /// (APP-BEHAVIOUR rule 10), and a narrow window clamps the wrapping text blocks instead of
    /// overflowing (H3). A real monitor switch stays a live <c>tools/uitest</c> step; these gates hold
    /// the plumbing the live pass depends on.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class SettingsDpiGeometryTests
    {
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
            if (failure != null) throw new Xunit.Sdk.XunitException("EXCEPTION: " + failure);
        }

        private static AppConfig Config() => new AppConfig { UiLanguage = "English", SettingsGroupsState = "", SettingsViewport = "" };

        [Fact]
        public void DeviceDpiFollowsASimulatedDpiChange()
        {
            OnUiThread(() =>
            {
                using var form = TestForms.NewSettings(Config());
                var _ = form.Handle;   // the machinery lives on the window's own message path
                // Small on purpose: Windows clamps any resize to SystemInformation.MaxWindowTrackSize,
                // and the session's display varies - the suggested rectangle must stay inside it.
                form.Size = new Size(600, 400);
                Size before = form.Size;

                var suggested = new WindowInterop.RECT
                {
                    Left = form.Left, Top = form.Top,
                    Right = form.Left + before.Width * 3 / 2,
                    Bottom = form.Top + before.Height * 3 / 2,
                };
                IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WindowInterop.RECT)));
                try
                {
                    Marshal.StructureToPtr(suggested, buffer, false);
                    IntPtr wParam = (IntPtr)((144 << 16) | 144);   // LOWORD = X dpi, HIWORD = Y dpi
                    WindowInterop.SendMessage(form.Handle, 0x02E0, wParam, buffer);
                }
                finally { Marshal.FreeHGlobal(buffer); }

                // The per-window value lives in a private field net48 exposes through DeviceDpi only on a
                // per-monitor-v2 thread - the app's UI thread in production. Here the property would
                // answer the process DPI whatever we store, so the gate peeks at the field itself.
                Assert.Equal(144, ControlDpi.Peek(form));
                NumericUpDown editor = SettingsForm.NumericEditors(form).First();
                Assert.Equal(144, ControlDpi.Peek(editor));   // the whole tree, not the form alone
                // The window takes the suggested rectangle clamped to the monitor it lands on (the work
                // area), which on a small session display - a 1024 px CI runner - is narrower than both
                // the suggestion and the system's tracking limit.
                int workWidth = Screen.FromRectangle(Rectangle.FromLTRB(suggested.Left, suggested.Top, suggested.Right, suggested.Bottom)).WorkingArea.Width;
                int expectedWidth = Math.Min(Math.Min(before.Width * 3 / 2, SystemInformation.MaxWindowTrackSize.Width), workWidth);
                Assert.Equal(expectedWidth, form.Width);
            });
        }

        [Fact]
        public void LogicalPixelsRoundTripThroughEveryDpi()
        {
            foreach (int dpi in new[] { 96, 120, 144, 168, 192 })
            {
                int logical = 1340;
                int device = ControlDpi.Scale(dpi, logical);
                Assert.Equal(device, ControlDpi.Scale(dpi, ControlDpi.Unscale(dpi, device)));
                Assert.Equal(logical, ControlDpi.Unscale(dpi, ControlDpi.Scale(dpi, logical)));
            }
            Assert.Equal(24, SettingsForm.TabIconSizeFor(96));
            Assert.Equal(36, SettingsForm.TabIconSizeFor(144));
        }

        [Fact]
        public void TabIconsRenderAtTheMonitorDpi()
        {
            using var list = SettingsForm.CreateTabIcons(ThemePalette.Light.TextPrimary, 144);
            Assert.Equal(36, list.ImageSize.Width);
            Assert.Equal(36, list.ImageSize.Height);
        }

        [Fact]
        public void NarrowWindowClampsWrappingTextBlocks()
        {
            OnUiThread(() =>
            {
                using var form = TestForms.NewSettings(Config());
                var _ = form.Handle;
                form.Size = new Size(940, 640);   // the minimum plus a little: a narrow window
                Application.DoEvents();

                Label hint = WalkForLabels(form)
                    .Where(l => l.Text.Length > 80 && l.MaximumSize.Width > 0)   // wrapping prose, not a caption
                    .OrderByDescending(l => l.MaximumSize.Width)
                    .First();
                int pageWidth = SettingsFormContentWidth(form, hint);
                Assert.True(hint.MaximumSize.Width < 890,
                    "H3 - a hint kept its 890 px cap on a " + pageWidth + " px page");
                Assert.True(hint.MaximumSize.Width >= SettingsForm.MinTextWidth - 20,
                    "H3 - the clamp crushed a hint to " + hint.MaximumSize.Width + " px");
            });
        }

        [Fact]
        public void TabStripIsCappedOnANarrowWindow()
        {
            OnUiThread(() =>
            {
                using var form = TestForms.NewSettings(Config());
                var _ = form.Handle;
                form.Size = new Size(940, 640);
                form.Reload();   // ends with ApplyLanguage -> AdjustTabStrip
                Application.DoEvents();

                TabControl? tabs = form.Tabs;
                Assert.NotNull(tabs);
                int cap = Math.Max(170, tabs!.Width / 4);
                Assert.True(tabs.ItemSize.Height <= cap,
                    "H3 - the strip reached " + tabs.ItemSize.Height + " px on a " + tabs.Width + " px window (cap " + cap + ")");
            });
        }

        private static System.Collections.Generic.IEnumerable<Label> WalkForLabels(Control root)
        {
            foreach (Control child in root.Controls)
            {
                if (child is Label label) yield return label;
                foreach (Label deeper in WalkForLabels(child)) yield return deeper;
            }
        }

        private static int SettingsFormContentWidth(Control form, Label hint)
        {
            for (Control? c = hint.Parent; c != null; c = c.Parent)
                if (c is FlowLayoutPanel flow && flow.AutoScroll) return flow.ClientSize.Width;
            return 0;
        }
    }
}

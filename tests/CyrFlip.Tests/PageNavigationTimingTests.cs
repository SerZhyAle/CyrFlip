using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Page-change timing of the settings window (ticket S0076 M3/M4, SETTINGS-NAVIGATION section 5): the trace
    /// itself, the stale-result rule, and the measurement run that <c>tools/uitest/Measure-PageNavigation.ps1</c>
    /// repeats in fresh processes. The run is the only thing here that writes raw samples; it asserts nothing about
    /// the budgets - the script does, from all processes' samples, so one fast process cannot hide a slow one.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class PageNavigationTimingTests
    {
        private static void Sta(Action action)
        {
            Exception? failure = null;
            var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromMinutes(5)), "UI check exceeded 5 minutes");
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        /// <summary>
        /// The app starts with <c>Application.SetCompatibleTextRenderingDefault(false)</c> (GDI text, measured by
        /// <c>TextRenderer</c>); a test host does not, and its labels measure through GDI+ - several times slower, and
        /// not what a person runs. The measuring process takes the app's setting before it creates its first window.
        /// </summary>
        private static void UseProductionTextRendering()
        {
            try { Application.SetCompatibleTextRenderingDefault(false); }
            catch (InvalidOperationException) { /* a window already exists in this process: its setting stands */ }
        }

        private static AppConfig Config() => new AppConfig { UiLanguage = "English", SettingsGroupsState = "", SettingsViewport = "" };

        [Fact]
        public void NearestRankIsTheContractsMethodAndNeverInterpolates()
        {
            double[] twenty = Enumerable.Range(1, 20).Select(i => (double)i).ToArray();
            Assert.Equal(19, PageNavigationTrace.NearestRank(twenty, 95));   // ceil(0.95 * 20) = 19th value
            Assert.Equal(20, PageNavigationTrace.NearestRank(twenty, 100));
            Assert.Equal(1, PageNavigationTrace.NearestRank(twenty, 1));
            Assert.Equal(7, PageNavigationTrace.NearestRank(new[] { 7.0 }, 95));
            Assert.True(double.IsNaN(PageNavigationTrace.NearestRank(Array.Empty<double>(), 95)));
        }

        private static void Click(SettingsForm form, int index)
        {
            SettingsRail rail = form.Rail;
            rail.EnsureVisible(index);
            Rectangle row = rail.RowBounds(index);
            var at = new Point(row.Left + row.Width / 2, row.Top + row.Height / 2);
            Invoke(rail, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, at.X, at.Y, 0));
            Invoke(rail, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, at.X, at.Y, 0));
        }

        private static void Invoke(Control control, string method, EventArgs args)
            => control.GetType().GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, new[] { args.GetType() }, null)!
                .Invoke(control, new object[] { args });

        private static void Pump(SettingsForm form, int timeoutMs = 10000)
        {
            var watch = Stopwatch.StartNew();
            do { Application.DoEvents(); }
            while (form.NavigationTrace.Pending && watch.ElapsedMilliseconds < timeoutMs);
            Application.DoEvents();
            Assert.False(form.NavigationTrace.Pending, "the page change never produced a complete frame");
        }

        [UiSweepFact]
        public void FirstAndWarmVisitsAreTimedFromTheInputToTheFinishedFrame()
        {
            Sta(() =>
            {
                using var form = TestForms.NewSettings(Config());
                form.Show();
                Application.DoEvents();
                form.NavigationTrace.Record();
                int start = form.Tabs!.SelectedIndex;
                int target = start == 3 ? 4 : 3;

                Click(form, target); Pump(form);
                Click(form, start); Pump(form);   // the page the window opened on was marked visited, so this one is warm
                Click(form, target); Pump(form);

                var samples = form.NavigationTrace.Samples;
                Assert.Equal(3, samples.Count);
                Assert.Equal(PageNavigationTrace.Kind.First, samples[0].Kind);
                Assert.Equal(PageNavigationTrace.Kind.Warm, samples[1].Kind);
                Assert.Equal(PageNavigationTrace.Kind.Warm, samples[2].Kind);
                Assert.All(samples, s => Assert.InRange(s.Milliseconds, 0.001, 5000));
                // The frame is the page the user asked for.
                Assert.Equal(target, form.Tabs.SelectedIndex);
                Assert.True(form.Tabs.SelectedTab!.Visible);
            });
        }

        [UiSweepFact]
        public void RapidSwitchingSupersedesEarlierNavigationsAndTouchesOnlyTheCurrentDestination()
        {
            Sta(() =>
            {
                using var form = TestForms.NewSettings(Config());
                form.Show();
                Application.DoEvents();
                form.NavigationTrace.Record();
                int a = 1, b = 2, c = 3;

                // Three clicks with no message processing between them: a user faster than the window.
                Click(form, a); Click(form, b); Click(form, c);
                Pump(form);

                var samples = form.NavigationTrace.Samples;
                Assert.Equal(2, samples.Count(s => s.Kind == PageNavigationTrace.Kind.Superseded));
                PageNavigationTrace.Sample timed = Assert.Single(samples, s => s.Kind != PageNavigationTrace.Kind.Superseded);
                Assert.Equal(form.Tabs!.TabPages[c].Tag!.ToString(), timed.Destination);
                // No old result displaced the current destination.
                Assert.Equal(c, form.Tabs.SelectedIndex);
                Assert.Equal(c, form.Rail.SelectedIndex);
                Assert.True(form.Tabs.TabPages[c].Visible);
                Assert.False(form.Tabs.TabPages[a].Visible);
                Assert.False(form.Tabs.TabPages[b].Visible);
            });
        }

        [UiSweepFact]
        public void NavigationRunsNoOperationAndKeepsTheWindowRectangleAndEdits()
        {
            Sta(() =>
            {
                var config = Config();
                using var form = TestForms.NewSettings(config);
                form.Show();
                Application.DoEvents();
                Rectangle bounds = form.Bounds;
                bool keepAwake = config.KeepSystemAwake;
                string theme = config.Theme;
                for (int i = 0; i < form.Tabs!.TabCount; i++) { Click(form, i); Application.DoEvents(); }
                for (int i = form.Tabs.TabCount - 1; i >= 0; i--) { Click(form, i); Application.DoEvents(); }
                Assert.Equal(bounds, form.Bounds);   // selecting a page never resizes or moves the outer window
                Assert.Equal(keepAwake, config.KeepSystemAwake);
                Assert.Equal(theme, config.Theme);
            });
        }

        /// <summary>
        /// The measurement. Without <c>CYRFLIP_NAV_OUT</c> it is a smoke pass of one warm cycle. With it, one process
        /// contributes: every destination's first visit (the order rotates by <c>CYRFLIP_NAV_RUN</c> so each destination is
        /// sometimes the very first thing a cold process shows) and <c>CYRFLIP_NAV_CYCLES</c> (default 20) warm visits of each.
        /// Raw rows: run,destination,kind,milliseconds.
        /// </summary>
        [UiSweepFact]
        public void MeasureFirstAndWarmVisits()
        {
            string? output = Environment.GetEnvironmentVariable("CYRFLIP_NAV_OUT");
            int run = int.TryParse(Environment.GetEnvironmentVariable("CYRFLIP_NAV_RUN"), out int r) ? r : 0;
            int cycles = output == null ? 1 : int.TryParse(Environment.GetEnvironmentVariable("CYRFLIP_NAV_CYCLES"), out int c) ? c : 20;
            Sta(() =>
            {
                UseProductionTextRendering();
                // The window opens on a different page in every process (the page on screen when the window appears is not a
                // timed first visit), so each destination, General included, is a genuine first visit in most processes.
                AppConfig config = Config();
                SettingsPageKey[] pages = SettingsPageKeys.DisplayOrder.ToArray();
                config.SettingsPageId = pages[run % pages.Length].ToString();
                using var form = TestForms.NewSettings(config);
                form.Show();
                for (int i = 0; i < 5; i++) { Application.DoEvents(); Thread.Sleep(20); }
                form.NavigationTrace.Record();
                int count = form.Tabs!.TabCount;
                int[] order = Enumerable.Range(0, count).Select(i => (i + run) % count).ToArray();
                foreach (int index in order) { Click(form, index); Pump(form); }
                for (int cycle = 0; cycle < cycles; cycle++)
                    foreach (int index in order) { Click(form, index); Pump(form); }

                var samples = form.NavigationTrace.Samples;
                Assert.DoesNotContain(samples, s => s.Kind == PageNavigationTrace.Kind.Superseded);
                if (output == null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                File.AppendAllLines(output, samples.Select(s => string.Join(",", run, s.Destination, s.Kind.ToString().ToLowerInvariant(),
                    s.Milliseconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture))));
                string meta = output + ".meta";
                if (!File.Exists(meta))
                    File.WriteAllText(meta, string.Join("\n",
                        "build=" + typeof(AppConfig).Assembly.GetName().Version,
                        "os=" + Environment.OSVersion,
                        "runtime=" + Environment.Version + " (" + (Environment.Is64BitProcess ? "x64" : "x86") + ")",
                        "machine=" + Environment.MachineName,
                        "processors=" + Environment.ProcessorCount,
                        "locale=English",
                        "profile=pointer interface=100 text=100",
                        "dpi=" + form.DeviceDpi,
                        "screen=" + Screen.PrimaryScreen.Bounds.Width + "x" + Screen.PrimaryScreen.Bounds.Height,
                        "window=" + form.Bounds.Width + "x" + form.Bounds.Height,
                        "destinations=" + count) + "\n");
            });
        }
    }
}

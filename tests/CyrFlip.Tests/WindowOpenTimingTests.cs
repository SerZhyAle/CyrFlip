using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The measuring run of ticket S0135 (phase 0) that <c>tools/uitest/Measure-WindowOpen.ps1</c> repeats in fresh processes.
    /// It builds every window of <see cref="ThemeCoverageTests.Windows"/> twice - the first construction in the process and a
    /// second, warm one - with the theme and the size profile initialised as the app does, shows it off-screen and lets
    /// <see cref="OpenTrace"/> record what it measures: the rows <c>open</c> (from the start of construction to the first painted
    /// frame after Show), <c>theme-attach</c> and <c>profile-attach</c> of every window, and, for the settings window, its page,
    /// construction and reload spans. The control and handle counts of every window go to a second file beside it. It asserts
    /// nothing about budgets: the script judges them from all processes' samples, so one fast process cannot hide a slow one.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class WindowOpenTimingTests
    {
        private static void Sta(Action action)
        {
            Exception? failure = null;
            var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromMinutes(10)), "UI check exceeded 10 minutes");
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        /// <summary>The app measures its text through GDI; a test host does not unless it asks (see PageNavigationTimingTests).</summary>
        private static void UseProductionTextRendering()
        {
            try { Application.SetCompatibleTextRenderingDefault(false); }
            catch (InvalidOperationException) { /* a window already exists in this process: its setting stands */ }
        }

        private static int CountControls(Control root)
        {
            int count = 0;
            foreach (Control child in root.Controls) count += 1 + CountControls(child);
            return count;
        }

        private static int CountHandles(Control root)
        {
            int count = root.IsHandleCreated ? 1 : 0;
            foreach (Control child in root.Controls) count += CountHandles(child);
            return count;
        }

        private static void Pump(int turns)
        {
            for (int i = 0; i < turns; i++) { Application.DoEvents(); Thread.Sleep(10); }
        }

        /// <summary>
        /// Without <c>CYRFLIP_OPEN_OUT</c> a smoke pass over a temporary file. With it, one process contributes rows to that file
        /// and its <c>.stats</c> companion for <c>CYRFLIP_OPEN_LANGUAGE</c> (default English) and <c>CYRFLIP_OPEN_THEME</c>
        /// (default light); <c>CYRFLIP_OPEN_RUN</c> is the process number and <c>CYRFLIP_OPEN_CHANGES</c> (default 20) how many
        /// check-box changes the settings window is timed over.
        /// Trace rows: <c>utc,process,window,span,milliseconds</c>. Stats rows: <c>run,language,theme,window,phase,controls,handles</c>.
        /// </summary>
        [UiSweepFact]
        public void MeasureOpen()
        {
            string? output = Environment.GetEnvironmentVariable("CYRFLIP_OPEN_OUT");
            string language = Environment.GetEnvironmentVariable("CYRFLIP_OPEN_LANGUAGE") ?? "English";
            int languageIndex = Array.IndexOf(Localization.Codes, language);   // a code ("ru") or an endonym ("Русский"), as the script prefers
            if (languageIndex >= 0) language = Localization.Names[languageIndex];
            string theme = Environment.GetEnvironmentVariable("CYRFLIP_OPEN_THEME") ?? "light";
            int run = int.TryParse(Environment.GetEnvironmentVariable("CYRFLIP_OPEN_RUN"), out int r) ? r : 0;
            int changes = output == null ? 3 : int.TryParse(Environment.GetEnvironmentVariable("CYRFLIP_OPEN_CHANGES"), out int c) ? c : 20;
            string trace = output ?? Path.Combine(Path.GetTempPath(), "CyrFlipOpenTrace", Guid.NewGuid().ToString("N") + ".csv");

            Sta(() =>
            {
                UseProductionTextRendering();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(trace))!);
                string stats = trace + ".stats";
                var config = new AppConfig { UiLanguage = language, Theme = theme, SettingsGroupsState = "", SettingsViewport = "" };
                ThemeManager.Initialize(ThemeModes.Parse(theme));
                UiProfileManager.Initialize(config);
                OpenTrace.Record(trace);
                try
                {
                    var statRows = new List<string>();
                    using (var windows = new ThemeCoverageTests { LayOutAllSettingsPages = false })
                    {
                        foreach ((string name, Func<Form> make) in windows.Windows(language))
                        {
                            // Built over a live settings window: its cost is the SettingsForm row's, counted there.
                            if (name == "SettingsSearchDialog") continue;
                            foreach (string phase in new[] { "cold", "warm" })
                            {
                                Form form = make();
                                try
                                {
                                    form.ShowInTaskbar = false;
                                    form.StartPosition = FormStartPosition.Manual;
                                    form.Location = new Point(-32000, -32000);
                                    form.Show();
                                    Pump(6);
                                    statRows.Add(string.Join(",", run, language, theme, form.GetType().Name, phase,
                                        CountControls(form), CountHandles(form)));
                                }
                                finally { form.Dispose(); }
                            }
                        }
                        windows.DisposeFixtures();
                    }

                    // The cost of a change inside the settings window: one check box, toggled in memory (no registry, no file), timed
                    // from the toggle to the painted frame (the "change" span of SettingsForm.Changed).
                    using (SettingsForm settings = TestForms.NewSettings(config, allPagesLaidOut: false))
                    {
                        settings.ShowInTaskbar = false;
                        settings.StartPosition = FormStartPosition.Manual;
                        settings.Location = new Point(-32000, -32000);
                        settings.Show();
                        Pump(10);
                        FieldInfo box = typeof(SettingsForm).GetField("_settingsBackup", BindingFlags.NonPublic | BindingFlags.Instance)!;
                        var toggle = (CheckBox)box.GetValue(settings)!;
                        for (int i = 0; i < changes; i++) { toggle.Checked = !toggle.Checked; Pump(4); }
                    }

                    File.AppendAllLines(stats, statRows);
                    string meta = trace + ".meta";
                    if (!File.Exists(meta))
                        File.WriteAllText(meta, string.Join("\n",
                            "build=" + typeof(AppConfig).Assembly.GetName().Version,
                            "os=" + Environment.OSVersion,
                            "runtime=" + Environment.Version + " (" + (Environment.Is64BitProcess ? "x64" : "x86") + ")",
                            "machine=" + Environment.MachineName,
                            "processors=" + Environment.ProcessorCount,
                            "locale=" + language,
                            "theme=" + theme,
                            "screen=" + Screen.PrimaryScreen.Bounds.Width + "x" + Screen.PrimaryScreen.Bounds.Height) + "\n");

                    if (output == null)
                    {
                        string[] rows = File.ReadAllLines(trace);
                        Assert.Contains(rows, row => row.Contains(",SettingsForm,construct,"));
                        Assert.Contains(rows, row => row.Contains(",SettingsForm,change,"));
                        Assert.True(rows.Count(row => row.Contains(",open,")) >= 10, "the open span was recorded for the windows");
                    }
                }
                finally
                {
                    OpenTrace.Stop();
                    ThemeManager.Shutdown();
                    UiProfileManager.Shutdown();
                }
            });
        }
    }
}

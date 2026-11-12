using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The skipped re-layout of a warm page show (<see cref="SettingsPagePanel"/>, ticket S0076): it may only skip
    /// work, never change a result. Every page shown warm with the skip leaves every control exactly where WinForms'
    /// own cascade puts it.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class SettingsPagePanelTests
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

        private static List<string> Snapshot(Control root)
        {
            var rows = new List<string>();
            void Walk(Control control, string path)
            {
                for (int i = 0; i < control.Controls.Count; i++)
                {
                    Control child = control.Controls[i];
                    string at = path + "/" + i + ":" + child.GetType().Name;
                    rows.Add(at + " " + child.Bounds + (child.Visible ? "" : " hidden"));
                    Walk(child, at);
                }
            }
            rows.Add("root " + root.Bounds + " client " + root.ClientSize + " display " + ((ScrollableControl)root).DisplayRectangle);
            Walk(root, "");
            return rows;
        }

        private static List<string>[] VisitAll(SettingsForm form)
        {
            TabControl tabs = form.Tabs!;
            var shots = new List<string>[tabs.TabCount];
            for (int i = 0; i < tabs.TabCount; i++)
            {
                tabs.SelectedIndex = i;
                Application.DoEvents();
                shots[i] = Snapshot(tabs.TabPages[i].Controls[0]);
            }
            return shots;
        }

        [UiSweepFact]
        public void AWarmShowWithoutTheCascadeLeavesEveryControlWhereTheCascadePutsIt()
        {
            Sta(() =>
            {
                bool saved = SettingsPagePanel.SkipRelayoutOnShow;
                try
                {
                    var config = new AppConfig { UiLanguage = "English", SettingsGroupsState = "", SettingsViewport = "" };
                    using var form = TestForms.NewSettings(config);
                    form.Show();
                    Application.DoEvents();
                    Assert.All(form.Tabs!.TabPages.Cast<TabPage>(), page => Assert.IsType<SettingsPagePanel>(page.Controls[0]));

                    SettingsPagePanel.SkipRelayoutOnShow = true;
                    VisitAll(form);                              // first visits: handles created, the ordinary path
                    int before = SettingsPagePanel.SkippedShows;
                    List<string>[] skipped = VisitAll(form);     // warm: the cascade skipped
                    // Every page was shown again with the skip.
                    Assert.True(SettingsPagePanel.SkippedShows - before >= form.Tabs.TabCount,
                        "skipped " + (SettingsPagePanel.SkippedShows - before) + " of " + form.Tabs.TabCount + " warm shows");
                    SettingsPagePanel.SkipRelayoutOnShow = false;
                    List<string>[] full = VisitAll(form);        // warm: WinForms' own cascade

                    for (int i = 0; i < full.Length; i++)
                        Assert.Equal(full[i], skipped[i]);
                }
                finally { SettingsPagePanel.SkipRelayoutOnShow = saved; }
            });
        }

        /// <summary>
        /// The page clamp sets every caption's width cap under one layout suspension (S0076: a first visit under 500 ms).
        /// The pages it leaves are the pages the unsuspended clamp leaves, control for control.
        /// </summary>
        [UiSweepFact]
        public void TheSuspendedClampLeavesEveryPageAsTheUnsuspendedClampDoes()
        {
            List<string>[] Run(bool suspend)
            {
                List<string>[]? shots = null;
                Sta(() =>
                {
                    bool saved = SettingsForm.SuspendClampLayout;
                    try
                    {
                        SettingsForm.SuspendClampLayout = suspend;
                        var config = new AppConfig { UiLanguage = "English", SettingsGroupsState = "", SettingsViewport = "", SettingsPageId = "Indicators" };
                        using var form = TestForms.NewSettings(config);
                        form.Show();
                        Application.DoEvents();
                        int clamps = form.PageClampCount;
                        VisitAll(form);                          // every first visit clamps its page
                        Assert.True(form.PageClampCount - clamps >= form.Tabs!.TabCount - 1);
                        // The oracle is a clamp at the width each page finally has: the unsuspended clamp of the opening page
                        // runs while its scroll bar comes and goes and is left 17 px narrow until the next clamp, where the
                        // suspended one lands on that result at once (measured 2026-10-30).
                        typeof(SettingsForm).GetMethod("ClampPageTextWidths", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null);
                        shots = VisitAll(form);
                    }
                    finally { SettingsForm.SuspendClampLayout = saved; }
                });
                return shots!;
            }

            List<string>[] suspended = Run(true);
            List<string>[] immediate = Run(false);
            for (int i = 0; i < immediate.Length; i++)
            {
                var differ = immediate[i].Zip(suspended[i], (a, b) => a == b ? null : a + " | " + b).Where(d => d != null).Take(6).ToList();
                Assert.True(differ.Count == 0 && immediate[i].Count == suspended[i].Count, "page " + i + ": " + string.Join("\n", differ));
            }
        }
    }
}

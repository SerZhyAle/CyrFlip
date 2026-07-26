using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The tray submenu rebuild. The load-bearing case is the <b>second</b> rebuild: the first one
    /// starts from an empty collection and can never fail, so a build that disposes its old items
    /// while enumerating them looks perfectly healthy until the user adds a scenario and changes
    /// anything - which is exactly the bug this guards
    /// (<c>InvalidOperationException: Collection was modified</c>, verified on net48).
    /// </summary>
    public class LauncherTrayMenuTests
    {
        private static readonly List<LauncherScenario> Two = new List<LauncherScenario>
        {
            new LauncherScenario { Name = "First", Path = "calc.exe" },
            new LauncherScenario { Name = "Second", Path = "notepad.exe" },
        };

        private static void OnUiThread(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() => { try { body(); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Exception("UI thread failed: " + failure, failure);
        }

        private static string[] Captions(ToolStripMenuItem menu)
            => menu.DropDownItems.Cast<ToolStripItem>()
                .Select(i => i is ToolStripSeparator ? "---" : i.Text).ToArray();

        [Fact]
        public void RepeatedRebuildsDoNotThrowAndLeaveNoStaleItems()
        {
            OnUiThread(() =>
            {
                using var menu = new ToolStripMenuItem();
                for (int pass = 0; pass < 3; pass++)
                    LauncherTrayMenu.Rebuild(menu, Two, _ => { }, () => { }, null, ru => ru);

                // Three passes over a non-empty menu, and the result is one menu's worth of items.
                Assert.Equal(new[] { "First", "Second", "---", "Управление сценариями..." }, Captions(menu));
            });
        }

        [Fact]
        public void ScenarioOrderIsPreservedAndEachItemRunsItsOwnScenario()
        {
            OnUiThread(() =>
            {
                using var menu = new ToolStripMenuItem();
                var launched = new List<string>();
                LauncherTrayMenu.Rebuild(menu, Two, s => launched.Add(s.Name), () => { }, null, ru => ru);

                // The captured loop variable must not collapse onto the last scenario.
                ((ToolStripMenuItem)menu.DropDownItems[0]).PerformClick();
                ((ToolStripMenuItem)menu.DropDownItems[1]).PerformClick();
                Assert.Equal(new[] { "First", "Second" }, launched);
            });
        }

        [Fact]
        public void AnEmptyListStillOffersManageAndSkipsTheSeparator()
        {
            OnUiThread(() =>
            {
                using var menu = new ToolStripMenuItem();
                LauncherTrayMenu.Rebuild(menu, new List<LauncherScenario>(), _ => { }, () => { }, null, ru => ru);
                Assert.Equal(new[] { "Управление сценариями..." }, Captions(menu));
            });
        }

        [Fact]
        public void TheImportItemAppearsOnlyWhenAnImportActionIsSupplied()
        {
            OnUiThread(() =>
            {
                using var menu = new ToolStripMenuItem();
                bool imported = false;
                LauncherTrayMenu.Rebuild(menu, Two, _ => { }, () => { }, () => imported = true, ru => ru);
                Assert.Equal(
                    new[] { "First", "Second", "---", "Управление сценариями...", "Импорт из OneClickRunner..." },
                    Captions(menu));

                ((ToolStripMenuItem)menu.DropDownItems[4]).PerformClick();
                Assert.True(imported);
            });
        }

        [Fact]
        public void ClearEmptiesTheSubmenuWithoutThrowing()
        {
            OnUiThread(() =>
            {
                using var menu = new ToolStripMenuItem();
                LauncherTrayMenu.Rebuild(menu, Two, _ => { }, () => { }, null, ru => ru);
                LauncherTrayMenu.Clear(menu);
                Assert.Empty(menu.DropDownItems);
                LauncherTrayMenu.Clear(menu); // idempotent
                Assert.Empty(menu.DropDownItems);
            });
        }

        /// <summary>
        /// The taskbar button's left-click menu is the same list built into a standalone
        /// <see cref="ContextMenuStrip"/>. It is rebuilt on every click, so the repeated pass matters
        /// here even more than it does for the tray submenu.
        /// </summary>
        [Fact]
        public void TheStandaloneTaskbarMenuMatchesTheTraySubmenuAndSurvivesRebuilds()
        {
            OnUiThread(() =>
            {
                using var menu = new ContextMenuStrip();
                var launched = new List<string>();
                for (int pass = 0; pass < 3; pass++)
                    LauncherTrayMenu.Rebuild(menu, Two, s => launched.Add(s.Name), () => { }, null, ru => ru);

                Assert.Equal(new[] { "First", "Second", "---", "Управление сценариями..." },
                    menu.Items.Cast<ToolStripItem>().Select(i => i is ToolStripSeparator ? "---" : i.Text).ToArray());

                // Still one live handler per item after the rebuilds, bound to the right scenario.
                ((ToolStripMenuItem)menu.Items[1]).PerformClick();
                Assert.Equal(new[] { "Second" }, launched);
            });
        }

        [Fact]
        public void CaptionsComeFromTheTranslator()
        {
            OnUiThread(() =>
            {
                using var menu = new ToolStripMenuItem();
                LauncherTrayMenu.Rebuild(menu, new List<LauncherScenario>(), _ => { }, () => { }, () => { },
                    ru => "T:" + ru);
                Assert.Equal("T:Быстрый запуск", menu.Text);
                Assert.Equal(new[] { "T:Управление сценариями...", "T:Импорт из OneClickRunner..." }, Captions(menu));
            });
        }
    }
}

using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// Builds the tray "Быстрый запуск" submenu from the current scenario list: the scenarios in
    /// stored order, a separator, "Manage scenarios…", and - only when OneClickRunner data is still
    /// on the machine - "Import from OneClickRunner…" (spec §8.1).
    ///
    /// It lives apart from <see cref="CyrFlipContext"/> for one reason worth stating: the old items
    /// must be <b>copied out before being disposed</b>. <see cref="ToolStripItem.Dispose"/> removes
    /// the item from its owner's collection, so disposing while enumerating that same collection
    /// throws "Collection was modified" - verified on net48, and the same hazard the settings window
    /// works around in every one of its Reload*Rows methods. Being a plain static seam, the rebuild
    /// is covered directly by <c>LauncherTrayMenuTests</c>.
    /// </summary>
    internal static class LauncherTrayMenu
    {
        public static void Rebuild(
            ToolStripMenuItem parent,
            IList<LauncherScenario> scenarios,
            Action<LauncherScenario> run,
            Action manage,
            Action? importFromOneClickRunner,
            Func<string, string> translate)
        {
            parent.Text = translate("Быстрый запуск");
            Fill(parent.DropDownItems, scenarios, run, manage, importFromOneClickRunner, translate);
        }

        /// <summary>
        /// The same list as a standalone menu - what a left-click on the launcher's taskbar button
        /// drops down (<see cref="LauncherTaskbarWindow"/>). Same items in the same order, so the two
        /// surfaces can never drift apart.
        /// </summary>
        public static void Rebuild(
            ContextMenuStrip menu,
            IList<LauncherScenario> scenarios,
            Action<LauncherScenario> run,
            Action manage,
            Action? importFromOneClickRunner,
            Func<string, string> translate)
            => Fill(menu.Items, scenarios, run, manage, importFromOneClickRunner, translate);

        private static void Fill(
            ToolStripItemCollection items,
            IList<LauncherScenario> scenarios,
            Action<LauncherScenario> run,
            Action manage,
            Action? importFromOneClickRunner,
            Func<string, string> translate)
        {
            Clear(items);

            foreach (LauncherScenario scenario in scenarios)
            {
                LauncherScenario captured = scenario;
                items.Add(new ToolStripMenuItem(scenario.Name, null, (_, _) => run(captured)));
            }

            if (scenarios.Count > 0)
                items.Add(new ToolStripSeparator());

            items.Add(new ToolStripMenuItem(
                translate("Управление сценариями..."), null, (_, _) => manage()));

            if (importFromOneClickRunner != null)
                items.Add(new ToolStripMenuItem(
                    translate("Импорт из OneClickRunner..."), null, (_, _) => importFromOneClickRunner()));
        }

        /// <summary>Drop every item (the launcher was switched off), leaving the parent empty.</summary>
        public static void Clear(ToolStripMenuItem parent) => Clear(parent.DropDownItems);

        private static void Clear(ToolStripItemCollection items)
        {
            // Copy first, clear, then dispose - never dispose while enumerating the live collection.
            var previous = new ToolStripItem[items.Count];
            items.CopyTo(previous, 0);
            items.Clear();
            foreach (ToolStripItem old in previous) old.Dispose();
        }
    }
}

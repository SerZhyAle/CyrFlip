using System;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The page list is visible to assistive technology (ticket S0060 phase P4 H2, carried to the page rail by
    /// S0076, SETTINGS-NAVIGATION section 2 rule 7): the rail exposes a <c>List</c> whose children are the
    /// pages - each a <c>ListItem</c> named by its caption, with the selected one marked and a real screen
    /// rectangle - and the page host beside it is one pane, so a walker does not read the page list twice. UI
    /// Automation's live enumeration stays a manual pass; these gates hold the object model a walker reads.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class SettingsTabAccessibilityTests
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

        [UiSweepFact]
        public void PageListExposesListItems()
        {
            OnUiThread(() =>
            {
                var config = new AppConfig { UiLanguage = "English", SettingsGroupsState = "", SettingsViewport = "" };
                using var form = TestForms.NewSettings(config);
                form.Show();
                TabControl tabs = form.Tabs!;
                AccessibleObject list = form.Rail.AccessibilityObject;

                Assert.Equal(AccessibleRole.List, list.Role);
                Assert.Equal(tabs.TabPages.Count, list.GetChildCount());

                for (int i = 0; i < tabs.TabPages.Count; i++)
                {
                    AccessibleObject? item = list.GetChild(i);
                    Assert.NotNull(item);
                    Assert.Equal(AccessibleRole.ListItem, item!.Role);
                    Assert.Equal(tabs.TabPages[i].Text, item.Name);
                    Assert.Same(list, item.Parent);
                }
                // The page host is one pane holding the page on screen, never a second page list.
                Assert.Equal(AccessibleRole.Pane, tabs.AccessibilityObject.Role);
                Assert.Equal(1, tabs.AccessibilityObject.GetChildCount());
            });
        }

        [UiSweepFact]
        public void SelectedPageIsMarkedAndSelectable()
        {
            OnUiThread(() =>
            {
                var config = new AppConfig { UiLanguage = "English", SettingsGroupsState = "", SettingsViewport = "" };
                using var form = TestForms.NewSettings(config);
                form.Show();
                TabControl tabs = form.Tabs!;
                AccessibleObject list = form.Rail.AccessibilityObject;

                tabs.SelectedIndex = 2;
                AccessibleObject? selected = list.GetChild(2);
                Assert.NotNull(selected);
                Assert.Equal(AccessibleStates.Selected, selected!.State & AccessibleStates.Selected);
                // GetChild builds the item on the spot, so the objects compare by what a walker reads.
                Assert.Equal(selected.Name, list.GetSelected()!.Name);

                // The rest are selectable but not selected - the walker's whole picture of the list.
                AccessibleObject? other = list.GetChild(0);
                Assert.NotNull(other);
                Assert.Equal(AccessibleStates.None, other!.State & AccessibleStates.Selected);
                Assert.Equal(AccessibleStates.Selectable, other.State & AccessibleStates.Selectable);

                // Selecting an item through accessibility chooses a destination only - it edits nothing.
                other.Select(AccessibleSelection.TakeSelection);
                Assert.Equal(0, tabs.SelectedIndex);
            });
        }

        [UiSweepFact]
        public void RailItemsFollowALiveLanguageChange()
        {
            OnUiThread(() =>
            {
                var config = new AppConfig { UiLanguage = "Русский", SettingsGroupsState = "", SettingsViewport = "" };
                using var form = TestForms.NewSettings(config);
                TabControl tabs = form.Tabs!;
                AccessibleObject list = form.Rail.AccessibilityObject;
                string russian = list.GetChild(0)!.Name!;

                config.UiLanguage = "English";
                form.Reload();

                string english = tabs.TabPages[0].Text;
                Assert.NotEqual(russian, english);
                Assert.Equal(english, list.GetChild(0)!.Name);
            });
        }
    }
}

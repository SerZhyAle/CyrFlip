using System;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The page list is visible to assistive technology (ticket S0060, phase P4, H2): the owner-drawn
    /// strip exposes a <c>PageTabList</c> whose children are the pages - each a <c>TabItem</c> named by
    /// its caption, with the selected one marked and a real screen rectangle. UI Automation's live
    /// enumeration stays a manual pass; these gates hold the object model a walker reads.
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

        [Fact]
        public void PageListExposesTabItems()
        {
            OnUiThread(() =>
            {
                var config = new AppConfig { UiLanguage = "English", SettingsGroupsState = "", SettingsViewport = "" };
                using var form = TestForms.NewSettings(config);
                TabControl tabs = form.Tabs!;
                AccessibleObject list = tabs.AccessibilityObject;

                Assert.Equal(AccessibleRole.PageTabList, list.Role);
                Assert.Equal(tabs.TabPages.Count, list.GetChildCount());

                for (int i = 0; i < tabs.TabPages.Count; i++)
                {
                    AccessibleObject? item = list.GetChild(i);
                    Assert.NotNull(item);
                    Assert.Equal(AccessibleRole.PageTab, item!.Role);
                    Assert.Equal(tabs.TabPages[i].Text, item.Name);
                    Assert.Equal(tabs.RectangleToScreen(tabs.GetTabRect(i)), item.Bounds);
                }
            });
        }

        [Fact]
        public void SelectedPageIsMarkedAndSelectable()
        {
            OnUiThread(() =>
            {
                var config = new AppConfig { UiLanguage = "English", SettingsGroupsState = "", SettingsViewport = "" };
                using var form = TestForms.NewSettings(config);
                TabControl tabs = form.Tabs!;
                AccessibleObject list = tabs.AccessibilityObject;

                tabs.SelectedIndex = 2;
                AccessibleObject? selected = list.GetChild(2);
                Assert.NotNull(selected);
                Assert.Equal(AccessibleStates.Selected | AccessibleStates.Focused,
                    selected!.State & (AccessibleStates.Selected | AccessibleStates.Focused));
                // GetChild builds the item on the spot, so the objects compare by what a walker reads.
                Assert.Equal(selected.Name, list.GetSelected()!.Name);

                // The rest are selectable but not selected - the walker's whole picture of the strip.
                AccessibleObject? other = list.GetChild(0);
                Assert.NotNull(other);
                Assert.Equal(AccessibleStates.None, other!.State & AccessibleStates.Selected);

                selected.Select(AccessibleSelection.TakeSelection);
                Assert.Equal(2, tabs.SelectedIndex);
            });
        }

        [Fact]
        public void TabItemsFollowALiveLanguageChange()
        {
            OnUiThread(() =>
            {
                var config = new AppConfig { UiLanguage = "Русский", SettingsGroupsState = "", SettingsViewport = "" };
                using var form = TestForms.NewSettings(config);
                TabControl tabs = form.Tabs!;
                AccessibleObject list = tabs.AccessibilityObject;
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

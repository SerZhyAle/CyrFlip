using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Two pieces of the settings window's own bookkeeping (ticket S0036 UI-10) that no screen shows:
    /// which texts are remembered as translatable captions, and the re-entrancy flag that keeps a
    /// reload from firing change handlers.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class SettingsFormBookkeepingTests
    {
        /// <summary>
        /// S0065 E-9: a failed settings save is told once, not on every toggle of a machine whose
        /// registry is denied - and a successful save re-arms the notice.
        /// </summary>
        [Fact]
        public void TheSaveFailureNoticeTellsOnceUntilASaveSucceeds()
        {
            bool told = false;
            Assert.True(SettingsForm.SaveNotice.ShouldTell(false, ref told));   // the first failure is told
            Assert.False(SettingsForm.SaveNotice.ShouldTell(false, ref told));  // still failing: silence
            Assert.False(SettingsForm.SaveNotice.ShouldTell(false, ref told));
            Assert.False(SettingsForm.SaveNotice.ShouldTell(true, ref told));   // saved: nothing to tell
            Assert.True(SettingsForm.SaveNotice.ShouldTell(false, ref told));   // the next failure tells again
        }

        private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        private static void OnSta(Action<SettingsForm> body)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using SettingsForm form = TestForms.NewSettings(new AppConfig { UiLanguage = "English" });
                    body(form);
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Exception("on the STA thread", failure);
        }

        /// <summary>
        /// A value label - a chord, the version, the marker size - is filled by Reload. Registered as a
        /// "source original", every re-translation would put its first value back over the live one.
        /// </summary>
        [Fact]
        public void NoValueLabelIsRememberedAsACaption()
        {
            OnSta(form =>
            {
                var remembered = (Dictionary<Control, string>)typeof(SettingsForm).GetField("_sourceTexts", Private)!.GetValue(form)!;
                foreach (string field in new[] { "_caseHotkeyValue", "_historyHotkeyValue", "_quickNotesHotkeyValue", "_markerSizeValue", "_version", "_opacityValue" })
                {
                    var label = (Control)typeof(SettingsForm).GetField(field, Private)!.GetValue(form)!;
                    Assert.False(remembered.ContainsKey(label), field + " is remembered as a caption: \"" + label.Text + "\"");
                }
                Assert.NotEmpty(remembered); // the walk itself did run
            });
        }

        /// <summary>Called inside a loading section, the toggle reload must leave the section loading.</summary>
        [Fact]
        public void ReloadingTheToggleComboRestoresTheLoadingFlag()
        {
            OnSta(form =>
            {
                FieldInfo loading = typeof(SettingsForm).GetField("_loading", Private)!;
                MethodInfo reload = typeof(SettingsForm).GetMethod("ReloadToggleCombo", Private)!;

                loading.SetValue(form, true);
                reload.Invoke(form, null);
                Assert.True((bool)loading.GetValue(form)!);

                loading.SetValue(form, false);
                reload.Invoke(form, null);
                Assert.False((bool)loading.GetValue(form)!);
            });
        }
    }
}

using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The size profile (ticket S0076 M2, UI-SIZING sections 2-3): the invariant tokens and offered steps, the
    /// way an unknown value reads as ordinary, the profile reaching a real window when its handle is created and
    /// again when the user chooses another, typography following interface scale and text enlargement together,
    /// and the covered components measuring again. The manager is process-wide state, so every test that
    /// initializes it runs in the serialized shared-GDI collection and shuts it down.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class UiProfileTests
    {
        private static void Sta(Action action)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { failure = ex; }
                finally { UiProfileManager.Shutdown(); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "UI check exceeded 60 seconds");
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        [Theory]
        [InlineData("pointer", false)]
        [InlineData("Touch", true)]
        [InlineData(" TOUCH ", true)]
        [InlineData("", false)]
        [InlineData("pen", false)]
        [InlineData(null, false)]
        public void InputTokenParsesAndUnknownReadsAsPointer(string? token, bool touch)
            => Assert.Equal(touch ? UiInput.Touch : UiInput.Pointer, UiProfiles.ParseInput(token));

        [Fact]
        public void OnlyOfferedStepsSurviveAndEverythingElseReadsAsOrdinary()
        {
            foreach (int step in UiProfiles.InterfaceSteps) Assert.Equal(step, UiProfiles.NormalizeInterface(step));
            foreach (int step in UiProfiles.TextSteps) Assert.Equal(step, UiProfiles.NormalizeText(step));
            foreach (int bad in new[] { int.MinValue, -1, 0, 1, 99, 101, 110, 301, int.MaxValue })
            {
                Assert.Equal(100, UiProfiles.NormalizeInterface(bad));
                Assert.Equal(100, UiProfiles.NormalizeText(bad));
            }
            // The range the drop-downs offer is inside what the resolver accepts, and starts and ends where the record says.
            Assert.Equal(75, UiProfiles.InterfaceSteps.Min()); Assert.Equal(200, UiProfiles.InterfaceSteps.Max());
            Assert.Equal(100, UiProfiles.TextSteps.Min()); Assert.Equal(200, UiProfiles.TextSteps.Max());
        }

        [Theory]
        [InlineData(false, 75, 24)]
        [InlineData(false, 100, 32)]
        [InlineData(false, 200, 64)]
        [InlineData(true, 75, 36)]
        [InlineData(true, 100, 48)]
        [InlineData(true, 200, 96)]
        public void TargetsAreTheDeclaredLogicalSizesAtEveryOfferedScale(bool touch, int percent, int target)
            => Assert.Equal(target, UiProfiles.Metrics(touch ? UiInput.Touch : UiInput.Pointer, percent, 100).Target);

        [Fact]
        public void TextEnlargementDoesNotChangeTargetsOrGlyphsOnlyTypography()
        {
            UiMetrics ordinary = UiProfiles.Metrics(UiInput.Pointer, 100, 100);
            UiMetrics large = UiProfiles.Metrics(UiInput.Pointer, 100, 200);
            Assert.Equal(ordinary.Target, large.Target);
            Assert.Equal(ordinary.Glyph, large.Glyph);
            Assert.Equal(ordinary.Gap, large.Gap);
            Assert.Equal(9f, UiProfiles.FontPoints(9f, ordinary));
            Assert.Equal(18f, UiProfiles.FontPoints(9f, large));
            Assert.Equal(13.5f, UiProfiles.FontPoints(9f, UiProfiles.Metrics(UiInput.Pointer, 150, 100)));
            Assert.Equal(22.5f, UiProfiles.FontPoints(9f, UiProfiles.Metrics(UiInput.Pointer, 125, 200)));
        }

        [Fact]
        public void ConfigStoresTheProfileAndAnUnreadableValueFallsBackToOrdinary()
        {
            var config = new AppConfig { UiInputProfile = "touch", UiInterfaceScale = 150, UiTextScale = 125 };
            MemoryConfigKey key = MemoryConfigKey.Capture(config);
            AppConfig back = key.Load();
            Assert.Equal("touch", back.UiInputProfile);
            Assert.Equal(150, back.UiInterfaceScale);
            Assert.Equal(125, back.UiTextScale);

            key.Values["UiInputProfile"] = "hand";
            key.Values["UiInterfaceScale"] = 137;
            key.Values["UiTextScale"] = 5000;
            AppConfig odd = key.Load();
            Assert.Equal("pointer", odd.UiInputProfile);
            Assert.Equal(100, odd.UiInterfaceScale);
            Assert.Equal(100, odd.UiTextScale);
        }

        [Fact]
        public void TheProfileIsThisPcsDisplayStateAndNeverTravelsInTheSettingsFile()
        {
            string[] keys = { "UiInputProfile", "UiInterfaceScale", "UiTextScale" };
            Assert.All(keys, k => Assert.DoesNotContain(SettingsFileInventory.Exported, e => e.Key == k));
            Assert.All(keys, k => Assert.Equal("this PC's display", SettingsFileInventory.Excluded[k]));
        }

        [Fact]
        public void ManagerIsInertUntilInitializedAndWindowsGetTheProfileAtHandleCreation()
        {
            Sta(() =>
            {
                using (var before = new ThemedForm())
                {
                    _ = before.Handle;
                    Assert.True(UiMetrics.For(before).Same(UiMetrics.Pointer));
                    Assert.False(UiProfileManager.IsInitialized);
                }

                UiProfileManager.Initialize(new AppConfig { UiInputProfile = "touch", UiInterfaceScale = 150, UiTextScale = 100 });
                using var form = new ThemedForm { Font = new Font("Segoe UI", 10f) };
                var child = new Panel(); form.Controls.Add(child);
                _ = form.Handle;
                UiMetrics seen = UiMetrics.For(child);   // a control inherits its window's profile
                Assert.True(seen.Touch); Assert.Equal(1.5, seen.InterfaceScale);
                Assert.Equal(15f, form.Font.SizeInPoints, 1);
            });
        }

        [Fact]
        public void ChoosingAnotherProfileReachesOpenWindowsOnceAndRestoresTheOriginalFont()
        {
            Sta(() =>
            {
                UiProfileManager.Initialize(new AppConfig());
                using var form = new ThemedForm { Font = new Font("Segoe UI", 9f) };
                var option = new BooleanOptionCheckBox { Text = "Keep the screen on", AutoSize = true };
                form.Controls.Add(option);
                form.Show();
                int ordinaryHeight = option.GetPreferredSize(Size.Empty).Height;
                Assert.Equal(9f, form.Font.SizeInPoints, 1);

                UiProfileManager.Set(UiInput.Pointer, 200, 100);
                Assert.Equal(18f, form.Font.SizeInPoints, 1);
                Assert.True(option.GetPreferredSize(Size.Empty).Height >= ordinaryHeight * 2 - 2, "the covered component must follow the scale");

                // Setting the same profile again does nothing (no resize on an unchanged choice).
                Font same = form.Font;
                UiProfileManager.Set(UiInput.Pointer, 200, 100);
                Assert.Same(same, form.Font);

                UiProfileManager.Set(UiInput.Touch, 100, 200);
                Assert.Equal(18f, form.Font.SizeInPoints, 1);   // text enlargement alone: the typography doubles
                Assert.True(UiMetrics.For(option).Touch);
                Assert.Equal(UiMetrics.For(option).Target, UiProfiles.Metrics(UiInput.Touch, 100, 200).Target);

                UiProfileManager.Set(UiInput.Pointer, 100, 100);
                Assert.Equal(9f, form.Font.SizeInPoints, 1);
            });
        }

        [Fact]
        public void AWindowsOwnFontChangeKeepsTheChosenSize()
        {
            Sta(() =>
            {
                UiProfileManager.Initialize(new AppConfig { UiInterfaceScale = 150, UiTextScale = 100 });
                using var form = new ThemedForm { Font = new Font("Segoe UI", 10f) };
                _ = form.Handle;
                Assert.Equal(15f, form.Font.SizeInPoints, 1);

                // A language with a script family of its own: the profile is composed on the new base at once.
                using var script = new Font("Arial", 9f);
                form.SetBaseFont(script);
                Assert.Equal("Arial", form.Font.FontFamily.Name);
                Assert.Equal(13.5f, form.Font.SizeInPoints, 1);

                // A font the window assigned itself becomes the base of the next profile, never scaled away.
                using var own = new Font("Tahoma", 8f);
                form.Font = own;
                UiProfileManager.Set(UiInput.Pointer, 200, 100);
                Assert.Equal("Tahoma", form.Font.FontFamily.Name);
                Assert.Equal(16f, form.Font.SizeInPoints, 1);
                UiProfileManager.Set(UiInput.Pointer, 100, 100);
                Assert.Same(own, form.Font);
            });
        }

        [UiSweepFact]
        public void SettingsWindowFollowsTheChosenProfileKeepsItsPageAndItsEdits()
        {
            Sta(() =>
            {
                var config = new AppConfig { UiLanguage = "English", SettingsGroupsState = "", SettingsViewport = "" };
                UiProfileManager.Initialize(config);
                using var form = TestForms.NewSettings(config);
                form.Show();
                form.Tabs!.SelectedIndex = 1;
                Application.DoEvents();
                float ordinary = form.Font.SizeInPoints;
                string page = form.Tabs.SelectedTab!.Text;

                UiProfileManager.Set(UiInput.Pointer, 150, 100);
                Application.DoEvents();
                Assert.Equal(Math.Round(ordinary * 1.5, 1), Math.Round(form.Font.SizeInPoints, 1));
                Assert.Equal(1, form.Tabs.SelectedIndex);
                Assert.Equal(page, form.Tabs.SelectedTab!.Text);
                // The rail's rows follow the profile too (their cached heights were released).
                Assert.True(form.Rail.RowBounds(0).Height >= UiProfiles.Metrics(UiInput.Pointer, 150, 100).Target - 1);

                UiProfileManager.Set(UiInput.Pointer, 100, 100);
                Application.DoEvents();
                Assert.Equal(ordinary, form.Font.SizeInPoints, 1);
            });
        }
    }
}

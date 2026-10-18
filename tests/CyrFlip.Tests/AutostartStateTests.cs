using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The packaged build reads its autostart state from the startupTask record Windows keeps under
    /// SystemAppData\{PackageFamilyName}\CyrFlipStartup\State. Before that read existed the checkbox
    /// reported "off" even while Windows was starting CyrFlip on every sign-in, so the decoding of
    /// that one value is worth pinning: the enum has five members and only two of them mean "on".
    /// </summary>
    public class AutostartStateTests
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        [Fact]
        public void Enabled_preference_survives_a_deleted_run_entry_and_restores_it()
        {
            WithRegistry(() =>
            {
                Autostart.Set(true);
                using var run = RuntimeRegistry.User.CreateSubKey(RunKey);
                string command = (string)run.GetValue("CyrFlip")!;
                run.DeleteValue("CyrFlip");
                Assert.True(Autostart.IsEnabled);
                Autostart.SyncToThisExe();
                Assert.Equal(command, run.GetValue("CyrFlip"));
            });
        }

        [Fact]
        public void Unchecking_prevents_restoration_on_later_launches()
        {
            WithRegistry(() =>
            {
                Autostart.Set(true);
                Autostart.Set(false);
                Autostart.SyncToThisExe();
                Assert.False(Autostart.IsEnabled);
                using var run = RuntimeRegistry.User.OpenSubKey(RunKey);
                Assert.Null(run?.GetValue("CyrFlip"));
            });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Upgrade_adopts_existing_autostart_once(bool hadEntry)
        {
            WithRegistry(() =>
            {
                using var run = RuntimeRegistry.User.CreateSubKey(RunKey);
                if (hadEntry) run.SetValue("CyrFlip", "old.exe", Microsoft.Win32.RegistryValueKind.String);
                Autostart.SyncToThisExe();
                Assert.Equal(hadEntry, Autostart.IsEnabled);
                run.DeleteValue("CyrFlip");
                Autostart.SyncToThisExe();
                Assert.Equal(hadEntry, run.GetValue("CyrFlip") != null);
            });
        }

        private static void WithRegistry(System.Action test)
        {
            var original = RuntimeRegistry.UserOverride.Value;
            RuntimeRegistry.UserOverride.Value = new RuntimeDataSafetyTests.FakeRegistry();
            try { test(); }
            finally { RuntimeRegistry.UserOverride.Value = original; }
        }

        [Theory]
        [InlineData(0, false)] // Disabled
        [InlineData(1, false)] // DisabledByUser
        [InlineData(2, true)]  // Enabled
        [InlineData(3, false)] // DisabledByPolicy
        [InlineData(4, true)]  // EnabledByPolicy
        public void StartupTaskState_maps_to_the_checkbox(int state, bool expected)
        {
            Assert.Equal(expected, Autostart.IsEnabledState(state));
        }

        [Fact]
        public void A_missing_or_unreadable_record_reads_as_off()
        {
            Assert.False(Autostart.IsEnabledState(null));          // never touched = manifest default
            Assert.False(Autostart.IsEnabledState("2"));           // not a DWORD: never guess
            Assert.False(Autostart.IsEnabledState(new byte[] { 2 }));
        }
    }
}

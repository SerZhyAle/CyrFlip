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

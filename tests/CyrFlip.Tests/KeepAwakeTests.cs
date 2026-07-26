using CyrFlip;
using Xunit;
using ES = CyrFlip.WindowInterop.EXECUTION_STATE;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Unit coverage for <see cref="KeepAwake"/>'s mask composition (spec §10). The real
    /// <c>SetThreadExecutionState</c> P/Invoke is swapped for a recorder so the tests never touch the
    /// machine's power policy. <see cref="KeepAwake"/> holds static state; the constructor runs before
    /// every <c>[Fact]</c>, re-pointing the delegate at this instance and resetting to both-off.
    /// </summary>
    public class KeepAwakeTests
    {
        private ES _last;

        public KeepAwakeTests()
        {
            KeepAwake.SetExecutionState = flags => { _last = flags; return flags; };
            KeepAwake.Reset();
        }

        [Fact]
        public void BothOff_IsBareContinuous()
        {
            KeepAwake.Reset();
            Assert.Equal(ES.ES_CONTINUOUS, _last);
            Assert.False(KeepAwake.KeepSystemAwake);
            Assert.False(KeepAwake.KeepScreenOn);
        }

        [Fact]
        public void SystemAwakeOnly_AddsSystemRequiredNotDisplay()
        {
            KeepAwake.SetSystemAwake(true);
            Assert.Equal(ES.ES_CONTINUOUS | ES.ES_SYSTEM_REQUIRED, _last);
            Assert.True(KeepAwake.KeepSystemAwake);
            Assert.False(KeepAwake.KeepScreenOn);
            Assert.False(_last.HasFlag(ES.ES_DISPLAY_REQUIRED));
        }

        [Fact]
        public void ScreenOnOnly_AddsDisplayRequiredNotSystem()
        {
            KeepAwake.SetScreenOn(true);
            Assert.Equal(ES.ES_CONTINUOUS | ES.ES_DISPLAY_REQUIRED, _last);
            Assert.True(KeepAwake.KeepScreenOn);
            Assert.False(KeepAwake.KeepSystemAwake);
            Assert.False(_last.HasFlag(ES.ES_SYSTEM_REQUIRED));
        }

        [Fact]
        public void BothOn_SetsBothBits()
        {
            KeepAwake.SetSystemAwake(true);
            KeepAwake.SetScreenOn(true);
            Assert.Equal(ES.ES_CONTINUOUS | ES.ES_SYSTEM_REQUIRED | ES.ES_DISPLAY_REQUIRED, _last);
            Assert.True(KeepAwake.KeepSystemAwake);
            Assert.True(KeepAwake.KeepScreenOn);
        }

        [Fact]
        public void RepeatedSet_IsIdempotent()
        {
            KeepAwake.SetSystemAwake(true);
            KeepAwake.SetSystemAwake(true);
            Assert.Equal(ES.ES_CONTINUOUS | ES.ES_SYSTEM_REQUIRED, _last);
            Assert.True(KeepAwake.KeepSystemAwake);
        }

        [Fact]
        public void TogglingOneOff_LeavesTheOther()
        {
            KeepAwake.SetSystemAwake(true);
            KeepAwake.SetScreenOn(true);
            KeepAwake.SetSystemAwake(false);
            Assert.Equal(ES.ES_CONTINUOUS | ES.ES_DISPLAY_REQUIRED, _last);
            Assert.False(KeepAwake.KeepSystemAwake);
            Assert.True(KeepAwake.KeepScreenOn);
        }

        [Fact]
        public void Reset_ClearsBothToBareContinuous()
        {
            KeepAwake.SetSystemAwake(true);
            KeepAwake.SetScreenOn(true);
            KeepAwake.Reset();
            Assert.Equal(ES.ES_CONTINUOUS, _last);
            Assert.False(KeepAwake.KeepSystemAwake);
            Assert.False(KeepAwake.KeepScreenOn);
        }

        [Fact]
        public void BuildMask_MatchesWhatWasSent()
        {
            KeepAwake.SetScreenOn(true);
            Assert.Equal(_last, KeepAwake.BuildMask());
        }
    }
}

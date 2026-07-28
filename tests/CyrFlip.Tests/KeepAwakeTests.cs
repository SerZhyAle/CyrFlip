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
        private int _calls;

        public KeepAwakeTests()
        {
            KeepAwake.SetExecutionState = flags => { _last = flags; _calls++; return flags; };
            KeepAwake.Reset();
            _calls = 0;
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

        /// <summary>
        /// Startup with both switches saved on: one call carrying both bits. Two calls would ask
        /// Windows for half the policy first, and the count is the only way to see the difference.
        /// </summary>
        [Fact]
        public void Restore_BothOn_SendsBothBitsInOneCall()
        {
            KeepAwake.Restore(true, true);
            Assert.Equal(1, _calls);
            Assert.Equal(ES.ES_CONTINUOUS | ES.ES_SYSTEM_REQUIRED | ES.ES_DISPLAY_REQUIRED, _last);
            Assert.True(KeepAwake.KeepSystemAwake);
            Assert.True(KeepAwake.KeepScreenOn);
        }

        [Fact]
        public void Restore_OneOn_SendsOnlyThatBit()
        {
            KeepAwake.Restore(false, true);
            Assert.Equal(ES.ES_CONTINUOUS | ES.ES_DISPLAY_REQUIRED, _last);
            Assert.False(KeepAwake.KeepSystemAwake);
            Assert.True(KeepAwake.KeepScreenOn);
        }

        /// <summary>
        /// The common case - both saved off - must leave the machine's idle policy alone rather than
        /// send a bare ES_CONTINUOUS that clears whatever another app asked for before us.
        /// </summary>
        [Fact]
        public void Restore_BothOff_FromIdle_SendsNothing()
        {
            KeepAwake.Restore(false, false);
            Assert.Equal(0, _calls);
            Assert.False(KeepAwake.KeepSystemAwake);
            Assert.False(KeepAwake.KeepScreenOn);
        }

        /// <summary>
        /// ..but a restore to off while a request of ours is standing still has to send the reset -
        /// the "send nothing" shortcut may never leave a live request behind.
        /// </summary>
        [Fact]
        public void Restore_BothOff_AfterARequest_StillClearsIt()
        {
            KeepAwake.SetSystemAwake(true);
            KeepAwake.Restore(false, false);
            Assert.Equal(ES.ES_CONTINUOUS, _last);
            Assert.False(KeepAwake.KeepSystemAwake);
            Assert.False(KeepAwake.KeepScreenOn);
        }
    }
}

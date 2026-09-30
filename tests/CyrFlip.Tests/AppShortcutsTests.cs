using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The unpackaged build's Start menu and desktop shortcuts. What is pinned is the decision, not the
    /// .lnk writing: a first run creates, a later run never recreates what the user deleted, an existing
    /// link follows the exe when the folder moves, and a link the user aimed elsewhere is left alone.
    /// </summary>
    public class AppShortcutsTests
    {
        private const string Exe = @"C:\Tools\CyrFlip\CyrFlip.exe";

        [Fact]
        public void A_first_run_creates_a_missing_shortcut() =>
            Assert.Equal(ShortcutAction.Create, AppShortcuts.Decide(firstRunDone: false, linkExists: false, linkTarget: null, Exe));

        [Fact]
        public void A_shortcut_the_user_deleted_is_never_recreated_by_a_start() =>
            Assert.Equal(ShortcutAction.None, AppShortcuts.Decide(firstRunDone: true, linkExists: false, linkTarget: null, Exe));

        [Fact]
        public void A_shortcut_to_this_exe_is_left_alone() =>
            Assert.Equal(ShortcutAction.None, AppShortcuts.Decide(true, true, @"c:\tools\cyrflip\CYRFLIP.EXE", Exe));

        [Fact]
        public void A_shortcut_to_a_moved_copy_follows_the_running_exe() =>
            Assert.Equal(ShortcutAction.Retarget, AppShortcuts.Decide(true, true, @"D:\Old\CyrFlip.exe", Exe));

        [Fact]
        public void An_existing_shortcut_on_a_first_run_is_repointed_rather_than_duplicated() =>
            Assert.Equal(ShortcutAction.Retarget, AppShortcuts.Decide(false, true, @"D:\Old\CyrFlip.exe", Exe));

        [Theory]
        [InlineData(@"C:\Windows\notepad.exe")]
        [InlineData(null)]
        [InlineData("")]
        public void A_shortcut_that_is_not_ours_or_unreadable_is_left_alone(string? target) =>
            Assert.Equal(ShortcutAction.None, AppShortcuts.Decide(true, true, target, Exe));

        [Theory]
        [InlineData(@"C:\Users\u\AppData\Local\Temp\Temp1_CyrFlip.zip\CyrFlip.exe", true)]
        [InlineData(@"P:\src\CyrFlip\bin\Release\net48\CyrFlip.exe", true)]
        [InlineData(@"P:\src\CyrFlip\bin\Debug\net48\CyrFlip.exe", true)]
        [InlineData(@"C:\Users\u\Downloads\CyrFlip\CyrFlip.exe", false)]
        [InlineData(@"C:\Users\u\AppData\Local\Microsoft\WinGet\Packages\SerZhyAle.CyrFlip_x\CyrFlip.exe", false)]
        [InlineData(@"C:\Users\u\AppData\Local\Temperature\CyrFlip.exe", false)]
        [InlineData("", true)]
        public void Transient_locations_are_never_linked(string exe, bool transient) =>
            Assert.Equal(transient, AppShortcuts.IsTransientLocation(exe, @"C:\Users\u\AppData\Local\Temp\"));
    }
}

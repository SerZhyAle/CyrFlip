using System;
using System.Collections.Generic;
using CyrFlip;
using Xunit;
using static CyrFlip.WindowInterop;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The keyboard hook without a desktop: the Win32 hook calls behind a seam, the events fed to
    /// <see cref="KeyboardHook.Decide"/> directly (ticket S0004, KC-1, KC-3, KC-8).
    /// </summary>
    public class KeyboardHookTests
    {
        private sealed class FakeHooks
        {
            public readonly Queue<IntPtr> Results = new Queue<IntPtr>();
            public readonly List<IntPtr> Unhooked = new List<IntPtr>();

            public KeyboardHook Create()
                => new KeyboardHook(_ => Results.Count > 0 ? Results.Dequeue() : (IntPtr)99,
                    handle => { Unhooked.Add(handle); return true; }, new PhysicalModifiers());
        }

        private static void Install(KeyboardHook hook, bool enabled = true)
            => hook.Install(Hotkey.CaseDefault, Hotkey.Parse("Ctrl+Shift+F10"), Hotkey.Parse(AppConfig.DefaultQuickNotesHotkey),
                deferInRemoteClient: false, enabled: enabled, caseEnabled: true, historyEnabled: true, quickNotesEnabled: false);

        private static KBDLLHOOKSTRUCT Key(uint vk, uint time, bool injectedByUs = false) => new KBDLLHOOKSTRUCT
        {
            vkCode = vk,
            time = time,
            flags = injectedByUs ? LLKHF_INJECTED : 0,
            dwExtraInfo = injectedByUs ? KeyInjection.Tag : IntPtr.Zero,
        };

        [Fact]
        public void AFailedReinstallKeepsTheOldHookAndKeepsReportingFailure()
        {
            var fake = new FakeHooks();
            fake.Results.Enqueue((IntPtr)1);            // Install
            fake.Results.Enqueue(IntPtr.Zero);          // three failed re-arms
            fake.Results.Enqueue(IntPtr.Zero);
            fake.Results.Enqueue(IntPtr.Zero);
            fake.Results.Enqueue((IntPtr)2);            // then one that works
            KeyboardHook hook = fake.Create();
            Install(hook);

            Assert.False(hook.Reinstall(refreshModifiers: false));
            Assert.False(hook.Reinstall(refreshModifiers: false));
            Assert.False(hook.Reinstall(refreshModifiers: false));
            Assert.Empty(fake.Unhooked); // the old hook may still be alive - never removed on a failure
            Assert.True(hook.Installed);

            Assert.True(hook.Reinstall(refreshModifiers: false));
            Assert.Equal(new[] { (IntPtr)1 }, fake.Unhooked); // the new one went in before the old came out
        }

        [Fact]
        public void ADisposedHookIsNotRearmed()
        {
            var fake = new FakeHooks();
            fake.Results.Enqueue((IntPtr)1);
            KeyboardHook hook = fake.Create();
            Install(hook);
            hook.Dispose();

            Assert.True(hook.Reinstall(refreshModifiers: false));
            Assert.False(hook.Installed);
            Assert.Equal(new[] { (IntPtr)1 }, fake.Unhooked);
        }

        [Fact]
        public void HeldChordFiresOnEachTapEvenAfterOurInjectedKeyUps()
        {
            KeyboardHook hook = new FakeHooks().Create();
            Install(hook);
            int fired = 0;
            var masks = new List<SideModifiers>();
            hook.CaseHotkeyPressed += (_, _) => fired++;
            hook.ChordFired += held => masks.Add(held);

            const uint F11 = 0x7A;
            Assert.False(hook.Decide(Key(0xA2, 10), down: true));  // LCtrl
            Assert.False(hook.Decide(Key(0xA0, 20), down: true));  // LShift
            Assert.True(hook.Decide(Key(F11, 30), down: true));    // fires
            Assert.True(hook.Decide(Key(F11, 60), down: true));    // repeat: swallowed, no event
            Assert.True(hook.Decide(Key(F11, 90), down: false));   // release: swallowed
            // The flip's injected key-ups.
            Assert.False(hook.Decide(Key(0xA0, 100, injectedByUs: true), down: false));
            Assert.False(hook.Decide(Key(0xA2, 100, injectedByUs: true), down: false));
            Assert.True(hook.Decide(Key(F11, 200), down: true));   // second tap: fires again

            Assert.Equal(2, fired);
            Assert.Equal(new[] { SideModifiers.LCtrl | SideModifiers.LShift, SideModifiers.LCtrl | SideModifiers.LShift }, masks);
        }

        // Windows hands a low-level hook PrintScreen as a key-up only, so a chord on it has to fire
        // on the release - and must not own the trigger afterwards, or it would eat the next press.
        [Theory]
        [InlineData("Ctrl+PrintScreen", new[] { 0xA2u })]
        [InlineData("Ctrl+Shift+PrintScreen", new[] { 0xA2u, 0xA0u })]
        public void APrintScreenChordFiresOnTheLoneKeyUpAndEveryTimeAfter(string chord, uint[] modifiers)
        {
            const uint PrintScreen = 0x2C;
            KeyboardHook hook = new FakeHooks().Create();
            Install(hook);
            hook.UpdateScreenshotHotkey(Hotkey.Parse(chord));
            hook.UpdateScreenshotEnabled(true);
            int fired = 0;
            hook.ScreenshotHotkeyPressed += (_, _) => fired++;

            uint time = 10;
            foreach (uint modifier in modifiers) hook.Decide(Key(modifier, time += 10), down: true);

            Assert.True(hook.Decide(Key(PrintScreen, time += 10), down: false)); // the only event Windows sends
            Assert.Equal(1, fired);
            Assert.True(hook.Decide(Key(PrintScreen, time += 300), down: false)); // second press: not eaten as a "release"
            Assert.Equal(2, fired);
        }

        [Fact]
        public void APrintScreenKeyUpWithoutTheChordsModifiersPassesThrough()
        {
            KeyboardHook hook = new FakeHooks().Create();
            Install(hook);
            hook.UpdateScreenshotHotkey(Hotkey.Parse("Ctrl+PrintScreen"));
            hook.UpdateScreenshotEnabled(true);
            int fired = 0;
            hook.ScreenshotHotkeyPressed += (_, _) => fired++;

            Assert.False(hook.Decide(Key(0x2C, 10), down: false)); // bare PrintScreen is Windows' own
            Assert.Equal(0, fired);
        }

        [Fact]
        public void APrintScreenChordStillWorksWhereTheKeyDownDoesArrive()
        {
            KeyboardHook hook = new FakeHooks().Create();
            Install(hook);
            hook.UpdateScreenshotHotkey(Hotkey.Parse("Ctrl+PrintScreen"));
            hook.UpdateScreenshotEnabled(true);
            int fired = 0;
            hook.ScreenshotHotkeyPressed += (_, _) => fired++;

            hook.Decide(Key(0xA2, 10), down: true);
            Assert.True(hook.Decide(Key(0x2C, 20), down: true));  // fires on the down
            Assert.True(hook.Decide(Key(0x2C, 50), down: false)); // its release is swallowed, no second fire
            Assert.Equal(1, fired);
        }

        [Fact]
        public void EscapeCancelsATranslationWithTheMasterSwitchOff()
        {
            KeyboardHook hook = new FakeHooks().Create();
            Install(hook, enabled: false);
            hook.UpdateCancelKeyWatch(true);
            int cancelled = 0;
            hook.CancelKeyPressed += (_, _) => cancelled++;

            Assert.False(hook.Decide(Key(0x1B, 10), down: true)); // passed through, never swallowed
            Assert.Equal(1, cancelled);
        }

        [Fact]
        public void TheMasterSwitchStillPassesChordsThrough()
        {
            KeyboardHook hook = new FakeHooks().Create();
            Install(hook, enabled: false);
            int fired = 0;
            hook.CaseHotkeyPressed += (_, _) => fired++;
            hook.Decide(Key(0xA2, 10), down: true);
            hook.Decide(Key(0xA0, 20), down: true);
            Assert.False(hook.Decide(Key(0x7A, 30), down: true));
            Assert.Equal(0, fired);
        }

        [Fact]
        public void SuspendedChordsPassThroughWithoutFiring()
        {
            KeyboardHook hook = new FakeHooks().Create();
            Install(hook, enabled: true);
            int fired = 0;
            hook.CaseHotkeyPressed += (_, _) => fired++;

            KeyboardHook.SuspendChords = true;
            try
            {
                hook.Decide(Key(0xA2, 10), down: true); // LCtrl
                hook.Decide(Key(0xA0, 20), down: true); // LShift
                Assert.False(hook.Decide(Key(0x7A, 30), down: true)); // F11 passes through, not swallowed
                Assert.Equal(0, fired);
            }
            finally
            {
                KeyboardHook.SuspendChords = false;
            }

            // Once resumed, chords fire normally
            Assert.True(hook.Decide(Key(0x7A, 50), down: true));
            Assert.Equal(1, fired);
        }
    }
}


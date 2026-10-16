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
        public void BareF1AlwaysPassesThroughTheGlobalHook()
        {
            using KeyboardHook hook = new FakeHooks().Create();
            Install(hook);
            Assert.False(hook.Decide(Key(0x70, 10), down: true));
            Assert.False(hook.Decide(Key(0x70, 20), down: false));
            hook.Install(Hotkey.Parse("F1"), Hotkey.Parse("Ctrl+Shift+F10"), Hotkey.Parse(AppConfig.DefaultQuickNotesHotkey),
                deferInRemoteClient: false, enabled: true, caseEnabled: true, historyEnabled: true, quickNotesEnabled: false);
            Assert.False(hook.Decide(Key(0x70, 30), down: true));
            Assert.False(hook.Decide(Key(0x70, 40), down: false));
        }

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

        // ---- The screenshots folder chord (S0057) ----

        [Fact]
        public void TheScreenshotFolderChordFiresWhileEnabledAndPassesThroughWhileDisabled()
        {
            // S0071 re-keyed the shipped folder chord to Ctrl+F10.
            const uint F10 = 0x79;
            KeyboardHook hook = new FakeHooks().Create();
            Install(hook);
            hook.UpdateScreenshotFolderHotkey(Hotkey.Parse(AppConfig.DefaultScreenshotFolderHotkey));
            int fired = 0;
            hook.ScreenshotFolderHotkeyPressed += (_, _) => fired++;

            // Not bound: saving is off, the keystroke belongs to whatever else wants it (S0057 D2).
            Assert.False(hook.Decide(Key(0xA2, 10), down: true));
            Assert.False(hook.Decide(Key(F10, 30), down: true));
            Assert.Equal(0, fired);

            // Live: the press fires and is swallowed with its repeat and its release. Modifier keys
            // themselves always pass through (ChordMatcher never swallows them).
            hook.UpdateScreenshotFolderEnabled(true);
            Assert.False(hook.Decide(Key(0xA2, 100), down: true));
            Assert.True(hook.Decide(Key(F10, 110), down: true));
            Assert.True(hook.Decide(Key(F10, 130), down: false)); // release: swallowed, no second fire
            Assert.Equal(1, fired);
        }

        [Fact]
        public void TheScreenshotFolderChordOwnsItsRepeatsAndItsRelease()
        {
            KeyboardHook hook = new FakeHooks().Create();
            Install(hook);
            hook.UpdateScreenshotFolderHotkey(Hotkey.Parse("Ctrl+Shift+Alt+F7"));
            hook.UpdateScreenshotFolderEnabled(true);
            int fired = 0;
            hook.ScreenshotFolderHotkeyPressed += (_, _) => fired++;

            const uint F7 = 0x76;
            Assert.False(hook.Decide(Key(0xA2, 10), down: true));
            Assert.False(hook.Decide(Key(0xA0, 20), down: true));
            Assert.False(hook.Decide(Key(0xA4, 30), down: true));
            Assert.True(hook.Decide(Key(F7, 40), down: true));   // fires
            Assert.True(hook.Decide(Key(F7, 60), down: true));   // auto-repeat: swallowed, no event
            Assert.True(hook.Decide(Key(F7, 80), down: false)); // release: swallowed, no event
            Assert.Equal(1, fired);
            Assert.True(hook.Decide(Key(F7, 200), down: true));  // the next press fires again
            Assert.Equal(2, fired);
        }

        [Fact]
        public void FewerModifiersNeverFireTheChordThatNeedsMore()
        {
            // S0071's chord map keeps two shipped prefix pairs, and exact matching (INPUT-CHORD rule 9)
            // tells each pair apart: folder Ctrl+F10 vs history Ctrl+Shift+F10, full capture Ctrl+F11
            // vs case flip Ctrl+Shift+F11.
            const uint F10 = 0x79, F11 = 0x7A;
            KeyboardHook hook = new FakeHooks().Create();
            Install(hook);
            hook.UpdateScreenshotFolderHotkey(Hotkey.Parse(AppConfig.DefaultScreenshotFolderHotkey));
            hook.UpdateScreenshotFolderEnabled(true);
            hook.UpdateScreenshotFullHotkey(Hotkey.Parse(AppConfig.DefaultScreenshotFullHotkey));
            hook.UpdateScreenshotFullEnabled(true);
            int folder = 0, history = 0, full = 0, caseFlip = 0;
            hook.ScreenshotFolderHotkeyPressed += (_, _) => folder++;
            hook.ClipboardHistoryHotkeyPressed += (_, _) => history++;
            hook.ScreenshotFullHotkeyPressed += (_, _) => full++;
            hook.CaseHotkeyPressed += (_, _) => caseFlip++;

            // Ctrl+F10: the folder's.
            Assert.False(hook.Decide(Key(0xA2, 10), down: true));
            Assert.True(hook.Decide(Key(F10, 30), down: true));
            Assert.True(hook.Decide(Key(F10, 40), down: false));  // its release is swallowed
            Assert.Equal(1, folder);
            Assert.Equal(0, history);

            // Ctrl+Shift+F10: the history's, not the folder's (rule 9 - exact matching).
            Assert.False(hook.Decide(Key(0xA0, 60), down: true));
            Assert.True(hook.Decide(Key(F10, 70), down: true));
            Assert.True(hook.Decide(Key(F10, 80), down: false));
            Assert.Equal(1, folder);
            Assert.Equal(1, history);

            // Ctrl+F11: the full capture's (Shift released, Ctrl still held).
            Assert.False(hook.Decide(Key(0xA0, 90), down: false));
            Assert.True(hook.Decide(Key(F11, 110), down: true));
            Assert.True(hook.Decide(Key(F11, 120), down: false));
            Assert.Equal(1, full);
            Assert.Equal(0, caseFlip);

            // Ctrl+Shift+F11: the case flip's, not the capture's.
            Assert.False(hook.Decide(Key(0xA0, 140), down: true));
            Assert.True(hook.Decide(Key(F11, 150), down: true));
            Assert.Equal(1, full);
            Assert.Equal(1, caseFlip);
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

        [Fact]
        public void ExchangeSendAndGetChordsFireWhenBoundAndEnabled()
        {
            KeyboardHook hook = new FakeHooks().Create();
            Install(hook, enabled: true);
            hook.UpdateExchangeSendHotkey(Hotkey.Parse("Ctrl+Shift+F8"));
            hook.UpdateExchangeSendEnabled(true);
            hook.UpdateExchangeGetHotkey(Hotkey.Parse("Ctrl+Shift+F9"));
            hook.UpdateExchangeGetEnabled(true);

            int sent = 0;
            int got = 0;
            hook.ExchangeSendHotkeyPressed += (_, _) => sent++;
            hook.ExchangeGetHotkeyPressed += (_, _) => got++;

            // LCtrl + LShift + F8 -> Send
            hook.Decide(Key(0xA2, 10), down: true);
            hook.Decide(Key(0xA0, 20), down: true);
            Assert.True(hook.Decide(Key(0x77, 30), down: true)); // F8 = 0x77
            Assert.Equal(1, sent);
            Assert.Equal(0, got);

            // Release F8, press F9 -> Get
            hook.Decide(Key(0x77, 40), down: false);
            Assert.True(hook.Decide(Key(0x78, 50), down: true)); // F9 = 0x78
            Assert.Equal(1, sent);
            Assert.Equal(1, got);
        }
    }
}


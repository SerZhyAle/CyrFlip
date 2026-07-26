using System;
using System.Collections.Generic;
using System.Reflection;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The conversion table is open-ended and lives in the registry, so the hook must be picky about
    /// what it turns into a live global chord: a half-written or corrupted row has to stay inert
    /// rather than fall back to the default chord and shadow the EN ⇄ RU row bound to it.
    /// </summary>
    public class LayoutConversionBindingTests
    {
        [Fact]
        public void GarbageChordDoesNotFallBackToTheDefaultHotkey()
        {
            Assert.False(Hotkey.TryParse("Ctrl+Shift+", out _));
            Assert.False(Hotkey.TryParse("", out _));
            Assert.False(Hotkey.TryParse(null, out _));

            // Parse keeps its documented fallback - which is exactly why the hook uses TryParse.
            Assert.True(Hotkey.Parse("Ctrl+Shift+").SameChord(Hotkey.Default));

            Assert.True(Hotkey.TryParse("Alt+Shift+F12", out Hotkey good));
            Assert.Equal("Shift+Alt+F12", good.Display); // Display normalizes to Ctrl, Shift, Alt, Win order
        }

        [Fact]
        public void OnlyCompleteProfilesBecomeLiveBindings()
        {
            var profiles = new List<LayoutConversionProfile>
            {
                new LayoutConversionProfile { SourceKlid = "00000409", TargetKlid = "00000419", Hotkey = "Ctrl+Shift+F12" },
                new LayoutConversionProfile { SourceKlid = "00000409", TargetKlid = "00000422", Hotkey = "Alt+Shift+F12" },
                new LayoutConversionProfile { SourceKlid = "00000409", TargetKlid = "00000419", Hotkey = "" },        // no chord yet
                new LayoutConversionProfile { SourceKlid = "00000409", TargetKlid = "", Hotkey = "Ctrl+Alt+F12" },    // no target
                new LayoutConversionProfile { SourceKlid = "00000409", TargetKlid = "00000419", Hotkey = "Ctrl+++" }, // corrupt chord
            };

            using var hook = new KeyboardHook();
            hook.UpdateConversionProfiles(profiles);

            Array bindings = (Array)typeof(KeyboardHook)
                .GetField("_conversionProfiles", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(hook)!;
            Assert.Equal(2, bindings.Length);
        }

        [Fact]
        public void BindingsAreSnapshotsSoLaterEditsCannotChangeALiveChord()
        {
            var profile = new LayoutConversionProfile { SourceKlid = "00000409", TargetKlid = "00000419", Hotkey = "Ctrl+Shift+F9" };
            using var hook = new KeyboardHook();
            hook.UpdateConversionProfiles(new List<LayoutConversionProfile> { profile });

            profile.Hotkey = "Ctrl+Shift+F8"; // the settings window edits the config object in place

            Array bindings = (Array)typeof(KeyboardHook)
                .GetField("_conversionProfiles", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(hook)!;
            object binding = bindings.GetValue(0)!;
            var bound = (Hotkey)binding.GetType().GetField("Hotkey")!.GetValue(binding)!;
            Assert.Equal("Ctrl+Shift+F9", bound.Display);
        }
    }
}

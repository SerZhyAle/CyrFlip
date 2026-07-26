using System.Collections.Generic;
using System.Reflection;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The hook's translation bindings, guarded the same way the conversion ones are: a row the user
    /// is editing must never be the row the callback is walking, a corrupt chord must stay inert
    /// rather than fall back to Ctrl+Shift+F12 (which the seeded EN ⇄ RU flip owns), and a disabled
    /// row must not swallow its key. None of that is visible from the outside, so the snapshot array
    /// is read back by reflection.
    /// </summary>
    public class TranslationBindingTests
    {
        [Fact]
        public void OnlyCompleteRowsBecomeLiveBindings()
        {
            var hook = new KeyboardHook();
            hook.UpdateTranslationProfiles(new List<TranslationProfile>
            {
                new TranslationProfile { TargetLang = "en", Hotkey = "Ctrl+Alt+E" },   // good
                new TranslationProfile { TargetLang = "de", Hotkey = "" },             // no chord yet
                new TranslationProfile { TargetLang = "", Hotkey = "Ctrl+Alt+D" },     // no target
                new TranslationProfile { TargetLang = "fr", Hotkey = "Ctrl+++" },      // unparseable
                new TranslationProfile { TargetLang = "uk", Hotkey = "Ctrl+Alt+U" },   // good
            });

            Assert.Equal(2, Bindings(hook).Length);
        }

        [Fact]
        public void ACorruptChordIsInertRatherThanSilentlyBecomingTheDefault()
        {
            var hook = new KeyboardHook();
            hook.UpdateTranslationProfiles(new[] { new TranslationProfile { TargetLang = "en", Hotkey = "nonsense" } });

            // Hotkey.Parse would answer Ctrl+Shift+F12 here - the flip's chord. TryParse must be used.
            Assert.Empty(Bindings(hook));
        }

        [Fact]
        public void BindingsAreSnapshotsSoALaterEditCannotChangeALiveChord()
        {
            var hook = new KeyboardHook();
            var row = new TranslationProfile { TargetLang = "en", Hotkey = "Ctrl+Alt+E", Enabled = true };
            hook.UpdateTranslationProfiles(new[] { row });

            row.Hotkey = "Ctrl+Alt+X";
            row.Enabled = false;
            row.TargetLang = "de";

            object binding = Assert.Single(Bindings(hook));
            var bound = (TranslationProfile)binding.GetType()
                .GetField("Profile", BindingFlags.Public | BindingFlags.Instance)!.GetValue(binding)!;
            Assert.Equal("Ctrl+Alt+E", bound.Hotkey);
            Assert.True(bound.Enabled);
            Assert.Equal("en", bound.TargetLang);
            Assert.Equal(row.Id, bound.Id); // the same row, an independent copy
        }

        [Fact]
        public void ADisabledRowIsCopiedButNeverMatches()
        {
            var hook = new KeyboardHook();
            hook.UpdateTranslationProfiles(new[]
            {
                new TranslationProfile { TargetLang = "en", Hotkey = "Ctrl+Alt+E", Enabled = false },
            });

            // It stays in the snapshot (the table shows it) but FindTranslation skips it, so the key
            // is never swallowed - which is what "the row's own switch" has to mean.
            object binding = Assert.Single(Bindings(hook));
            var bound = (TranslationProfile)binding.GetType()
                .GetField("Profile", BindingFlags.Public | BindingFlags.Instance)!.GetValue(binding)!;
            Assert.False(bound.Enabled);
            Assert.Null(FindTranslation(hook, 0x45)); // VK 'E'
        }

        [Fact]
        public void AnEmptySetClearsTheBindingsSoADisabledFeatureCostsTheCallbackNothing()
        {
            var hook = new KeyboardHook();
            hook.UpdateTranslationProfiles(new[] { new TranslationProfile { TargetLang = "en", Hotkey = "Ctrl+Alt+E" } });
            Assert.Single(Bindings(hook));

            hook.UpdateTranslationProfiles(new TranslationProfile[0]);
            Assert.Empty(Bindings(hook));
        }

        private static object[] Bindings(KeyboardHook hook)
        {
            var field = typeof(KeyboardHook).GetField("_translationProfiles", BindingFlags.NonPublic | BindingFlags.Instance)!;
            return (object[])field.GetValue(hook)!;
        }

        private static TranslationProfile? FindTranslation(KeyboardHook hook, uint vkCode)
        {
            MethodInfo find = typeof(KeyboardHook).GetMethod("FindTranslation", BindingFlags.NonPublic | BindingFlags.Instance)!;
            return (TranslationProfile?)find.Invoke(hook, new object[] { vkCode });
        }
    }
}

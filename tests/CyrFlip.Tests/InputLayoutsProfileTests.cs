using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using CyrFlip;
using Microsoft.Win32;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The modern-store edit behind every layout action on the "Windows languages" tab (ticket S0007).
    /// <see cref="InputLayouts.ApplyLayouts"/> takes the User Profile as a model, so the whole matrix -
    /// what survives, what moves, what the order becomes - is proven without touching the registry.
    /// The load-bearing rule is WL-1: an IME/TIP value is never removed, whatever the tab does.
    /// </summary>
    public class InputLayoutsProfileTests
    {
        private const string Pinyin = "0804:{81D4E9C9-1D3B-41BC-9E6C-4B40BF79E35E}{FA550B04-5AD7-411F-A5AC-CA038EC515D7}";
        private const string JapaneseIme = "0411:{03B5835F-F03C-411B-9CE2-AA23E1171E36}{A76C93D9-5523-4E90-AAFA-4DB112F9AC76}";

        private static string Tag(ushort lang)
        {
            switch (lang)
            {
                case 0x0409: return "en-US";
                case 0x0419: return "ru";
                case 0x0422: return "uk";
                case 0x0804: return "zh-Hans-CN";
                case 0x0411: return "ja";
                default: return "x-" + lang.ToString("x4");
            }
        }

        private static InputLayouts.ProfileModel Model(params (string tag, string[] values)[] subkeys)
        {
            var model = new InputLayouts.ProfileModel();
            foreach ((string tag, string[] values) in subkeys)
            {
                model.Languages.Add(tag);
                model.Subkeys[tag] = new List<string>(values);
            }
            return model;
        }

        [Fact]
        public void MovingALayoutKeepsEveryTipValue()
        {
            var model = Model(
                ("en-US", new[] { "0409:00000409", "CachedLanguageName" }),
                ("zh-Hans-CN", new[] { Pinyin, "0804:00000804", "CachedLanguageName" }),
                ("ru", new[] { "0419:00000419" }));

            // The Preload view of that profile, with Russian moved up.
            InputLayouts.ApplyLayouts(model, new List<string> { "00000409", "00000419", "00000804" }, null, null, Tag);

            Assert.Contains(Pinyin, model.Subkeys["zh-Hans-CN"]);
            Assert.Contains("0804:00000804", model.Subkeys["zh-Hans-CN"]);
            Assert.Contains("CachedLanguageName", model.Subkeys["zh-Hans-CN"]);
            Assert.Equal(new[] { "en-US", "zh-Hans-CN", "ru" }, model.Languages);
        }

        [Fact]
        public void ALanguageWithOnlyATipSurvivesTheRemovalOfEveryLayoutAroundIt()
        {
            var model = Model(
                ("en-US", new[] { "0409:00000409" }),
                ("ja", new[] { JapaneseIme }),
                ("ru", new[] { "0419:00000419" }));

            // Japanese shows up in Preload as its primary KLID; Russian is removed.
            InputLayouts.ApplyLayouts(model, new List<string> { "00000409", "00000411" }, null, null, Tag, removed: "00000419");

            Assert.Equal(new[] { JapaneseIme }, model.Subkeys["ja"]);
            Assert.False(model.Subkeys.ContainsKey("ru"));
            Assert.Equal(new[] { "en-US", "ja" }, model.Languages);
        }

        [Fact]
        public void APrimaryKlidThatStandsForATipIsNotWrittenAsAKeyboard()
        {
            var model = Model(("zh-Hans-CN", new[] { Pinyin }));

            InputLayouts.ApplyLayouts(model, new List<string> { "00000804" }, null, null, Tag);

            Assert.Equal(new[] { Pinyin }, model.Subkeys["zh-Hans-CN"]);
        }

        [Fact]
        public void AnExplicitAddOfThatKlidIsWrittenAllTheSame()
        {
            var model = Model(("zh-Hans-CN", new[] { Pinyin }));

            InputLayouts.ApplyLayouts(model, new List<string> { "00000804" }, "00000804", null, Tag);

            Assert.Equal(new[] { Pinyin, "0804:00000804" }, model.Subkeys["zh-Hans-CN"]);
        }

        [Fact]
        public void ALanguageKeepsTheKeyboardItHoldsFromAnotherLanguage()
        {
            // Russian with a US keyboard: the US layout lives under "ru", not under "en-US".
            var model = Model(("ru", new[] { "0419:00000419", "0419:00000409" }));

            InputLayouts.ApplyLayouts(model, new List<string> { "00000409", "00000419" }, null, null, Tag);

            Assert.False(model.Subkeys.ContainsKey("en-US"));
            Assert.Equal(new[] { "0419:00000409", "0419:00000419" }, model.Subkeys["ru"]);
            Assert.Equal(new[] { "ru" }, model.Languages);
        }

        [Fact]
        public void ANewLanguageIsAppendedAndTheExistingOrderIsKept()
        {
            var model = Model(("ru", new[] { "0419:00000419" }), ("en-US", new[] { "0409:00000409" }));

            // Ukrainian added at the front of the layout list still goes to the end of Languages.
            InputLayouts.ApplyLayouts(model, new List<string> { "00000422", "00000409", "00000419" }, "00000422", null, Tag);

            Assert.Equal(new[] { "ru", "en-US", "uk" }, model.Languages);
            Assert.Equal(new[] { "0422:00000422" }, model.Subkeys["uk"]);
        }

        [Fact]
        public void RemovingTheLastKeyboardOfALanguageDropsIt()
        {
            var model = Model(("en-US", new[] { "0409:00000409" }), ("uk", new[] { "0422:00000422", "CachedLanguageName" }));

            InputLayouts.ApplyLayouts(model, new List<string> { "00000409" }, null, null, Tag, removed: "00000422");

            Assert.False(model.Subkeys.ContainsKey("uk"));
            Assert.Equal(new[] { "en-US" }, model.Languages);
        }

        [Fact]
        public void ASubkeyWithoutAnyLayoutIsNotCyrFlipsToDelete()
        {
            // Something CyrFlip does not model (a display-language-only entry): left exactly as is.
            var model = Model(("en-US", new[] { "0409:00000409" }), ("fr-FR", new[] { "CachedLanguageName" }));

            InputLayouts.ApplyLayouts(model, new List<string> { "00000409" }, null, null, Tag);

            Assert.True(model.Subkeys.ContainsKey("fr-FR"));
            Assert.Equal(new[] { "en-US", "fr-FR" }, model.Languages);
        }

        [Fact]
        public void LayoutValuesFollowTheListOrderWhileEverythingElseStaysPut()
        {
            var model = Model(("en-US", new[] { "0409:00000409", "CachedLanguageName", "0409:00010409" }));

            InputLayouts.ApplyLayouts(model, new List<string> { "00010409", "00000409" }, null, null, Tag);

            Assert.Equal(new[] { "0409:00010409", "CachedLanguageName", "0409:00000409" }, model.Subkeys["en-US"]);
        }

        [Fact]
        public void MakeDefaultWritesTheOverrideAndLeavesLanguagesAlone()
        {
            var model = Model(("en-US", new[] { "0409:00000409" }), ("ru", new[] { "0419:00000419", "0419:00000409" }));

            InputLayouts.ApplyLayouts(model, new List<string> { "00000419", "00000409" }, null, "00000419", Tag);

            Assert.Equal("0419:00000419", model.InputMethodOverride);
            Assert.Equal(new[] { "en-US", "ru" }, model.Languages);
        }

        [Fact]
        public void MakeDefaultOfATipLanguageNamesTheTip()
        {
            var model = Model(("en-US", new[] { "0409:00000409" }), ("zh-Hans-CN", new[] { Pinyin }));

            InputLayouts.ApplyLayouts(model, new List<string> { "00000804", "00000409" }, null, "00000804", Tag);

            Assert.Equal(Pinyin, model.InputMethodOverride);
        }

        [Theory]
        [InlineData("0419:00000419", true)]
        [InlineData("0409:0001040A", true)]
        [InlineData("CachedLanguageName", false)]
        [InlineData(Pinyin, false)]
        [InlineData("0419:0000041", false)]
        public void OnlyAPlainLayoutValueCountsAsOne(string name, bool layout)
        {
            Assert.Equal(layout, InputLayouts.IsLayoutValue(name));
        }

        // ---- WL-7: unload the layout that was removed ----

        [Fact]
        public void RemovingStandardRussianUnloadsOnlyItsOwnHandle()
        {
            IntPtr russian = (IntPtr)0x04190419;
            IntPtr typewriter = new IntPtr(unchecked((int)0xF0080419));
            IntPtr us = (IntPtr)0x04090409;
            var decode = new Dictionary<IntPtr, string>
            {
                [russian] = "00000419", [typewriter] = "00010419", [us] = "00000409",
            };

            List<IntPtr> unload = InputLayouts.HklsToUnload(new[] { us, typewriter, russian }, "00000419", h => decode[h]);

            Assert.Equal(new[] { russian }, unload);
        }

        [Fact]
        public void AnImeHandleIsNeverUnloadedForAKeyboard()
        {
            IntPtr ime = new IntPtr(unchecked((int)0xE0010411));

            Assert.Empty(InputLayouts.HklsToUnload(new[] { ime }, "00000411", _ => "00000411"));
        }

        // ---- WL-5: the snapshot remembers each value's kind ----

        [Fact]
        public void EveryRegistryKindSurvivesTheSnapshotThroughJson()
        {
            var cases = new List<(object value, RegistryValueKind kind)>
            {
                (1, RegistryValueKind.DWord),
                (unchecked((int)0xF0010409), RegistryValueKind.DWord),
                (5_000_000_000L, RegistryValueKind.QWord),
                ("0419:00000419", RegistryValueKind.String),
                ("%USERPROFILE%", RegistryValueKind.ExpandString),
                (new[] { "ru", "en-US" }, RegistryValueKind.MultiString),
                (new byte[] { 1, 2, 250 }, RegistryValueKind.Binary),
            };
            var serializer = new JavaScriptSerializer();
            foreach ((object value, RegistryValueKind kind) in cases)
            {
                string json = serializer.Serialize(InputLayouts.EncodeValue(value, kind));
                object raw = serializer.Deserialize<Dictionary<string, object>>(json);

                Assert.True(InputLayouts.TryDecodeValue(raw, out object back, out RegistryValueKind backKind), json);
                Assert.Equal(kind, backKind);
                Assert.Equal(value, back);
            }
        }

        [Fact]
        public void SomethingThatIsNotAnEncodedValueIsRefused()
        {
            Assert.False(InputLayouts.TryDecodeValue("1", out _, out _));
            Assert.False(InputLayouts.TryDecodeValue(new Dictionary<string, object> { ["k"] = "Nonsense", ["v"] = 1 }, out _, out _));
        }

        // ---- S0007 section 21: WL-8..WL-11, WL-13 ----

        [Fact]
        public void AShortListNeverCostsAKeyboardItDidNotName()
        {
            // WL-10: Preload came back with only US; the add must not wipe Russian and Ukrainian.
            var model = Model(
                ("en-US", new[] { "0409:00000409" }),
                ("ru", new[] { "0419:00000419", "0419:00010419" }),
                ("uk", new[] { "0422:00000422" }));

            InputLayouts.ApplyLayouts(model, new List<string> { "00000409", "00000407" }, "00000407", null, Tag);

            Assert.Equal(new[] { "0419:00000419", "0419:00010419" }, model.Subkeys["ru"]);
            Assert.Equal(new[] { "0422:00000422" }, model.Subkeys["uk"]);
            Assert.Contains("0407:00000407", model.Subkeys["x-0407"]);
        }

        [Fact]
        public void ARemovalTakesOnlyTheLayoutItNames()
        {
            var model = Model(
                ("en-US", new[] { "0409:00000409" }),
                ("ru", new[] { "0419:00000419", "0419:00010419" }));

            // The list also misses Russian Typewriter; only standard Russian was asked for.
            InputLayouts.ApplyLayouts(model, new List<string> { "00000409" }, null, null, Tag, removed: "00000419");

            Assert.Equal(new[] { "0419:00010419" }, model.Subkeys["ru"]);
            Assert.Equal(new[] { "en-US", "ru" }, model.Languages);
        }

        [Fact]
        public void APrimaryKlidBackedOnlyByATipIsManagedByWindows()
        {
            var tipOnly = Model(("en-US", new[] { "0409:00000409" }), ("zh-Hans-CN", new[] { Pinyin }));
            Assert.True(InputLayouts.IsManagedByWindows(tipOnly, "00000804"));
            Assert.False(InputLayouts.IsManagedByWindows(tipOnly, "00000409"));

            var withKeyboard = Model(("zh-Hans-CN", new[] { Pinyin, "0804:00000804" }));
            Assert.False(InputLayouts.IsManagedByWindows(withKeyboard, "00000804"));

            // An alternate layout is never a TIP's stand-in.
            Assert.False(InputLayouts.IsManagedByWindows(tipOnly, "00010804"));
        }

        [Fact]
        public void TheArrowsStayInsideOneLanguage()
        {
            // Russian holding a US keyboard, then Ukrainian: US and Russian are one group.
            var model = Model(
                ("ru", new[] { "0419:00000419", "0419:00000409" }),
                ("uk", new[] { "0422:00000422" }),
                ("zh-Hans-CN", new[] { Pinyin }));
            Assert.Equal("ru", InputLayouts.GroupOf(model, "00000409"));
            Assert.Equal("zh-Hans-CN", InputLayouts.GroupOf(model, "00000804"));
            Assert.Equal("0x0407", InputLayouts.GroupOf(model, "00000407"));

            var rows = new List<InputLayouts.Installed>();
            foreach (string klid in new[] { "00000419", "00000409", "00000422", "00000804" })
                rows.Add(new InputLayouts.Installed { Klid = klid, Group = InputLayouts.GroupOf(model, klid) });

            Assert.False(InputLayouts.CanMove(rows, 0, -1));
            Assert.True(InputLayouts.CanMove(rows, 0, +1));
            Assert.True(InputLayouts.CanMove(rows, 1, -1));
            Assert.False(InputLayouts.CanMove(rows, 1, +1));
            Assert.False(InputLayouts.CanMove(rows, 2, -1));
            Assert.False(InputLayouts.CanMove(rows, 3, +1));
        }

        [Fact]
        public void OnlyASnapshotThatCanPutLayoutsBackIsRestorable()
        {
            const string whole = "{\"format\":2,\"preload\":{\"1\":\"00000409\"},\"substitutes\":{},\"profile\":{\"root\":{},\"subkeys\":{\"en-US\":{}}}}";
            Assert.True(InputLayouts.TryParseSnapshot(whole, out _));

            // WL-11: a dump that lost Preload or the language subkeys would delete and recreate nothing.
            Assert.False(InputLayouts.TryParseSnapshot(whole.Replace("{\"1\":\"00000409\"}", "{}"), out _));
            Assert.False(InputLayouts.TryParseSnapshot("{\"format\":2,\"preload\":{\"1\":\"00000409\"},\"profile\":{}}", out _));
            Assert.False(InputLayouts.TryParseSnapshot(whole.Replace("{\"en-US\":{}}", "{}"), out _));
            // CF-4: unreadable text is not a backup.
            Assert.False(InputLayouts.TryParseSnapshot("", out _));
            Assert.False(InputLayouts.TryParseSnapshot("not json", out _));

            // Format 1, as releases before S0007 wrote it.
            Assert.True(InputLayouts.TryParseSnapshot("{\"preload\":{\"1\":\"00000419\"},\"substitutes\":{},\"profile\":{\"Languages\":[\"ru\"],\"subkeys\":{\"ru\":{\"0419:00000419\":1}}}}", out _));
        }

        [Fact]
        public void AFormatOneRestoreDropsOnlyTheOverrideCyrFlipWrote()
        {
            Assert.True(InputLayouts.ShouldDropOverride("0419:00000419", "0419:00000419"));
            // The user picked another default in Windows since - theirs, kept.
            Assert.False(InputLayouts.ShouldDropOverride("0409:00000409", "0419:00000419"));
            // CyrFlip never wrote one.
            Assert.False(InputLayouts.ShouldDropOverride("0419:00000419", ""));
            Assert.False(InputLayouts.ShouldDropOverride(null, "0419:00000419"));
        }
    }
}

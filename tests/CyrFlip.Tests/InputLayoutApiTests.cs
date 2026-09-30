using System;
using System.Collections.Generic;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// WL-1 phase B (ticket S0007): the edits made through the documented input-profile API, over a fake
    /// <see cref="IInputLayoutApi"/>. Two rules carry it: an edit touches only the entries it names -
    /// a TIP and a keyboard under another language survive by construction - and an API that refuses,
    /// is missing, or says yes without doing it answers <c>null</c>, which sends the caller to the
    /// phase-A registry path instead of reporting a change that never happened.
    /// </summary>
    public class InputLayoutApiTests
    {
        private const string Pinyin = "0804:{81D4E9C9-1D3B-41BC-9E6C-4B40BF79E35E}{FA550B04-5AD7-411F-A5AC-CA038EC515D7}";

        private sealed class FakeApi : IInputLayoutApi
        {
            public readonly List<(string id, bool tip)> Entries = new List<(string, bool)>();
            public string? Default;
            public bool Available = true;
            public bool Refuse;
            public bool Lie; // answers true and changes nothing
            public readonly List<string> Calls = new List<string>();

            public FakeApi(params string[] ids)
            {
                foreach (string id in ids) Entries.Add((id, id.Length > 5 && id[5] == '{'));
                if (ids.Length > 0) Default = ids[0];
            }

            public bool TryEnumerate(out List<LayoutOrTip> entries)
            {
                entries = new List<LayoutOrTip>();
                if (!Available) return false;
                foreach ((string id, bool tip) in Entries)
                {
                    LayoutOrTip? e = InputLayoutApi.Parse(id, tip ? 1u : 2u, id == Default ? 1u : 0u);
                    if (e == null) return false;
                    entries.Add(e);
                }
                return entries.Count > 0;
            }

            public bool Install(string ids, bool uninstall)
            {
                Calls.Add((uninstall ? "uninstall " : "install ") + ids);
                if (Refuse) return false;
                if (Lie) return true;
                foreach (string id in ids.Split(';'))
                {
                    if (uninstall) Entries.RemoveAll(e => string.Equals(e.id, id, StringComparison.OrdinalIgnoreCase));
                    else Entries.Add((id, false));
                }
                return true;
            }

            public bool SetDefault(string id)
            {
                Calls.Add("default " + id);
                if (Refuse) return false;
                if (!Lie) Default = id;
                return true;
            }
        }

        [Fact]
        public void TheIdOfAKeyboardNamesItsOwnLanguage()
        {
            Assert.Equal("0419:00000419", InputLayoutApi.IdFor("00000419"));
            Assert.Equal("0409:00010409", InputLayoutApi.IdFor("00010409"));
        }

        [Fact]
        public void ParseReadsKeyboardsAndTipsAndRefusesAnythingElse()
        {
            LayoutOrTip? kb = InputLayoutApi.Parse("0419:00000409", 2, 1);
            Assert.NotNull(kb);
            Assert.False(kb!.IsTip);
            Assert.Equal("00000409", kb.Klid);
            Assert.Equal((ushort)0x0419, kb.LangId);
            Assert.True(kb.IsDefault);

            LayoutOrTip? tip = InputLayoutApi.Parse(Pinyin, 1, 0);
            Assert.NotNull(tip);
            Assert.True(tip!.IsTip);
            Assert.Equal("", tip.Klid);
            Assert.Equal((ushort)0x0804, tip.LangId);

            Assert.Null(InputLayoutApi.Parse("", 2, 0));
            Assert.Null(InputLayoutApi.Parse("0419-00000419", 2, 0));
            Assert.Null(InputLayoutApi.Parse("0419:0419", 2, 0));
            Assert.Null(InputLayoutApi.Parse("zzzz:00000419", 2, 0));
        }

        [Fact]
        public void AddInstallsUnderItsOwnLanguageAndTouchesNothingElse()
        {
            var api = new FakeApi("0409:00000409", Pinyin, "0419:00000409");
            Assert.Equal(InputLayouts.EditResult.Ok, InputLayoutApi.Add(api, "00000422"));
            Assert.Equal(new[] { "install 0422:00000422" }, api.Calls);
            Assert.Contains((Pinyin, true), api.Entries);
            Assert.Contains(("0419:00000409", false), api.Entries);
        }

        [Fact]
        public void AddingAKeyboardAlreadyEnabledUnderAnotherLanguageWritesNothing()
        {
            // Russian with a US keyboard: adding US must not create a second US under en-US (WL-2).
            var api = new FakeApi("0419:00000419", "0419:00000409");
            Assert.Equal(InputLayouts.EditResult.Ok, InputLayoutApi.Add(api, "00000409"));
            Assert.Empty(api.Calls);
        }

        [Fact]
        public void RemoveUninstallsEveryEntryOfThatKeyboardAndKeepsTheTip()
        {
            var api = new FakeApi("0409:00000409", "0419:00000409", Pinyin, "0419:00000419");
            Assert.Equal(InputLayouts.EditResult.Ok, InputLayoutApi.Remove(api, "00000409"));
            Assert.Equal(new[] { "uninstall 0409:00000409;0419:00000409" }, api.Calls);
            Assert.Equal(2, api.Entries.Count);
            Assert.Contains(api.Entries, e => e.tip);
        }

        [Fact]
        public void RemovingTheOnlyEntryLeftIsRefusedWithoutACall()
        {
            var api = new FakeApi("0409:00000409");
            Assert.Equal(InputLayouts.EditResult.LastLayout, InputLayoutApi.Remove(api, "00000409"));
            Assert.Empty(api.Calls);
        }

        [Fact]
        public void TheStandInRowOfATipIsLeftToTheCaller()
        {
            // Pinyin shows up in Preload as 00000804 - the API lists no keyboard of that KLID.
            var api = new FakeApi("0409:00000409", Pinyin);
            Assert.Null(InputLayoutApi.Remove(api, "00000804"));
            Assert.Empty(api.Calls);
        }

        [Fact]
        public void MakeDefaultPicksTheHostLanguageEntryOrTheTipOfAPrimaryKlid()
        {
            var api = new FakeApi("0409:00000409", "0419:00000409", Pinyin);
            Assert.Equal(InputLayouts.EditResult.Ok, InputLayoutApi.MakeDefault(api, "00000804"));
            Assert.Equal(new[] { "default " + Pinyin }, api.Calls);

            var ru = new FakeApi("0419:00000419", "0419:00000409");
            Assert.Equal(InputLayouts.EditResult.Ok, InputLayoutApi.MakeDefault(ru, "00000409"));
            Assert.Equal(new[] { "default 0419:00000409" }, ru.Calls);
        }

        [Fact]
        public void MakingTheDefaultDefaultCallsNothing()
        {
            var api = new FakeApi("0409:00000409", "0419:00000419");
            Assert.Equal(InputLayouts.EditResult.Ok, InputLayoutApi.MakeDefault(api, "00000409"));
            Assert.Empty(api.Calls);
        }

        [Fact]
        public void AMissingApiSendsEveryEditToTheRegistryPath()
        {
            var api = new FakeApi("0409:00000409", "0419:00000419") { Available = false };
            Assert.Null(InputLayoutApi.Add(api, "00000422"));
            Assert.Null(InputLayoutApi.Remove(api, "00000419"));
            Assert.Null(InputLayoutApi.MakeDefault(api, "00000419"));
            Assert.Empty(api.Calls);
        }

        [Fact]
        public void ARefusalOrAYesThatChangedNothingIsNotReportedAsDone()
        {
            foreach (bool lie in new[] { false, true })
            {
                var api = new FakeApi("0409:00000409", "0419:00000419") { Refuse = !lie, Lie = lie };
                Assert.Null(InputLayoutApi.Add(api, "00000422"));
                Assert.Null(InputLayoutApi.Remove(api, "00000419"));
                Assert.Null(InputLayoutApi.MakeDefault(api, "00000419"));
                Assert.Equal(3, api.Calls.Count);
            }
        }
    }
}

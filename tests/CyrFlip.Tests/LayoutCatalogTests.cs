using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The pure half of the layout verbs (ticket S0109): what a value typed after <c>--to</c> or <c>--from</c> means
    /// (<see cref="LayoutValue"/>), which installed layout it picks (<see cref="LayoutCatalog"/>), the rotation of the
    /// tray click that <c>layout switch --to next</c> shares (<see cref="LayoutSwitcher.NextAfter"/>) and the
    /// converter's layout-list seam. No keyboard, no window and no registry: the catalog takes its list as an argument.
    /// </summary>
    public class LayoutCatalogTests
    {
        private static LayoutEntry E(long hkl, string klid, string code, string label = "")
            => new LayoutEntry(new IntPtr(hkl), klid, code, label);

        private static readonly LayoutEntry Us = E(0x04090409, "00000409", "EN", "US");
        private static readonly LayoutEntry Ru = E(0x04190419, "00000419", "RU", "Russian");
        private static readonly LayoutEntry De = E(0x04070407, "00000407", "DE", "German");
        private static readonly LayoutEntry Dvorak = E(0x00010409, "00010409", "EN", "United States-Dvorak");

        // ------------------------------------------------------------------ the grammar of a value

        [Theory]
        [InlineData("en", "Code", "EN")]
        [InlineData("EN", "Code", "EN")]
        [InlineData("ru", "Code", "RU")]
        [InlineData("haw", "Code", "HAW")]
        [InlineData("0C00", "Code", "0C00")]
        [InlineData("zz", "Code", "ZZ")]
        [InlineData("00000409", "Klid", "00000409")]
        [InlineData("0000040a", "Klid", "0000040A")]
        [InlineData("00000000", "Klid", "00000000")]
        [InlineData("FFFFFFFF", "Klid", "FFFFFFFF")]
        [InlineData("next", "Next", "NEXT")]
        [InlineData("NEXT", "Next", "NEXT")]
        [InlineData("Next", "Next", "NEXT")]
        public void AWellFormedValueIsAKlidACodeOrNext(string value, string kind, string normal)
        {
            LayoutValue parsed = LayoutValue.Parse(value);
            Assert.Equal(kind, parsed.Kind.ToString());
            Assert.Equal(normal, parsed.Text);
        }

        [Theory]
        [InlineData("")]
        [InlineData("banana")]
        [InlineData("e")]
        [InlineData("12345")]
        [InlineData("0000040G")]
        [InlineData("en ")]
        [InlineData(" en")]
        [InlineData("e n")]
        [InlineData("рус")]
        [InlineData("en-US")]
        public void AnythingElseNamesNothing(string value)
            => Assert.Equal(LayoutValueKind.Invalid, LayoutValue.Parse(value).Kind);

        [Fact]
        public void ANullValueNamesNothing()
            => Assert.Equal(LayoutValueKind.Invalid, LayoutValue.Parse(null).Kind);

        // ------------------------------------------------------------------ which layout a value picks

        [Fact]
        public void ACodeTakesTheFirstLayoutOfThatCodeInWindowsOrderAndAKlidIsExact()
        {
            var dvorakFirst = new LayoutCatalog(new[] { Dvorak, Ru, Us });
            Assert.Same(Dvorak, dvorakFirst.Find(LayoutValue.Parse("en")));
            Assert.Same(Us, dvorakFirst.Find(LayoutValue.Parse("00000409")));
            Assert.Same(Dvorak, dvorakFirst.Find(LayoutValue.Parse("00010409")));

            var usFirst = new LayoutCatalog(new[] { Us, Ru, Dvorak });
            Assert.Same(Us, usFirst.Find(LayoutValue.Parse("EN")));
            Assert.Same(Ru, usFirst.Find(LayoutValue.Parse("ru")));
        }

        [Fact]
        public void ALayoutThatIsNotInTheListIsNotFoundAndNextPicksNothing()
        {
            var catalog = new LayoutCatalog(new[] { Us, Ru });
            Assert.Null(catalog.Find(LayoutValue.Parse("de")));
            Assert.Null(catalog.Find(LayoutValue.Parse("00000407")));
            Assert.Null(catalog.Find(LayoutValue.Parse("next")));
            Assert.Null(catalog.Find(LayoutValue.Parse("banana")));
        }

        [Fact]
        public void TheCuratedPrimaryKlidIsTheFirstOfTheLanguagesOwnList()
        {
            Assert.Equal("00000409", LayoutCatalog.CuratedPrimaryKlid("en"));
            Assert.Equal("00000419", LayoutCatalog.CuratedPrimaryKlid("RU"));
            Assert.Equal("00000422", LayoutCatalog.CuratedPrimaryKlid("uk"));
            Assert.Equal("00000407", LayoutCatalog.CuratedPrimaryKlid("de"));
            Assert.Null(LayoutCatalog.CuratedPrimaryKlid("zz"));
            Assert.Null(LayoutCatalog.CuratedPrimaryKlid("nl"));
        }

        [Fact]
        public void AValueNamesSomethingWhenALayoutACuratedLanguageOrACultureCarriesIt()
        {
            var none = new LayoutCatalog(new LayoutEntry[0]);
            Assert.True(none.NamesSomething(LayoutValue.Parse("ru")));   // curated
            Assert.True(none.NamesSomething(LayoutValue.Parse("nl")));   // a Windows culture, not curated
            Assert.True(none.NamesSomething(LayoutValue.Parse("00000419")));
            Assert.False(none.NamesSomething(LayoutValue.Parse("zz")));
            Assert.False(none.NamesSomething(LayoutValue.Parse("00000000")));
            Assert.False(none.NamesSomething(LayoutValue.Parse("FFFFFFFF")));
            Assert.False(none.NamesSomething(LayoutValue.Parse("next")));
            Assert.False(none.NamesSomething(LayoutValue.Parse("banana")));
        }

        [Fact]
        public void ACodeAnInstalledLayoutCarriesIsNamedEvenWhenNoCultureHasIt()
        {
            // A language Windows cannot name is drawn as its four hexadecimal digits, and that is the code of its layouts.
            LayoutEntry odd = E(0x0C000C00, "00000C00", "0C00");
            Assert.False(new LayoutCatalog(new[] { Us }).NamesSomething(LayoutValue.Parse("0C00")));
            Assert.True(new LayoutCatalog(new[] { Us, odd }).NamesSomething(LayoutValue.Parse("0c00")));
        }

        // ------------------------------------------------------------------ the rotation of the tray click

        /// <summary>The arithmetic <c>SwitchToNext</c> had before it moved into <c>NextAfter</c>, copied as it was.</summary>
        private static IntPtr TheOldRotation(IntPtr[] installed, IntPtr current)
        {
            int index = Array.IndexOf(installed, current);
            IntPtr next = index >= 0 ? installed[(index + 1) % installed.Length] : installed[0];
            if (next == current)
                next = installed[(index + 1 + installed.Length) % installed.Length == 0 ? 1 : 0];
            return next;
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(5)]
        public void NextAfterIsTheOldTrayArithmeticOnEveryCurrentLayoutAndOnAnUnlistedOne(int count)
        {
            var installed = new IntPtr[count];
            for (int i = 0; i < count; i++) installed[i] = new IntPtr(0x10000 + i);
            foreach (IntPtr current in installed)
                Assert.Equal(TheOldRotation(installed, current), LayoutSwitcher.NextAfter(installed, current));
            IntPtr unlisted = new IntPtr(0x7777);
            Assert.Equal(TheOldRotation(installed, unlisted), LayoutSwitcher.NextAfter(installed, unlisted));
            Assert.Equal(installed[0], LayoutSwitcher.NextAfter(installed, unlisted));
        }

        [Fact]
        public void NextAfterWrapsAtTheEndAndWalksTheListOtherwise()
        {
            var installed = new[] { new IntPtr(1), new IntPtr(2), new IntPtr(3) };
            Assert.Equal(new IntPtr(2), LayoutSwitcher.NextAfter(installed, new IntPtr(1)));
            Assert.Equal(new IntPtr(3), LayoutSwitcher.NextAfter(installed, new IntPtr(2)));
            Assert.Equal(new IntPtr(1), LayoutSwitcher.NextAfter(installed, new IntPtr(3)));
        }

        [Fact]
        public void NextAfterKeepsTheOddDuplicateHandleBranchOfTheOldCode()
        {
            var installed = new[] { new IntPtr(1), new IntPtr(1), new IntPtr(2) };
            Assert.Equal(TheOldRotation(installed, new IntPtr(1)), LayoutSwitcher.NextAfter(installed, new IntPtr(1)));
        }

        // ------------------------------------------------------------------ the converter's layout-list seam

        [Fact]
        public void WithNoLayoutResolvedTheSeededPairStillConvertsThroughTheTable()
        {
            Func<string?, IntPtr> noLayouts = _ => IntPtr.Zero;
            Assert.Equal("привет", KeyboardLayoutConverter.Convert("ghbdtn", "00000409", "00000419", true, true, noLayouts));
            Assert.Equal("ghbdtn", KeyboardLayoutConverter.Convert("привет", "00000419", "00000409", true, true, noLayouts));
        }

        [Fact]
        public void WithNoLayoutResolvedAnyOtherPairLeavesTheTextAlone()
        {
            Func<string?, IntPtr> noLayouts = _ => IntPtr.Zero;
            Assert.Equal("ghbdtn", KeyboardLayoutConverter.Convert("ghbdtn", "00000409", "0000040C", true, true, noLayouts));
        }

        [Fact]
        public void TheSeamIsAskedForEachSideOnceAndNotForAnEmptyText()
        {
            int asked = 0;
            Func<string?, IntPtr> counting = _ => { asked++; return IntPtr.Zero; };
            Assert.Equal("", KeyboardLayoutConverter.Convert("", "00000409", "00000419", true, true, counting));
            Assert.Equal(0, asked);
            KeyboardLayoutConverter.Convert("a", "00000409", "00000419", true, true, counting);
            Assert.Equal(2, asked);
        }

        [Fact]
        public void TheSymbolSwitchReachesTheTableFallbackToo()
        {
            Func<string?, IntPtr> noLayouts = _ => IntPtr.Zero;
            Assert.Equal(".привет", KeyboardLayoutConverter.Convert("/ghbdtn", "00000409", "00000419", true, true, noLayouts));
            Assert.Equal("/привет", KeyboardLayoutConverter.Convert("/ghbdtn", "00000409", "00000419", false, true, noLayouts));
        }

        // ------------------------------------------------------------------ what the pure files may touch

        private static readonly string SourceFolder = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "CyrFlip"));

        private static string Source(string name) => File.ReadAllText(Path.Combine(SourceFolder, name));

        [Fact]
        public void TheCatalogKnowsNoWin32NoRegistryAndNoConfiguration()
        {
            string text = Source("LayoutCatalog.cs");
            Assert.DoesNotContain("DllImport", text);
            Assert.DoesNotContain("Registry", text);
            Assert.DoesNotContain("AppConfig", text);
        }

        [Fact]
        public void TheLayoutSwitcherNeverActivatesAWindow()
            => Assert.DoesNotContain("ForegroundActivator", Source("LayoutSwitcher.cs"));

        [Fact]
        public void TheVerbsOfTheLayoutAndTextGroupNeverLoadALayoutAndTheTextOnesNeverTouchAWindowOrTheClipboard()
        {
            string text = Source("AppCommandsLayoutText.cs");
            // layout switch resolves over the installed list only: nothing here can add a layout to the person's Windows.
            Assert.DoesNotContain("LoadKeyboardLayout", text);
            Assert.DoesNotContain("ResolveInstalled", text);
            int host = text.IndexOf("internal sealed class IndicatorLayoutHost", StringComparison.Ordinal);
            Assert.True(host > 0);
            string handlers = text.Substring(0, host);
            Assert.False(Regex.IsMatch(handlers, @"Clipboard|ForegroundActivator|Windows\.Forms|\bForm\b|ShowDialog"),
                "The handlers are pure functions; the window and the clipboard are another file's.");
        }
    }
}

using System;
using System.Collections.Generic;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Typability hint and filter verification (ticket S0056 section 13):
    /// fake layout provider, plain key, Shift, AltGr, absent character,
    /// dead-key-only character reported as not typable, cache reuse and invalidation.
    /// </summary>
    public class SymbolTypabilityTests
    {
        [Fact]
        public void PlainKeyShiftAndAltGrAreTypable()
        {
            var hklDe = new IntPtr(0x0407); // German
            var hklUs = new IntPtr(0x0409); // US

            IntPtr[] layouts = new[] { hklUs, hklDe };

            short VkScan(char c, IntPtr hkl)
            {
                if (hkl == hklUs && c == 'a')
                {
                    // Plain key 'A' on US: VK_A (0x41), mod = 0
                    return 0x0041;
                }
                if (hkl == hklUs && c == 'A')
                {
                    // Shift + 'A' on US: VK_A (0x41), mod = 1
                    return 0x0141;
                }
                if (hkl == hklDe && c == '€')
                {
                    // AltGr + 'E' on DE: VK_E (0x45), mod = 6 (Ctrl+Alt = AltGr)
                    return (short)((6 << 8) | 0x45);
                }
                return -1;
            }

            string LayoutName(IntPtr hkl) => hkl == hklDe ? "Немецкая" : "США";

            var typability = new SymbolTypability(() => layouts, VkScan, LayoutName);

            var entryPlain = new SymbolEntry("a", 'a', "test", "LETTER A", "буква А", "");
            var entryShift = new SymbolEntry("A", 'A', "test", "CAPITAL A", "заглавная А", "");
            var entryAltGr = new SymbolEntry("€", 0x20AC, "currency", "EURO SIGN", "евро", "");

            TypabilityInfo plainInfo = typability.Query(entryPlain);
            Assert.True(plainInfo.IsTypable);
            Assert.Equal("A", plainInfo.Chord);
            Assert.Equal("США", plainInfo.LayoutName);

            TypabilityInfo shiftInfo = typability.Query(entryShift);
            Assert.True(shiftInfo.IsTypable);
            Assert.Equal("Shift+A", shiftInfo.Chord);
            Assert.Equal("США", shiftInfo.LayoutName);

            TypabilityInfo altGrInfo = typability.Query(entryAltGr);
            Assert.True(altGrInfo.IsTypable);
            Assert.Equal("AltGr+E", altGrInfo.Chord);
            Assert.Equal("Немецкая", altGrInfo.LayoutName);

            // Localization hint
            string ruHint = altGrInfo.GetHint("Русский");
            Assert.Contains("AltGr+E", ruHint);
            Assert.Contains("Немецкая", ruHint);
        }

        [Fact]
        public void AbsentAndDeadKeyCharactersReportedAsNotTypable()
        {
            var hklUs = new IntPtr(0x0409);
            IntPtr[] layouts = new[] { hklUs };

            // For both absent character and dead-key composition (e.g. 'e with acute' via dead key),
            // VkKeyScanEx returns -1.
            short VkScan(char c, IntPtr hkl) => -1;

            var typability = new SymbolTypability(() => layouts, VkScan, _ => "США");

            var absent = new SymbolEntry("‡", 0x2021, "punctuation", "DOUBLE DAGGER", "двойной крестик", "");
            var deadKeyChar = new SymbolEntry("é", 0x00E9, "latin", "LATIN SMALL LETTER E WITH ACUTE", "e с акутом", "");

            TypabilityInfo absentInfo = typability.Query(absent);
            Assert.False(absentInfo.IsTypable);
            Assert.Null(absentInfo.Chord);
            Assert.Equal("Нет на ваших раскладках", absentInfo.GetHint("Русский"));

            TypabilityInfo deadInfo = typability.Query(deadKeyChar);
            Assert.False(deadInfo.IsTypable);
            Assert.Null(deadInfo.Chord);
            Assert.Equal("Нет на ваших раскладках", deadInfo.GetHint("Русский"));
        }

        [Fact]
        public void CacheReusedForSameLayoutsAndInvalidatedOnLayoutChange()
        {
            var hkl1 = new IntPtr(0x0409);
            var hkl2 = new IntPtr(0x0419);
            IntPtr[] currentLayouts = new[] { hkl1 };

            int scanCallCount = 0;
            short VkScan(char c, IntPtr hkl)
            {
                scanCallCount++;
                return (short)((1 << 8) | 0x41); // Shift+A
            }

            var typability = new SymbolTypability(() => currentLayouts, VkScan, _ => "Layout");
            var entry = new SymbolEntry("A", 'A', "test", "A", "А", "");

            // 1. First query computes and caches
            TypabilityInfo info1 = typability.Query(entry);
            Assert.True(info1.IsTypable);
            Assert.Equal(1, scanCallCount);

            // 2. Second query with same layouts reuses cache without invoking VkScan
            TypabilityInfo info2 = typability.Query(entry);
            Assert.Same(info1, info2);
            Assert.Equal(1, scanCallCount);

            // 3. Layout set changes -> cache invalidated on next query
            currentLayouts = new[] { hkl1, hkl2 };
            TypabilityInfo info3 = typability.Query(entry);
            Assert.True(info3.IsTypable);
            Assert.True(scanCallCount > 1);
        }

        [Fact]
        public void SurrogatePairsAreNeverTypable()
        {
            var hkl = new IntPtr(0x0409);
            var typability = new SymbolTypability(() => new[] { hkl }, (c, h) => 0x0041, _ => "Layout");

            string surrogate = char.ConvertFromUtf32(0x1F600);
            var entry = new SymbolEntry(surrogate, 0x1F600, "symbols", "SMILE", "смайл", "");

            TypabilityInfo info = typability.Query(entry);
            Assert.False(info.IsTypable);
        }
    }
}

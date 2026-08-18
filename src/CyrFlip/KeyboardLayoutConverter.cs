using System;
using System.Text;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// Converts characters by their physical key position between two Windows layouts. It asks
    /// Windows for both mappings, so it naturally covers the installed national layouts instead
    /// of carrying a fragile hand-written alphabet table.
    /// </summary>
    internal static class KeyboardLayoutConverter
    {
        private const int VK_SPACE = 0x20;
        private const string UsKlid = "00000409";
        private const string RussianKlid = "00000419";

        /// <param name="convertSymbols">
        /// Whether a key that is punctuation in <b>both</b> layouts is converted - see
        /// <see cref="AppConfig.ConvertSymbols"/>. Default true, which is how every release before the
        /// switch behaved. Passed on to the <see cref="TransliterationEngine"/> fallback too, so the
        /// setting means the same thing on a machine that never installed the Russian keyboard.
        /// </param>
        public static string Convert(string input, string sourceKlid, string targetKlid, bool convertSymbols = true)
        {
            if (string.IsNullOrEmpty(input)) return input ?? "";

            IntPtr source = ResolveInstalled(sourceKlid);
            IntPtr target = ResolveInstalled(targetKlid);
            if (source == IntPtr.Zero || target == IntPtr.Zero || source == target)
                return IsBuiltInPair(sourceKlid, targetKlid) ? TransliterationEngine.Transliterate(input, convertSymbols) : input;

            var output = new StringBuilder(input.Length);
            foreach (char c in input)
            {
                // Per-character direction, so one press fixes mixed text. The pair's own direction is
                // tried first; a character the source layout cannot even produce (Cyrillic under a US
                // source) was typed the other way round, so it is converted back. "ghbdtnпривет"
                // becomes "приветghbdtn" instead of doubling the first half.
                if (TryConvertChar(c, source, target, convertSymbols, out char forward)) output.Append(forward);
                else if (TryConvertChar(c, target, source, convertSymbols, out char backward)) output.Append(backward);
                else output.Append(c);
            }
            return output.ToString();
        }

        /// <summary>
        /// The US ⇄ Russian pair, in either direction - the one row of the table CyrFlip seeds itself
        /// with. It falls back to the hand-written QWERTY ⇄ ЙЦУКЕН table (<see cref="TransliterationEngine"/>)
        /// when Windows has no mapping to ask about, so the headline flip keeps working on a machine
        /// that never installed the Russian keyboard. Every other pair honestly does nothing there:
        /// only these two layouts have a table of our own to fall back to.
        /// </summary>
        private static bool IsBuiltInPair(string? a, string? b)
            => (string.Equals(a, UsKlid, StringComparison.OrdinalIgnoreCase) && string.Equals(b, RussianKlid, StringComparison.OrdinalIgnoreCase))
            || (string.Equals(a, RussianKlid, StringComparison.OrdinalIgnoreCase) && string.Equals(b, UsKlid, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The HKL for a KLID, but only when that layout is actually installed for this user.
        /// <c>LoadKeyboardLayout</c> *adds* an unknown layout to the Windows input list, so a saved
        /// profile pointing at a layout the user has since removed would silently reinstall it.
        /// Anything we accidentally load that way is unloaded again and the caller gets zero -
        /// the conversion then does nothing, which is the honest outcome.
        /// </summary>
        internal static IntPtr ResolveInstalled(string? klid)
        {
            if (klid == null || klid.Length != 8) return IntPtr.Zero;

            IntPtr[] before = InstalledLayouts();
            IntPtr hkl = LoadKeyboardLayout(klid, KLF_NOTELLSHELL | KLF_SUBSTITUTE_OK);
            if (hkl == IntPtr.Zero) return IntPtr.Zero;
            if (Array.IndexOf(before, hkl) >= 0) return hkl;

            UnloadKeyboardLayout(hkl);
            return IntPtr.Zero;
        }

        /// <summary>
        /// True when <paramref name="klid"/> is the layout the given window is typing in right now.
        /// Drives the bidirectional conversion: with the target layout already active, the user is
        /// looking at text typed the other way round, so the pair is applied in reverse.
        /// </summary>
        internal static bool IsActiveLayout(IntPtr hwnd, string? klid)
        {
            if (hwnd == IntPtr.Zero) return false;
            IntPtr wanted = ResolveInstalled(klid);
            if (wanted == IntPtr.Zero) return false;
            return GetKeyboardLayout(GetWindowThreadProcessId(hwnd, out _)) == wanted;
        }

        /// <summary>
        /// The layouts Windows has loaded for this session, in preload order - i.e. the rotation
        /// Alt+Shift walks. Shared with <see cref="LayoutSwitcher.SwitchToNext"/>.
        /// </summary>
        internal static IntPtr[] InstalledLayouts()
        {
            uint count = GetKeyboardLayoutList(0, null);
            if (count == 0) return new IntPtr[0];
            var list = new IntPtr[count];
            uint filled = GetKeyboardLayoutList((int)count, list);
            if (filled == count) return list;

            var trimmed = new IntPtr[filled];
            Array.Copy(list, trimmed, filled);
            return trimmed;
        }

        /// <summary>
        /// Convert one character from <paramref name="source"/> to <paramref name="target"/>, or answer
        /// false when this direction has nothing to say about it - the source layout cannot produce the
        /// character at all, or the key maps to nothing readable in the target. The caller then tries
        /// the opposite direction, which is what makes the conversion per-character bidirectional.
        /// </summary>
        private static bool TryConvertChar(char character, IntPtr source, IntPtr target, bool convertSymbols, out char converted)
        {
            converted = character;

            short encoded = VkKeyScanEx(character, source);
            if (encoded == -1) return false; // IME/composed/dead-key output is not one physical key.

            byte vk = (byte)(encoded & 0xff);
            byte modifiers = (byte)((encoded >> 8) & 0xff);

            // Same physical key, not the same virtual key. On AZERTY/QWERTZ the letter keys sit under
            // different VK codes than on QWERTY (French 'a' is the US 'q' key), so the source VK is
            // turned into its scan code *in the source layout* and read back as the target layout's
            // VK. For QWERTY ↔ ЙЦУКЕН both steps are identities, so the classic flip is unaffected.
            uint scan = MapVirtualKeyEx(vk, MAPVK_VK_TO_VSC, source);
            if (scan == 0) return false;
            uint targetVk = MapVirtualKeyEx(scan, MAPVK_VSC_TO_VK, target);
            if (targetVk == 0) return false;

            byte[] state = new byte[256];
            if ((modifiers & 1) != 0) state[Hotkey.VK_SHIFT] = 0x80;
            if ((modifiers & 2) != 0) state[Hotkey.VK_CONTROL] = 0x80;
            if ((modifiers & 4) != 0) state[Hotkey.VK_MENU] = 0x80;

            var chars = new StringBuilder(8);
            int count = ToUnicodeEx(targetVk, scan, state, chars, chars.Capacity, 0, target);

            // A negative result means the key is a dead key, and it stays *latched* inside the layout:
            // the user's next real keystroke would come out composed. Flush it before returning.
            // Preserving the original character is safer than emitting a half-composed one.
            if (count < 0) FlushDeadKey(target);
            if (count != 1) return false;

            // A key that is punctuation on both sides says nothing about which layout the user meant:
            // "/" is "." on the Russian key, and a slash in front of a word is often one the user typed
            // on purpose - a command, a path, or the numpad divide, which produces the very same
            // character and cannot be told apart once the text is on the clipboard. Whether to convert
            // those is therefore the user's call. Punctuation that becomes a *letter* never is: "," is
            // "б" and "[" is "х", which nobody types by accident inside a Russian word.
            char produced = chars[0];
            if (!convertSymbols && !char.IsLetter(character) && !char.IsLetter(produced)) return false;

            converted = produced;
            return true;
        }

        /// <summary>Clear a latched dead key by pressing Space against the layout until it composes.</summary>
        private static void FlushDeadKey(IntPtr layout)
        {
            uint scan = MapVirtualKeyEx(VK_SPACE, MAPVK_VK_TO_VSC, layout);
            var state = new byte[256];
            var sink = new StringBuilder(8);
            for (int attempt = 0; attempt < 3; attempt++)
                if (ToUnicodeEx(VK_SPACE, scan, state, sink, sink.Capacity, 0, layout) >= 0) return;
        }
    }
}

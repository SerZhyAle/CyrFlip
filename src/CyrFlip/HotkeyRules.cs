using System;
using System.Collections.Generic;
using System.Text;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// Which chords the hotkey dialog refuses because taking them would break ordinary typing
    /// (ticket S0004, KC-5). A CyrFlip chord is swallowed system-wide - and one assigned as a Windows
    /// language hotkey keeps being swallowed after CyrFlip is closed - so a chord that is also a
    /// character, a selection gesture or an editing command must never be accepted.
    ///
    /// <para>The rules are pure: what a chord types on a layout is asked through a delegate, so the
    /// whole matrix is tested with fake layouts. <see cref="Check(Hotkey)"/> is the live version,
    /// asking <c>ToUnicodeEx</c> about <b>every installed</b> layout - a chord must not start eating a
    /// character the moment the user switches to another layout.</para>
    /// </summary>
    internal static class HotkeyRules
    {
        public enum Refusal
        {
            None,
            /// <summary>Shift as the only modifier on anything but F1-F24: capitals, selection, Shift+Tab.</summary>
            ShiftOnly,
            /// <summary>Ctrl+C/V/X/Z/Y/A/S, Ctrl+Insert, Shift+Insert, Shift+Delete.</summary>
            EditingChord,
            /// <summary>The chord types a character on an installed layout (AltGr+E = "€" on German).</summary>
            TypesCharacter,
        }

        /// <summary>A refusal and, for <see cref="Refusal.TypesCharacter"/>, what it would eat and where.</summary>
        public readonly struct Verdict
        {
            public readonly Refusal Refusal;
            public readonly string Character;
            public readonly IntPtr Layout;

            public Verdict(Refusal refusal, string character = "", IntPtr layout = default)
            {
                Refusal = refusal; Character = character; Layout = layout;
            }

            public bool Refused => Refusal != Refusal.None;
        }

        private const int VK_INSERT = 0x2D, VK_DELETE = 0x2E, VK_F1 = 0x70, VK_F24 = 0x87;

        // Ctrl + one of these is an editing command in practically every application.
        private static readonly int[] EditingLetters = { 'C', 'V', 'X', 'Z', 'Y', 'A', 'S' };

        /// <summary>The rules against the live system: every installed layout, asked through <c>ToUnicodeEx</c>.</summary>
        public static Verdict Check(Hotkey hotkey) => Check(hotkey, InstalledLayouts(), CharacterTyped);

        /// <param name="layouts">The layouts to ask about.</param>
        /// <param name="typed">What the chord types on a layout, or null/empty for nothing.</param>
        public static Verdict Check(Hotkey hotkey, IEnumerable<IntPtr> layouts, Func<Hotkey, IntPtr, string?> typed)
        {
            // Win belongs to Windows' own shortcuts, never to typing.
            if (hotkey.Win) return new Verdict(Refusal.None);

            bool shiftOnly = hotkey.Shift && !hotkey.Ctrl && !hotkey.Alt;
            bool ctrlOnly = hotkey.Ctrl && !hotkey.Shift && !hotkey.Alt;

            if (shiftOnly && (hotkey.Vk == VK_INSERT || hotkey.Vk == VK_DELETE))
                return new Verdict(Refusal.EditingChord);
            // Stricter than "printable keys": Shift+Tab, Shift+Enter, Shift+Home/End/PageUp select
            // or navigate, so Shift alone is only safe on the function keys.
            if (shiftOnly && (hotkey.Vk < VK_F1 || hotkey.Vk > VK_F24))
                return new Verdict(Refusal.ShiftOnly);
            if (ctrlOnly && (hotkey.Vk == VK_INSERT || Array.IndexOf(EditingLetters, hotkey.Vk) >= 0))
                return new Verdict(Refusal.EditingChord);

            // Ctrl+Alt is how Windows spells AltGr, so that is the chord that reaches a layout's third
            // and fourth characters. Alt alone is a menu accelerator; Ctrl alone yields control
            // characters (and a space for Ctrl+Space, which nobody means as typing).
            if (hotkey.Ctrl && hotkey.Alt)
                foreach (IntPtr layout in layouts)
                {
                    string? character = typed(hotkey, layout);
                    if (!string.IsNullOrEmpty(character))
                        return new Verdict(Refusal.TypesCharacter, character!, layout);
                }

            return new Verdict(Refusal.None);
        }

        /// <summary>
        /// The Russian-source localization key that explains a refusal; the character and the layout
        /// name fill <c>{0}</c> and <c>{1}</c> of the <see cref="Refusal.TypesCharacter"/> one.
        /// </summary>
        public static string ReasonKey(Refusal refusal)
        {
            switch (refusal)
            {
                case Refusal.ShiftOnly:
                    return "Shift без Ctrl и Alt годится только для F1-F24: с другой клавишей такая комбинация мешала бы печатать и выделять текст.";
                case Refusal.EditingChord:
                    return "Это стандартная команда правки (копировать, вставить, отменить..) - её занимать нельзя.";
                case Refusal.TypesCharacter:
                    return "На раскладке «{1}» эта комбинация печатает «{0}» - CyrFlip перехватывал бы этот символ.";
                default:
                    return "";
            }
        }

        /// <summary>The refusal as a sentence in <paramref name="uiLanguage"/>, or "" when accepted.</summary>
        public static string Describe(Verdict verdict, string uiLanguage)
        {
            if (!verdict.Refused) return "";
            string text = Localization.Translate(uiLanguage, ReasonKey(verdict.Refusal));
            if (verdict.Refusal != Refusal.TypesCharacter) return text;
            return string.Format(text, verdict.Character, LanguageHotkeys.LanguageName(verdict.Layout));
        }

        private static IntPtr[] InstalledLayouts()
        {
            try
            {
                int count = (int)GetKeyboardLayoutList(0, null);
                var list = new IntPtr[Math.Max(count, 0)];
                if (count > 0) GetKeyboardLayoutList(count, list);
                return list;
            }
            catch { return new IntPtr[0]; }
        }

        /// <summary>
        /// What <paramref name="hotkey"/> types on <paramref name="layout"/>, via <c>ToUnicodeEx</c>
        /// with the chord's modifiers down. Control characters (Ctrl+A is U+0001) are not typing; a
        /// dead key is, since it composes the next character. Flag 4 leaves the keyboard state alone
        /// (Windows 10 1607+), so no dead key stays latched behind the dialog.
        /// </summary>
        internal static string? CharacterTyped(Hotkey hotkey, IntPtr layout)
        {
            try
            {
                var state = new byte[256];
                if (hotkey.Ctrl) state[Hotkey.VK_CONTROL] = state[PhysicalModifiers.VK_LCONTROL] = 0x80;
                if (hotkey.Alt) state[Hotkey.VK_MENU] = state[PhysicalModifiers.VK_LMENU] = 0x80;
                if (hotkey.Shift) state[Hotkey.VK_SHIFT] = state[PhysicalModifiers.VK_LSHIFT] = 0x80;

                uint scan = MapVirtualKeyEx((uint)hotkey.Vk, MAPVK_VK_TO_VSC, layout);
                var buffer = new StringBuilder(8);
                int count = ToUnicodeEx((uint)hotkey.Vk, scan, state, buffer, buffer.Capacity, 4, layout);
                if (count < 0) return buffer.Length > 0 ? buffer.ToString(0, 1) : "´";
                if (count == 0) return null;

                string text = buffer.ToString(0, Math.Min(count, buffer.Length));
                foreach (char c in text)
                    if (c < 0x20 || c == 0x7F) return null;
                return text;
            }
            catch { return null; }
        }
    }
}

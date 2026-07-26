using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// Editor for Windows' own "switch straight to this input language" hotkeys.
    ///
    /// <para><b>CyrFlip does not handle these chords.</b> <see cref="KeyboardHook"/> neither matches
    /// nor swallows them - we only write them into the OS settings and ask Windows to re-read them.
    /// They keep working when CyrFlip is closed. That is the whole point of the feature: a macro can
    /// press Ctrl+1 and be sure the next characters are typed in Latin, with no helper app in the loop.</para>
    ///
    /// <para><b>Storage</b> (verified empirically 2026-07-24, see PLAN/LanguageHotkeys_Spec_Idea_v0.1.md):
    /// <c>HKCU\Control Panel\Input Method\Hot Keys\&lt;id&gt;</c>, one subkey per assignment, each holding
    /// three 4-byte little-endian <c>REG_BINARY</c> values - <c>Key Modifiers</c> (a MOD_* mask),
    /// <c>Virtual Key</c> and <c>Target IME</c> (the destination HKL). The ids that mean
    /// "direct switch to a language" are <see cref="DirectSwitchFirst"/>..<see cref="DirectSwitchLast"/>;
    /// ids outside that range are the built-in IME chords (0x10, 0x70, 0x200…) and are never touched.</para>
    ///
    /// <para><b>Why this beats the built-in dialog:</b> Windows' own UI only offers Ctrl+Shift+digit and
    /// Left Alt+Shift+digit, but the stored format carries an arbitrary modifier mask and an arbitrary
    /// virtual-key, so plain Ctrl+1 is a legal record. Whether the IMM engine honours every mask it can
    /// store is not something we can assert from the format alone - hence <see cref="Assign"/> reports
    /// only that the value was written, and the UI says so in those terms.</para>
    /// </summary>
    internal static class LanguageHotkeys
    {
        private const string HotKeysPath = @"Control Panel\Input Method\Hot Keys";
        private const string TogglePath = @"Keyboard Layout\Toggle";

        /// <summary>IME_HOTKEY_DSWITCH_FIRST/LAST - the 32 "switch to language X" slots.</summary>
        public const int DirectSwitchFirst = 0x00000100;
        public const int DirectSwitchLast = 0x0000011F;

        // MOD_* flags as stored in "Key Modifiers".
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_RIGHT = 0x4000;
        public const uint MOD_LEFT = 0x8000;

        /// <summary>The modifier bits that identify the chord; the side bits are presentation only.</summary>
        private const uint ModifierMask = MOD_ALT | MOD_CONTROL | MOD_SHIFT | MOD_WIN;

        /// <summary>Re-reads the input-language hotkey settings from the registry.</summary>
        private const uint SPI_SETLANGTOGGLE = 0x005B;

        /// <summary>One assignment stored in the direct-switch range.</summary>
        internal sealed class Entry
        {
            public int Id { get; set; }
            public IntPtr TargetHkl { get; set; }
            public uint Modifiers { get; set; }
            public uint VirtualKey { get; set; }

            /// <summary>Human-readable chord, e.g. "Ctrl+1" or "Left Alt+Shift+2".</summary>
            public string Display => FormatChord(Modifiers, VirtualKey);
        }

        public enum AssignStatus
        {
            Ok,
            /// <summary>Another language already owns this chord (its name is returned separately).</summary>
            ChordTaken,
            /// <summary>All 32 direct-switch slots are occupied by other languages.</summary>
            NoFreeSlot,
            Failed,
        }

        // ---- Reading ----

        /// <summary>Every assignment currently stored in the direct-switch range, ordered by id.</summary>
        public static List<Entry> ReadAll()
        {
            var result = new List<Entry>();
            try
            {
                using RegistryKey? root = Registry.CurrentUser.OpenSubKey(HotKeysPath);
                if (root == null) return result;

                foreach (string name in root.GetSubKeyNames())
                {
                    if (!int.TryParse(name, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int id))
                        continue;
                    if (id < DirectSwitchFirst || id > DirectSwitchLast)
                        continue; // built-in IME chord - not ours to read or edit

                    using RegistryKey? key = root.OpenSubKey(name);
                    if (key == null) continue;

                    uint vk = ReadDword(key, "Virtual Key");
                    if (vk == 0) continue; // no trigger key => not a usable assignment

                    result.Add(new Entry
                    {
                        Id = id,
                        Modifiers = ReadDword(key, "Key Modifiers"),
                        VirtualKey = vk,
                        TargetHkl = new IntPtr(ReadDword(key, "Target IME")),
                    });
                }
            }
            catch { /* an unreadable key just means "nothing assigned" */ }

            result.Sort((a, b) => a.Id.CompareTo(b.Id));
            return result;
        }

        /// <summary>The assignment pointing at <paramref name="hkl"/>, or null if that layout has none.</summary>
        public static Entry? FindFor(IntPtr hkl)
            => ReadAll().Find(e => e.TargetHkl == hkl);

        /// <summary>Input layouts installed in this session, in Windows' own order, without duplicates.</summary>
        public static IntPtr[] InstalledLayouts()
        {
            try
            {
                int count = (int)GetKeyboardLayoutList(0, null);
                if (count <= 0) return new IntPtr[0];

                var list = new IntPtr[count];
                GetKeyboardLayoutList(count, list);

                var unique = new List<IntPtr>(count);
                foreach (IntPtr hkl in list)
                    if (hkl != IntPtr.Zero && !unique.Contains(hkl)) unique.Add(hkl);
                return unique.ToArray();
            }
            catch { return new IntPtr[0]; }
        }

        // ---- Writing ----

        /// <summary>
        /// Point <paramref name="hkl"/> at <paramref name="hotkey"/>, reusing that layout's existing
        /// slot if it has one. Refuses a chord already owned by a different layout rather than
        /// silently stealing it. Does not call <see cref="ApplyToWindows"/> - the caller decides when.
        /// </summary>
        public static AssignStatus Assign(IntPtr hkl, Hotkey hotkey, out string conflictingLanguage)
        {
            conflictingLanguage = "";
            uint modifiers = ToModifiers(hotkey);
            uint vk = (uint)hotkey.Vk;

            List<Entry> entries = ReadAll();
            var used = new List<int>(entries.Count);
            int? existing = null;

            foreach (Entry e in entries)
            {
                used.Add(e.Id);
                if (e.TargetHkl == hkl) { existing = e.Id; continue; }
                if (SameChord(e.Modifiers, e.VirtualKey, modifiers, vk))
                {
                    conflictingLanguage = LanguageName(e.TargetHkl);
                    return AssignStatus.ChordTaken;
                }
            }

            int slot = PickSlot(used, existing);
            if (slot < 0) return AssignStatus.NoFreeSlot;

            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(HotKeysPath + "\\" + slot.ToString("X8", CultureInfo.InvariantCulture));
                if (key == null) return AssignStatus.Failed;
                WriteDword(key, "Key Modifiers", modifiers);
                WriteDword(key, "Virtual Key", vk);
                WriteDword(key, "Target IME", unchecked((uint)(long)hkl));
                return AssignStatus.Ok;
            }
            catch { return AssignStatus.Failed; }
        }

        /// <summary>Drop the assignment for <paramref name="hkl"/>, if any.</summary>
        public static void Clear(IntPtr hkl)
        {
            Entry? entry = FindFor(hkl);
            if (entry != null) Remove(entry.Id);
        }

        /// <summary>Delete one slot by id. Ignores ids outside the direct-switch range.</summary>
        public static void Remove(int id)
        {
            if (id < DirectSwitchFirst || id > DirectSwitchLast) return;
            try
            {
                using RegistryKey? root = Registry.CurrentUser.OpenSubKey(HotKeysPath, writable: true);
                root?.DeleteSubKeyTree(id.ToString("X8", CultureInfo.InvariantCulture), throwOnMissingSubKey: false);
            }
            catch { }
        }

        /// <summary>Ask Windows to re-read the input-language hotkey settings.</summary>
        public static void ApplyToWindows()
        {
            try { SystemParametersInfo(SPI_SETLANGTOGGLE, 0, IntPtr.Zero, 0); } catch { }
        }

        // ---- Backup / restore ----

        /// <summary>Snapshot of the whole direct-switch range as JSON, for "put it back as it was".</summary>
        public static string BackupAll()
        {
            var rows = new List<Dictionary<string, object>>();
            foreach (Entry e in ReadAll())
            {
                rows.Add(new Dictionary<string, object>
                {
                    ["id"] = e.Id,
                    ["mod"] = e.Modifiers,
                    ["vk"] = e.VirtualKey,
                    ["hkl"] = unchecked((uint)(long)e.TargetHkl),
                });
            }
            try { return new JavaScriptSerializer().Serialize(rows); } catch { return ""; }
        }

        /// <summary>
        /// Restore a <see cref="BackupAll"/> snapshot: clears the direct-switch range, then rewrites
        /// exactly what was captured. An empty snapshot legitimately means "there was nothing here".
        /// </summary>
        public static void RestoreAll(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;

            List<Dictionary<string, object>>? rows;
            try { rows = new JavaScriptSerializer().Deserialize<List<Dictionary<string, object>>>(json); }
            catch { return; }
            if (rows == null) return;

            foreach (Entry e in ReadAll()) Remove(e.Id);

            foreach (Dictionary<string, object> row in rows)
            {
                try
                {
                    int id = Convert.ToInt32(row["id"], CultureInfo.InvariantCulture);
                    if (id < DirectSwitchFirst || id > DirectSwitchLast) continue;
                    using RegistryKey? key = Registry.CurrentUser.CreateSubKey(HotKeysPath + "\\" + id.ToString("X8", CultureInfo.InvariantCulture));
                    if (key == null) continue;
                    WriteDword(key, "Key Modifiers", Convert.ToUInt32(row["mod"], CultureInfo.InvariantCulture));
                    WriteDword(key, "Virtual Key", Convert.ToUInt32(row["vk"], CultureInfo.InvariantCulture));
                    WriteDword(key, "Target IME", Convert.ToUInt32(row["hkl"], CultureInfo.InvariantCulture));
                }
                catch { /* skip the malformed row, restore the rest */ }
            }
            ApplyToWindows();
        }

        // ---- Naming ----

        /// <summary>Native name of the language behind an HKL ("English (United States)", "русский").</summary>
        public static string LanguageName(IntPtr hkl)
        {
            int langId = (int)((long)hkl & 0xFFFF);
            try { return CultureInfo.GetCultureInfo(langId).NativeName; }
            catch { return "0x" + langId.ToString("X4", CultureInfo.InvariantCulture); }
        }

        /// <summary>
        /// Layout description ("US", "Russian") for the common case where the HKL is a standard
        /// layout - its KLID is then "0000" + LANGID. Alternate layouts (Dvorak, typewriter variants)
        /// encode a layout id in the high word that cannot be mapped back without guessing, so they
        /// get an empty string and the UI falls back to the raw HKL, which is never wrong.
        /// </summary>
        public static string LayoutName(IntPtr hkl)
        {
            uint value = unchecked((uint)(long)hkl);
            ushort lang = (ushort)(value & 0xFFFF);
            if ((ushort)(value >> 16) != lang) return "";
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Control\Keyboard Layouts\0000" + lang.ToString("X4", CultureInfo.InvariantCulture));
                return key?.GetValue("Layout Text") as string ?? "";
            }
            catch { return ""; }
        }

        /// <summary>The HKL as Windows writes it, e.g. "04090409".</summary>
        public static string HklText(IntPtr hkl)
            => unchecked((uint)(long)hkl).ToString("X8", CultureInfo.InvariantCulture);

        /// <summary>
        /// The whole-cycle switch code from <c>HKCU\Keyboard Layout\Toggle</c>. The 1/2/3/4 meanings are
        /// established by observation, not documentation, so <see cref="ToggleLabel"/> keeps the number
        /// visible. "3" is the code Windows writes for "no cycle hotkey".
        /// </summary>
        public static string ToggleCode()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(TogglePath);
                string? code = (key?.GetValue("Language Hotkey") ?? key?.GetValue("Hotkey")) as string;
                return code ?? "3";
            }
            catch { return "3"; }
        }

        /// <summary>Human label for a toggle code, with the raw number kept for traceability.</summary>
        public static string ToggleLabel(string code)
        {
            switch (code)
            {
                case "1": return "Left Alt+Shift (1)";
                case "2": return "Ctrl+Shift (2)";
                case "3": return "—  (3)";
                case "4": return "` (4)";
                default: return code;
            }
        }

        /// <summary>The toggle codes CyrFlip offers, in menu order.</summary>
        public static readonly string[] ToggleCodes = { "1", "2", "4", "3" };

        /// <summary>
        /// Set the whole-cycle switch hotkey. Windows keeps this choice as three parallel string values
        /// (<c>Hotkey</c>, <c>Language Hotkey</c>, <c>Layout Hotkey</c>); all three are written so the
        /// legacy dialog and the modern settings agree. Re-read by the OS via <see cref="ApplyToWindows"/>.
        /// </summary>
        public static void SetToggle(string code)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(TogglePath);
                if (key == null) return;
                key.SetValue("Hotkey", code, RegistryValueKind.String);
                key.SetValue("Language Hotkey", code, RegistryValueKind.String);
                // The layout-level cycle stays off unless the language cycle is off too, mirroring the
                // Windows dialog, where "Switch keyboard layout" defaults to the same chord family.
                key.SetValue("Layout Hotkey", code == "3" ? "3" : code, RegistryValueKind.String);
            }
            catch { }
        }

        // ---- Pure helpers (unit-tested) ----

        /// <summary>
        /// Which slot to write into: the layout's current one if it already has an assignment,
        /// otherwise the lowest free id in the direct-switch range. Never reuses an id held by
        /// another entry, even an orphaned one. Returns -1 when the range is full.
        /// </summary>
        public static int PickSlot(ICollection<int> usedIds, int? existing)
        {
            if (existing.HasValue) return existing.Value;
            for (int id = DirectSwitchFirst; id <= DirectSwitchLast; id++)
                if (!usedIds.Contains(id)) return id;
            return -1;
        }

        /// <summary>Hotkey → the MOD_* mask Windows stores. Both side bits: either Ctrl/Shift/Alt works.</summary>
        public static uint ToModifiers(Hotkey hotkey)
        {
            uint mask = MOD_LEFT | MOD_RIGHT;
            if (hotkey.Ctrl) mask |= MOD_CONTROL;
            if (hotkey.Shift) mask |= MOD_SHIFT;
            if (hotkey.Alt) mask |= MOD_ALT;
            if (hotkey.Win) mask |= MOD_WIN;
            return mask;
        }

        /// <summary>Stored mask + virtual key → the chord as text, e.g. "Ctrl+1", "Left Alt+Shift+2".</summary>
        public static string FormatChord(uint modifiers, uint virtualKey)
        {
            var parts = new List<string>(4);
            if ((modifiers & MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((modifiers & MOD_SHIFT) != 0) parts.Add("Shift");
            if ((modifiers & MOD_ALT) != 0) parts.Add("Alt");
            if ((modifiers & MOD_WIN) != 0) parts.Add("Win");
            parts.Add(Hotkey.NameForVk((int)virtualKey));
            string chord = string.Join("+", parts);

            // Windows' own "Left Alt+Shift" preset stores MOD_LEFT without MOD_RIGHT. The side bits
            // cover the whole record, not one modifier, so the qualifier goes in front of the chord
            // rather than being repeated on each part - which would claim more than the format says.
            bool leftOnly = (modifiers & MOD_LEFT) != 0 && (modifiers & MOD_RIGHT) == 0;
            return leftOnly ? "Left " + chord : chord;
        }

        /// <summary>Chord equality, ignoring the left/right presentation bits.</summary>
        public static bool SameChord(uint modifiersA, uint vkA, uint modifiersB, uint vkB)
            => vkA == vkB && (modifiersA & ModifierMask) == (modifiersB & ModifierMask);

        // ---- Registry primitives ----

        private static uint ReadDword(RegistryKey key, string name)
        {
            var raw = key.GetValue(name) as byte[];
            return raw != null && raw.Length >= 4 ? BitConverter.ToUInt32(raw, 0) : 0u;
        }

        private static void WriteDword(RegistryKey key, string name, uint value)
            => key.SetValue(name, BitConverter.GetBytes(value), RegistryValueKind.Binary);
    }
}

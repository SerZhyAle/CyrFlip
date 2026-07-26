using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// Installs, removes and reorders Windows keyboard layouts from CyrFlip's settings, so the user
    /// never has to open the Windows "Language &amp; region" pane. Everything here is a layout that
    /// already ships with Windows (its DLL is present) - <b>nothing is downloaded</b>; installing a
    /// display-language pack stays Windows' job.
    ///
    /// <para><b>Two stores, kept in sync.</b> Windows 11 keeps input layouts in two places at once:
    /// the legacy <c>HKCU\Keyboard Layout\Preload</c>/<c>Substitutes</c> (what
    /// <c>GetKeyboardLayoutList</c> reads) and the modern, authoritative
    /// <c>HKCU\Control Panel\International\User Profile</c> (a <c>Languages</c> BCP-47 list plus a
    /// <c>&lt;langid&gt;:&lt;klid&gt; = 1</c> value per keyboard). The modern store re-syncs the legacy
    /// one on sign-in, so writing only Preload would be undone on reboot. We therefore rebuild <b>both</b>
    /// from one canonical ordered KLID list on every change (<see cref="Persist"/>), and drive the live
    /// session with the documented <c>LoadKeyboardLayout</c>/<c>UnloadKeyboardLayout</c> APIs.</para>
    ///
    /// <para><b>Reversibility.</b> <see cref="BackupAll"/> captures both stores verbatim before the first
    /// edit; <see cref="RestoreAll"/> puts them back byte-for-byte. As with the language hotkeys, a change
    /// may need a sign-out/in to fully settle - the UI says so rather than pretending otherwise.</para>
    ///
    /// <para>A <b>KLID</b> is the 8-hex-digit id Windows files layouts under
    /// (<c>HKLM\SYSTEM\CurrentControlSet\Control\Keyboard Layouts\&lt;klid&gt;</c>); its low four hex
    /// digits are the language id. Preload cannot list two layouts of one language directly, so the
    /// second and later ones use a <c>d&lt;nnn&gt;&lt;langid&gt;</c> device handle resolved through
    /// <c>Substitutes</c> - constructed deterministically by <see cref="BuildPreload"/>.</para>
    /// </summary>
    internal static class InputLayouts
    {
        private const string LayoutsPath = @"SYSTEM\CurrentControlSet\Control\Keyboard Layouts";
        private const string PreloadPath = @"Keyboard Layout\Preload";
        private const string SubstitutesPath = @"Keyboard Layout\Substitutes";
        private const string ProfilePath = @"Control Panel\International\User Profile";

        /// <summary>A layout Windows knows how to load, whether or not it is currently installed.</summary>
        internal sealed class Available
        {
            public string Klid { get; set; } = "";
            public ushort LangId { get; set; }
            public string LanguageName { get; set; } = "";
            public string DisplayName { get; set; } = "";
        }

        /// <summary>A layout currently in the user's input list.</summary>
        internal sealed class Installed
        {
            public string Klid { get; set; } = "";
            public ushort LangId { get; set; }
            public string LanguageName { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public bool IsDefault { get; set; }
        }

        // ---- Reading ----

        /// <summary>Every loadable layout, grouped implicitly by language, sorted for a picker.</summary>
        public static List<Available> ListAvailable()
        {
            var result = new List<Available>();
            try
            {
                using RegistryKey? root = Registry.LocalMachine.OpenSubKey(LayoutsPath);
                if (root == null) return result;

                foreach (string klid in root.GetSubKeyNames())
                {
                    if (klid.Length != 8 || !uint.TryParse(klid, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
                        continue;
                    using RegistryKey? key = root.OpenSubKey(klid);
                    if (key == null) continue;

                    ushort langId = LangIdOf(klid);
                    result.Add(new Available
                    {
                        Klid = klid,
                        LangId = langId,
                        LanguageName = LanguageName(langId),
                        DisplayName = DisplayNameOf(key),
                    });
                }
            }
            catch { }

            result.Sort((a, b) =>
            {
                int byLang = string.Compare(a.LanguageName, b.LanguageName, StringComparison.CurrentCultureIgnoreCase);
                return byLang != 0 ? byLang : string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase);
            });
            return result;
        }

        /// <summary>The installed layouts, in Preload order (first = default), from the legacy store.</summary>
        public static List<Installed> ListInstalled()
        {
            var result = new List<Installed>();
            List<string> klids = EffectiveKlids();
            for (int i = 0; i < klids.Count; i++)
            {
                string klid = klids[i];
                ushort langId = LangIdOf(klid);
                result.Add(new Installed
                {
                    Klid = klid,
                    LangId = langId,
                    LanguageName = LanguageName(langId),
                    DisplayName = DisplayNameFor(klid),
                    IsDefault = i == 0,
                });
            }
            return result;
        }

        /// <summary>Ordered effective KLIDs: Preload entries with any Substitutes redirection applied.</summary>
        public static List<string> EffectiveKlids()
        {
            var result = new List<string>();
            try
            {
                using RegistryKey? preload = Registry.CurrentUser.OpenSubKey(PreloadPath);
                if (preload == null) return result;
                using RegistryKey? subs = Registry.CurrentUser.OpenSubKey(SubstitutesPath);

                // Preload entries are named "1".."N"; honour their numeric order, not string order.
                var names = new List<string>(preload.GetValueNames());
                names.Sort((a, b) =>
                {
                    bool ia = int.TryParse(a, out int na), ib = int.TryParse(b, out int nb);
                    if (ia && ib) return na.CompareTo(nb);
                    return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                });

                foreach (string name in names)
                {
                    if (!(preload.GetValue(name) is string raw) || raw.Length == 0) continue;
                    string effective = subs?.GetValue(raw) as string ?? raw;
                    if (effective.Length == 8) result.Add(effective.ToLowerInvariant());
                }
            }
            catch { }
            return result;
        }

        // ---- Writing ----

        /// <summary>
        /// Add <paramref name="klid"/> to the input list (idempotent) and activate it now. Returns false
        /// only if the live load failed; the registry is still made consistent so persistence holds.
        /// </summary>
        public static bool Add(string klid)
        {
            klid = klid.ToLowerInvariant();
            List<string> klids = EffectiveKlids();
            if (!klids.Contains(klid)) klids.Add(klid);
            Persist(klids);

            IntPtr hkl = LoadKeyboardLayout(klid, KLF_ACTIVATE | KLF_SUBSTITUTE_OK);
            return hkl != IntPtr.Zero;
        }

        /// <summary>
        /// Remove <paramref name="klid"/> from the input list and unload it live. Refuses to drop the
        /// last remaining layout - Windows must always keep one - reporting that via the return value.
        /// </summary>
        public static bool Remove(string klid)
        {
            klid = klid.ToLowerInvariant();
            List<string> klids = EffectiveKlids();
            if (klids.Count <= 1) return false;
            klids.RemoveAll(k => k == klid);
            Persist(klids);

            // A layout can't be unloaded while it's the active one, so make sure something else is active.
            foreach (IntPtr hkl in InstalledLayoutsLive())
            {
                if (KlidFromHkl(hkl) == klid)
                {
                    ActivateAnyOther(hkl);
                    UnloadKeyboardLayout(hkl);
                    break;
                }
            }
            return true;
        }

        /// <summary>Move <paramref name="klid"/> to the front so Windows uses it as the default layout.</summary>
        public static void MakeDefault(string klid)
        {
            klid = klid.ToLowerInvariant();
            List<string> klids = EffectiveKlids();
            if (!klids.Remove(klid)) return;
            klids.Insert(0, klid);
            Persist(klids);
        }

        /// <summary>Shift <paramref name="klid"/> one place up (-1) or down (+1) in the list.</summary>
        public static void Move(string klid, int delta)
        {
            klid = klid.ToLowerInvariant();
            List<string> klids = EffectiveKlids();
            int i = klids.IndexOf(klid);
            if (i < 0) return;
            int j = i + delta;
            if (j < 0 || j >= klids.Count) return;
            (klids[i], klids[j]) = (klids[j], klids[i]);
            Persist(klids);
        }

        /// <summary>Rewrites both the legacy and the modern store from one canonical ordered KLID list.</summary>
        public static void Persist(List<string> klids)
        {
            WriteLegacy(klids);
            WriteModern(klids);
        }

        private static void WriteLegacy(List<string> klids)
        {
            (Dictionary<string, string> preload, Dictionary<string, string> subs) = BuildPreload(klids);
            try
            {
                // Recreate Preload from scratch so removed entries don't linger.
                Registry.CurrentUser.DeleteSubKey(PreloadPath, throwOnMissingSubKey: false);
                using (RegistryKey? key = Registry.CurrentUser.CreateSubKey(PreloadPath))
                    if (key != null)
                        foreach (KeyValuePair<string, string> pair in preload)
                            key.SetValue(pair.Key, pair.Value, RegistryValueKind.String);

                using (RegistryKey? key = Registry.CurrentUser.CreateSubKey(SubstitutesPath))
                    if (key != null)
                    {
                        foreach (string name in key.GetValueNames()) key.DeleteValue(name, throwOnMissingValue: false);
                        foreach (KeyValuePair<string, string> pair in subs)
                            key.SetValue(pair.Key, pair.Value, RegistryValueKind.String);
                    }
            }
            catch { }
        }

        private static void WriteModern(List<string> klids)
        {
            try
            {
                var byLang = GroupByLanguageTag(klids); // preserves first-seen order
                using RegistryKey? profile = Registry.CurrentUser.CreateSubKey(ProfilePath);
                if (profile == null) return;

                // Drop language subkeys we no longer have any layout for.
                var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in byLang) wanted.Add(kv.Key);
                foreach (string existing in profile.GetSubKeyNames())
                    if (!wanted.Contains(existing))
                        profile.DeleteSubKeyTree(existing, throwOnMissingSubKey: false);

                var tags = new List<string>();
                foreach (KeyValuePair<string, List<string>> lang in byLang)
                {
                    tags.Add(lang.Key);
                    using RegistryKey? langKey = profile.CreateSubKey(lang.Key);
                    if (langKey == null) continue;

                    // Rewrite the langid:klid set; keep CachedLanguageName - Windows regenerates it if absent.
                    foreach (string name in langKey.GetValueNames())
                        if (name.Contains(":")) langKey.DeleteValue(name, throwOnMissingValue: false);
                    foreach (string klid in lang.Value)
                        langKey.SetValue(LangIdOf(klid).ToString("X4", CultureInfo.InvariantCulture) + ":" + klid, 1, RegistryValueKind.DWord);
                }
                profile.SetValue("Languages", tags.ToArray(), RegistryValueKind.MultiString);
            }
            catch { }
        }

        // ---- Backup / restore ----

        /// <summary>JSON snapshot of Preload, Substitutes and the whole User Profile subtree.</summary>
        public static string BackupAll()
        {
            var snap = new Dictionary<string, object>
            {
                ["preload"] = DumpValues(Registry.CurrentUser, PreloadPath),
                ["substitutes"] = DumpValues(Registry.CurrentUser, SubstitutesPath),
                ["profile"] = DumpProfile(),
            };
            try { return new JavaScriptSerializer().Serialize(snap); } catch { return ""; }
        }

        /// <summary>Restore a <see cref="BackupAll"/> snapshot exactly, replacing the current state.</summary>
        public static void RestoreAll(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            Dictionary<string, object>? snap;
            try { snap = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json); }
            catch { return; }
            if (snap == null) return;

            try
            {
                Registry.CurrentUser.DeleteSubKey(PreloadPath, throwOnMissingSubKey: false);
                using (RegistryKey? key = Registry.CurrentUser.CreateSubKey(PreloadPath))
                    RestoreValues(key, snap, "preload", RegistryValueKind.String);

                using (RegistryKey? key = Registry.CurrentUser.CreateSubKey(SubstitutesPath))
                {
                    if (key != null) foreach (string n in key.GetValueNames()) key.DeleteValue(n, false);
                    RestoreValues(key, snap, "substitutes", RegistryValueKind.String);
                }

                Registry.CurrentUser.DeleteSubKeyTree(ProfilePath, throwOnMissingSubKey: false);
                using (RegistryKey? profile = Registry.CurrentUser.CreateSubKey(ProfilePath))
                    RestoreProfile(profile, snap);
            }
            catch { }
        }

        // ---- Pure helpers (unit-tested) ----

        /// <summary>Language id encoded in a KLID: its low four hex digits.</summary>
        public static ushort LangIdOf(string klid)
        {
            if (klid.Length >= 4 && ushort.TryParse(klid.Substring(klid.Length - 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort id))
                return id;
            return 0;
        }

        /// <summary>
        /// Builds the legacy Preload + Substitutes tables for an ordered KLID list. The first layout of
        /// each language goes into Preload directly; the second and later use a <c>d&lt;nnn&gt;&lt;langid&gt;</c>
        /// device handle listed in Preload and redirected to the real KLID via Substitutes - the scheme
        /// Windows itself uses, since Preload can't name two layouts of one language.
        /// </summary>
        public static (Dictionary<string, string> preload, Dictionary<string, string> substitutes) BuildPreload(List<string> klids)
        {
            var preload = new Dictionary<string, string>();
            var subs = new Dictionary<string, string>();
            var perLangCount = new Dictionary<ushort, int>();

            int index = 1;
            foreach (string raw in klids)
            {
                string klid = raw.ToLowerInvariant();
                ushort lang = LangIdOf(klid);
                perLangCount.TryGetValue(lang, out int seen);
                perLangCount[lang] = seen + 1;

                string entry;
                if (seen == 0)
                {
                    entry = klid; // first of its language: the KLID stands on its own
                }
                else
                {
                    // d001<langid>, d002<langid>, … - a device handle redirected to the real KLID.
                    entry = "d" + seen.ToString("D3", CultureInfo.InvariantCulture).Substring(0, 3) + lang.ToString("x4", CultureInfo.InvariantCulture);
                    subs[entry] = klid;
                }
                preload[index.ToString(CultureInfo.InvariantCulture)] = entry;
                index++;
            }
            return (preload, subs);
        }

        /// <summary>Groups KLIDs by their BCP-47 language tag, preserving first-seen order end-to-end.</summary>
        public static Dictionary<string, List<string>> GroupByLanguageTag(List<string> klids)
        {
            // A plain Dictionary preserves insertion order on .NET Framework in practice; we depend on
            // that only for a stable UI, never for correctness.
            var byLang = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in klids)
            {
                string klid = raw.ToLowerInvariant();
                string tag = Bcp47ForLangId(LangIdOf(klid));
                if (!byLang.TryGetValue(tag, out List<string>? list)) { list = new List<string>(); byLang[tag] = list; }
                if (!list.Contains(klid)) list.Add(klid);
            }
            return byLang;
        }

        /// <summary>
        /// BCP-47 tag for a language id, reusing whatever tag the user's profile already carries for that
        /// language so we never split one language across two spellings ("ru" vs "ru-RU"). New languages
        /// fall back to <c>LCIDToLocaleName</c>; if that mismatches Windows' own choice the tag is cosmetic
        /// - the layout still loads live - and a sign-in re-normalizes it.
        /// </summary>
        public static string Bcp47ForLangId(ushort langId)
        {
            string? existing = ExistingProfileTagFor(langId);
            if (existing != null) return existing;

            try
            {
                var sb = new StringBuilder(85);
                if (LCIDToLocaleName(langId, sb, sb.Capacity, 0) > 0 && sb.Length > 0)
                    return sb.ToString();
            }
            catch { }
            return "0x" + langId.ToString("x4", CultureInfo.InvariantCulture);
        }

        // ---- Naming / lookup ----

        public static string LanguageName(ushort langId)
        {
            try { return CultureInfo.GetCultureInfo(langId).NativeName; }
            catch { return "0x" + langId.ToString("X4", CultureInfo.InvariantCulture); }
        }

        private static string DisplayNameFor(string klid)
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(LayoutsPath + "\\" + klid);
                return key != null ? DisplayNameOf(key) : klid;
            }
            catch { return klid; }
        }

        private static string DisplayNameOf(RegistryKey key)
        {
            if (key.GetValue("Layout Display Name") is string indirect && indirect.StartsWith("@"))
            {
                try
                {
                    var sb = new StringBuilder(256);
                    if (SHLoadIndirectString(indirect, sb, sb.Capacity, IntPtr.Zero) == 0 && sb.Length > 0)
                        return sb.ToString();
                }
                catch { }
            }
            return key.GetValue("Layout Text") as string ?? "";
        }

        private static string? ExistingProfileTagFor(ushort langId)
        {
            try
            {
                using RegistryKey? profile = Registry.CurrentUser.OpenSubKey(ProfilePath);
                if (profile == null) return null;
                foreach (string tag in profile.GetSubKeyNames())
                {
                    using RegistryKey? langKey = profile.OpenSubKey(tag);
                    if (langKey == null) continue;
                    foreach (string valueName in langKey.GetValueNames())
                    {
                        int colon = valueName.IndexOf(':');
                        if (colon == 4 && ushort.TryParse(valueName.Substring(0, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort id) && id == langId)
                            return tag;
                    }
                }
            }
            catch { }
            return null;
        }

        // ---- Live-session helpers ----

        private static IntPtr[] InstalledLayoutsLive()
        {
            try
            {
                int count = (int)GetKeyboardLayoutList(0, null);
                if (count <= 0) return new IntPtr[0];
                var list = new IntPtr[count];
                GetKeyboardLayoutList(count, list);
                return list;
            }
            catch { return new IntPtr[0]; }
        }

        /// <summary>Best-effort KLID for a live HKL: the standard "0000"+langid, matched against the list.</summary>
        private static string KlidFromHkl(IntPtr hkl)
        {
            ushort lang = (ushort)((long)hkl & 0xFFFF);
            string basic = lang.ToString("x8", CultureInfo.InvariantCulture);
            foreach (string klid in EffectiveKlids())
                if (klid == basic && LangIdOf(klid) == lang) return klid;
            // Fall back to the first effective KLID of this language (single-keyboard languages are exact).
            foreach (string klid in EffectiveKlids())
                if (LangIdOf(klid) == lang) return klid;
            return basic;
        }

        private static void ActivateAnyOther(IntPtr current)
        {
            foreach (IntPtr hkl in InstalledLayoutsLive())
                if (hkl != current) { ActivateKeyboardLayout(hkl, 0); return; }
        }

        // ---- Registry snapshot primitives ----

        private static Dictionary<string, string> DumpValues(RegistryKey root, string path)
        {
            var map = new Dictionary<string, string>();
            try
            {
                using RegistryKey? key = root.OpenSubKey(path);
                if (key != null)
                    foreach (string name in key.GetValueNames())
                        if (key.GetValue(name) is string s) map[name] = s;
            }
            catch { }
            return map;
        }

        private static Dictionary<string, object> DumpProfile()
        {
            var profileDump = new Dictionary<string, object>();
            try
            {
                using RegistryKey? profile = Registry.CurrentUser.OpenSubKey(ProfilePath);
                if (profile == null) return profileDump;

                if (profile.GetValue("Languages") is string[] langs) profileDump["Languages"] = langs;
                if (profile.GetValue("WindowsOverride") is string ov) profileDump["WindowsOverride"] = ov;

                var subs = new Dictionary<string, Dictionary<string, object>>();
                foreach (string tag in profile.GetSubKeyNames())
                {
                    using RegistryKey? langKey = profile.OpenSubKey(tag);
                    if (langKey == null) continue;
                    var values = new Dictionary<string, object>();
                    foreach (string name in langKey.GetValueNames())
                        values[name] = langKey.GetValue(name) ?? "";
                    subs[tag] = values;
                }
                profileDump["subkeys"] = subs;
            }
            catch { }
            return profileDump;
        }

        private static void RestoreValues(RegistryKey? key, Dictionary<string, object> snap, string field, RegistryValueKind kind)
        {
            if (key == null || !(snap.TryGetValue(field, out object? raw) && raw is Dictionary<string, object> map)) return;
            foreach (KeyValuePair<string, object> pair in map)
                key.SetValue(pair.Key, pair.Value?.ToString() ?? "", kind);
        }

        private static void RestoreProfile(RegistryKey? profile, Dictionary<string, object> snap)
        {
            if (profile == null || !(snap.TryGetValue("profile", out object? raw) && raw is Dictionary<string, object> p)) return;

            if (p.TryGetValue("Languages", out object? langs)) profile.SetValue("Languages", ToStringArray(langs), RegistryValueKind.MultiString);
            if (p.TryGetValue("WindowsOverride", out object? ov) && ov != null) profile.SetValue("WindowsOverride", ov.ToString(), RegistryValueKind.String);

            if (p.TryGetValue("subkeys", out object? subsObj) && subsObj is Dictionary<string, object> subs)
            {
                foreach (KeyValuePair<string, object> lang in subs)
                {
                    if (!(lang.Value is Dictionary<string, object> values)) continue;
                    using RegistryKey? langKey = profile.CreateSubKey(lang.Key);
                    if (langKey == null) continue;
                    foreach (KeyValuePair<string, object> v in values)
                    {
                        // Only two shapes occur here: the DWORD "1" markers and the string cache name.
                        if (v.Value is int i) langKey.SetValue(v.Key, i, RegistryValueKind.DWord);
                        else if (v.Value != null && int.TryParse(v.Value.ToString(), out int n) && v.Key.Contains(":")) langKey.SetValue(v.Key, n, RegistryValueKind.DWord);
                        else langKey.SetValue(v.Key, v.Value?.ToString() ?? "", RegistryValueKind.String);
                    }
                }
            }
        }

        private static string[] ToStringArray(object? value)
        {
            if (value is string[] arr) return arr;
            if (value is System.Collections.IEnumerable en)
            {
                var list = new List<string>();
                foreach (object o in en) if (o != null) list.Add(o.ToString());
                return list.ToArray();
            }
            return new string[0];
        }
    }
}

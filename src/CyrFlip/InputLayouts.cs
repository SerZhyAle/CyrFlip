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
    /// one on sign-in, so writing only Preload would be undone on reboot. We therefore bring <b>both</b>
    /// in line with one canonical ordered KLID list on every change (<see cref="Persist"/>) - the legacy
    /// store rebuilt, the modern one edited as a diff that never touches an IME/TIP value or a
    /// language CyrFlip did not empty itself - and drive the live session with the documented
    /// <c>LoadKeyboardLayout</c>/<c>UnloadKeyboardLayout</c> APIs.</para>
    ///
    /// <para><b>Phase B (ticket S0007 WL-1/WL-2).</b> Add, remove and "make default" go first through
    /// the documented input-profile API (<see cref="InputLayoutApi"/>), which works on language +
    /// keyboard/TIP pairs and writes both stores itself; the registry path above is the verified
    /// fallback for an API that is missing or did not do what it said, and the only path for ↑↓,
    /// which the API has no call for.</para>
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
            /// <summary>
            /// The modern-store language the keyboard lives under (<c>ru</c> for a US keyboard added to
            /// Russian). Up/down only moves a layout within its group - see <see cref="CanMove"/>.
            /// </summary>
            public string Group { get; set; } = "";
            /// <summary>
            /// The row stands for an IME/TIP (Pinyin as <c>00000804</c>), not for a keyboard CyrFlip
            /// can remove - see <see cref="IsManagedByWindows"/>.
            /// </summary>
            public bool ManagedByWindows { get; set; }
        }

        /// <summary>What a layout edit did. Anything but <see cref="Ok"/> means nothing was written.</summary>
        internal enum EditResult
        {
            Ok,
            /// <summary>The input list could not be read whole; an edit built on it would drop layouts (WL-10).</summary>
            Unreadable,
            /// <summary>Windows must keep one layout.</summary>
            LastLayout,
            /// <summary>
            /// The row is the stand-in a TIP shows up as in Preload (Pinyin as <c>00000804</c>); it cannot
            /// say which of its language's input methods it stands for, so it is not removed from here (WL-9).
            /// </summary>
            ManagedByWindows,
            /// <summary>Up/down across languages - the modern store would undo it at sign-in (WL-8).</summary>
            CrossLanguage,
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
            ProfileModel model = ReadProfileModelOrEmpty();
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
                    Group = GroupOf(model, klid),
                    ManagedByWindows = IsManagedByWindows(model, klid),
                });
            }
            return result;
        }

        /// <summary>Ordered effective KLIDs: Preload entries with any Substitutes redirection applied.</summary>
        public static List<string> EffectiveKlids()
        {
            TryReadEffectiveKlids(out List<string> klids);
            return klids;
        }

        /// <summary>
        /// The effective KLIDs, and whether that list is <b>whole</b>: false when Preload is missing,
        /// empty, unreadable, or holds an entry that does not resolve to a KLID. Every edit is built on
        /// this list and rewrites Preload from it, so an edit on a partial read would delete every
        /// layout the read missed - an edit refuses instead (ticket S0007, WL-10).
        /// </summary>
        internal static bool TryReadEffectiveKlids(out List<string> result)
        {
            result = new List<string>();
            try
            {
                using RegistryKey? preload = Registry.CurrentUser.OpenSubKey(PreloadPath);
                if (preload == null) return false;
                using RegistryKey? subs = Registry.CurrentUser.OpenSubKey(SubstitutesPath);

                // Preload entries are named "1".."N"; honour their numeric order, not string order.
                var names = new List<string>(preload.GetValueNames());
                names.Sort((a, b) =>
                {
                    bool ia = int.TryParse(a, out int na), ib = int.TryParse(b, out int nb);
                    if (ia && ib) return na.CompareTo(nb);
                    return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                });

                bool whole = true;
                foreach (string name in names)
                {
                    if (!(preload.GetValue(name) is string raw) || raw.Length == 0) { whole = false; continue; }
                    string effective = subs?.GetValue(raw) as string ?? raw;
                    if (IsKlid(effective)) result.Add(effective.ToLowerInvariant());
                    else whole = false;
                }
                return whole && result.Count > 0;
            }
            catch { return false; }
        }

        private static bool IsKlid(string text)
            => text.Length == 8 && uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);

        // ---- Writing ----

        /// <summary>
        /// Add <paramref name="klid"/> to the input list (idempotent) and activate it now. The registry is
        /// made consistent even when the live load fails, so persistence holds.
        /// </summary>
        public static EditResult Add(string klid)
        {
            klid = klid.ToLowerInvariant();
            if (!TryReadEffectiveKlids(out List<string> klids)) return EditResult.Unreadable;
            if (InputLayoutApi.Add(InputLayoutApi.Current, klid) == EditResult.Ok)
                ReconcileLegacy(list => { if (!list.Contains(klid)) list.Add(klid); });
            else
            {
                if (!klids.Contains(klid)) klids.Add(klid);
                Persist(klids, added: klid);
            }

            LoadKeyboardLayout(klid, KLF_ACTIVATE | KLF_SUBSTITUTE_OK);
            return EditResult.Ok;
        }

        /// <summary>
        /// Remove <paramref name="klid"/> from the input list and unload it live. Refuses to drop the
        /// last remaining layout - Windows must always keep one - and a row that stands for an IME,
        /// which Windows would put back at the next sign-in (WL-9).
        /// </summary>
        public static EditResult Remove(string klid)
        {
            klid = klid.ToLowerInvariant();
            if (!TryReadEffectiveKlids(out List<string> klids)) return EditResult.Unreadable;
            if (klids.Count <= 1) return EditResult.LastLayout;
            if (IsManagedByWindows(ReadProfileModelOrEmpty(), klid)) return EditResult.ManagedByWindows;
            EditResult? viaApi = InputLayoutApi.Remove(InputLayoutApi.Current, klid);
            if (viaApi == EditResult.LastLayout) return EditResult.LastLayout;
            if (viaApi == EditResult.Ok)
                ReconcileLegacy(list => list.RemoveAll(k => k == klid));
            else
            {
                klids.RemoveAll(k => k == klid);
                Persist(klids, removed: klid);
            }

            // A layout can't be unloaded while it's the active one, so make sure something else is active.
            foreach (IntPtr hkl in HklsToUnload(InstalledLayoutsLive(), klid, LayoutIdentity.KlidForHkl))
            {
                ActivateAnyOther(hkl);
                UnloadKeyboardLayout(hkl);
            }
            return EditResult.Ok;
        }

        /// <summary>
        /// Make <paramref name="klid"/> the default input method. The legacy list puts it first; the
        /// modern store records it as <c>InputMethodOverride</c>, which is what Windows' own "default
        /// input method" setting writes - never by reordering <c>Languages</c>, the user's preferred
        /// language list for apps and web sites (ticket S0007, WL-2). <paramref name="writtenOverride"/>
        /// is the value written, so restoring a backup that never captured it can take it back (WL-13).
        /// </summary>
        public static EditResult MakeDefault(string klid, out string? writtenOverride)
        {
            writtenOverride = null;
            klid = klid.ToLowerInvariant();
            if (!TryReadEffectiveKlids(out List<string> klids)) return EditResult.Unreadable;
            if (!klids.Contains(klid)) return EditResult.Ok;
            string? overrideBefore = ReadInputMethodOverride();
            if (InputLayoutApi.MakeDefault(InputLayoutApi.Current, klid) == EditResult.Ok)
            {
                ReconcileLegacy(list => { if (list.Remove(klid)) list.Insert(0, klid); });
                string? overrideAfter = ReadInputMethodOverride();
                if (overrideAfter != null && !string.Equals(overrideAfter, overrideBefore, StringComparison.OrdinalIgnoreCase))
                    writtenOverride = overrideAfter;
                return EditResult.Ok;
            }
            klids.Remove(klid);
            klids.Insert(0, klid);
            writtenOverride = Persist(klids, makeDefault: klid);
            return EditResult.Ok;
        }

        /// <summary>
        /// After an edit the input-profile API made, bring the legacy Preload in line only where Windows
        /// left it behind - the list the tab shows and the default it marks are read from there. A
        /// Preload that does not read whole is Windows' to settle at sign-in, never ours to rebuild.
        /// </summary>
        private static void ReconcileLegacy(Action<List<string>> edit)
        {
            if (!TryReadEffectiveKlids(out List<string> klids)) return;
            var edited = new List<string>(klids);
            edit(edited);
            if (edited.Count > 0 && !SameSequence(klids, edited)) WriteLegacy(edited);
        }

        private static string? ReadInputMethodOverride()
        {
            try
            {
                using RegistryKey? profile = Registry.CurrentUser.OpenSubKey(ProfilePath);
                return profile?.GetValue("InputMethodOverride") as string;
            }
            catch { return null; }
        }

        /// <summary>
        /// Shift <paramref name="klid"/> one place up (-1) or down (+1), <b>within its language</b>: the
        /// order of the languages is the modern store's <c>Languages</c> list, which rebuilds Preload at
        /// sign-in, so a move across languages would only be undone there (WL-8).
        /// </summary>
        public static EditResult Move(string klid, int delta)
        {
            klid = klid.ToLowerInvariant();
            if (!TryReadEffectiveKlids(out List<string> klids)) return EditResult.Unreadable;
            int i = klids.IndexOf(klid);
            if (i < 0) return EditResult.Ok;
            int j = i + delta;
            if (j < 0 || j >= klids.Count) return EditResult.Ok;
            ProfileModel model = ReadProfileModelOrEmpty();
            if (!string.Equals(GroupOf(model, klids[i]), GroupOf(model, klids[j]), StringComparison.OrdinalIgnoreCase))
                return EditResult.CrossLanguage;
            (klids[i], klids[j]) = (klids[j], klids[i]);
            Persist(klids);
            return EditResult.Ok;
        }

        /// <summary>May the row at <paramref name="index"/> move by <paramref name="delta"/>? Only onto a neighbour of its own language (WL-8).</summary>
        internal static bool CanMove(IList<Installed> rows, int index, int delta)
        {
            int j = index + delta;
            if (index < 0 || index >= rows.Count || j < 0 || j >= rows.Count) return false;
            return string.Equals(rows[index].Group, rows[j].Group, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Rewrites the legacy store from one canonical ordered KLID list and brings the modern store in
        /// line with it <b>without touching anything it does not model</b> (see <see cref="ApplyLayouts"/>).
        /// Returns the <c>InputMethodOverride</c> it wrote, if it wrote one.
        /// </summary>
        public static string? Persist(List<string> klids, string? added = null, string? makeDefault = null, string? removed = null)
        {
            WriteLegacy(klids);
            return WriteModern(klids, added, makeDefault, removed);
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

        private static string? WriteModern(List<string> klids, string? added, string? makeDefault, string? removed)
        {
            try
            {
                using RegistryKey? profile = Registry.CurrentUser.CreateSubKey(ProfilePath);
                if (profile == null) return null;
                ProfileModel before = ReadProfileModel(profile);
                ProfileModel after = before.Clone();
                ApplyLayouts(after, klids, added, makeDefault, Bcp47ForLangId, removed);
                WriteProfileDiff(profile, before, after);
                return after.InputMethodOverride != null && after.InputMethodOverride != before.InputMethodOverride
                    ? after.InputMethodOverride : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// The part of <c>HKCU\Control Panel\International\User Profile</c> the layout edits reason
        /// about: the <c>Languages</c> list, the value <b>names</b> of each language subkey in registry
        /// order, and <c>InputMethodOverride</c>. Value data is never carried through the model - a
        /// value CyrFlip does not own is simply never written, which is what keeps it intact.
        /// </summary>
        internal sealed class ProfileModel
        {
            public List<string> Languages { get; set; } = new List<string>();
            public Dictionary<string, List<string>> Subkeys { get; set; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            public string? InputMethodOverride { get; set; }

            public ProfileModel Clone()
            {
                var copy = new ProfileModel { Languages = new List<string>(Languages), InputMethodOverride = InputMethodOverride };
                foreach (KeyValuePair<string, List<string>> pair in Subkeys) copy.Subkeys[pair.Key] = new List<string>(pair.Value);
                return copy;
            }
        }

        /// <summary>A plain keyboard-layout value, <c>0419:00000419</c> - the only kind CyrFlip ever deletes.</summary>
        internal static bool IsLayoutValue(string name)
        {
            if (name.Length != 13 || name[4] != ':') return false;
            for (int i = 0; i < name.Length; i++)
                if (i != 4 && !Uri.IsHexDigit(name[i])) return false;
            return true;
        }

        /// <summary>A text input processor (IME/TIP), <c>0804:{CLSID}{PROFILE}</c> - never touched.</summary>
        internal static bool IsTipValue(string name) => name.Length > 5 && name[4] == ':' && name[5] == '{';

        /// <summary>
        /// Brings the modern profile in line with an ordered KLID list. <b>Only the plain layout values of
        /// <paramref name="removed"/> are ever removed</b> - a layout the profile lists and the KLID list
        /// does not is kept, so a list that came back short can never cost a keyboard (WL-10) - and a TIP
        /// (Microsoft Pinyin, the Japanese and Korean IMEs, every third-party
        /// input method) is outside what CyrFlip models and survives every edit (ticket S0007, WL-1).
        /// A keyboard stays under the language it already lives in (Russian with a US keyboard keeps
        /// <c>0419:00000409</c> under <c>ru</c>); a new one goes under its own language; a language
        /// subkey is dropped only when this pass removed its last layout and it holds no TIP; and the
        /// order of <c>Languages</c> is kept, with new tags appended (WL-2).
        /// </summary>
        internal static void ApplyLayouts(ProfileModel model, IList<string> klids, string? added, string? makeDefault, Func<ushort, string> tagFor, string? removed = null)
        {
            removed = removed?.ToLowerInvariant();
            var wanted = new List<string>();
            foreach (string k in klids)
            {
                string lower = k.ToLowerInvariant();
                if (!wanted.Contains(lower)) wanted.Add(lower);
            }
            added = added?.ToLowerInvariant();

            var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var emptied = new List<string>();
            foreach (KeyValuePair<string, List<string>> sub in model.Subkeys)
            {
                bool hadLayout = false;
                sub.Value.RemoveAll(name =>
                {
                    if (!IsLayoutValue(name)) return false;
                    hadLayout = true;
                    string klid = name.Substring(5).ToLowerInvariant();
                    if (wanted.Contains(klid)) { placed.Add(klid); return false; }
                    return klid == removed;
                });
                if (hadLayout && !sub.Value.Exists(IsLayoutValue) && !sub.Value.Exists(IsTipValue))
                    emptied.Add(sub.Key);
            }
            foreach (string tag in emptied)
            {
                model.Subkeys.Remove(tag);
                model.Languages.RemoveAll(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
            }

            foreach (string klid in wanted)
            {
                if (placed.Contains(klid)) continue;
                string prefix = LangIdOf(klid).ToString("X4", CultureInfo.InvariantCulture) + ":";
                string tag = tagFor(LangIdOf(klid));
                model.Subkeys.TryGetValue(tag, out List<string>? values);
                // A language whose input method is a TIP shows up in Preload as its primary KLID
                // (Pinyin as 00000804). That entry *is* the TIP; writing 0804:00000804 beside it would
                // install a keyboard the user never asked for. Only an explicit add writes it.
                if (klid != added && values != null && IsPrimaryKlid(klid)
                    && values.Exists(v => IsTipValue(v) && v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (values == null) { values = new List<string>(); model.Subkeys[tag] = values; }
                values.Add(prefix + klid);
                if (!model.Languages.Exists(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
                    model.Languages.Add(tag);
            }

            // Within a language the layout values follow the list order; everything else keeps its place.
            foreach (List<string> values in model.Subkeys.Values)
            {
                List<string> layouts = values.FindAll(IsLayoutValue);
                if (layouts.Count < 2) continue;
                var sorted = new List<string>(layouts);
                sorted.Sort((a, b) =>
                {
                    int ia = wanted.IndexOf(a.Substring(5).ToLowerInvariant()), ib = wanted.IndexOf(b.Substring(5).ToLowerInvariant());
                    return ia != ib ? ia.CompareTo(ib) : layouts.IndexOf(a).CompareTo(layouts.IndexOf(b));
                });
                int n = 0;
                for (int i = 0; i < values.Count; i++)
                    if (IsLayoutValue(values[i])) values[i] = sorted[n++];
            }

            if (makeDefault != null)
                model.InputMethodOverride = OverrideFor(model, makeDefault.ToLowerInvariant());
        }

        /// <summary>
        /// The <c>InputMethodOverride</c> spelling of a KLID: the value it is stored under (so a US
        /// keyboard under Russian stays <c>0419:00000409</c>), or the TIP a primary KLID stands for.
        /// </summary>
        private static string OverrideFor(ProfileModel model, string klid)
        {
            foreach (List<string> values in model.Subkeys.Values)
            {
                string? layout = values.Find(v => IsLayoutValue(v) && v.Substring(5).Equals(klid, StringComparison.OrdinalIgnoreCase));
                if (layout != null) return layout;
            }
            string prefix = LangIdOf(klid).ToString("X4", CultureInfo.InvariantCulture) + ":";
            if (IsPrimaryKlid(klid))
                foreach (List<string> values in model.Subkeys.Values)
                {
                    string? tip = values.Find(v => IsTipValue(v) && v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                    if (tip != null) return tip;
                }
            return prefix + klid;
        }

        private static bool IsPrimaryKlid(string klid)
            => klid.Length == 8 && klid.StartsWith("0000", StringComparison.Ordinal);

        /// <summary>
        /// The Preload row <paramref name="klid"/> is an IME/TIP rather than a keyboard: a primary KLID
        /// with no plain layout value anywhere in the profile and a TIP of its language beside it
        /// (Pinyin shows up in Preload as <c>00000804</c>). Phase A never deletes a TIP value, so
        /// removing such a row would only be undone at the next sign-in (WL-9).
        /// </summary>
        internal static bool IsManagedByWindows(ProfileModel model, string klid)
        {
            if (!IsPrimaryKlid(klid.ToLowerInvariant())) return false;
            string prefix = LangIdOf(klid).ToString("X4", CultureInfo.InvariantCulture) + ":";
            bool tip = false;
            foreach (List<string> values in model.Subkeys.Values)
                foreach (string v in values)
                {
                    if (IsLayoutValue(v) && v.Substring(5).Equals(klid, StringComparison.OrdinalIgnoreCase)) return false;
                    if (IsTipValue(v) && v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) tip = true;
                }
            return tip;
        }

        /// <summary>
        /// The language subkey a KLID lives under in the modern store - where its plain layout value is,
        /// else where the TIP it stands for is - or, for a layout the profile does not list, its own
        /// language id (WL-8).
        /// </summary>
        internal static string GroupOf(ProfileModel model, string klid)
        {
            foreach (KeyValuePair<string, List<string>> sub in model.Subkeys)
                if (sub.Value.Exists(v => IsLayoutValue(v) && v.Substring(5).Equals(klid, StringComparison.OrdinalIgnoreCase)))
                    return sub.Key;
            string prefix = LangIdOf(klid).ToString("X4", CultureInfo.InvariantCulture) + ":";
            if (IsPrimaryKlid(klid.ToLowerInvariant()))
                foreach (KeyValuePair<string, List<string>> sub in model.Subkeys)
                    if (sub.Value.Exists(v => IsTipValue(v) && v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                        return sub.Key;
            return "0x" + LangIdOf(klid).ToString("x4", CultureInfo.InvariantCulture);
        }

        private static ProfileModel ReadProfileModelOrEmpty()
        {
            try
            {
                using RegistryKey? profile = Registry.CurrentUser.OpenSubKey(ProfilePath);
                if (profile != null) return ReadProfileModel(profile);
            }
            catch { }
            return new ProfileModel();
        }

        private static ProfileModel ReadProfileModel(RegistryKey profile)
        {
            var model = new ProfileModel();
            if (profile.GetValue("Languages") is string[] langs) model.Languages.AddRange(langs);
            model.InputMethodOverride = profile.GetValue("InputMethodOverride") as string;
            foreach (string tag in profile.GetSubKeyNames())
            {
                using RegistryKey? langKey = profile.OpenSubKey(tag);
                if (langKey != null) model.Subkeys[tag] = new List<string>(langKey.GetValueNames());
            }
            return model;
        }

        /// <summary>
        /// Writes only the difference between two models: dropped language subkeys, the layout values of
        /// a language whose layout set or order changed, and <c>Languages</c> / <c>InputMethodOverride</c>
        /// when they changed. A value that is not a plain layout is never deleted or rewritten.
        /// </summary>
        private static void WriteProfileDiff(RegistryKey profile, ProfileModel before, ProfileModel after)
        {
            foreach (string tag in before.Subkeys.Keys)
                if (!after.Subkeys.ContainsKey(tag))
                    profile.DeleteSubKeyTree(tag, throwOnMissingSubKey: false);

            foreach (KeyValuePair<string, List<string>> sub in after.Subkeys)
            {
                before.Subkeys.TryGetValue(sub.Key, out List<string>? old);
                List<string> oldLayouts = old?.FindAll(IsLayoutValue) ?? new List<string>();
                List<string> newLayouts = sub.Value.FindAll(IsLayoutValue);
                if (old != null && SameSequence(oldLayouts, newLayouts)) continue;

                using RegistryKey? langKey = profile.CreateSubKey(sub.Key);
                if (langKey == null) continue;
                foreach (string name in oldLayouts) langKey.DeleteValue(name, throwOnMissingValue: false);
                foreach (string name in newLayouts) langKey.SetValue(name, 1, RegistryValueKind.DWord);
            }

            if (!SameSequence(before.Languages, after.Languages))
                profile.SetValue("Languages", after.Languages.ToArray(), RegistryValueKind.MultiString);
            if (after.InputMethodOverride != null && after.InputMethodOverride != before.InputMethodOverride)
                profile.SetValue("InputMethodOverride", after.InputMethodOverride, RegistryValueKind.String);
        }

        private static bool SameSequence(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        // ---- Backup / restore ----

        /// <summary>
        /// JSON snapshot of Preload, Substitutes and the whole User Profile subtree - every value of the
        /// profile root and of each language subkey, each with its registry kind (format 2, ticket
        /// S0007 WL-5). Format 1 (no <c>format</c> field) captured only <c>Languages</c> and
        /// <c>WindowsOverride</c> at the root and untyped subkey values; <see cref="RestoreAll"/> still
        /// reads it, because a one-time backup taken by an older release is the only one that user has.
        /// </summary>
        public static string BackupAll()
        {
            // All or nothing (WL-11): a partial dump saved as the one-time backup would, on restore,
            // delete every language subkey and recreate none. "" leaves the next edit to try again.
            try
            {
                Dictionary<string, string> preload = DumpValues(Registry.CurrentUser, PreloadPath);
                Dictionary<string, object> profile = DumpProfile();
                var snap = new Dictionary<string, object>
                {
                    ["format"] = 2,
                    ["preload"] = preload,
                    ["substitutes"] = DumpValues(Registry.CurrentUser, SubstitutesPath),
                    ["profile"] = profile,
                };
                string json = new JavaScriptSerializer().Serialize(snap);
                return TryParseSnapshot(json, out _) ? json : "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// Reads a snapshot and says whether restoring it can only put things back: it must name at least
        /// one Preload entry and at least one profile language subkey - the restore deletes the live ones
        /// of both before it writes the captured ones (WL-11). Either format.
        /// </summary>
        internal static bool TryParseSnapshot(string json, out Dictionary<string, object> snap)
        {
            snap = new Dictionary<string, object>();
            if (string.IsNullOrWhiteSpace(json)) return false;
            try { snap = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json) ?? snap; }
            catch { return false; }
            return snap.TryGetValue("preload", out object? preload) && preload is Dictionary<string, object> entries && entries.Count > 0
                && snap.TryGetValue("profile", out object? profile) && profile is Dictionary<string, object> p
                && p.TryGetValue("subkeys", out object? subs) && subs is Dictionary<string, object> tags && tags.Count > 0;
        }

        /// <summary>
        /// Restore a <see cref="BackupAll"/> snapshot: Preload and Substitutes exactly, the language
        /// subkeys of the profile exactly, the profile root values that were captured with their own
        /// kinds - the root itself is never deleted, so values the snapshot did not capture
        /// (<c>HttpAcceptLanguageOptOut</c>, the text-prediction switches..) stay. Then the live session
        /// is brought in line with the restored list. False, with nothing written, for a snapshot that
        /// does not read or would not put a layout back (CF-4, WL-11).
        /// <paramref name="overrideWrittenByCyrFlip"/> is the <c>InputMethodOverride</c> CyrFlip last
        /// wrote: a format-1 snapshot never captured that value, so it is removed when it is still ours (WL-13).
        /// </summary>
        public static bool RestoreAll(string json, string? overrideWrittenByCyrFlip = null)
        {
            if (!TryParseSnapshot(json, out Dictionary<string, object> snap)) return false;

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

                using (RegistryKey? profile = Registry.CurrentUser.CreateSubKey(ProfilePath))
                    if (profile != null)
                    {
                        foreach (string tag in profile.GetSubKeyNames())
                            profile.DeleteSubKeyTree(tag, throwOnMissingSubKey: false);
                        RestoreProfile(profile, snap);
                        if (!snap.ContainsKey("format") && ShouldDropOverride(profile.GetValue("InputMethodOverride") as string, overrideWrittenByCyrFlip))
                            profile.DeleteValue("InputMethodOverride", throwOnMissingValue: false);
                    }
            }
            catch { }
            SyncLiveSession();
            return true;
        }

        /// <summary>
        /// A format-1 restore removes <c>InputMethodOverride</c> only when it still holds exactly the
        /// value CyrFlip wrote - one the user set in Windows since is theirs (WL-13).
        /// </summary>
        internal static bool ShouldDropOverride(string? current, string? writtenByCyrFlip)
            => !string.IsNullOrEmpty(writtenByCyrFlip) && string.Equals(current, writtenByCyrFlip, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Load every layout the legacy list now names and unload the live ones it no longer does, so a
        /// restore shows at once rather than after the next sign-in. IME handles are left alone.
        /// </summary>
        private static void SyncLiveSession()
        {
            try
            {
                List<string> klids = EffectiveKlids();
                foreach (string klid in klids) LoadKeyboardLayout(klid, KLF_SUBSTITUTE_OK);
                foreach (IntPtr hkl in InstalledLayoutsLive())
                {
                    if ((unchecked((uint)(long)hkl) >> 28) == 0xE) continue;
                    string klid = LayoutIdentity.KlidForHkl(hkl).ToLowerInvariant();
                    if (klid.Length == 8 && !klids.Contains(klid))
                    {
                        ActivateAnyOther(hkl);
                        UnloadKeyboardLayout(hkl);
                    }
                }
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

        /// <summary>
        /// The live handles that belong to <paramref name="klid"/>, decoded through
        /// <see cref="LayoutIdentity"/> - the tested HKL → KLID decode - so removing standard Russian
        /// never unloads Russian Typewriter (ticket S0007, WL-7). An IME handle (<c>0xE0xx</c>) has no
        /// KLID of its own and is never unloaded on behalf of a keyboard.
        /// </summary>
        internal static List<IntPtr> HklsToUnload(IEnumerable<IntPtr> live, string klid, Func<IntPtr, string> klidOf)
        {
            var result = new List<IntPtr>();
            foreach (IntPtr hkl in live)
            {
                uint value = unchecked((uint)(long)hkl);
                if ((value >> 28) == 0xE) continue;
                if (string.Equals(klidOf(hkl), klid, StringComparison.OrdinalIgnoreCase) && !result.Contains(hkl))
                    result.Add(hkl);
            }
            return result;
        }

        private static void ActivateAnyOther(IntPtr current)
        {
            foreach (IntPtr hkl in InstalledLayoutsLive())
                if (hkl != current) { ActivateKeyboardLayout(hkl, 0); return; }
        }

        // ---- Registry snapshot primitives ----

        // The dumps throw rather than return what they managed to read: BackupAll turns any failure
        // into "no backup yet", never into a partial one (WL-11).
        private static Dictionary<string, string> DumpValues(RegistryKey root, string path)
        {
            var map = new Dictionary<string, string>();
            using RegistryKey? key = root.OpenSubKey(path);
            if (key != null)
                foreach (string name in key.GetValueNames())
                    map[name] = key.GetValue(name) as string ?? throw new InvalidOperationException(path + "\\" + name);
            return map;
        }

        private static Dictionary<string, object> DumpProfile()
        {
            var profileDump = new Dictionary<string, object>();
            using RegistryKey? profile = Registry.CurrentUser.OpenSubKey(ProfilePath);
            if (profile == null) return profileDump;

            profileDump["root"] = DumpTyped(profile);
            var subs = new Dictionary<string, object>();
            foreach (string tag in profile.GetSubKeyNames())
            {
                using RegistryKey? langKey = profile.OpenSubKey(tag) ?? throw new InvalidOperationException(ProfilePath + "\\" + tag);
                subs[tag] = DumpTyped(langKey);
            }
            profileDump["subkeys"] = subs;
            return profileDump;
        }

        private static Dictionary<string, object> DumpTyped(RegistryKey key)
        {
            var values = new Dictionary<string, object>();
            foreach (string name in key.GetValueNames())
            {
                object? value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (value != null) values[name] = EncodeValue(value, key.GetValueKind(name));
            }
            return values;
        }

        /// <summary>One registry value as JSON-safe data that remembers its kind: <c>{"k": kind, "v": value}</c>.</summary>
        internal static Dictionary<string, object> EncodeValue(object value, RegistryValueKind kind)
        {
            object encoded;
            switch (kind)
            {
                case RegistryValueKind.DWord: encoded = unchecked((uint)Convert.ToInt32(value, CultureInfo.InvariantCulture)); break;
                case RegistryValueKind.QWord: encoded = Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture); break;
                case RegistryValueKind.MultiString: encoded = value as string[] ?? new string[0]; break;
                case RegistryValueKind.String:
                case RegistryValueKind.ExpandString: encoded = value.ToString() ?? ""; break;
                default:
                    kind = RegistryValueKind.Binary;
                    encoded = Convert.ToBase64String(value as byte[] ?? new byte[0]);
                    break;
            }
            return new Dictionary<string, object> { ["k"] = kind.ToString(), ["v"] = encoded };
        }

        /// <summary>The inverse of <see cref="EncodeValue"/>; false for anything that is not its shape.</summary>
        internal static bool TryDecodeValue(object? raw, out object value, out RegistryValueKind kind)
        {
            value = ""; kind = RegistryValueKind.String;
            if (!(raw is Dictionary<string, object> map) || !map.TryGetValue("k", out object? k) || !map.TryGetValue("v", out object? v)
                || !Enum.TryParse(k as string, out kind))
                return false;
            try
            {
                switch (kind)
                {
                    case RegistryValueKind.DWord: value = unchecked((int)Convert.ToUInt32(v, CultureInfo.InvariantCulture)); return true;
                    case RegistryValueKind.QWord: value = long.Parse(v?.ToString() ?? "", CultureInfo.InvariantCulture); return true;
                    case RegistryValueKind.MultiString: value = ToStringArray(v); return true;
                    case RegistryValueKind.String:
                    case RegistryValueKind.ExpandString: value = v?.ToString() ?? ""; return true;
                    case RegistryValueKind.Binary: value = Convert.FromBase64String(v?.ToString() ?? ""); return true;
                    default: return false;
                }
            }
            catch { return false; }
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

            if (snap.ContainsKey("format"))
            {
                RestoreTyped(profile, p.TryGetValue("root", out object? root) ? root : null);
                // InputMethodOverride is the one root value CyrFlip itself writes; absent from the
                // snapshot means it was absent before CyrFlip's first edit.
                if (!(root is Dictionary<string, object> captured && captured.ContainsKey("InputMethodOverride")))
                    profile.DeleteValue("InputMethodOverride", throwOnMissingValue: false);
                if (p.TryGetValue("subkeys", out object? typedSubs) && typedSubs is Dictionary<string, object> tags)
                    foreach (KeyValuePair<string, object> lang in tags)
                    {
                        using RegistryKey? langKey = profile.CreateSubKey(lang.Key);
                        RestoreTyped(langKey, lang.Value);
                    }
                return;
            }

            // Format 1 - written by releases before S0007.
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

        private static void RestoreTyped(RegistryKey? key, object? values)
        {
            if (key == null || !(values is Dictionary<string, object> map)) return;
            foreach (KeyValuePair<string, object> pair in map)
                if (TryDecodeValue(pair.Value, out object value, out RegistryValueKind kind))
                    key.SetValue(pair.Key, value, kind);
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

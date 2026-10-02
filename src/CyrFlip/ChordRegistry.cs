using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>Who a chord can belong to. The first six are CyrFlip's; the last is Windows'.</summary>
    internal enum ChordKind
    {
        Case,
        History,
        QuickNotes,
        Conversion,
        Translation,
        Launcher,
        /// <summary>The Graphics module's region capture (S0026).</summary>
        Screenshot,
        /// <summary>The keyboard chord that opens the text context menu at the caret (S0045 K2).</summary>
        TextMenu,
        /// <summary>A Windows direct-language hotkey (<see cref="LanguageHotkeys"/>) - read-only here.</summary>
        WindowsLanguage,
    }

    /// <summary>One chord and what owns it.</summary>
    internal sealed class ChordOwner
    {
        public ChordOwner(ChordKind kind, string id, Hotkey chord, object? source = null)
        {
            Kind = kind; Id = id; Chord = chord; Source = source;
        }

        public ChordKind Kind { get; }
        /// <summary>The row / scenario / Windows slot id; empty for the three fixed chords.</summary>
        public string Id { get; }
        public Hotkey Chord { get; }
        /// <summary>The profile, scenario or Windows entry behind it - what the label is made from.</summary>
        public object? Source { get; }

        public bool IsWindows => Kind == ChordKind.WindowsLanguage;
    }

    /// <summary>
    /// Every chord on this machine that CyrFlip knows about and who owns it - the one
    /// chord-ownership check behind every setter and every enable switch (ticket S0004, KC-6).
    ///
    /// <para>Before, each setter asked its own question, and the questions disagreed: only
    /// <b>active</b> chords counted, so a chord could be given to a second action while the first
    /// was switched off, the master switch turned the whole check off, and switching a row back on
    /// was not checked at all. Untick the case flip, give its Ctrl+Shift+F11 to a conversion row,
    /// tick the case flip again - and the conversion silently never fired, because the hook tests
    /// the case chord first. Here <b>a switched-off chord still owns its chord</b>, whatever switch
    /// is off, so that sequence is refused at its second step.</para>
    ///
    /// <para>Windows' own direct-language hotkeys are in the registry as read-only owners: a clash
    /// with one is the user's call to make (it is their system), so it is a question, never a
    /// refusal. The Windows layout-cycle toggle (Alt+Shift, Ctrl+Shift, grave) is deliberately not
    /// here: it is a bare-modifier gesture or a key with no modifier, and no CyrFlip chord - always a
    /// trigger key plus at least one modifier - can equal it. Its interaction with CyrFlip's chords
    /// is the bare-modifier release, which <see cref="KeyInjection"/>'s mask key handles (KC-4).</para>
    /// </summary>
    internal sealed class ChordRegistry
    {
        private readonly List<ChordOwner> _owners;

        public ChordRegistry(IEnumerable<ChordOwner> owners) => _owners = new List<ChordOwner>(owners);

        public IReadOnlyList<ChordOwner> Owners => _owners;

        /// <summary>Everything the config, the launcher store and Windows' hotkey records say.</summary>
        public static ChordRegistry Build(AppConfig config, IEnumerable<LauncherScenario> scenarios,
            IEnumerable<LanguageHotkeys.Entry> windowsHotkeys)
        {
            var owners = new List<ChordOwner>();
            void Add(ChordKind kind, string id, string? chord, object? source)
            {
                // A chord that does not parse binds nothing (the hook drops it too), so it owns nothing.
                if (Hotkey.TryParse(chord, out Hotkey parsed))
                    owners.Add(new ChordOwner(kind, id, parsed, source));
            }

            Add(ChordKind.Case, "", config.CaseHotkey, null);
            Add(ChordKind.History, "", config.ClipboardHistoryHotkey, null);
            Add(ChordKind.QuickNotes, "", config.QuickNotesHotkey, null);
            Add(ChordKind.Screenshot, "", config.ScreenshotHotkey, null);
            Add(ChordKind.TextMenu, "", config.TextMenuHotkey, null);
            foreach (LayoutConversionProfile profile in config.LayoutConversionProfiles)
                if (profile != null) Add(ChordKind.Conversion, profile.Id, profile.Hotkey, profile);
            foreach (TranslationProfile profile in config.TranslateProfiles)
                if (profile != null) Add(ChordKind.Translation, profile.Id, profile.Hotkey, profile);
            foreach (LauncherScenario scenario in scenarios)
                Add(ChordKind.Launcher, scenario.Id.ToString(), scenario.Hotkey, scenario);
            foreach (LanguageHotkeys.Entry entry in windowsHotkeys)
                owners.Add(new ChordOwner(ChordKind.WindowsLanguage, entry.Id.ToString(), ToHotkey(entry), entry));
            return new ChordRegistry(owners);
        }

        /// <summary>A Windows hotkey record as a <see cref="Hotkey"/>; the side bits are presentation only.</summary>
        internal static Hotkey ToHotkey(LanguageHotkeys.Entry entry)
        {
            int vk = (int)entry.VirtualKey;
            return new Hotkey((entry.Modifiers & LanguageHotkeys.MOD_CONTROL) != 0,
                (entry.Modifiers & LanguageHotkeys.MOD_SHIFT) != 0,
                (entry.Modifiers & LanguageHotkeys.MOD_ALT) != 0,
                (entry.Modifiers & LanguageHotkeys.MOD_WIN) != 0,
                vk, Hotkey.NameForVk(vk));
        }

        /// <summary>
        /// The CyrFlip action other than (<paramref name="kind"/>, <paramref name="id"/>) that owns
        /// <paramref name="chord"/>, enabled or not, or null.
        /// </summary>
        public ChordOwner? CyrFlipOwnerOf(Hotkey chord, ChordKind kind, string? id = null)
        {
            foreach (ChordOwner owner in _owners)
                if (!owner.IsWindows && !IsSelf(owner, kind, id) && owner.Chord.SameChord(chord))
                    return owner;
            return null;
        }

        /// <summary>The Windows direct-language hotkey on <paramref name="chord"/>, or null.</summary>
        public ChordOwner? WindowsOwnerOf(Hotkey chord)
        {
            foreach (ChordOwner owner in _owners)
                if (owner.IsWindows && owner.Chord.SameChord(chord))
                    return owner;
            return null;
        }

        /// <summary>
        /// Pairs of CyrFlip actions sharing one chord. The setters make new ones impossible; these
        /// are what a config written before this check can still carry.
        /// </summary>
        public List<KeyValuePair<ChordOwner, ChordOwner>> Duplicates()
        {
            var pairs = new List<KeyValuePair<ChordOwner, ChordOwner>>();
            for (int i = 0; i < _owners.Count; i++)
            {
                if (_owners[i].IsWindows) continue;
                for (int j = i + 1; j < _owners.Count; j++)
                    if (!_owners[j].IsWindows && _owners[i].Chord.SameChord(_owners[j].Chord))
                        pairs.Add(new KeyValuePair<ChordOwner, ChordOwner>(_owners[i], _owners[j]));
            }
            return pairs;
        }

        // The three fixed chords have the empty id, so for them the kind alone is "self"; a new row
        // (id null) has no self at all, since no stored row carries an empty id.
        private static bool IsSelf(ChordOwner owner, ChordKind kind, string? id)
            => owner.Kind == kind && owner.Id == (id ?? "");

        /// <summary>What the user calls the owner, in <paramref name="uiLanguage"/>.</summary>
        public static string Label(ChordOwner owner, string uiLanguage, Func<string, string> layoutName)
        {
            string T(string key) => Localization.Translate(uiLanguage, key);
            switch (owner.Kind)
            {
                case ChordKind.Case: return T("Исправить CapsLock");
                case ChordKind.History: return T("Менеджер буфера");
                case ChordKind.QuickNotes: return T("Быстрые заметки");
                case ChordKind.Screenshot: return T("Снимок области экрана");
                case ChordKind.TextMenu: return T("Открыть меню с клавиатуры");
                case ChordKind.Conversion:
                    var conversion = (LayoutConversionProfile)owner.Source!;
                    return layoutName(conversion.SourceKlid) + " ⇄ " + layoutName(conversion.TargetKlid);
                case ChordKind.Translation:
                    var translation = (TranslationProfile)owner.Source!;
                    return T("Перевод") + ": " + TranslationLanguages.Label(translation.TargetLang, uiLanguage);
                case ChordKind.Launcher: return ((LauncherScenario)owner.Source!).Name;
                default: return LanguageHotkeys.LanguageName(((LanguageHotkeys.Entry)owner.Source!).TargetHkl);
            }
        }
    }

    /// <summary>
    /// The conversation around <see cref="ChordRegistry"/>: the refusal when another CyrFlip action
    /// owns a chord, the question when a Windows language hotkey does. Shared by the tray's setters
    /// and the settings window so the two cannot word it - or decide it - differently.
    /// </summary>
    internal static class ChordGuard
    {
        /// <summary>
        /// True when <paramref name="chord"/> may go to (<paramref name="kind"/>, <paramref name="id"/>).
        /// A CyrFlip owner refuses it; a Windows owner is asked about when <paramref name="askAboutWindows"/>
        /// (a new assignment), and ignored for a switch that only turns an existing chord back on.
        /// </summary>
        public static bool IsFree(IWin32Window? parent, ChordRegistry registry, Hotkey chord, ChordKind kind, string? id,
            string uiLanguage, Func<string, string> layoutName, bool askAboutWindows = true)
        {
            string T(string key) => Localization.Translate(uiLanguage, key);

            ChordOwner? owner = registry.CyrFlipOwnerOf(chord, kind, id);
            if (owner != null)
            {
                ConfirmDialog.Show(parent, uiLanguage, Localization.Format(T, "Комбинация {0} уже занята действием «{1}».",
                    chord.Display, ChordRegistry.Label(owner, uiLanguage, layoutName)),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (!askAboutWindows) return true;
            ChordOwner? windows = registry.WindowsOwnerOf(chord);
            if (windows == null) return true;
            return ConfirmDialog.Show(parent, uiLanguage, Localization.Format(T, "Комбинация {0} назначена в Windows для переключения на язык «{1}». Если её займёт CyrFlip, Windows её больше не получит. Всё равно назначить?",
                    chord.Display, ChordRegistry.Label(windows, uiLanguage, layoutName)),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, danger: true) == DialogResult.Yes;
        }

        /// <summary>
        /// Say so when a config carries two CyrFlip actions on one chord (written before the check
        /// existed). A warning, not a refusal: switching a whole module off over one row would
        /// leave the user unable to reach the row that needs changing.
        /// </summary>
        public static void WarnDuplicates(IWin32Window? parent, ChordRegistry registry, string uiLanguage,
            Func<string, string> layoutName)
        {
            List<KeyValuePair<ChordOwner, ChordOwner>> pairs = registry.Duplicates();
            if (pairs.Count == 0) return;
            KeyValuePair<ChordOwner, ChordOwner> first = pairs[0];
            ConfirmDialog.Show(parent, uiLanguage, Localization.Format(uiLanguage,
                    "Комбинация {0} назначена сразу двум действиям: «{1}» и «{2}». Сработает только одно - смените одну из них.",
                    first.Key.Chord.Display, ChordRegistry.Label(first.Key, uiLanguage, layoutName),
                    ChordRegistry.Label(first.Value, uiLanguage, layoutName)),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace CyrFlip
{
    /// <summary>Why a row of a reset was not reset (ticket S0056, R4 and R8).</summary>
    internal enum ChordSkipReason
    {
        SaveFailed,
        /// <summary>The shipped chord is held by another CyrFlip action.</summary>
        Taken,
        /// <summary>The row changed between the plan and the apply: nothing is applied from a stale plan.</summary>
        Changed,
        /// <summary>The row or scenario is gone.</summary>
        Missing,
    }

    internal sealed class ChordResetSkip
    {
        public ChordResetSkip(string caption, ChordSkipReason reason, string? heldBy, string? chord = null)
        {
            Caption = caption; Reason = reason; HeldBy = heldBy; Chord = chord ?? "";
        }

        public string Caption { get; }
        public ChordSkipReason Reason { get; }
        /// <summary>The action that holds the chord, for <see cref="ChordSkipReason.Taken"/>.</summary>
        public string? HeldBy { get; }
        /// <summary>The chord that was to be put back, as shown to the user.</summary>
        public string Chord { get; }
    }

    /// <summary>One edit the reset will make: a chord and, where the row has one, its own switch.</summary>
    internal sealed class ChordResetStep
    {
        public ChordGroup Group { get; set; }
        public ChordKind? Kind { get; set; }
        public string Id { get; set; } = "";
        public string Caption { get; set; } = "";
        /// <summary>The row as it was when the plan was made - the apply refuses a row that no longer matches.</summary>
        public string FromChord { get; set; } = "";
        public bool FromSwitch { get; set; }
        /// <summary>The chord token to write; empty means "clear the chord".</summary>
        public string ToChord { get; set; } = "";
        /// <summary>The switch to write; null leaves the switch as it is.</summary>
        public bool? ToSwitch { get; set; }

        public bool Clears => ToChord.Length == 0;
        public bool ChangesChord => !ChordDefaults.SameChordToken(FromChord, ToChord);
    }

    internal sealed class ChordResetResult
    {
        public List<ChordResetStep> Applied { get; } = new List<ChordResetStep>();
        public List<ChordResetSkip> Skipped { get; } = new List<ChordResetSkip>();
        public bool MasterRestored { get; set; }
        /// <summary>A scenario's file was rewritten - the caller refreshes the launcher surfaces.</summary>
        public bool ScenariosChanged => Applied.Any(step => step.Group == ChordGroup.Launcher);
    }

    /// <summary>
    /// "Put the chords back the way they came" (ticket S0056 section 6) as a value: computed once from the
    /// live state, shown to the user as counts and reasons, and applied once - the same object, with the
    /// state re-checked at apply time (R8). Pure: the config, the scenarios and Windows' records are
    /// arguments; scenario files are written through a callback.
    ///
    /// <para><b>What it touches</b> (R1): a chord token and that chord's own per-row switch. <b>What it never
    /// touches</b> (R2): a module switch, a row's languages, the number of rows, a scenario's program.
    /// A row the user made has no shipped chord, so its reset is "clear the chord" (R3). A shipped chord
    /// another CyrFlip action holds is not stolen: that one row is reported (R4).</para>
    /// </summary>
    internal sealed class ChordResetPlan
    {
        private ChordResetPlan(List<ChordResetStep> steps, List<ChordResetSkip> skipped, int alreadyDefault,
            bool restoreMaster, List<KeyValuePair<string, string>> windowsHolds)
        {
            Steps = steps; Skipped = skipped; AlreadyDefault = alreadyDefault;
            RestoreMaster = restoreMaster; WindowsHolds = windowsHolds;
        }

        public IReadOnlyList<ChordResetStep> Steps { get; }
        /// <summary>Rows that cannot be reset because a CyrFlip action holds their shipped chord (R4).</summary>
        public IReadOnlyList<ChordResetSkip> Skipped { get; }
        /// <summary>Rows that already are what they shipped as.</summary>
        public int AlreadyDefault { get; }
        /// <summary>"Reset all" also turns the master switch back on (D4); a single-row reset never does.</summary>
        public bool RestoreMaster { get; }
        /// <summary>(row caption, Windows action) for a chord a Windows language hotkey holds - asked once per reset (R5).</summary>
        public IReadOnlyList<KeyValuePair<string, string>> WindowsHolds { get; }

        public bool IsEmpty => Steps.Count == 0 && !RestoreMaster;
        /// <summary>A chord the user set by hand would change - the confirmation paints Yes in the danger role (R7).</summary>
        public bool ChangesChords => Steps.Any(step => step.ChangesChord);

        /// <summary>The plan for every row of the overview.</summary>
        public static ChordResetPlan ForAll(AppConfig config, IEnumerable<ChordOverviewRow> rows,
            IEnumerable<LauncherScenario> scenarios, IEnumerable<LanguageHotkeys.Entry> windowsHotkeys,
            string uiLanguage, Func<string, string> layoutName)
            => Build(config, rows, scenarios, windowsHotkeys, uiLanguage, layoutName, restoreMaster: true);

        /// <summary>The plan for one row (R6): no master switch, no confirmation, one visible edit.</summary>
        public static ChordResetPlan ForRow(AppConfig config, ChordOverviewRow row,
            IEnumerable<LauncherScenario> scenarios, IEnumerable<LanguageHotkeys.Entry> windowsHotkeys,
            string uiLanguage, Func<string, string> layoutName)
            => Build(config, new[] { row }, scenarios, windowsHotkeys, uiLanguage, layoutName, restoreMaster: false);

        private static ChordResetPlan Build(AppConfig config, IEnumerable<ChordOverviewRow> rows,
            IEnumerable<LauncherScenario> scenarios, IEnumerable<LanguageHotkeys.Entry> windowsHotkeys,
            string uiLanguage, Func<string, string> layoutName, bool restoreMaster)
        {
            var scenarioList = scenarios.ToList();
            var windowsList = windowsHotkeys.ToList();
            ChordRegistry registry = ChordRegistry.Build(config, scenarioList, windowsList);
            var steps = new List<ChordResetStep>();
            var skipped = new List<ChordResetSkip>();
            var windowsHolds = new List<KeyValuePair<string, string>>();
            int already = 0;

            foreach (ChordOverviewRow row in rows)
            {
                if (row.ReadOnly) continue;
                if (!row.Differs) { already++; continue; }

                var step = new ChordResetStep
                {
                    Group = row.Group, Kind = row.Kind, Id = row.Id, Caption = row.Caption,
                    FromChord = row.StoredChord, FromSwitch = row.SwitchOn,
                    ToChord = row.DefaultChord ?? "",
                    ToSwitch = row.HasSwitch && row.DefaultChord != null ? row.DefaultSwitchOn : (bool?)null,
                };

                if (step.ToChord.Length > 0 && Hotkey.TryParse(step.ToChord, out Hotkey shipped))
                {
                    bool switchesOn = step.ToSwitch == true && !row.SwitchOn;
                    // A chord that only comes back on is checked like assigning it (KC-6); one that stays
                    // as it is, switched on already, owes nothing.
                    if ((step.ChangesChord || switchesOn) && row.Kind.HasValue)
                    {
                        ChordOwner? holder = registry.CyrFlipOwnerOf(shipped, row.Kind.Value, row.Id);
                        if (holder != null)
                        {
                            skipped.Add(new ChordResetSkip(row.Caption, ChordSkipReason.Taken,
                                ChordRegistry.Label(holder, uiLanguage, layoutName), shipped.Display));
                            continue;
                        }
                    }
                    if (step.ChangesChord)
                    {
                        ChordOwner? windows = registry.WindowsOwnerOf(shipped);
                        if (windows != null)
                            windowsHolds.Add(new KeyValuePair<string, string>(row.Caption,
                                ChordRegistry.Label(windows, uiLanguage, layoutName)));
                    }
                }
                steps.Add(step);
            }

            return new ChordResetPlan(steps, skipped, already, restoreMaster && !config.EnableHotkeys, windowsHolds);
        }

        /// <summary>
        /// Apply the plan to the live state. Each step is re-checked first: a row that is gone, no longer
        /// what the plan saw, or whose shipped chord has since been taken is skipped and reported, never
        /// applied from a stale plan (R8). The caller saves the config and rebinds the hook.
        /// </summary>
        public ChordResetResult Apply(AppConfig config, IReadOnlyList<LauncherScenario> scenarios,
            IEnumerable<LanguageHotkeys.Entry> windowsHotkeys, string uiLanguage, Func<string, string> layoutName,
            Action<LauncherScenario> saveScenario)
        {
            var result = new ChordResetResult();
            // What the plan already knew it could not do is part of what the user is told.
            result.Skipped.AddRange(Skipped);
            var windowsList = windowsHotkeys.ToList();

            foreach (ChordResetStep step in Steps)
            {
                // The registry is rebuilt per step: an earlier step may have freed or taken a chord.
                ChordRegistry registry = ChordRegistry.Build(config, scenarios, windowsList);

                if (step.Group == ChordGroup.Mouse)
                {
                    string live = MouseChord.Parse(config.ContextMenuChord).Token;
                    if (!string.Equals(live, step.FromChord, StringComparison.Ordinal))
                    { result.Skipped.Add(new ChordResetSkip(step.Caption, ChordSkipReason.Changed, null)); continue; }
                    config.ContextMenuChord = step.ToChord;
                    result.Applied.Add(step);
                    continue;
                }

                if (!TryLocate(config, scenarios, step, out Func<string> getChord, out Func<bool> getSwitch,
                        out Action<string> setChord, out Action<bool>? setSwitch, out LauncherScenario? scenario))
                { result.Skipped.Add(new ChordResetSkip(step.Caption, ChordSkipReason.Missing, null)); continue; }

                if (!ChordDefaults.SameChordToken(getChord(), step.FromChord)
                    || (setSwitch != null && getSwitch() != step.FromSwitch))
                { result.Skipped.Add(new ChordResetSkip(step.Caption, ChordSkipReason.Changed, null)); continue; }

                if (step.ToChord.Length > 0 && step.Kind.HasValue && Hotkey.TryParse(step.ToChord, out Hotkey chord)
                    && (step.ChangesChord || (step.ToSwitch == true && !step.FromSwitch)))
                {
                    ChordOwner? holder = registry.CyrFlipOwnerOf(chord, step.Kind.Value, step.Id);
                    if (holder != null)
                    {
                        result.Skipped.Add(new ChordResetSkip(step.Caption, ChordSkipReason.Taken,
                            ChordRegistry.Label(holder, uiLanguage, layoutName), chord.Display));
                        continue;
                    }
                }

                setChord(step.ToChord);
                if (step.ToSwitch.HasValue && setSwitch != null) setSwitch(step.ToSwitch.Value);
                if (scenario != null)
                {
                    try { saveScenario(scenario); }
                    catch (Exception failure)
                    {
                        setChord(step.FromChord);
                        if (setSwitch != null) setSwitch(step.FromSwitch);
                        result.Skipped.Add(new ChordResetSkip(step.Caption, ChordSkipReason.SaveFailed,
                            FailureCause.Describe(failure, uiLanguage)));
                        continue;
                    }
                }
                result.Applied.Add(step);
            }

            if (RestoreMaster && !config.EnableHotkeys)
            {
                config.EnableHotkeys = true;
                result.MasterRestored = true;
            }
            return result;
        }

        private static bool TryLocate(AppConfig config, IReadOnlyList<LauncherScenario> scenarios, ChordResetStep step,
            out Func<string> getChord, out Func<bool> getSwitch, out Action<string> setChord,
            out Action<bool>? setSwitch, out LauncherScenario? scenario)
        {
            getChord = () => ""; getSwitch = () => false; setChord = _ => { }; setSwitch = null; scenario = null;
            if (!step.Kind.HasValue) return false;
            switch (step.Kind.Value)
            {
                case ChordKind.Conversion:
                    LayoutConversionProfile? conversion = config.LayoutConversionProfiles.FirstOrDefault(p => p != null && p.Id == step.Id);
                    if (conversion == null) return false;
                    getChord = () => conversion.Hotkey; setChord = value => conversion.Hotkey = value;
                    getSwitch = () => conversion.Enabled; setSwitch = value => conversion.Enabled = value;
                    return true;
                case ChordKind.Translation:
                    TranslationProfile? translation = config.TranslateProfiles.FirstOrDefault(p => p != null && p.Id == step.Id);
                    if (translation == null) return false;
                    getChord = () => translation.Hotkey; setChord = value => translation.Hotkey = value;
                    getSwitch = () => translation.Enabled; setSwitch = value => translation.Enabled = value;
                    return true;
                case ChordKind.Launcher:
                    LauncherScenario? found = scenarios.FirstOrDefault(s => s != null && s.Id.ToString() == step.Id);
                    if (found == null) return false;
                    scenario = found;
                    getChord = () => found.Hotkey; setChord = value => found.Hotkey = value;
                    return true;
                case ChordKind.WindowsLanguage:
                    return false;
                default:
                    ChordKind kind = step.Kind.Value;
                    getChord = () => ChordConfigAccess.GetChord(config, kind);
                    setChord = value => ChordConfigAccess.SetChord(config, kind, value);
                    getSwitch = () => ChordConfigAccess.GetSwitch(config, kind);
                    setSwitch = value => ChordConfigAccess.SetSwitch(config, kind, value);
                    return true;
            }
        }
    }
}

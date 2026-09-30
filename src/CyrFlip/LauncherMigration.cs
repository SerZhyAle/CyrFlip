using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CyrFlip
{
    /// <summary>
    /// One-way, non-destructive import of OneClickRunner's scenarios
    /// (<c>%APPDATA%\OneClickRunner\Scenarios\*.xml</c>) into the CyrFlip store. The source is only
    /// ever read - never renamed, rewritten or deleted (tech plan §6.3, <c>SCENARIO-FILE</c> rule 7), so
    /// a parallel OneClickRunner install keeps working. Original Guids survive unless they collide with
    /// an existing CyrFlip scenario with other content, in which case a fresh Guid is assigned and counted
    /// for the summary (spec §5.3, <c>SCENARIO-FILE</c> rule 8 - a migration preserves identity where a plain
    /// import replaces it). A scenario already present with the same id and content is skipped and counted,
    /// so repeating the migration does not double the list (S0014 B5).
    /// </summary>
    internal static class LauncherMigration
    {
        internal sealed class Result
        {
            public int Imported;
            /// <summary>Corrupt/unreadable source files, by name.</summary>
            public readonly List<string> Skipped = new List<string>();
            /// <summary>Scenarios that arrived with a colliding Guid and got a fresh one.</summary>
            public int NewIds;
            /// <summary>Scenarios already in the store from an earlier migration (same id, same content): skipped.</summary>
            public int AlreadyPresent;
            /// <summary>Scenarios whose chord something else already owned, so it was cleared.</summary>
            public int ChordsDropped;
        }

        /// <summary>
        /// Clear <paramref name="scenario"/>'s chord when something else already owns it, and say
        /// whether it did. The scenario is already in the store, so it ignores itself in the check.
        /// One chord belongs to one action - the same rule the XML import, the scenario dialog and
        /// every setter keep (S0034 LS2-8).
        /// </summary>
        public static bool StripClashingHotkey(LauncherScenarioStore store, LauncherScenario scenario, ChordRegistry chords)
        {
            if (scenario.Hotkey.Length == 0) return false;
            if (Hotkey.TryParse(scenario.Hotkey, out Hotkey chord)
                && chords.CyrFlipOwnerOf(chord, ChordKind.Launcher, scenario.Id.ToString()) == null) return false;
            scenario.Hotkey = "";
            store.Update(scenario);
            return true;
        }

        /// <summary>True when the OneClickRunner scenario folder exists and holds at least one XML.</summary>
        public static bool SourceExists(string? sourceFolder = null)
        {
            string folder = sourceFolder ?? LauncherScenarioStore.OneClickRunnerFolder;
            try { return Directory.Exists(folder) && Directory.GetFiles(folder, "*.xml").Length > 0; }
            catch { return false; }
        }

        public static int SourceCount(string? sourceFolder = null)
        {
            string folder = sourceFolder ?? LauncherScenarioStore.OneClickRunnerFolder;
            try { return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.xml").Length : 0; }
            catch { return 0; }
        }

        /// <summary>
        /// Copy every readable scenario into <paramref name="store"/>, appending to the end in the
        /// source's own order (<see cref="InSourceOrder"/>). Legacy <c>SPECIAL_YTDLP</c> files arrive as the yt-dlp type (the store's
        /// reader normalizes them), so old OneClickRunner files keep their meaning.
        ///
        /// <para>A file CyrFlip wrote carries its <c>Hotkey</c> element, and the import is repeatable
        /// by design: with <paramref name="chords"/> given, every imported chord that something else
        /// already owns is cleared (S0034 LS2-8) - a second import used to bind one chord to two
        /// scenarios. The registry is rebuilt per scenario, since each clear changes it.</para>
        /// </summary>
        public static Result Import(LauncherScenarioStore store, string? sourceFolder = null,
            Func<ChordRegistry>? chords = null)
        {
            var result = new Result();
            string folder = sourceFolder ?? LauncherScenarioStore.OneClickRunnerFolder;
            if (!Directory.Exists(folder))
                return result;

            var taken = new HashSet<Guid>();
            foreach (LauncherScenario existing in store.All)
                taken.Add(existing.Id);

            string[] files;
            try { files = Directory.GetFiles(folder, "*.xml"); }
            catch (Exception ex)
            {
                LauncherLog.Log("Migration: cannot list " + folder + ": " + ex.Message);
                return result;
            }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            // Read everything first, then append in the source product's own order (SCENARIO-FILE
            // rule 5: Order, ties by name case-insensitively, "so two readers show the same order").
            // File-name order put "RDP Connect P7" above "Calculator". The last two keys only make
            // the order total - the rule leaves a case-only name tie open (S0014, catalog proposal B4).
            var readable = new List<LauncherScenario>();
            foreach (string file in files)
            {
                LauncherScenario? item = LauncherScenarioStore.TryRead(file, out _);
                if (item == null)
                {
                    result.Skipped.Add(Path.GetFileName(file));
                    LauncherLog.Log("Migration: skipped unreadable " + Path.GetFileName(file));
                    continue;
                }
                readable.Add(item);
            }

            var added = new List<LauncherScenario>();
            foreach (LauncherScenario item in InSourceOrder(readable))
            {
                if (taken.Contains(item.Id))
                {
                    // Same id and same content is this very scenario arriving a second time: skipped
                    // and counted (rule 8, S0014 B5). Same id with other content is a genuine collision.
                    LauncherScenario? present = store.Find(item.Id);
                    if (present != null && SameContent(present, item))
                    {
                        result.AlreadyPresent++;
                        continue;
                    }
                    item.Id = Guid.NewGuid();
                    result.NewIds++;
                }
                taken.Add(item.Id);
                item.Filename = string.Empty; // the CyrFlip store names its own files
                store.Add(item);
                added.Add(item);
                result.Imported++;
            }

            if (chords != null)
                foreach (LauncherScenario item in added)
                    if (StripClashingHotkey(store, item, chords()))
                        result.ChordsDropped++;

            LauncherLog.Log($"Migration: imported {result.Imported}, already present {result.AlreadyPresent}, "
                + $"skipped {result.Skipped.Count}, renumbered {result.NewIds}, chords dropped {result.ChordsDropped}");
            return result;
        }

        /// <summary>"The same scenario" for a repeated migration: name, target and arguments agree (ordinal).</summary>
        private static bool SameContent(LauncherScenario a, LauncherScenario b)
            => string.Equals(a.Name, b.Name, StringComparison.Ordinal)
               && string.Equals(a.Path, b.Path, StringComparison.Ordinal)
               && string.Equals(a.Arguments, b.Arguments, StringComparison.Ordinal);

        /// <summary>
        /// <c>SCENARIO-FILE</c> rule 5 as a total order: <c>Order</c>, then <c>Name</c> ignoring case,
        /// then <c>Name</c> ordinal, then <c>Id</c>.
        /// </summary>
        internal static IEnumerable<LauncherScenario> InSourceOrder(IEnumerable<LauncherScenario> items) => items
            .OrderBy(s => s.Order)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ThenBy(s => s.Id);
    }
}

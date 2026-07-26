using System;
using System.Collections.Generic;
using System.IO;

namespace CyrFlip
{
    /// <summary>
    /// One-way, non-destructive import of OneClickRunner's scenarios
    /// (<c>%APPDATA%\OneClickRunner\Scenarios\*.xml</c>) into the CyrFlip store. The source is only
    /// ever read - never renamed, rewritten or deleted (tech plan §6.3), so a parallel OneClickRunner
    /// install keeps working. Original Guids survive unless they collide with an existing CyrFlip
    /// scenario, in which case a fresh Guid is assigned and counted for the summary (spec §5.3).
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
        /// source's own order. Legacy <c>SPECIAL_YTDLP</c> files arrive as the yt-dlp type (the store's
        /// reader normalizes them), so old OneClickRunner files keep their meaning.
        /// </summary>
        public static Result Import(LauncherScenarioStore store, string? sourceFolder = null)
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

            foreach (string file in files)
            {
                LauncherScenario? item = LauncherScenarioStore.TryRead(file, out _);
                if (item == null)
                {
                    result.Skipped.Add(Path.GetFileName(file));
                    LauncherLog.Log("Migration: skipped unreadable " + Path.GetFileName(file));
                    continue;
                }

                if (taken.Contains(item.Id))
                {
                    item.Id = Guid.NewGuid();
                    result.NewIds++;
                }
                taken.Add(item.Id);
                item.Filename = string.Empty; // the CyrFlip store names its own files
                store.Add(item);
                result.Imported++;
            }

            LauncherLog.Log($"Migration: imported {result.Imported}, skipped {result.Skipped.Count}, renumbered {result.NewIds} from {folder}");
            return result;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Source gate ensuring every window with TopMost = true belongs to the companion registry
    /// or has an explicit, documented exclusion reason (S0094).
    /// </summary>
    public class CompanionSurfaceGateTests
    {
        private static readonly string Source = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "CyrFlip"));

        private static readonly Regex TopMostAssign = new Regex(@"\bTopMost\s*=\s*(?:true|_?config\.TrafficLightPinned)", RegexOptions.Compiled);
        private static readonly Regex Comment = new Regex(@"^\s*///?", RegexOptions.Compiled);

        [Fact]
        public void EveryTopMostWindowBelongsToRegistryOrExclusionList()
        {
            var hitsByFile = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (string file in Directory.GetFiles(Source, "*.cs"))
            {
                string name = Path.GetFileName(file);
                string className = Path.GetFileNameWithoutExtension(file);

                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (!Comment.IsMatch(line) && TopMostAssign.IsMatch(line))
                    {
                        if (!hitsByFile.TryGetValue(className, out var list))
                        {
                            list = new List<string>();
                            hitsByFile[className] = list;
                        }
                        list.Add($"{name}:{i + 1}: {line.Trim()}");
                    }
                }
            }

            var uncataloged = new List<string>();

            foreach (var kvp in hitsByFile)
            {
                string className = kvp.Key;
                bool isCompanion = CompanionSurfaceRegistry.CompanionTypeNames.Contains(className);
                bool isExcluded = CompanionSurfaceRegistry.ExcludedTopmostWindows.TryGetValue(className, out string? reason)
                                  && !string.IsNullOrWhiteSpace(reason);

                if (!isCompanion && !isExcluded)
                {
                    uncataloged.Add($"Window '{className}' sets TopMost but is not in CompanionSurfaceRegistry or ExcludedTopmostWindows:\n  "
                        + string.Join("\n  ", kvp.Value));
                }
            }

            Assert.True(uncataloged.Count == 0,
                "TopMost window without yield policy or documented exclusion (S0094):\n" + string.Join("\n", uncataloged));
        }

        [Fact]
        public void ExcludedWindowsAllHaveDocumentedReasons()
        {
            foreach (var kvp in CompanionSurfaceRegistry.ExcludedTopmostWindows)
            {
                Assert.False(string.IsNullOrWhiteSpace(kvp.Value),
                    $"Excluded window '{kvp.Key}' must have a documented reason.");
                Assert.DoesNotContain(kvp.Key, CompanionSurfaceRegistry.CompanionTypeNames);
            }
        }

        [Fact]
        public void TheGateReadsRealSourceAndFindsKnownCompanions()
        {
            Assert.True(File.Exists(Path.Combine(Source, "ClipboardHistoryWindow.cs")));
            foreach (string companion in CompanionSurfaceRegistry.CompanionTypeNames)
            {
                Assert.True(File.Exists(Path.Combine(Source, $"{companion}.cs")), $"Source file for companion {companion} missing.");
            }
        }
    }
}

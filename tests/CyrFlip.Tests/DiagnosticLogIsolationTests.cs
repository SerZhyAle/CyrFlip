using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    [Collection(DiagnosticLogCollection.Name)]
    public sealed class DiagnosticLogIsolationTests
    {
        /// <summary>
        /// Every file the support bundle collects resolves into the private test folder, whichever
        /// collection a test belongs to - the override is set when the assembly loads (S0029 RB-5).
        /// </summary>
        [Fact]
        public void EveryDiagnosticLogResolvesIntoThePrivateTestFolder()
        {
            Assert.False(string.IsNullOrEmpty(TestAssemblyInit.LogFolder), "the module initializer did not run");
            Assert.Equal(TestAssemblyInit.LogFolder, DiagnosticLog.OverrideFolder);
            foreach (string name in SupportBundle.LogFiles)
                Assert.Equal(Path.Combine(TestAssemblyInit.LogFolder, name), DiagnosticLog.Path(name));
            Assert.Equal(Path.Combine(TestAssemblyInit.LogFolder, QuickNotesLog.FileName), DiagnosticLog.Path(QuickNotesLog.FileName));
        }

        [Fact]
        public void LoggingDuringTestsNeverTouchesTheRealDiagnosticFolder()
        {
            LauncherLog.Log("test isolation probe");
            QuickNotesLog.Log("test isolation probe");
            TranslateLog.Log("test isolation probe");
            TextMenuLog.Log("test isolation probe");
            ClipboardHistoryLog.Log("test isolation probe");
            ClipboardFlipLog.Log("test isolation probe");
            Assert.True(DiagnosticLog.Flush(TimeSpan.FromSeconds(10)), "the log writer did not drain");

            Assert.True(File.Exists(Path.Combine(TestAssemblyInit.LogFolder, "launcher.log")), "the probe did not reach the private folder");

            // A running CyrFlip writes these very files for real (every layout switch, every menu), so
            // on a developer's machine with the tray app up a changed timestamp proves nothing. The
            // path assertions above still hold there; the snapshot comparison is for a quiet machine
            // and for CI, where nothing else owns the folder.
            if (Process.GetProcessesByName("CyrFlip").Length > 0) return;

            var problems = new List<string>();
            foreach (string name in SupportBundle.LogFiles)
            {
                var info = new FileInfo(Path.Combine(DiagnosticLog.ProductionFolder, name));
                if (TestAssemblyInit.ProductionSnapshot.TryGetValue(name, out var original))
                {
                    if (!info.Exists) problems.Add(name + " disappeared during the test run");
                    else if (info.Length != original.Length || info.LastWriteTimeUtc != original.LastWriteTimeUtc)
                        problems.Add(name + " was written during the test run");
                }
                else if (info.Exists)
                {
                    problems.Add(name + " was created in the real diagnostic folder");
                }
            }
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }
    }
}

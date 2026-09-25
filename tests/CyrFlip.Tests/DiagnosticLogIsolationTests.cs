using System;
using System.Collections.Generic;
using System.IO;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    [Collection(DiagnosticLogCollection.Name)]
    public sealed class DiagnosticLogIsolationTests
    {
        [Fact]
        public void LoggingDuringTestsNeverTouchesTheRealDiagnosticFolder()
        {
            string[] names = { "launcher.log", QuickNotesLog.FileName };
            var before = new Dictionary<string, (long Length, DateTime LastWriteTimeUtc)>();
            foreach (string name in names)
            {
                string path = Path.Combine(DiagnosticLog.ProductionFolder, name);
                if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    before.Add(name, (info.Length, info.LastWriteTimeUtc));
                }
            }

            LauncherLog.Log("test isolation probe");
            QuickNotesLog.Log("test isolation probe");

            Assert.Equal(Path.Combine(DiagnosticLogTestFixture.Folder, "launcher.log"), DiagnosticLog.Path("launcher.log"));
            Assert.Equal(Path.Combine(DiagnosticLogTestFixture.Folder, QuickNotesLog.FileName), DiagnosticLog.Path(QuickNotesLog.FileName));
            foreach (string name in names)
            {
                string path = Path.Combine(DiagnosticLog.ProductionFolder, name);
                if (before.TryGetValue(name, out var original))
                {
                    var info = new FileInfo(path);
                    Assert.True(info.Exists, name + " disappeared during the test run");
                    Assert.Equal(original.Length, info.Length);
                    Assert.Equal(original.LastWriteTimeUtc, info.LastWriteTimeUtc);
                }
                else
                {
                    Assert.False(File.Exists(path), name + " was created in the real diagnostic folder");
                }
            }
        }
    }
}

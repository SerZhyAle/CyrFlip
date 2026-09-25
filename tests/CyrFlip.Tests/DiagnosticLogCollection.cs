using System;
using System.IO;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// One test-assembly-wide destination for diagnostics. The app's logs are user evidence and
    /// must never contain test traffic, so every test which can exercise a logging path joins this
    /// collection before the first such path is constructed.
    /// </summary>
    [CollectionDefinition(Name)]
    public sealed class DiagnosticLogCollection : ICollectionFixture<DiagnosticLogTestFixture>
    {
        public const string Name = "diagnostic log isolation";
    }

    public sealed class DiagnosticLogTestFixture : IDisposable
    {
        public DiagnosticLogTestFixture()
        {
            Folder = Path.Combine(Path.GetTempPath(), "CyrFlipTests", "diagnostics-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            DiagnosticLog.OverrideFolder = Folder;
        }

        public static string Folder { get; private set; } = string.Empty;

        public void Dispose()
        {
            try { Directory.Delete(Folder, recursive: true); } catch { }
        }
    }
}

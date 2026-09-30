using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using CyrFlip;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Runs once when the test assembly loads - before xUnit discovers or runs a single test - and
    /// points every diagnostic log at a private temp folder (ticket S0029 RB-5). The override used to be
    /// set by the <see cref="DiagnosticLogCollection"/> fixture, i.e. only once a test <i>in</i> that
    /// collection started; a launcher or history test scheduled earlier wrote its lines into the real
    /// <c>launcher.log</c>, a file the support bundle mails to the author.
    /// </summary>
    internal static class TestAssemblyInit
    {
        /// <summary>The private folder every test's diagnostics land in.</summary>
        public static string LogFolder { get; private set; } = string.Empty;

        /// <summary>
        /// Each support-bundle file in the real data folder as it was before any test ran; absent
        /// files are absent here. <see cref="DiagnosticLogIsolationTests"/> compares against it.
        /// </summary>
        public static IReadOnlyDictionary<string, (long Length, DateTime LastWriteTimeUtc)> ProductionSnapshot { get; private set; }
            = new Dictionary<string, (long, DateTime)>();

        [ModuleInitializer]
        internal static void Initialize()
        {
            var snapshot = new Dictionary<string, (long, DateTime)>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in SupportBundle.LogFiles)
            {
                try
                {
                    var info = new FileInfo(Path.Combine(DiagnosticLog.ProductionFolder, name));
                    if (info.Exists) snapshot[name] = (info.Length, info.LastWriteTimeUtc);
                }
                catch { }
            }
            ProductionSnapshot = snapshot;

            LogFolder = Path.Combine(Path.GetTempPath(), "CyrFlipTests", "diagnostics-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(LogFolder);
            DiagnosticLog.OverrideFolder = LogFolder;
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { Directory.Delete(LogFolder, recursive: true); } catch { }
            };
        }
    }
}

namespace System.Runtime.CompilerServices
{
    /// <summary>net48 lacks the attribute; the C# compiler only needs a type of this name.</summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute { }
}

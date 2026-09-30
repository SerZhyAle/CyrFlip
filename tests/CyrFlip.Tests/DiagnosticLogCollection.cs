using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Kept for the classes that already join it. The isolation itself no longer depends on this
    /// collection: <see cref="TestAssemblyInit"/> sets the log override when the assembly loads, so
    /// every test - in this collection or not, in any order - logs into the private folder.
    /// </summary>
    [CollectionDefinition(Name)]
    public sealed class DiagnosticLogCollection : ICollectionFixture<DiagnosticLogTestFixture>
    {
        public const string Name = "diagnostic log isolation";
    }

    public sealed class DiagnosticLogTestFixture
    {
        public static string Folder => TestAssemblyInit.LogFolder;
    }
}

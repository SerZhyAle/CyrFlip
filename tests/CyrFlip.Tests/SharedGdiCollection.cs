using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Tests that touch <b>process-wide GDI state</b> must not run at the same time as each other.
    ///
    /// <para>What they share is <see cref="CyrFlip.LauncherBrand"/>: it caches the launcher's icon and
    /// bitmap in plain static dictionaries and hands the <i>same instance</i> to every caller
    /// ("callers must not dispose it - UI thread only"). That is a correct assumption in the app,
    /// which has exactly one UI thread, and a false one in a test runner, which gives each test class
    /// its own thread and runs the classes in parallel. The consequences were three different-looking
    /// failures with one cause: GDI+ answering concurrent use of a single <c>Bitmap</c> with
    /// <c>InvalidOperationException: Object is currently in use elsewhere</c>, a
    /// <c>NullReferenceException</c> from a <c>Dictionary</c> corrupted by concurrent writes, and a
    /// settings window that came back half-built (an empty version label).</para>
    ///
    /// <para>The flake pre-dates the tests that finally exposed it - <c>LauncherBrandTests</c> and the
    /// settings-window walk were already racing over that cache - which is exactly why it is fixed
    /// here rather than in the app: there is no concurrency to fix in the app.</para>
    ///
    /// <para>xUnit runs the classes of one collection sequentially, so joining this collection is all
    /// a test needs to do. Add any new test that builds a <c>SettingsForm</c> or asks
    /// <c>LauncherBrand</c> for an image to it.</para>
    /// </summary>
    [CollectionDefinition(Name)]
    public sealed class SharedGdiCollection
    {
        public const string Name = "shared GDI state";
    }
}

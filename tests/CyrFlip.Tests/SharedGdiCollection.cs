using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Tests that build real windows or touch <b>process-wide GDI state</b> must not run at the same
    /// time as each other.
    ///
    /// <para>The original offender was <see cref="CyrFlip.LauncherBrand"/>: it handed the <i>same</i>
    /// GDI+ instances to every caller, which is correct on the app's single UI thread and a race in a
    /// test runner that gives each test class its own thread - GDI+ answered concurrent use of one
    /// <c>Bitmap</c> with <c>InvalidOperationException: Object is currently in use elsewhere</c>, a
    /// <c>Dictionary</c> corrupted by concurrent writes threw <c>NullReferenceException</c>, and a
    /// settings window came back half-built. LauncherBrand now caches per thread, so a GDI+ object
    /// of its making never crosses a thread boundary again.</para>
    ///
    /// <para>The collection itself stays, as a seat belt for everything process-wide that no cache fix
    /// covers: GDI handle pressure (twenty cores each building a settings window with hundreds of
    /// controls at once would race the 10 000 handles-per-process ceiling), shared font metrics and
    /// whatever the next static cache forgets. xUnit runs the classes of one collection sequentially,
    /// so joining it is all a test needs to do. Add any new test that builds a <c>SettingsForm</c> or
    /// asks <c>LauncherBrand</c> for an image to it.</para>
    /// </summary>
    [CollectionDefinition(Name)]
    public sealed class SharedGdiCollection
    {
        public const string Name = "shared GDI state";
    }
}

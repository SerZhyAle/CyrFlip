using System.Drawing;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The launcher's mark comes from an embedded resource, and the only thing standing between the
    /// icon file and the running app is the <c>LogicalName</c> in CyrFlip.csproj. Get that wrong and
    /// nothing throws: <see cref="LauncherBrand"/> quietly hands out null and both surfaces that
    /// carry the mark (settings page header, taskbar button) simply lose it. Hence a test that the
    /// resource is actually reachable and decodes at the sizes the app asks for.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]   // shares LauncherBrand's cache - see SharedGdiCollection
    public class LauncherBrandTests
    {
        [Theory]
        [InlineData(16)]
        [InlineData(28)]
        [InlineData(32)]
        public void TheEmbeddedMarkLoadsAtEverySizeTheAppAsksFor(int size)
        {
            Assert.NotNull(LauncherBrand.GetIcon(size));

            Bitmap? image = LauncherBrand.GetImage(size);
            Assert.NotNull(image);
            Assert.Equal(size, image!.Width);
            Assert.Equal(size, image.Height);
        }

        [Fact]
        public void TheMarkIsCachedAndHandedOutShared()
        {
            // Callers must not dispose it - two calls giving two objects would mean one of them is
            // free to be disposed out from under a PictureBox that is still showing it.
            Assert.Same(LauncherBrand.GetIcon(32), LauncherBrand.GetIcon(32));
            Assert.Same(LauncherBrand.GetImage(32), LauncherBrand.GetImage(32));
        }
    }
}

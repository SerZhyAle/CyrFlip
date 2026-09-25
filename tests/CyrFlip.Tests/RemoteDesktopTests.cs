using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Which paste targets read the clipboard late - a remote session or a VM, whose far side asks
    /// for the data in its own time - and so get the long waits (ticket S0009, FP-1).
    /// </summary>
    public sealed class RemoteDesktopTests
    {
        [Theory]
        [InlineData("mstsc", null)]
        [InlineData("msrdc", null)]
        [InlineData("MSRDCW", null)]           // process names are compared without case
        [InlineData("wfica32", null)]          // Citrix
        [InlineData("CDViewer", null)]
        [InlineData("vmconnect", null)]        // Hyper-V
        [InlineData("vmware", null)]
        [InlineData("vmplayer", null)]
        [InlineData("VirtualBoxVM", null)]
        [InlineData("somehost", "VMwareUnityHostWndClass")] // a console found by its window class
        [InlineData(null, "VirtualBox Machine")]
        public void Remote_and_vm_consoles_are_slow_targets(string? process, string? windowClass)
        {
            Assert.True(RemoteDesktop.IsSlowClipboardTarget(process, windowClass));
        }

        [Theory]
        [InlineData("notepad", "Notepad")]
        [InlineData("Code", "Chrome_WidgetWin_1")]
        [InlineData("WINWORD", "OpusApp")]
        [InlineData(null, null)]
        public void Ordinary_windows_are_not(string? process, string? windowClass)
        {
            Assert.False(RemoteDesktop.IsSlowClipboardTarget(process, windowClass));
        }
    }
}

using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Which clipboard read may replace the Symbols window's text field (ticket S0056, revision 2026-10-08).
    /// The reads come from a worker, so a late read must never overwrite a newer state of the field, and the
    /// palette's own copy must not be read back as a foreign change.
    /// </summary>
    public class SymbolsClipboardMirrorTests
    {
        [Fact]
        public void ReadOnShowIsAlwaysTaken()
        {
            var mirror = new SymbolsClipboardMirror();
            mirror.Own(10);
            Assert.True(mirror.Accept(10, force: true));
        }

        [Fact]
        public void ForeignChangeNewerThanTheFieldIsTaken()
        {
            var mirror = new SymbolsClipboardMirror();
            mirror.Own(10);

            Assert.True(mirror.Accept(11, force: false));
            Assert.Equal(11u, mirror.Sequence);
        }

        [Fact]
        public void EchoOfTheOwnWriteIsNotTakenAgain()
        {
            var mirror = new SymbolsClipboardMirror();
            mirror.Own(10);

            Assert.False(mirror.Accept(10, force: false));
        }

        [Fact]
        public void LateReadOfAChangeBeforeOwnPickIsDropped()
        {
            // The race: a read of a change made before the pick arrives after the pick has been written
            var mirror = new SymbolsClipboardMirror();
            mirror.Accept(5, force: true);
            mirror.Own(9);

            Assert.False(mirror.Accept(6, force: false));
            Assert.Equal(9u, mirror.Sequence);
        }

        [Fact]
        public void SequenceNumberWrapsAroundAsTheDwordDoes()
        {
            var mirror = new SymbolsClipboardMirror();
            mirror.Own(uint.MaxValue - 1);

            Assert.True(mirror.Accept(uint.MaxValue, force: false));
            Assert.True(mirror.Accept(1, force: false));           // past zero: newer
            Assert.False(mirror.Accept(uint.MaxValue, force: false)); // older than the wrapped one
        }

        [Fact]
        public void SnapshotCountsAsReadOnlyWhenTheClipboardWasOpened()
        {
            Assert.False(new SymbolsClipboardSnapshot(0, null, busy: false).Read); // could not open
            Assert.False(new SymbolsClipboardSnapshot(5, null, busy: true).Read);  // copy guard taken
            Assert.True(new SymbolsClipboardSnapshot(5, "text", busy: false).Read);
            Assert.True(new SymbolsClipboardSnapshot(5, null, busy: false).Read);  // a picture: read, nothing to mirror
        }
    }
}

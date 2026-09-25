namespace CyrFlip
{
    /// <summary>
    /// Which clipboard updates the history may record, decided by the clipboard sequence number
    /// rather than by the clock (spec S0005 CH-5, CH-6).
    ///
    /// <para>A CyrFlip operation that borrows the clipboard (a flip, a capture for a note or a
    /// translation) brackets itself with <see cref="Begin"/> / <see cref="End"/>: every update while
    /// one is active is scaffolding, and so is every update at or below the sequence number the
    /// clipboard had when the last one finished - the posted <c>WM_CLIPBOARDUPDATE</c>s of the
    /// restore arrive after the worker is done. A real copy made 100 ms later has a higher number and
    /// is recorded; a five-second flip leaks nothing. The old gate was "ignore everything for 2 s from
    /// the chord", which did both of those wrong.</para>
    ///
    /// <para>The history's own write (restoring an entry) is one exact sequence number, skipped by
    /// equality - never a later one. The boolean it replaces was not consumed while the history was
    /// paused, and then swallowed the user's next real copy.</para>
    ///
    /// Thread-safe: <see cref="Begin"/>/<see cref="End"/> run on the clipboard workers, the checks on
    /// the UI thread and on the capture thread.
    /// </summary>
    internal sealed class ClipboardHistoryGate
    {
        private readonly object _lock = new object();
        private int _active;
        private bool _hasEndMark;
        private uint _endMark;
        private bool _hasOwnWrite;
        private uint _ownWrite;

        public void Begin()
        {
            lock (_lock) _active++;
        }

        /// <param name="sequenceAfterRestore">The clipboard sequence number once the operation handed the clipboard back.</param>
        public void End(uint sequenceAfterRestore)
        {
            lock (_lock)
            {
                if (_active > 0) _active--;
                if (!_hasEndMark || After(sequenceAfterRestore, _endMark)) _endMark = sequenceAfterRestore;
                _hasEndMark = true;
            }
        }

        /// <summary>The history itself wrote the clipboard, which now has <paramref name="sequence"/>.</summary>
        public void ExpectOwnWrite(uint sequence)
        {
            lock (_lock) { _ownWrite = sequence; _hasOwnWrite = true; }
        }

        public bool ShouldCapture(uint sequence)
        {
            lock (_lock)
            {
                if (_active > 0) return false;
                if (_hasEndMark && !After(sequence, _endMark)) return false;
                if (_hasOwnWrite && sequence == _ownWrite) return false;
                return true;
            }
        }

        /// <summary>Wrap-safe "a is later than b" for the 32-bit sequence counter.</summary>
        private static bool After(uint a, uint b) => unchecked((int)(a - b)) > 0;
    }
}

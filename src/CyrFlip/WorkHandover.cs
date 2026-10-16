namespace CyrFlip
{
    /// <summary>
    /// What becomes of a long operation started from the settings window when the window is hidden
    /// (APP-BEHAVIOUR rule 3, "when the last window closes, the work the user was watching ends, or is
    /// handed over deliberately - never an orphaned process"; ticket S0060 P7-i). The Ollama download and
    /// a model pull take minutes and the window is hidden, not closed, so cancelling them on every hide
    /// threw away a multi-gigabyte download because the user went back to their editor.
    ///
    /// <para>The policy: <b>hiding the window hands a running operation over</b> - it keeps going, and
    /// its last status line is told through the tray when it ends (once, and only while the window is
    /// still hidden: a user who came back is watching it). <b>Disposing the window</b> - the real end,
    /// the application exiting - cancels it; so does the Cancel button, which stays the one explicit way.
    /// Pure, so the whole sequence is unit-tested without a window or a network.</para>
    /// </summary>
    internal sealed class WorkHandover
    {
        private bool _handedOver;

        /// <summary>An operation was handed over and its end has not been announced yet.</summary>
        public bool HandedOver => _handedOver;

        /// <summary>
        /// The window was hidden. True when the work must be cancelled now - the window is being disposed
        /// (the process is ending) or nothing runs, where cancelling a stale token is a harmless no-op.
        /// False when it keeps running: it is handed over, and <see cref="ShouldAnnounce"/> will say
        /// when to tell the user.
        /// </summary>
        public bool OnHidden(bool workRunning, bool windowEnding)
        {
            if (windowEnding)
            {
                _handedOver = false;
                return true;
            }
            if (!workRunning) return true;
            _handedOver = true;
            return false;
        }

        /// <summary>The window is on screen again: the user is watching the work, no announcement is owed.</summary>
        public void OnShown() => _handedOver = false;

        /// <summary>
        /// The operation's status line changed. True exactly once - when the handed-over work has ended
        /// and the window is still hidden - so the result is told a single time and never while the user
        /// can read it on the page.
        /// </summary>
        public bool ShouldAnnounce(bool workRunning, bool windowVisible)
        {
            if (!_handedOver || workRunning || windowVisible) return false;
            _handedOver = false;
            return true;
        }
    }
}

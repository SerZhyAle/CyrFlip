using System;

namespace CyrFlip
{
    /// <summary>
    /// What the Symbols window reads from the clipboard, taken on the clipboard worker (ticket S0056).
    /// </summary>
    internal readonly struct SymbolsClipboardSnapshot
    {
        public SymbolsClipboardSnapshot(uint sequence, string? text, bool busy)
        {
            Sequence = sequence;
            Text = text;
            Busy = busy;
        }

        /// <summary>The clipboard sequence number of the content read; 0 when the clipboard could not be opened.</summary>
        public uint Sequence { get; }

        /// <summary>The text the field may show; null when the clipboard holds nothing the field should mirror.</summary>
        public string? Text { get; }

        /// <summary>The copy guard was taken: nothing was read, and the caller asks again shortly.</summary>
        public bool Busy { get; }

        /// <summary>True when the clipboard was actually read.</summary>
        public bool Read => !Busy && Sequence != 0;
    }

    /// <summary>
    /// Decides which clipboard read may replace the text field (ticket S0056, revision 2026-10-08).
    /// The clipboard is read on a worker, so a read can arrive after a newer change, or after the palette's own
    /// copy has already been shown. A read therefore replaces the field only when its content is newer than the
    /// content the field already reflects, or when the palette asks for a fresh read on show.
    /// </summary>
    internal sealed class SymbolsClipboardMirror
    {
        /// <summary>The sequence number of the clipboard content the field reflects.</summary>
        public uint Sequence { get; private set; }

        /// <summary>Whether a read with this sequence number replaces the field.</summary>
        public bool Accept(uint sequence, bool force)
        {
            if (!force && !IsNewer(sequence)) return false;
            Sequence = sequence;
            return true;
        }

        /// <summary>The palette wrote the clipboard itself: its own change is what the field already shows.</summary>
        public void Own(uint sequence) => Sequence = sequence;

        // The sequence number is a DWORD that wraps; the difference is judged in two's complement.
        private bool IsNewer(uint sequence) => unchecked((int)(sequence - Sequence)) > 0;
    }
}

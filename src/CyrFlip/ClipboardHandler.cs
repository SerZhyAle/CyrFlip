using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// Runs a clipboard transform (a layout conversion <see cref="ConvertLayout"/> or a case flip
    /// <see cref="FlipCase"/>): grab the active selection (synthesized Ctrl+C), apply the transform,
    /// and paste the result back (synthesized Ctrl+V); optionally switch the input layout or set
    /// CapsLock to match the result afterwards. (spec §2.2)
    ///
    /// The two halves are also available on their own - <see cref="TakeSelection"/> and
    /// <see cref="ReplaceSelection"/> - because the translator needs seconds of network time between
    /// them and cannot hold the clipboard for that long. <see cref="Run"/> is exactly those two
    /// halves back to back, so the flips keep their original single-backup behaviour.
    ///
    /// Clipboard access uses <see cref="Win32Clipboard"/> (raw Win32, no OLE) so it can't hang
    /// on the background thread. An empty selection is a no-op; if the foreground window changes
    /// mid-operation it is cancelled; the original clipboard is restored at the end - but only when
    /// the operation actually changed it, and only once the target has taken the paste (S0009).
    /// </summary>
    internal sealed class ClipboardHandler
    {
        private const int VK_C = 0x43;
        private const int VK_V = 0x56;
        private const int VK_DELETE = 0x2E;

        /// <summary>
        /// The longest selection a flip converts (ticket S0009, FP-6, open decision 3). Beyond it the
        /// text is not even read: a 10-million-character selection used to be copied five times over
        /// and converted for tens of seconds while every other chord waited.
        /// </summary>
        internal const int MaxFlipChars = 1000000;

        /// <summary>Result of a flip, for the caller to surface (e.g. a tray balloon).</summary>
        public enum FlipResult { Flipped, NoSelection, NoChange, Cancelled, Failed, TooLarge }

        /// <summary>The plain editing commands CyrFlip's own context menu offers first.</summary>
        public enum EditCommand { Copy, Cut, Paste }

        /// <summary>Outcome of the copy half on its own.</summary>
        public enum CaptureResult { Captured, NoSelection, Cancelled, Failed, TooLarge }

        /// <summary>
        /// What the clipboard held before we borrowed it - text, an image, a file selection, and the
        /// small companions that give those their meaning.
        ///
        /// <para>It used to hold text alone, and everything else was simply destroyed: the copy half
        /// of a flip ends in <c>EmptyClipboard</c>, so a user who had a screenshot or a set of files
        /// on the clipboard and then fixed a word with a chord lost them for good. Those three formats
        /// are what people actually keep in a clipboard; the companions of a rich copy (RTF, HTML) are
        /// still lost when a flip really replaces the clipboard - but no longer when it did not
        /// (<see cref="RestoreAction.Unchanged"/>).</para>
        ///
        /// <para>The companions (<see cref="Companions"/>) are what a bare restore used to strip: the
        /// "do not record" markers of a password manager (FP-2 - without them a flip over a copied
        /// password put the password into Win+V and the cloud clipboard), "Preferred DropEffect"
        /// (FP-7 - without it a Cut of files pasted as a copy) and CF_LOCALE (FP-8).</para>
        /// </summary>
        internal sealed class ClipboardBackup
        {
            public ClipboardBackup(bool hadText, string? text, byte[]? image = null, byte[]? files = null,
                IReadOnlyList<KeyValuePair<uint, byte[]>>? companions = null, uint sequence = 0, bool unreadable = false,
                bool wasEmpty = false)
                : this(hadText, text == null ? null : Win32Clipboard.UnicodeBytes(text), image, files, companions, sequence, unreadable, wasEmpty, raw: true)
            {
            }

            private ClipboardBackup(bool hadText, byte[]? textBytes, byte[]? image, byte[]? files,
                IReadOnlyList<KeyValuePair<uint, byte[]>>? companions, uint sequence, bool unreadable, bool wasEmpty, bool raw)
            {
                WasEmpty = wasEmpty;
                HadText = hadText;
                TextBytes = textBytes;
                Image = image;
                Files = files;
                Companions = companions ?? new KeyValuePair<uint, byte[]>[0];
                Sequence = sequence;
                Unreadable = unreadable;
            }

            /// <summary>The backup exactly as it was read: the text as the owner's raw CF_UNICODETEXT block.</summary>
            internal static ClipboardBackup FromRaw(bool hadText, byte[]? textBytes, byte[]? image, byte[]? files,
                IReadOnlyList<KeyValuePair<uint, byte[]>> companions, uint sequence, bool unreadable, bool wasEmpty = false)
                => new ClipboardBackup(hadText, textBytes, image, files, companions, sequence, unreadable, wasEmpty, raw: true);

            public bool HadText { get; }

            /// <summary>
            /// CF_UNICODETEXT as the owner stored it, NUL and padding included. Kept as bytes, not a
            /// string: the backup is one copy out and one copy back, never a decode and re-encode of
            /// what may be megabytes of text (FP-6).
            /// </summary>
            public byte[]? TextBytes { get; }

            /// <summary>The text, decoded up to its terminating NUL; null when there was none.</summary>
            public string? Text
            {
                get
                {
                    if (TextBytes == null) return null;
                    string raw = Encoding.Unicode.GetString(TextBytes, 0, TextBytes.Length & ~1);
                    int nul = raw.IndexOf('\0');
                    return nul >= 0 ? raw.Substring(0, nul) : raw;
                }
            }

            /// <summary>Device-independent bitmap (a screenshot, a copied picture), or null.</summary>
            public byte[]? Image { get; }

            /// <summary>A copied file selection (CF_HDROP), or null.</summary>
            public byte[]? Files { get; }

            /// <summary>The small formats that travel with the content (see the class remarks).</summary>
            public IReadOnlyList<KeyValuePair<uint, byte[]>> Companions { get; }

            /// <summary>The clipboard sequence number the backup was read at (FP-3).</summary>
            public uint Sequence { get; }

            /// <summary>
            /// The clipboard held something that could not be read - another process kept it locked, or
            /// its owner failed to render a format. A flip must not go ahead: it would overwrite content
            /// it has no copy of (FP-5).
            /// </summary>
            public bool Unreadable { get; }

            /// <summary>True when there is anything at all worth handing back. Companions alone are not content.</summary>
            public bool HasContent => (HadText && TextBytes != null) || Image != null || Files != null;

            /// <summary>
            /// The clipboard held no format at all. Handing that back means emptying it (S0032 FP2-4):
            /// otherwise the selection a flip copied stays there - never recorded by the history, and
            /// after a flip still owned by CyrFlip's window until exit.
            /// </summary>
            public bool WasEmpty { get; }

            /// <summary>Whether a restore has anything to put back - content, or the emptiness itself.</summary>
            public bool Restorable => HasContent || WasEmpty;
        }

        /// <summary>
        /// What a paste left on the clipboard, so the restore can tell "still ours" from "somebody has
        /// written since" (FP-1). A delayed-rendered paste is judged by ownership - any other writer
        /// empties the clipboard first, which takes it from our owner window - and a plain write by the
        /// sequence number it left.
        /// </summary>
        internal readonly struct PasteReceipt
        {
            private readonly ClipboardOwner? _owner;
            private readonly uint _sequence;
            private readonly bool _wrote;

            private PasteReceipt(ClipboardOwner? owner, uint sequence)
            {
                _owner = owner;
                _sequence = sequence;
                _wrote = true;
            }

            public static PasteReceipt BySequence(uint sequence) => new PasteReceipt(null, sequence);
            public static PasteReceipt ByOwner(ClipboardOwner owner, uint sequence) => new PasteReceipt(owner, sequence);

            /// <summary>False for the default receipt: the operation pasted nothing.</summary>
            public bool Wrote => _wrote;

            /// <summary>
            /// Asked inside the restore's open: is the clipboard still holding what we pasted? Null when
            /// nothing was pasted (a capture only), which leaves the decision to the backup's sequence.
            /// </summary>
            public bool? StillOurs(uint current)
            {
                if (!_wrote) return null;
                return _owner != null ? _owner.OwnsClipboard() : current == _sequence;
            }
        }

        /// <summary>
        /// Cap on a backed-up image. Beyond it the picture is left to its fate rather than copied
        /// through memory twice on every flip - the old behaviour for every non-text format, now the
        /// exception rather than the rule.
        /// </summary>
        internal const int MaxBackupImageBytes = 64 * 1024 * 1024;

        /// <summary>
        /// Cap on backed-up text (S0032 FP2-5). Unlike an image, text over it is not dropped but makes
        /// the backup unreadable - the flip is refused with its balloon rather than destroying a
        /// clipboard it holds no copy of. 64 MB of UTF-16 is 32 million characters.
        /// </summary>
        internal const int MaxBackupTextBytes = 64 * 1024 * 1024;

        /// <summary>Cap on one companion format; a Shell IDList Array for thousands of files stays far below it.</summary>
        internal const int MaxCompanionBytes = 1024 * 1024;

        private readonly IClipboardReader _reader;
        private readonly Action<IList<KeyStroke>> _send;

        public ClipboardHandler() : this(Win32Clipboard.Reader, KeyInjection.Send) { }

        /// <summary>The seams: the clipboard the backup reads and the keyboard every synthesized key goes to.</summary>
        internal ClipboardHandler(IClipboardReader reader, Action<IList<KeyStroke>> send)
        {
            _reader = reader;
            _send = send;
        }

        /// <summary>
        /// Convert a selection by physical key position between the two layouts of a table row - the
        /// seeded EN ⇄ RU flip included. The pair works <b>both ways</b>: when the target layout is
        /// already the active one, the text in front of the user was typed the other way round, so
        /// the same chord converts target → source instead.
        /// </summary>
        /// <param name="switchLayoutAfter">
        /// When true, after a successful conversion also switch the target window's input language to
        /// the layout the text now reads in, so the user can keep typing in it.
        /// </param>
        public FlipResult ConvertLayout(LayoutConversionProfile profile, bool switchLayoutAfter = false, bool convertSymbols = true)
        {
            if (profile == null || !profile.IsUsable) return FlipResult.Failed;

            bool reverse = KeyboardLayoutConverter.IsActiveLayout(GetForegroundWindow(), profile.TargetKlid);
            string from = reverse ? profile.TargetKlid : profile.SourceKlid;
            string to = reverse ? profile.SourceKlid : profile.TargetKlid;
            // The converted text is in the target layout's language, and that is the locale an ANSI-only
            // app needs to turn it back into characters rather than question marks (FP-8).
            return Run(text => KeyboardLayoutConverter.Convert(text, from, to, convertSymbols),
                syncCapsAfter: false, targetKlid: switchLayoutAfter ? to : null, locale: InputLayouts.LangIdOf(to));
        }

        /// <summary>
        /// Invert the case of the selection (UPPER ↔ lower) via <see cref="CaseFlipEngine"/> -
        /// the "I left CapsLock on" fix. Never switches the input language afterwards.
        /// </summary>
        /// <param name="syncCapsAfter">
        /// When true, after a successful case flip also bring the physical CapsLock key into line
        /// with the corrected text - on when it ends in a capital, off when it ends in a small
        /// letter (<see cref="CaseFlipEngine.DesiredCapsLock"/>) - so continued typing matches what
        /// is now on screen. The exact analogue of switchLayoutAfter, which likewise sets the layout
        /// the text now reads in rather than merely changing it.
        /// </param>
        public FlipResult FlipCase(bool syncCapsAfter = false)
            => Run(CaseFlipEngine.Flip, syncCapsAfter: syncCapsAfter); // same language: keep the selection's own locale

        /// <summary>
        /// Cut / Copy / Paste from the text context menu, <b>through the very pipeline the flips
        /// use</b> - not a second implementation of it.
        ///
        /// That is the whole point of this method. A hand-rolled "just SendInput a Ctrl+C" looks
        /// equivalent and is not: the capture settles before it injects anything, then <b>waits for
        /// the clipboard sequence number to actually change</b> - which is what makes the copy reliable
        /// instead of hopeful, and what tells "nothing was selected" apart from "the app ignored us".
        /// The paste half likewise gives the target the same 30 ms before and 140 ms after that
        /// <see cref="ReplaceSelection"/> gives it.
        ///
        /// Unlike a flip this <b>does not back up and restore the clipboard</b>: the user asked to
        /// copy, so the copy has to stay - on the clipboard and, with the feature on, in the history.
        ///
        /// Cut is copy plus a Delete rather than a Ctrl+X, so its visible half rides on the capture we
        /// have just verified; in a field that cannot be edited the Delete simply does nothing, which
        /// is the correct outcome there anyway. An editor's whole-line copy (no selection, FP-4) is not
        /// a selection to cut: the Delete would remove one character of it.
        /// </summary>
        public FlipResult RunEdit(EditCommand command)
        {
            if (command == EditCommand.Paste)
            {
                IntPtr foreground = GetForegroundWindow();
                Thread.Sleep(30);
                SideModifiers pasteReleased = SendCtrlChord(VK_V);
                Thread.Sleep(PasteTiming.KeystrokeSettleMs); // let the target app consume the paste
                RestorePhysicalModifiers(pasteReleased);
                return GetForegroundWindow() != foreground ? FlipResult.Cancelled : FlipResult.Flipped;
            }

            SideModifiers released = SideModifiers.None;
            try
            {
                // The copy only has to be seen to have landed, not read (S0032 FP2-5): with a cap of one
                // character anything longer answers "too large" unread, which here means "there".
                CaptureResult captured = CaptureSelection(1, refuseLineCopy: command == EditCommand.Cut,
                    out _, out _, out released, presenceOnly: true);
                if (captured != CaptureResult.Captured) return ToFlipResult(captured);

                // The Delete goes before the held modifiers come back: a Shift pressed again first
                // would turn it into Shift+Delete - "delete permanently" in Explorer.
                if (command == EditCommand.Cut)
                {
                    _send(new[] { new KeyStroke(VK_DELETE, false), new KeyStroke(VK_DELETE, true) });
                    Thread.Sleep(60);
                }
                return FlipResult.Flipped;
            }
            finally
            {
                RestorePhysicalModifiers(released);
            }
        }

        /// <param name="transform">The text transform to apply to the captured selection.</param>
        /// <param name="syncCapsAfter">
        /// When true, set CapsLock to match the replaced text after a successful replace.
        /// </param>
        /// <param name="targetKlid">
        /// When set, the layout to switch the target window to after a successful replace.
        /// </param>
        /// <param name="locale">
        /// The CF_LOCALE to write beside the result; null keeps the one the selection was copied with.
        /// </param>
        private FlipResult Run(Func<string, string> transform, bool syncCapsAfter, string? targetKlid = null, uint? locale = null)
        {
            ClipboardBackup backup = BackupClipboard(_reader);
            if (backup.Unreadable)
            {
                // Before a single key is sent: going ahead would overwrite a clipboard we hold no copy of.
                ClipboardFlipLog.Log("flip refused: the clipboard could not be backed up");
                return FlipResult.Failed;
            }

            PasteReceipt receipt = default;
            try
            {
                CaptureResult captured;
                Win32Clipboard.CaptureRead read;
                IntPtr foreground;
                SideModifiers released = SideModifiers.None;
                try { captured = CaptureSelection(MaxFlipChars, refuseLineCopy: true, out read, out foreground, out released); }
                finally { RestorePhysicalModifiers(released); }
                if (captured != CaptureResult.Captured) return ToFlipResult(captured); // spec §5.3 - nothing selected → no-op

                string selected = read.Text;
                string converted = transform(selected);
                if (converted == selected)
                    return FlipResult.NoChange;

                // The desired CapsLock state is read off the text we are about to paste, not off the
                // key's current state - see CaseFlipEngine.DesiredCapsLock.
                bool? capsAfter = syncCapsAfter ? CaseFlipEngine.DesiredCapsLock(converted) : null;
                return ReplaceSelection(converted, foreground, out receipt, capsAfter, targetKlid, locale ?? read.Locale);
            }
            finally
            {
                RestoreClipboard(backup, receipt);
            }
        }

        private static FlipResult ToFlipResult(CaptureResult captured)
        {
            switch (captured)
            {
                case CaptureResult.Cancelled: return FlipResult.Cancelled;
                case CaptureResult.Failed: return FlipResult.Failed;
                case CaptureResult.TooLarge: return FlipResult.TooLarge;
                case CaptureResult.Captured: return FlipResult.Flipped;
                default: return FlipResult.NoSelection;
            }
        }

        /// <summary>The live clipboard's backup - see <see cref="BackupClipboard(IClipboardReader)"/>.</summary>
        internal static ClipboardBackup BackupClipboard() => BackupClipboard(Win32Clipboard.Reader);

        /// <summary>
        /// What the clipboard holds right now, so it can be handed back afterwards - all of it read in
        /// <b>one</b> open, with the sequence number, so the pieces describe the same content. Each
        /// format is read only when it is present, so the ordinary case - a flip over plain text -
        /// costs what it always did.
        ///
        /// <para>"Empty" and "unreadable" are told apart (FP-5): a clipboard that could not be opened
        /// while it held formats, or whose text, picture or files could not be read, comes back
        /// <see cref="ClipboardBackup.Unreadable"/> - the flip stops rather than destroy it.</para>
        /// </summary>
        internal static ClipboardBackup BackupClipboard(IClipboardReader reader)
        {
            uint sequence = reader.Sequence();
            int count = reader.CountFormats();
            if (!reader.Open())
                return ClipboardBackup.FromRaw(false, null, null, null, new KeyValuePair<uint, byte[]>[0], sequence,
                    unreadable: count > 0, wasEmpty: count == 0);
            bool hadText, failed = false, wasEmpty;
            byte[]? text = null, image = null, files = null;
            List<KeyValuePair<uint, byte[]>> companions;
            try
            {
                wasEmpty = reader.CountFormats() == 0;

                // The only content whose failure refuses the flip (S0032 FP2-2): the text is what a
                // flip overwrites. A picture or a file list its owner fails to render - a huge Excel
                // range, a broken synthesized bitmap - could not be pasted by anyone either, and
                // refusing every flip until the next copy over it helped nobody.
                hadText = reader.IsAvailable(CF_UNICODETEXT);
                if (hadText) failed = ReadText(reader, out text);

                if (reader.IsAvailable(CF_DIB) && !ReadContent(reader, CF_DIB, MaxBackupImageBytes, out image))
                    ClipboardFlipLog.Log("backup: the picture could not be rendered and is not carried");
                if (reader.IsAvailable(CF_HDROP) && !ReadContent(reader, CF_HDROP, int.MaxValue, out files))
                    ClipboardFlipLog.Log("backup: the file list could not be rendered and is not carried");

                companions = ReadCompanions(reader);
                // Reading a delay-rendered format makes its owner render it, and that may move the
                // sequence (S0032 FP2-1): the number that describes the content is the one after the
                // last read. Otherwise the chord with nothing selected would see "changed" and rewrite
                // Excel's live copy with text and a picture.
                sequence = reader.Sequence();
            }
            finally { reader.Close(); }
            // And once more after the close: a render that lands as the clipboard closes moves it too.
            // Nobody else can have written in between that a restore could mistake for ours - the
            // flip's own Ctrl+C comes after this.
            sequence = reader.Sequence();
            return ClipboardBackup.FromRaw(hadText, text, image, files, companions, sequence, failed, wasEmpty);
        }

        /// <summary>The text: true when it failed or is over <see cref="MaxBackupTextBytes"/> - either way unreadable.</summary>
        private static bool ReadText(IClipboardReader reader, out byte[]? text)
        {
            ClipboardRead result = reader.Read(CF_UNICODETEXT, MaxBackupTextBytes, out text);
            if (result == ClipboardRead.Ok) return false;
            text = null;
            if (result == ClipboardRead.TooLarge)
                ClipboardFlipLog.Log("backup: the text on the clipboard is over " + MaxBackupTextBytes / (1024 * 1024) + " MB");
            return result == ClipboardRead.Failed || result == ClipboardRead.TooLarge;
        }

        /// <summary>
        /// One of the three content formats. False only for a real failure; a format over its cap or
        /// holding nothing is simply not carried (<paramref name="data"/> null).
        /// </summary>
        private static bool ReadContent(IClipboardReader reader, uint format, int maxBytes, out byte[]? data)
        {
            ClipboardRead result = reader.Read(format, maxBytes, out data);
            if (result != ClipboardRead.Ok) data = null;
            return result != ClipboardRead.Failed;
        }

        /// <summary>
        /// The companions that are present. The four privacy markers are carried <b>whatever it takes</b>:
        /// their presence is the message, so one that is present but unreadable goes back as a DWORD 0 -
        /// "do not record" - rather than being dropped, which would publish the secret it guards.
        /// </summary>
        private static List<KeyValuePair<uint, byte[]>> ReadCompanions(IClipboardReader reader)
        {
            var companions = new List<KeyValuePair<uint, byte[]>>();

            foreach (uint format in new[] { CF_LOCALE, ClipboardFormats.PreferredDropEffect, ClipboardFormats.ShellIdListArray })
                if (reader.IsAvailable(format) && reader.Read(format, MaxCompanionBytes, out byte[]? data) == ClipboardRead.Ok && data != null)
                    companions.Add(new KeyValuePair<uint, byte[]>(format, data));

            foreach (uint marker in new[] { ClipboardFormats.ExcludeFromMonitor, ClipboardFormats.ViewerIgnore,
                         ClipboardFormats.CanIncludeInHistory, ClipboardFormats.CanUploadToCloud })
            {
                if (!reader.IsAvailable(marker)) continue;
                bool read = reader.Read(marker, MaxCompanionBytes, out byte[]? data) == ClipboardRead.Ok && data != null;
                bool usable = read && (marker == ClipboardFormats.ExcludeFromMonitor || marker == ClipboardFormats.ViewerIgnore
                    || data!.Length >= sizeof(uint));
                companions.Add(new KeyValuePair<uint, byte[]>(marker, usable ? data! : Win32Clipboard.DwordBytes(0)));
            }
            return companions;
        }

        /// <summary>Everything a restore puts back, content first, in one list for the single open/empty/refill pass.</summary>
        internal static List<KeyValuePair<uint, byte[]>> RestorePayloads(ClipboardBackup backup)
        {
            var payloads = new List<KeyValuePair<uint, byte[]>>(3 + backup.Companions.Count);
            if (!backup.HasContent) return payloads;
            if (backup.HadText && backup.TextBytes != null)
                payloads.Add(new KeyValuePair<uint, byte[]>(CF_UNICODETEXT, backup.TextBytes));
            if (backup.Image != null)
                payloads.Add(new KeyValuePair<uint, byte[]>(CF_DIB, backup.Image));
            if (backup.Files != null)
                payloads.Add(new KeyValuePair<uint, byte[]>(CF_HDROP, backup.Files));
            payloads.AddRange(backup.Companions);
            return payloads;
        }

        /// <summary>
        /// Put back what the clipboard held - all of it, in one open/empty/refill pass, and only when
        /// <see cref="ClipboardRestore.Plan"/> says so. The decision is taken inside that open, where
        /// nobody else can write: a flip that changed nothing leaves the clipboard exactly as it was
        /// (FP-3), and one whose paste somebody has since written over leaves theirs alone (FP-1).
        /// </summary>
        internal static RestoreAction RestoreClipboard(ClipboardBackup backup, PasteReceipt receipt = default)
        {
            // The cheap, common answer first: nothing moved, so there is nothing to open.
            if (!receipt.Wrote && GetClipboardSequenceNumber() == backup.Sequence)
                return RestoreAction.Unchanged;

            RestoreAction action = RestoreAction.NothingToRestore;
            bool opened = false;
            Win32Clipboard.RestoreIf(current =>
            {
                opened = true;
                action = ClipboardRestore.Plan(backup.Sequence, backup.Restorable, current, receipt.StillOurs(current));
                return action == RestoreAction.Restore;
            }, RestorePayloads(backup));

            if (!opened)
                ClipboardFlipLog.Log("restore skipped: the clipboard stayed locked");
            else if (action == RestoreAction.TakenOver)
                ClipboardFlipLog.Log("restore skipped: the clipboard was written after the paste");
            return action;
        }

        /// <summary>
        /// The copy half on its own - back up, synthesize a clean Ctrl+C, wait for the selection, hand
        /// the clipboard straight back. For callers that need seconds between reading the selection and
        /// doing anything with it (the translator, a quick note): the clipboard is theirs again at once.
        /// </summary>
        /// <param name="maxChars">
        /// The consumer's own cap (S0032 FP2-5): a longer selection is refused unread as
        /// <see cref="CaptureResult.TooLarge"/> instead of being marshalled whole into this process.
        /// </param>
        internal CaptureResult TakeSelection(out string selection, out IntPtr foreground, int maxChars = MaxFlipChars)
        {
            selection = "";
            foreground = IntPtr.Zero;
            ClipboardBackup backup = BackupClipboard(_reader);
            if (backup.Unreadable)
            {
                ClipboardFlipLog.Log("capture refused: the clipboard could not be backed up");
                return CaptureResult.Failed;
            }

            SideModifiers released = SideModifiers.None;
            try
            {
                CaptureResult result = CaptureSelection(maxChars, refuseLineCopy: true,
                    out Win32Clipboard.CaptureRead read, out foreground, out released);
                if (result == CaptureResult.Captured) selection = read.Text;
                return result;
            }
            finally
            {
                RestorePhysicalModifiers(released);
                RestoreClipboard(backup);
            }
        }

        /// <summary>
        /// The copy half without the restore - the caller presses <paramref name="released"/> again.
        /// A remote-desktop or VM console gets a longer poll: its far side copies, then announces the
        /// data, and only then does the clipboard here move (FP-1, the mirror case).
        /// </summary>
        /// <param name="refuseLineCopy">
        /// Treat an editor's "no selection - copy the whole line" as nothing selected (FP-4).
        /// </param>
        /// <param name="presenceOnly">A selection over <paramref name="maxChars"/> counts as captured, not too large.</param>
        private CaptureResult CaptureSelection(int maxChars, bool refuseLineCopy, out Win32Clipboard.CaptureRead read,
            out IntPtr foreground, out SideModifiers released, bool presenceOnly = false)
        {
            read = default;
            read.Text = "";
            foreground = GetForegroundWindow();
            bool remote = RemoteDesktop.IsSlowClipboardTarget(foreground);
            uint initialSeq = GetClipboardSequenceNumber();

            Thread.Sleep(20);
            released = SendCtrlChord(VK_C);

            // Wait for the copy to populate the clipboard (selection may be empty).
            int polls = PasteTiming.CapturePoll(remote) / 40;
            for (int i = 0; i < polls; i++)
            {
                Thread.Sleep(40);
                if (GetForegroundWindow() != foreground)
                    return CaptureResult.Cancelled;

                if (GetClipboardSequenceNumber() == initialSeq) continue;
                if (!Win32Clipboard.TryReadCapture(maxChars, out read)) continue;
                if (read.TooLarge && !presenceOnly)
                {
                    ClipboardFlipLog.Log("selection over " + maxChars + " chars refused");
                    return CaptureResult.TooLarge;
                }
                if (read.Text.Length == 0 && !read.TooLarge) continue;
                if (refuseLineCopy && read.LineCopy)
                {
                    ClipboardFlipLog.Log("editor line copy (no selection) ignored");
                    return CaptureResult.NoSelection;
                }
                return CaptureResult.Captured;
            }

            return CaptureResult.NoSelection;
        }

        /// <summary>
        /// The paste half: put <paramref name="text"/> on the clipboard and send Ctrl+V into
        /// <paramref name="foreground"/>, provided it is still the focused window - then wait until the
        /// target has actually taken the text, so the restore that follows cannot race it (FP-1).
        /// </summary>
        /// <param name="receipt">What the restore needs to know about this paste.</param>
        /// <param name="locale">The CF_LOCALE to write beside the text; 0 leaves it to Windows.</param>
        /// <param name="transient">
        /// True (a flip, a pasted translation) when the clipboard is restored afterwards: the text goes
        /// up delay-rendered and marked "do not record", and the call returns only once the target has
        /// read it or the cap has passed. False when the text is meant to stay on the clipboard (the
        /// translator's "copy" option): a plain write, and no restore to race.
        /// </param>
        internal FlipResult ReplaceSelection(string text, IntPtr foreground, out PasteReceipt receipt,
            bool? capsAfter = null, string? targetKlid = null, uint locale = 0, bool transient = true)
        {
            receipt = default;

            // spec §5.3 - focus moved elsewhere mid-flip → don't paste into the wrong window.
            if (GetForegroundWindow() != foreground)
                return FlipResult.Cancelled;

            // A remote or VM console's clipboard bridge is itself a clipboard monitor, and one that
            // honoured "exclude from monitors" would never carry the text to the far side - so it gets
            // only the two history markers (TransientMarks.HistoryOnly).
            bool remote = RemoteDesktop.IsSlowClipboardTarget(foreground);
            TransientMarks marks = !transient ? TransientMarks.None : remote ? TransientMarks.HistoryOnly : TransientMarks.All;

            ClipboardOwner? owner = transient ? ClipboardOwner.Shared : null;
            PasteOffer? offer = owner?.Offer(text, locale, marks, foreground);
            if (owner != null && offer != null)
            {
                receipt = PasteReceipt.ByOwner(owner, offer.OwnSequence);
            }
            else
            {
                if (!Win32Clipboard.TrySetText(text, locale, marks))
                    return FlipResult.Failed;
                receipt = PasteReceipt.BySequence(GetClipboardSequenceNumber());
            }

            Thread.Sleep(30);
            offer?.Arm();
            SideModifiers released = SendCtrlChord(VK_V);
            long sentAt = PasteWait.NowMs();
            Thread.Sleep(PasteTiming.KeystrokeSettleMs); // let the target process the keystroke itself

            // Press again the modifier keys the user is still physically holding.
            RestorePhysicalModifiers(released);

            if (transient)
            {
                PasteOutcome outcome = PasteWait.Run(offer, remote, sentAt, PasteWait.NowMs, Thread.Sleep);
                if (outcome == PasteOutcome.TimedOut)
                    ClipboardFlipLog.Log("paste not consumed within " + PasteTiming.Cap(remote) + " ms (" + text.Length + " chars)");
                else if (outcome == PasteOutcome.RenderedEarly)
                    ClipboardFlipLog.Log("paste rendered before Ctrl+V by " + offer!.RenderedBy + "; fixed wait used");
                else if (outcome == PasteOutcome.TakenOver)
                    ClipboardFlipLog.Log("clipboard written by another program during the paste");
            }

            // Optionally flip the keyboard layout too, so continued typing matches the result.
            if (targetKlid != null && targetKlid.Length > 0)
                LayoutSwitcher.SwitchTo(foreground, targetKlid);

            // Optionally bring CapsLock into line with the pasted text (the case-flip counterpart
            // of the layout switch above - both set a state, neither merely changes one).
            if (capsAfter.HasValue)
                SetCapsLock(capsAfter.Value);

            return FlipResult.Flipped;
        }

        // ---- synthesized input ----------------------------------------------------------

        // NOTE: the chord is usually still held while we synthesize input, so the modifiers that
        // would corrupt a plain Ctrl+C / Ctrl+V (Shift/Alt/Win) are released first and pressed again
        // afterwards. Which keys are held comes from the hook's physical table, never from
        // GetAsyncKeyState - our own key-ups falsify that one (ticket S0004, KC-1). The plans are in
        // KeyInjection, where they are unit-tested; don't "simplify" the explicit up/downs away.

        /// <summary>Send a clean Ctrl+<paramref name="vk"/>; returns the modifier keys it released.</summary>
        private SideModifiers SendCtrlChord(int vk)
        {
            List<KeyStroke> plan = KeyInjection.CtrlChord(PhysicalModifiers.Shared.Snapshot(), vk, out SideModifiers released);
            _send(plan);
            return released;
        }

        /// <summary>
        /// Press again, side for side, the keys a plan released that the user is <b>still physically</b>
        /// holding - a key let go in the meantime stays up, or it would be stuck down.
        /// </summary>
        private void RestorePhysicalModifiers(SideModifiers released)
        {
            if (released == SideModifiers.None) return;
            _send(KeyInjection.Restore(released, PhysicalModifiers.Shared.Snapshot()));
        }

        /// <summary>
        /// Put CapsLock into <paramref name="on"/>. There is no API that sets the lock state
        /// directly - a synthesized down+up only flips it - so the current state is read first and
        /// nothing is sent when it already matches. That check is what makes the call idempotent:
        /// correcting the same text twice, or correcting it after the user has already pressed
        /// CapsLock by hand, must not leave the key backwards.
        ///
        /// <para>With "Press SHIFT to turn off Caps Lock" set (<c>KLLF_SHIFTLOCK</c>) the CapsLock key
        /// only ever turns the lock <i>on</i>, so a tap that should have turned it off changes nothing;
        /// Shift is what turns it off there (FP-11). The state is read again after the tap, and Shift
        /// is tapped only when it is still on - never while the user physically holds a Shift, whose
        /// release the tap would fake.</para>
        /// </summary>
        private void SetCapsLock(bool on)
        {
            if (CursorIndicator.IsCapsLockOn() == on) return;
            _send(new[] { new KeyStroke(VK_CAPITAL, false), new KeyStroke(VK_CAPITAL, true) });
            if (on) return;

            for (int i = 0; i < 5; i++)
            {
                Thread.Sleep(20);
                if (!CursorIndicator.IsCapsLockOn()) return;
            }
            if ((PhysicalModifiers.Shared.Snapshot() & SideModifiers.Shift) != 0) return;
            _send(KeyInjection.ShiftTap());
            ClipboardFlipLog.Log("CapsLock turned off with Shift (the CapsLock key did not)");
        }
    }
}

using System;
using System.Collections.Generic;
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
    /// The two halves are also available on their own - <see cref="TryCaptureSelection"/> and
    /// <see cref="ReplaceSelection"/> - because the translator needs seconds of network time between
    /// them and cannot hold the clipboard for that long. <see cref="Run"/> is exactly those two
    /// halves back to back, so the flips keep their original single-backup behaviour.
    ///
    /// Clipboard access uses <see cref="Win32Clipboard"/> (raw Win32, no OLE) so it can't hang
    /// on the background thread. An empty selection is a no-op; if the foreground window changes
    /// mid-operation it is cancelled; the original clipboard is restored at the end.
    /// </summary>
    internal sealed class ClipboardHandler
    {
        private const int VK_C = 0x43;
        private const int VK_V = 0x56;
        private const int VK_DELETE = 0x2E;

        /// <summary>Result of a flip, for the caller to surface (e.g. a tray balloon).</summary>
        public enum FlipResult { Flipped, NoSelection, NoChange, Cancelled, Failed }

        /// <summary>The plain editing commands CyrFlip's own context menu offers first.</summary>
        public enum EditCommand { Copy, Cut, Paste }

        /// <summary>Outcome of the copy half on its own.</summary>
        public enum CaptureResult { Captured, NoSelection, Cancelled }

        /// <summary>
        /// What the clipboard held before we borrowed it - text, an image and a file selection.
        ///
        /// <para>It used to hold text alone, and everything else was simply destroyed: the copy half
        /// of a flip ends in <c>EmptyClipboard</c>, so a user who had a screenshot or a set of files
        /// on the clipboard and then fixed a word with a chord lost them for good, with nothing to
        /// restore from. Those three formats are what people actually keep in a clipboard; the
        /// companions of a rich copy (RTF, HTML) are still lost, and text is what carries the
        /// meaning there.</para>
        /// </summary>
        internal readonly struct ClipboardBackup
        {
            public readonly bool HadText;
            public readonly string? Text;
            /// <summary>Device-independent bitmap (a screenshot, a copied picture), or null.</summary>
            public readonly byte[]? Image;
            /// <summary>A copied file selection (CF_HDROP), or null.</summary>
            public readonly byte[]? Files;

            public ClipboardBackup(bool hadText, string? text, byte[]? image = null, byte[]? files = null)
            {
                HadText = hadText;
                Text = text;
                Image = image;
                Files = files;
            }

            /// <summary>True when there is anything at all worth handing back.</summary>
            public bool HasContent => (HadText && Text != null) || Image != null || Files != null;
        }

        /// <summary>
        /// Cap on a backed-up image. Beyond it the picture is left to its fate rather than copied
        /// through memory twice on every flip - the old behaviour for every non-text format, now the
        /// exception rather than the rule.
        /// </summary>
        internal const int MaxBackupImageBytes = 64 * 1024 * 1024;

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
            return Run(text => KeyboardLayoutConverter.Convert(text, from, to, convertSymbols),
                syncCapsAfter: false, targetKlid: switchLayoutAfter ? to : null);
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
            => Run(CaseFlipEngine.Flip, syncCapsAfter: syncCapsAfter);

        /// <summary>
        /// Cut / Copy / Paste from the text context menu, <b>through the very pipeline the flips
        /// use</b> - not a second implementation of it.
        ///
        /// That is the whole point of this method. A hand-rolled "just SendInput a Ctrl+C" looks
        /// equivalent and is not: <see cref="TryCaptureSelection"/> settles before it injects
        /// anything, then <b>waits for the clipboard sequence number to actually change</b> - which is
        /// what makes the copy reliable instead of hopeful, and what tells "nothing was selected"
        /// apart from "the app ignored us". The paste half likewise gives the target the same 30 ms
        /// before and 140 ms after that <see cref="ReplaceSelection"/> gives it.
        ///
        /// Unlike a flip this <b>does not back up and restore the clipboard</b>: the user asked to
        /// copy, so the copy has to stay - on the clipboard and, with the feature on, in the history.
        ///
        /// Cut is copy plus a Delete rather than a Ctrl+X, so its visible half rides on the capture we
        /// have just verified; in a field that cannot be edited the Delete simply does nothing, which
        /// is the correct outcome there anyway.
        /// </summary>
        public FlipResult RunEdit(EditCommand command)
        {
            if (command == EditCommand.Paste)
            {
                IntPtr foreground = GetForegroundWindow();
                Thread.Sleep(30);
                SideModifiers pasteReleased = SendCtrlChord(VK_V);
                Thread.Sleep(140); // let the target app consume the paste
                RestorePhysicalModifiers(pasteReleased);
                return GetForegroundWindow() != foreground ? FlipResult.Cancelled : FlipResult.Flipped;
            }

            SideModifiers released = SideModifiers.None;
            try
            {
                CaptureResult captured = CaptureSelection(out _, out _, out released);
                if (captured == CaptureResult.Cancelled) return FlipResult.Cancelled;
                if (captured == CaptureResult.NoSelection) return FlipResult.NoSelection;

                // The Delete goes before the held modifiers come back: a Shift pressed again first
                // would turn it into Shift+Delete - "delete permanently" in Explorer.
                if (command == EditCommand.Cut)
                {
                    KeyInjection.Send(new KeyStroke(VK_DELETE, false), new KeyStroke(VK_DELETE, true));
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
        private FlipResult Run(Func<string, string> transform, bool syncCapsAfter, string? targetKlid = null)
        {
            ClipboardBackup backup = BackupClipboard();
            try
            {
                CaptureResult captured = TryCaptureSelection(out string selected, out IntPtr foreground);
                if (captured == CaptureResult.Cancelled) return FlipResult.Cancelled;
                if (captured == CaptureResult.NoSelection) return FlipResult.NoSelection; // spec §5.3 - nothing selected → no-op

                string converted = transform(selected);
                if (converted == selected)
                    return FlipResult.NoChange;

                // The desired CapsLock state is read off the text we are about to paste, not off the
                // key's current state - see CaseFlipEngine.DesiredCapsLock.
                bool? capsAfter = syncCapsAfter ? CaseFlipEngine.DesiredCapsLock(converted) : null;
                return ReplaceSelection(converted, foreground, capsAfter, targetKlid);
            }
            finally
            {
                RestoreClipboard(backup);
            }
        }

        /// <summary>
        /// What the clipboard holds right now, so it can be handed back afterwards. Each format is
        /// read only when it is actually present, so the ordinary case - a flip over plain text -
        /// costs exactly what it always did.
        /// </summary>
        internal static ClipboardBackup BackupClipboard()
        {
            bool hadText = IsClipboardFormatAvailable(CF_UNICODETEXT);
            string? text = hadText && Win32Clipboard.TryGetText(out string current) ? current : null;

            byte[]? image = null;
            if (IsClipboardFormatAvailable(CF_DIB))
                Win32Clipboard.TryGetBytes(CF_DIB, out image, MaxBackupImageBytes);

            byte[]? files = null;
            if (IsClipboardFormatAvailable(CF_HDROP))
                Win32Clipboard.TryGetBytes(CF_HDROP, out files);

            return new ClipboardBackup(hadText, text, image, files);
        }

        /// <summary>
        /// Put back what the clipboard held - all of it, in one open/empty/refill pass. Restoring
        /// the formats one at a time would not work: every write empties the clipboard first and
        /// would drop whatever the previous write had just restored.
        /// </summary>
        internal static void RestoreClipboard(ClipboardBackup backup)
        {
            if (!backup.HasContent) return;

            // The common case by far - text only - keeps its original single-call path.
            if (backup.Image == null && backup.Files == null)
            {
                if (backup.HadText && backup.Text != null)
                    Win32Clipboard.TrySetText(backup.Text);
                return;
            }

            var payloads = new List<KeyValuePair<uint, byte[]>>(3);
            if (backup.HadText && backup.Text != null)
                payloads.Add(new KeyValuePair<uint, byte[]>(CF_UNICODETEXT,
                    System.Text.Encoding.Unicode.GetBytes(backup.Text + "\0")));
            if (backup.Image != null)
                payloads.Add(new KeyValuePair<uint, byte[]>(CF_DIB, backup.Image));
            if (backup.Files != null)
                payloads.Add(new KeyValuePair<uint, byte[]>(CF_HDROP, backup.Files));

            Win32Clipboard.Restore(payloads);
        }

        /// <summary>
        /// The copy half: synthesize a clean Ctrl+C and wait for the selection to reach the
        /// clipboard. The caller owns the backup so a long-running transform (the translator) can
        /// hand the clipboard back immediately and take it again later. The modifiers the user is
        /// still holding are pressed again before it returns.
        /// </summary>
        internal CaptureResult TryCaptureSelection(out string selection, out IntPtr foreground)
        {
            SideModifiers released = SideModifiers.None;
            try
            {
                return CaptureSelection(out selection, out foreground, out released);
            }
            finally
            {
                RestorePhysicalModifiers(released);
            }
        }

        /// <summary>The copy half without the restore - the caller presses <paramref name="released"/> again.</summary>
        private static CaptureResult CaptureSelection(out string selection, out IntPtr foreground,
            out SideModifiers released)
        {
            selection = "";
            foreground = GetForegroundWindow();
            uint initialSeq = GetClipboardSequenceNumber();

            Thread.Sleep(20);
            released = SendCtrlChord(VK_C);

            // Wait for the copy to populate the clipboard (selection may be empty).
            for (int i = 0; i < 12; i++)
            {
                Thread.Sleep(40);
                if (GetForegroundWindow() != foreground)
                    return CaptureResult.Cancelled;

                uint currentSeq = GetClipboardSequenceNumber();
                if (currentSeq == initialSeq) continue;
                if (Win32Clipboard.TryGetText(out string text) && text.Length > 0)
                {
                    selection = text;
                    return CaptureResult.Captured;
                }
            }

            return CaptureResult.NoSelection;
        }

        /// <summary>
        /// The paste half: put <paramref name="text"/> on the clipboard and send Ctrl+V into
        /// <paramref name="foreground"/>, provided it is still the focused window.
        /// </summary>
        internal FlipResult ReplaceSelection(string text, IntPtr foreground,
            bool? capsAfter = null, string? targetKlid = null)
        {
            // spec §5.3 - focus moved elsewhere mid-flip → don't paste into the wrong window.
            if (GetForegroundWindow() != foreground)
                return FlipResult.Cancelled;

            if (!Win32Clipboard.TrySetText(text))
                return FlipResult.Failed;

            Thread.Sleep(30);
            SideModifiers released = SendCtrlChord(VK_V);
            Thread.Sleep(140); // let the target app consume the paste before we restore

            // Press again the modifier keys the user is still physically holding.
            RestorePhysicalModifiers(released);

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
        private static SideModifiers SendCtrlChord(int vk)
        {
            List<KeyStroke> plan = KeyInjection.CtrlChord(PhysicalModifiers.Shared.Snapshot(), vk, out SideModifiers released);
            KeyInjection.Send(plan);
            return released;
        }

        /// <summary>
        /// Press again, side for side, the keys a plan released that the user is <b>still physically</b>
        /// holding - a key let go in the meantime stays up, or it would be stuck down.
        /// </summary>
        private static void RestorePhysicalModifiers(SideModifiers released)
        {
            if (released == SideModifiers.None) return;
            KeyInjection.Send(KeyInjection.Restore(released, PhysicalModifiers.Shared.Snapshot()));
        }

        /// <summary>
        /// Put CapsLock into <paramref name="on"/>. There is no API that sets the lock state
        /// directly - a synthesized down+up only flips it - so the current state is read first and
        /// nothing is sent when it already matches. That check is what makes the call idempotent:
        /// correcting the same text twice, or correcting it after the user has already pressed
        /// CapsLock by hand, must not leave the key backwards.
        /// </summary>
        private static void SetCapsLock(bool on)
        {
            if (CursorIndicator.IsCapsLockOn() == on) return;
            KeyInjection.Send(new KeyStroke(VK_CAPITAL, false), new KeyStroke(VK_CAPITAL, true));
        }
    }
}

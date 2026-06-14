using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// Runs the flip: grab the active selection (synthesized Ctrl+C), transliterate it, and
    /// paste the result back (synthesized Ctrl+V). (spec §2.2)
    ///
    /// Clipboard access uses <see cref="Win32Clipboard"/> (raw Win32, no OLE) so it can't hang
    /// on the background thread. An empty selection is a no-op; if the foreground window changes
    /// mid-flip the operation is cancelled; the original clipboard is restored at the end.
    /// </summary>
    internal sealed class ClipboardHandler
    {
        private const int VK_C = 0x43;
        private const int VK_V = 0x56;

        /// <summary>Result of a flip, for the caller to surface (e.g. a tray balloon).</summary>
        public enum FlipResult { Flipped, NoSelection, NoChange, Cancelled, Failed }

        public FlipResult Flip()
        {
            IntPtr foreground = GetForegroundWindow();

            // Track physical modifier states before we release them.
            bool ctrlDown = (GetAsyncKeyState(Hotkey.VK_CONTROL) & 0x8000) != 0;
            bool shiftDown = (GetAsyncKeyState(Hotkey.VK_SHIFT) & 0x8000) != 0;
            bool altDown = (GetAsyncKeyState(Hotkey.VK_MENU) & 0x8000) != 0;
            bool winDown = (GetAsyncKeyState(Hotkey.VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(Hotkey.VK_RWIN) & 0x8000) != 0;

            bool originalHadText = IsClipboardFormatAvailable(CF_UNICODETEXT);
            string? original = originalHadText && Win32Clipboard.TryGetText(out string o) ? o : null;
            uint initialSeq = GetClipboardSequenceNumber();

            try
            {
                Thread.Sleep(20);
                SendCopy();

                // Wait for the copy to populate the clipboard (selection may be empty).
                string selected = "";
                for (int i = 0; i < 12; i++)
                {
                    Thread.Sleep(40);
                    if (GetForegroundWindow() != foreground)
                        return FlipResult.Cancelled;

                    uint currentSeq = GetClipboardSequenceNumber();
                    if (currentSeq != initialSeq)
                    {
                        if (Win32Clipboard.TryGetText(out string t) && t.Length > 0)
                        {
                            selected = t;
                            break;
                        }
                    }
                }

                if (selected.Length == 0)
                    return FlipResult.NoSelection; // spec §5.3 - nothing selected → no-op

                string converted = TransliterationEngine.Transliterate(selected);
                if (converted == selected)
                    return FlipResult.NoChange;

                // spec §5.3 - focus moved elsewhere mid-flip → don't paste into the wrong window.
                if (GetForegroundWindow() != foreground)
                    return FlipResult.Cancelled;

                if (!Win32Clipboard.TrySetText(converted))
                    return FlipResult.Failed;

                Thread.Sleep(30);
                SendPaste();
                Thread.Sleep(140); // let the target app consume the paste before we restore

                // Restore physical modifier keys that the user is still physically holding.
                RestorePhysicalModifiers(ctrlDown, shiftDown, altDown, winDown);

                return FlipResult.Flipped;
            }
            finally
            {
                // Only restore text if the clipboard originally held text.
                if (originalHadText && original != null)
                {
                    Win32Clipboard.TrySetText(original);
                }
            }
        }

        private static void RestorePhysicalModifiers(bool ctrl, bool shift, bool alt, bool win)
        {
            var restore = new System.Collections.Generic.List<(int vk, bool up)>();
            if (ctrl && (GetAsyncKeyState(Hotkey.VK_CONTROL) & 0x8000) != 0)
                restore.Add((Hotkey.VK_CONTROL, false));
            if (shift && (GetAsyncKeyState(Hotkey.VK_SHIFT) & 0x8000) != 0)
                restore.Add((Hotkey.VK_SHIFT, false));
            if (alt && (GetAsyncKeyState(Hotkey.VK_MENU) & 0x8000) != 0)
                restore.Add((Hotkey.VK_MENU, false));
            if (win && ((GetAsyncKeyState(Hotkey.VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(Hotkey.VK_RWIN) & 0x8000) != 0))
                restore.Add((Hotkey.VK_LWIN, false));

            if (restore.Count > 0)
                Send(restore.ToArray());
        }

        // ---- synthesized input ----------------------------------------------------------

        // NOTE: the hotkey is held down while we synthesize input, so we first release the
        // modifiers that would corrupt a plain Ctrl+C / Ctrl+V (Shift/Alt/Win) and drive Ctrl
        // ourselves. This intentionally leaves the OS modifier state briefly out of sync with the
        // keys the user is physically holding - don't "simplify" the explicit up/downs away.
        private static void SendCopy()
        {
            // Release any held modifiers that would corrupt Ctrl+C, then send a clean Ctrl+C.
            Send(
                (Hotkey.VK_SHIFT, true), (Hotkey.VK_MENU, true),
                (Hotkey.VK_LWIN, true), (Hotkey.VK_RWIN, true),
                (Hotkey.VK_CONTROL, false), (VK_C, false),
                (VK_C, true), (Hotkey.VK_CONTROL, true));
        }

        private static void SendPaste()
        {
            Send(
                (Hotkey.VK_SHIFT, true), (Hotkey.VK_MENU, true),
                (Hotkey.VK_CONTROL, false), (VK_V, false),
                (VK_V, true), (Hotkey.VK_CONTROL, true));
        }

        private static void Send(params (int vk, bool up)[] keys)
        {
            INPUT[] inputs = keys.Select(k => new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = (ushort)k.vk,
                        dwFlags = k.up ? KEYEVENTF_KEYUP : 0u,
                    },
                },
            }).ToArray();

            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }
    }
}

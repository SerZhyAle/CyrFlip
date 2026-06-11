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
            string? original = Win32Clipboard.TryGetText(out string o) ? o : null;

            try
            {
                Win32Clipboard.TryClear();
                Thread.Sleep(30);
                SendCopy();

                // Wait for the copy to populate the clipboard (selection may be empty).
                string selected = "";
                for (int i = 0; i < 12; i++)
                {
                    Thread.Sleep(40);
                    if (Win32Clipboard.TryGetText(out string t) && t.Length > 0)
                    {
                        selected = t;
                        break;
                    }
                }

                if (selected.Length == 0)
                    return FlipResult.NoSelection; // spec §5.3 — nothing selected → no-op

                string converted = TransliterationEngine.Transliterate(selected);
                if (converted == selected)
                    return FlipResult.NoChange;

                // spec §5.3 — focus moved elsewhere mid-flip → don't paste into the wrong window.
                if (GetForegroundWindow() != foreground)
                    return FlipResult.Cancelled;

                if (!Win32Clipboard.TrySetText(converted))
                    return FlipResult.Failed;

                Thread.Sleep(30);
                SendPaste();
                Thread.Sleep(140); // let the target app consume the paste before we restore
                return FlipResult.Flipped;
            }
            finally
            {
                if (original != null)
                    Win32Clipboard.TrySetText(original);
                else
                    Win32Clipboard.TryClear();
            }
        }

        // ---- synthesized input ----------------------------------------------------------

        // NOTE: the hotkey is held down while we synthesize input, so we first release the
        // modifiers that would corrupt a plain Ctrl+C / Ctrl+V (Shift/Alt/Win) and drive Ctrl
        // ourselves. This intentionally leaves the OS modifier state briefly out of sync with the
        // keys the user is physically holding — don't "simplify" the explicit up/downs away.
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

using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// Runs the flip: grab the active selection (synthesized Ctrl+C), transliterate it, and
    /// paste the result back (synthesized Ctrl+V). (spec §2.2)
    ///
    /// Must run on an STA thread (the WinForms clipboard requirement). Clipboard access is
    /// retried on lock (max 3, spec §5.3); an empty selection is a no-op; if the foreground
    /// window changes mid-flip the operation is cancelled and the clipboard restored.
    /// </summary>
    internal sealed class ClipboardHandler
    {
        private const int VK_C = 0x43;
        private const int VK_V = 0x56;
        private const int ClipboardRetries = 3;

        /// <summary>Result of a flip, for the caller to surface (e.g. a tray balloon).</summary>
        public enum FlipResult { Flipped, NoSelection, NoChange, Cancelled, Failed }

        public FlipResult Flip()
        {
            IntPtr foreground = GetForegroundWindow();
            string? original = TryGetText(out string o) ? o : null;

            try
            {
                if (!TryClear())
                    return FlipResult.Failed;

                Thread.Sleep(30);
                SendCopy();

                // Wait for the copy to populate the clipboard (selection may be empty).
                string selected = "";
                for (int i = 0; i < 6; i++)
                {
                    Thread.Sleep(45);
                    if (TryGetText(out string t) && t.Length > 0)
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

                if (!TrySetText(converted))
                    return FlipResult.Failed;

                Thread.Sleep(30);
                SendPaste();
                Thread.Sleep(140); // let the target app consume the paste before we restore
                return FlipResult.Flipped;
            }
            finally
            {
                RestoreClipboard(original);
            }
        }

        private static void RestoreClipboard(string? original)
        {
            if (original != null)
                TrySetText(original);
            else
                TryClear();
        }

        // ---- synthesized input ----------------------------------------------------------

        private static void SendCopy()
        {
            // Release any held modifiers that would corrupt Ctrl+C, then send a clean Ctrl+C.
            Send(
                (Hotkey.VK_SHIFT, true), (Hotkey.VK_MENU, true),
                (Hotkey.VK_LWIN, true), (Hotkey.VK_RWIN, true),
                ((ushort)Hotkey.VK_CONTROL, false), ((ushort)VK_C, false),
                ((ushort)VK_C, true), ((ushort)Hotkey.VK_CONTROL, true));
        }

        private static void SendPaste()
        {
            Send(
                (Hotkey.VK_SHIFT, true), (Hotkey.VK_MENU, true),
                ((ushort)Hotkey.VK_CONTROL, false), ((ushort)VK_V, false),
                ((ushort)VK_V, true), ((ushort)Hotkey.VK_CONTROL, true));
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

        // ---- clipboard with lock retries (spec §5.3) ------------------------------------

        private static bool TryGetText(out string text)
        {
            for (int i = 0; i < ClipboardRetries; i++)
            {
                try
                {
                    text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
                    return true;
                }
                catch (ExternalException) { Thread.Sleep(40); }
            }
            text = string.Empty;
            return false;
        }

        private static bool TrySetText(string text)
        {
            for (int i = 0; i < ClipboardRetries; i++)
            {
                try
                {
                    if (text.Length == 0)
                        Clipboard.Clear();
                    else
                        Clipboard.SetText(text);
                    return true;
                }
                catch (ExternalException) { Thread.Sleep(40); }
            }
            return false;
        }

        private static bool TryClear()
        {
            for (int i = 0; i < ClipboardRetries; i++)
            {
                try
                {
                    Clipboard.Clear();
                    return true;
                }
                catch (ExternalException) { Thread.Sleep(40); }
            }
            return false;
        }
    }
}

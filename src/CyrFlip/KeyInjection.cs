using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>One synthesized key event.</summary>
    internal readonly struct KeyStroke
    {
        public readonly int Vk;
        public readonly bool Up;

        public KeyStroke(int vk, bool up) { Vk = vk; Up = up; }

        public override string ToString() => "0x" + Vk.ToString("X2") + (Up ? " up" : " down");
    }

    /// <summary>
    /// Every key CyrFlip synthesizes goes through here (ticket S0004, KC-1 and KC-4). The plans are
    /// pure - held modifiers in, key events out - so what reaches the target window is unit-tested;
    /// <see cref="Send"/> is the one place that talks to <c>SendInput</c>.
    ///
    /// <para>Three rules the plans keep:</para>
    /// <list type="number">
    /// <item><b>Side-specific keys.</b> A generic <c>VK_SHIFT</c> injected without a scan code is
    /// resolved by Windows to the <i>left</i> key: with the right Shift held, the "release" released
    /// nothing, the synthesized Ctrl+C arrived as Ctrl+Shift+C, and the generic re-press afterwards
    /// left the left key logically down after the user let go of the right one.</item>
    /// <item><b>Only what is held</b> is released, and only what was released and is still
    /// physically held is pressed again.</item>
    /// <item><b>The mask key.</b> Windows reads "modifiers down, modifiers up, nothing between" as a
    /// gesture of its own: Ctrl+Shift or Alt+Shift switches the layout, a lone Alt opens the menu
    /// bar, a lone Win the Start menu. A swallowed trigger or a released modifier produces exactly
    /// that, so an unassigned key (<see cref="MaskVk"/>, the AutoHotkey <c>#MenuMaskKey</c>
    /// technique) is tapped while the modifiers are still down.</item>
    /// </list>
    /// </summary>
    internal static class KeyInjection
    {
        /// <summary>
        /// Carried in <c>dwExtraInfo</c> of every key CyrFlip injects, so the keyboard hook can tell
        /// our own events from another tool's and keep them out of the physical modifier table.
        /// </summary>
        public static readonly System.IntPtr Tag = (System.IntPtr)0x43594650; // "CYFP"

        /// <summary>An unassigned virtual key: nothing acts on it, but it ends a bare-modifier gesture.</summary>
        public const int MaskVk = 0xE8;

        private const SideModifiers MaskedModifiers = SideModifiers.Shift | SideModifiers.Alt | SideModifiers.Win;

        /// <summary>
        /// The mask tap, when a modifier Windows would read on its own release is held; empty
        /// otherwise (a lone Ctrl release does nothing).
        /// </summary>
        public static List<KeyStroke> Mask(SideModifiers held)
        {
            var plan = new List<KeyStroke>(2);
            if ((held & MaskedModifiers) != 0)
            {
                plan.Add(new KeyStroke(MaskVk, false));
                plan.Add(new KeyStroke(MaskVk, true));
            }
            return plan;
        }

        /// <summary>
        /// A clean Ctrl+<paramref name="vk"/> while the user physically holds <paramref name="held"/>:
        /// mask, release the held Shift/Alt/Win keys, then press the key under Ctrl. A Ctrl the user
        /// already holds is reused and left down - releasing it would make the table and the
        /// system disagree until the user's own key-up arrived.
        /// </summary>
        /// <param name="released">The side-specific keys the plan released; hand them to <see cref="Restore"/>.</param>
        public static List<KeyStroke> CtrlChord(SideModifiers held, int vk, out SideModifiers released)
        {
            var plan = Mask(held);
            released = held & MaskedModifiers;
            foreach (SideModifiers side in Sides(released))
                plan.Add(new KeyStroke(VkOf(side), true));

            SideModifiers ctrl = (held & SideModifiers.RCtrl) != 0 && (held & SideModifiers.LCtrl) == 0
                ? SideModifiers.RCtrl : SideModifiers.LCtrl;
            bool ctrlHeld = (held & ctrl) != 0;
            plan.Add(new KeyStroke(VkOf(ctrl), false));
            plan.Add(new KeyStroke(vk, false));
            plan.Add(new KeyStroke(vk, true));
            if (!ctrlHeld) plan.Add(new KeyStroke(VkOf(ctrl), true));
            return plan;
        }

        /// <summary>
        /// Press again the keys a plan released that are still physically held, then mask, so the
        /// user's own release of them is not read as a bare-modifier gesture either.
        /// </summary>
        public static List<KeyStroke> Restore(SideModifiers released, SideModifiers stillHeld)
        {
            SideModifiers again = released & stillHeld;
            var plan = new List<KeyStroke>(10);
            foreach (SideModifiers side in Sides(again))
                plan.Add(new KeyStroke(VkOf(side), false));
            plan.AddRange(Mask(again));
            return plan;
        }

        /// <summary>The single keys of a set, one at a time, in a fixed order.</summary>
        internal static IEnumerable<SideModifiers> Sides(SideModifiers set)
        {
            for (int bit = 1; bit <= (int)SideModifiers.RWin; bit <<= 1)
                if (((int)set & bit) != 0)
                    yield return (SideModifiers)bit;
        }

        internal static int VkOf(SideModifiers side)
        {
            switch (side)
            {
                case SideModifiers.LCtrl: return PhysicalModifiers.VK_LCONTROL;
                case SideModifiers.RCtrl: return PhysicalModifiers.VK_RCONTROL;
                case SideModifiers.LShift: return PhysicalModifiers.VK_LSHIFT;
                case SideModifiers.RShift: return PhysicalModifiers.VK_RSHIFT;
                case SideModifiers.LAlt: return PhysicalModifiers.VK_LMENU;
                case SideModifiers.RAlt: return PhysicalModifiers.VK_RMENU;
                case SideModifiers.LWin: return Hotkey.VK_LWIN;
                case SideModifiers.RWin: return Hotkey.VK_RWIN;
                default: throw new System.ArgumentOutOfRangeException(nameof(side));
            }
        }

        /// <summary>Keys whose scan code carries the E0 prefix - the extended flag must go with them.</summary>
        internal static bool IsExtended(int vk)
            => vk == PhysicalModifiers.VK_RCONTROL || vk == PhysicalModifiers.VK_RMENU
            || vk == Hotkey.VK_LWIN || vk == Hotkey.VK_RWIN
            || (vk >= 0x21 && vk <= 0x2E && vk != 0x2C); // PageUp..Delete except PrintScreen

        /// <summary>Tap the mask key if <paramref name="held"/> needs it - after a chord fires.</summary>
        public static void SendMask(SideModifiers held) => Send(Mask(held));

        /// <summary>Inject <paramref name="keys"/> in order, each with its real scan code and our tag.</summary>
        public static void Send(IList<KeyStroke> keys)
        {
            if (keys.Count == 0) return;
            INPUT[] inputs = keys.Select(k => new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = (ushort)k.Vk,
                        wScan = (ushort)(MapVirtualKey((uint)k.Vk, MAPVK_VK_TO_VSC) & 0xFF),
                        dwFlags = (k.Up ? KEYEVENTF_KEYUP : 0u) | (IsExtended(k.Vk) ? KEYEVENTF_EXTENDEDKEY : 0u),
                        dwExtraInfo = Tag,
                    },
                },
            }).ToArray();

            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        /// <summary>Convenience for a fixed sequence (a Delete tap, a CapsLock tap).</summary>
        public static void Send(params KeyStroke[] keys) => Send((IList<KeyStroke>)keys);
    }
}

using System;
using System.Collections.Generic;

namespace CyrFlip
{
    /// <summary>The mouse button a <see cref="MouseChord"/> can be built on.</summary>
    internal enum MouseChordButton
    {
        Right,
        Middle,
    }

    /// <summary>
    /// The chord that opens CyrFlip's own text context menu: a mouse button plus the modifier keys
    /// that must be held with it (spec §4). Deliberately a separate type from <see cref="Hotkey"/> -
    /// a keyboard chord always needs a trigger VK and a mouse chord never has one, so one shared type
    /// would carry a field that is meaningless half the time.
    ///
    /// Stored as an invariant token ("Ctrl+RightClick"), shown to the user translated: the registry
    /// value has to survive a change of UI language.
    /// </summary>
    internal readonly struct MouseChord
    {
        /// <summary>The Russian source strings the button names are translated from.</summary>
        public const string RightButtonName = "правая кнопка мыши";
        public const string MiddleButtonName = "средняя кнопка мыши";

        public bool Ctrl { get; }
        public bool Shift { get; }
        public bool Alt { get; }
        public MouseChordButton Button { get; }

        public MouseChord(bool ctrl, bool shift, bool alt, MouseChordButton button)
        {
            Ctrl = ctrl; Shift = shift; Alt = alt; Button = button;
        }

        /// <summary>
        /// Ctrl + right click. Nothing in Windows claims it, unlike Shift + right click (the shell's
        /// extended context menu) or the middle button (autoscroll, "open in a new tab").
        /// </summary>
        public static MouseChord Default => new MouseChord(true, false, false, MouseChordButton.Right);

        /// <summary>
        /// The chords offered in settings, in menu order. A fixed list rather than a capture dialog
        /// because the right button <b>must</b> come with a modifier - otherwise CyrFlip would eat
        /// every context menu in the system - and a list makes that impossible to get wrong.
        /// </summary>
        public static readonly string[] Choices =
        {
            "Ctrl+RightClick", "Shift+RightClick", "Alt+RightClick",
            "MiddleClick", "Ctrl+MiddleClick", "Shift+MiddleClick",
        };

        /// <summary>The invariant token stored in the registry; round-trips through <see cref="TryParse"/>.</summary>
        public string Token
        {
            get
            {
                var parts = new List<string>(4);
                if (Ctrl) parts.Add("Ctrl");
                if (Shift) parts.Add("Shift");
                if (Alt) parts.Add("Alt");
                parts.Add(Button == MouseChordButton.Right ? "RightClick" : "MiddleClick");
                return string.Join("+", parts);
            }
        }

        /// <summary>The Russian source string for this chord's button, to be run through Localization.</summary>
        public string ButtonNameKey
            => Button == MouseChordButton.Right ? RightButtonName : MiddleButtonName;

        /// <summary>"Ctrl + правая кнопка мыши" - modifiers stay Latin, the button name is translated.</summary>
        public string Display(Func<string, string> translate)
        {
            var parts = new List<string>(4);
            if (Ctrl) parts.Add("Ctrl");
            if (Shift) parts.Add("Shift");
            if (Alt) parts.Add("Alt");
            parts.Add(translate(ButtonNameKey));
            return string.Join(" + ", parts);
        }

        /// <summary>
        /// True when the modifiers physically held match this chord exactly - no more, no less, the
        /// same rule <see cref="KeyboardHook.Matches"/> applies. Win is never part of a mouse chord,
        /// so holding it means "not this chord".
        /// </summary>
        public bool Matches(bool ctrl, bool shift, bool alt, bool win)
            => !win && ctrl == Ctrl && shift == Shift && alt == Alt;

        /// <summary>Parse a stored token; an unusable one falls back to <see cref="Default"/>.</summary>
        public static MouseChord Parse(string? text) => TryParse(text, out MouseChord chord) ? chord : Default;

        /// <summary>
        /// Parse without the fallback. Fails on a token with no button, and - deliberately - on a bare
        /// right click: a hand-edited registry value must not be able to make CyrFlip swallow every
        /// context menu in Windows.
        /// </summary>
        public static bool TryParse(string? text, out MouseChord chord)
        {
            chord = Default;
            if (string.IsNullOrWhiteSpace(text)) return false;

            bool ctrl = false, shift = false, alt = false;
            MouseChordButton? button = null;

            foreach (string raw in text!.Split('+'))
            {
                switch (raw.Trim().ToLowerInvariant())
                {
                    case "": break;
                    case "ctrl":
                    case "control": ctrl = true; break;
                    case "shift": shift = true; break;
                    case "alt": alt = true; break;
                    case "right":
                    case "rmb":
                    case "rightclick": button = MouseChordButton.Right; break;
                    case "middle":
                    case "mmb":
                    case "middleclick": button = MouseChordButton.Middle; break;
                }
            }

            if (button == null) return false;
            if (button == MouseChordButton.Right && !ctrl && !shift && !alt) return false;

            chord = new MouseChord(ctrl, shift, alt, button.Value);
            return true;
        }
    }
}

using System;
using System.Drawing;

namespace CyrFlip
{
    /// <summary>
    /// The annotation editor's eight fixed swatches (S0071 6.4): white, black, red, orange, yellow,
    /// green, cyan, blue; the default ink is red.
    ///
    /// <para><b>Content, not theme</b> - the <c>LAYOUT-PALETTE</c> precedent: the swatches name what
    /// will be drawn onto the user's screenshot, so they are the same in light, dark and high
    /// contrast, and they live here beside <see cref="AnnotationShape"/>, never in
    /// <see cref="ThemePalette"/>. Every other colour the editor and the toolbar use - chrome,
    /// border, button ink, hover - reads <c>ThemePalette</c> roles only (<c>APP-STYLE</c> rule 4);
    /// this file is the declared exception. The accessible names are the English source keys the
    /// localization table carries for all 13 UI languages.</para>
    /// </summary>
    internal static class AnnotationPalette
    {
        public static readonly Color[] Swatches =
        {
            Color.White,
            Color.Black,
            Color.Red,
            Color.FromArgb(255, 140, 0),   // orange - pure orange (255,165,0) is hard to tell from yellow at swatch size
            Color.Yellow,
            Color.FromArgb(0, 176, 80),    // green - a mid green that reads on both a light and a dark screenshot
            Color.Cyan,
            Color.FromArgb(0, 102, 204),   // blue - a mid blue, again readable over typical screenshots
        };

        public static Color Default => Color.Red;

        public const int DefaultIndex = 2;

        /// <summary>The swatch index of an ink the toolbar currently holds; 0 when it is none of them.</summary>
        public static int IndexOf(Color ink)
        {
            for (int i = 0; i < Swatches.Length; i++)
                if (Swatches[i].ToArgb() == ink.ToArgb())
                    return i;
            return 0;
        }

        /// <summary>The swatch's accessible name, the same words every tooltip and refusal uses.</summary>
        public static string Name(int index, Func<string, string> translate)
        {
            switch (index)
            {
                case 0: return translate("White");
                case 1: return translate("Black");
                case 2: return translate("Red");
                case 3: return translate("Orange");
                case 4: return translate("Yellow");
                case 5: return translate("Green");
                case 6: return translate("Cyan");
                case 7: return translate("Blue");
                default: throw new ArgumentOutOfRangeException(nameof(index), index, "not a swatch");
            }
        }
    }
}

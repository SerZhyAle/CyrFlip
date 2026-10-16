using System;
using System.Drawing;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The direction and the script font of a window that lives across language changes
    /// (APP-BEHAVIOUR rule 8, ticket S0060 P7). The direction is a fact of the language
    /// (<see cref="Localization.IsRightToLeft"/>) and the family is the script's own
    /// (<see cref="Localization.FontFamily"/>) - and both are re-read, never "left alone when the new
    /// language needs nothing": switching from Hindi back to English has to switch the family back
    /// too, or Nirmala UI stays for good. One implementation, so a window cannot get half of it.
    ///
    /// <para>A window that never needed a script font keeps the font it was built with: the family is
    /// replaced only for a language that has its own, and put back (to the system message font) when
    /// the window was given one before.</para>
    ///
    /// <para>The caller owns the font it is handed back: WinForms never disposes a font that was
    /// assigned to a control, so the previous one is released here and the last one by the caller's
    /// <c>Dispose</c>.</para>
    /// </summary>
    internal static class WindowScript
    {
        /// <summary>The direction the language is written in.</summary>
        internal static RightToLeft DirectionFor(string language)
            => Localization.IsRightToLeft(language) ? RightToLeft.Yes : RightToLeft.No;

        /// <summary>
        /// Apply the language's direction and font family to <paramref name="form"/>. Returns true when
        /// the font was replaced (the caller re-measures what depends on it).
        /// </summary>
        internal static bool Apply(Form form, string language, ref Font? ownFont)
        {
            if (form is ThemedForm themed) themed.HelpLanguage = language;
            RightToLeft direction = DirectionFor(language);
            form.RightToLeft = direction;
            form.RightToLeftLayout = direction == RightToLeft.Yes;

            string? script = Localization.FontFamily(language);
            if (script == null && ownFont == null) return false; // the built font was never replaced

            Font next;
            try
            {
                using Font system = SystemFonts.MessageBoxFont;
                string wanted = script ?? system.FontFamily.Name;
                if (ownFont != null && form.Font.FontFamily.Name == wanted) return false;
                next = new Font(wanted, system.SizeInPoints);
            }
            catch (ArgumentException)
            {
                return false; // the family is missing on this machine - the current font stays
            }
            Font? previous = ownFont;
            form.Font = ownFont = next;
            previous?.Dispose();
            return true;
        }
    }
}

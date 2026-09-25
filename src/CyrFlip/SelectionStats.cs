using System;

namespace CyrFlip
{
    /// <summary>
    /// "How much did I just select?" - the two numbers the context menu prints above its commands,
    /// so the user knows roughly how big the clipboard is about to get before pressing anything.
    ///
    /// It is pure text arithmetic on the text <see cref="SelectionProbe"/> already read, which is
    /// what makes it free: nothing is copied, nothing is asked of the other process twice.
    ///
    /// Two rules are worth stating because both are decisions rather than arithmetic. A
    /// <b>trailing</b> line break does not open a new line - selecting one whole line by dragging
    /// past its end is one line, not two, which is what the user sees on screen. And a line break is
    /// counted once however it is written: <c>\r\n</c> is one break, not two.
    /// </summary>
    internal static class SelectionStats
    {
        /// <summary>
        /// Count the lines and characters of <paramref name="text"/>. Characters are counted exactly
        /// as they are - spaces and line breaks included - because that is what the clipboard will
        /// hold.
        /// </summary>
        public static void Count(string? text, out int lines, out int chars)
        {
            lines = 0;
            chars = 0;
            if (string.IsNullOrEmpty(text)) return;

            chars = text!.Length;

            int breaks = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n') breaks++;
                else if (c == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n')) breaks++; // a lone CR
            }

            char last = text[text.Length - 1];
            lines = last == '\n' || last == '\r' ? breaks : breaks + 1;
            if (lines < 1) lines = 1; // a selection that is nothing but one line break is still a line
        }

        /// <summary>
        /// The number as the menu prints it. A count taken from a <b>capped</b> read gets a "+",
        /// because the honest statement there is "at least this much" - the probe stops reading at
        /// <see cref="SelectionProbe.MaxTextChars"/>, and silently printing that ceiling as if it
        /// were the answer is exactly the kind of number a user would trust and act on.
        /// </summary>
        public static string Format(int value, bool truncated) => truncated ? value + "+" : value.ToString();
    }
}

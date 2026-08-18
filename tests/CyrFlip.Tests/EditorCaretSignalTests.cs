using System;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The rule that decides whether the app draws its caret marker inside VS Code. Both halves have a
    /// failure that is only visible on screen: too generous and the overlay disappears where the
    /// extension cannot draw (the chat box, the terminal), too strict and the user sees two markers at
    /// one caret - the report this exists to fix.
    /// </summary>
    public class EditorCaretSignalTests
    {
        private static readonly DateTime Now = new DateTime(2026, 8, 11, 23, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void AJustWrittenSignalIsALiveClaim()
            => Assert.True(EditorCaretSignal.IsFresh(Now.AddMilliseconds(-100), Now));

        /// <summary>
        /// The claim lapses on its own. VS Code cannot tell us whether the focus is in the editor or in
        /// the chat, so the extension refreshes the file only while there is recent editor activity -
        /// and the moment it stops, the overlay has to come back.
        /// </summary>
        [Fact]
        public void AStaleSignalIsNotAClaim()
            => Assert.False(EditorCaretSignal.IsFresh(Now.AddMilliseconds(-(EditorCaretSignal.FreshMs + 1)), Now));

        /// <summary>A file dated in the future (a clock change, a copy from another machine) would
        /// otherwise hide the overlay until the clocks agreed again.</summary>
        [Fact]
        public void ASignalFromTheFutureIsRefused()
            => Assert.False(EditorCaretSignal.IsFresh(Now.AddSeconds(30), Now));

        [Theory]
        [InlineData(@"C:\Users\x\AppData\Local\Programs\Microsoft VS Code\Code.exe", true)]
        [InlineData(@"C:\Program Files\Microsoft VS Code Insiders\Code - Insiders.exe", true)]
        [InlineData(@"C:\Users\x\AppData\Local\Programs\cursor\Cursor.exe", true)]
        [InlineData(@"C:\Program Files\VSCodium\VSCodium.exe", true)]
        [InlineData(@"C:\Windows\System32\notepad.exe", false)]
        [InlineData(@"C:\dev\code-review-tool.exe", false)] // starts with "code", is not one
        [InlineData("", false)]
        [InlineData(null, false)]
        public void OnlyAVsCodeFamilyEditorCanClaimTheCaret(string? image, bool expected)
            => Assert.Equal(expected, EditorCaretSignal.IsEditorImage(image));
    }
}

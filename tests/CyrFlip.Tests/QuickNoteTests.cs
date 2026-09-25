using System;
using System.Collections.Generic;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The note model: the invariants the whole feature rests on. The two that matter most are the
    /// ones a reader would not guess - a blank name is <b>no</b> name rather than the caption
    /// «Без имени», and the list caption is derived on the way to the screen and never written back,
    /// which is what makes editing the first line of an unnamed note harmless.
    /// </summary>
    public class QuickNoteTests
    {
        private static QuickNote Text(string raw, string? title = null) => new QuickNote
        {
            Kind = QuickNoteKind.Text,
            RawText = raw,
            Title = QuickNote.NormalizeTitle(title),
        };

        private static QuickNote Checklist(params (string text, bool done)[] items)
        {
            var note = new QuickNote { Kind = QuickNoteKind.Checklist };
            foreach ((string text, bool done) in items)
                note.Items.Add(new QuickNoteItem { Text = text, IsChecked = done, Position = note.Items.Count });
            return note;
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t \n ")]
        public void ABlankNameIsNoNameAtAll(string? title)
            => Assert.Null(QuickNote.NormalizeTitle(title));

        [Fact]
        public void ANameIsTrimmedButOtherwiseLeftAlone()
            => Assert.Equal("TODO: race", QuickNote.NormalizeTitle("  TODO: race  "));

        [Fact]
        public void TheListCaptionFallsBackToTheFirstNonEmptyLineAndIsNeverStored()
        {
            QuickNote note = Text("\n\n  lock (_gate) { }\nsecond line");

            Assert.Equal("lock (_gate) { }", note.Preview());
            // The whole point: the caption was derived, not assigned.
            Assert.Null(note.Title);
        }

        [Fact]
        public void TheNameWinsOverTheBodyInTheCaption()
            => Assert.Equal("Race in the history", Text("body text", "Race in the history").Preview());

        [Fact]
        public void ALongCaptionIsElidedRatherThanCutOffMidWord()
        {
            string preview = Text(new string('x', 200)).Preview(20);

            Assert.Equal(20, preview.Length);
            Assert.EndsWith("…", preview);
        }

        [Fact]
        public void AChecklistCaptionComesFromItsFirstNonEmptyItem()
            => Assert.Equal("check the lock", Checklist(("", false), ("check the lock", false)).Preview());

        [Fact]
        public void AnEmptyNoteIsEmptyAndAnythingAtAllMakesItNot()
        {
            Assert.True(Text("").IsEmpty);
            Assert.True(Checklist(("", false)).IsEmpty);
            Assert.False(Text("x").IsEmpty);
            Assert.False(Text("", "named").IsEmpty);
            Assert.False(Checklist(("item", false)).IsEmpty);
        }

        [Fact]
        public void WhitespaceIsContentInTheBodyEvenThoughItIsNotAName()
        {
            // A body of a single tab is a deliberate thing to have pasted; a title of a single tab
            // is not a title. The two rules are different on purpose.
            Assert.False(Text("\t").IsEmpty);
            Assert.True(Text("", "\t").IsEmpty);
        }

        // ---- Search ----

        [Fact]
        public void SearchLooksInTheNameTheBodyAndEveryItem()
        {
            Assert.True(Text("nothing here", "HttpClient").Matches("httpclient"));
            Assert.True(Text("var c = new HttpClient();").Matches("httpclient"));
            Assert.True(Checklist(("retire the HttpClient", false)).Matches("httpclient"));
            Assert.False(Text("nothing here").Matches("httpclient"));
        }

        [Fact]
        public void AnEmptyQueryMatchesEverything()
        {
            Assert.True(Text("").Matches(""));
            Assert.True(Text("x").Matches("   "));
            Assert.True(Text("x").Matches(null));
        }

        // ---- Converting between the two kinds ----

        [Fact]
        public void TextBecomesOneItemPerLineIncludingTheBlankOnes()
        {
            QuickNote note = Text("first\n\nthird");
            note.ConvertTo(QuickNoteKind.Checklist);

            Assert.Equal(QuickNoteKind.Checklist, note.Kind);
            Assert.Equal(new[] { "first", "", "third" }, note.Items.ConvertAll(i => i.Text).ToArray());
            Assert.Equal("", note.RawText);
        }

        [Fact]
        public void AChecklistBecomesOneLinePerItemAndTheTicksAreGone()
        {
            QuickNote note = Checklist(("first", true), ("second", false));
            note.ConvertTo(QuickNoteKind.Text);

            Assert.Equal("first\nsecond", note.RawText);
            Assert.Empty(note.Items);
        }

        [Fact]
        public void LosingTheTickMarksIsReportedBeforeItHappens()
        {
            Assert.True(Checklist(("done", true)).LosesDataConvertingTo(QuickNoteKind.Text));
            Assert.False(Checklist(("open", false)).LosesDataConvertingTo(QuickNoteKind.Text));
        }

        [Fact]
        public void LosingCrlfIsReportedBeforeItHappens()
        {
            // Items hold no line endings, so CRLF cannot survive the trip - and the user is told.
            Assert.True(Text("a\r\nb").LosesDataConvertingTo(QuickNoteKind.Checklist));
            Assert.False(Text("a\nb").LosesDataConvertingTo(QuickNoteKind.Checklist));
        }

        [Fact]
        public void ConvertingToTheSameKindLosesNothingAndChangesNothing()
        {
            QuickNote note = Text("a\r\nb");
            Assert.False(note.LosesDataConvertingTo(QuickNoteKind.Text));
            note.ConvertTo(QuickNoteKind.Text);
            Assert.Equal("a\r\nb", note.RawText);
        }

        [Fact]
        public void AnLfOnlyBodySurvivesTheRoundTripThroughAChecklist()
        {
            QuickNote note = Text("line one\nline two\n");
            note.ConvertTo(QuickNoteKind.Checklist);
            note.ConvertTo(QuickNoteKind.Text);

            Assert.Equal("line one\nline two\n", note.RawText);
        }

        // ---- Export ----

        [Fact]
        public void PlainTextExportIsTheNameThenTheExactBody()
        {
            string text = Text("\tif (x) {\r\n\t\treturn;\r\n\t}", "snippet").ToPlainText();

            Assert.Equal("snippet\n\n\tif (x) {\r\n\t\treturn;\r\n\t}", text);
        }

        [Fact]
        public void AnUnnamedNoteExportsAsItsBodyAloneWithNoInventedHeading()
            => Assert.Equal("just the body", Text("just the body").ToPlainText());

        [Fact]
        public void DatesAreLeftOutUnlessAskedFor()
        {
            var note = Text("code");
            note.CreatedAtUtc = note.UpdatedAtUtc = new DateTime(2026, 8, 11, 9, 30, 0, DateTimeKind.Utc);

            Assert.DoesNotContain("2026", note.ToPlainText());
            Assert.Contains("2026", note.ToPlainText(metadata: true));
        }

        [Fact]
        public void AChecklistExportsAsMarkdownTaskLines()
            => Assert.Equal("- [x] done\n- [ ] open", Checklist(("done", true), ("open", false)).Body());

        [Fact]
        public void MarkdownExportTurnsTheNameIntoAHeadingAndNothingElse()
        {
            Assert.Equal("## snippet\n\nbody", Text("body", "snippet").ToMarkdown());
            Assert.Equal("body", Text("body").ToMarkdown());
        }

        // ---- Google Keep ----

        [Fact]
        public void TheKeepTextCarriesPlainLinesBecauseThatIsWhatKeepTurnsIntoItems()
        {
            string keep = Checklist(("first", true), ("second", false)).ToKeepText();

            Assert.Equal("first\nsecond", keep);
            Assert.DoesNotContain("[x]", keep);
        }

        [Fact]
        public void TheKeepTextOfATextNoteIsTheNameAndTheExactBody()
            => Assert.Equal("name\n\nbody\twith\ttabs", Text("body\twith\ttabs", "name").ToKeepText());

        // ---- The size cap ----

        [Fact]
        public void TheSizeCapIsMeasuredInUtf8BytesNotCharacters()
        {
            Assert.False(QuickNote.ExceedsLimit(new string('a', QuickNote.MaxBytes)));
            Assert.True(QuickNote.ExceedsLimit(new string('a', QuickNote.MaxBytes + 1)));
            // Cyrillic is two bytes per character in UTF-8, so half as many fit.
            Assert.True(QuickNote.ExceedsLimit(new string('я', QuickNote.MaxBytes / 2 + 1)));
        }

        [Fact]
        public void CloningCopiesTheItemsRatherThanSharingThem()
        {
            QuickNote note = Checklist(("item", false));
            QuickNote copy = note.Clone();
            copy.Items[0].IsChecked = true;

            Assert.False(note.Items[0].IsChecked);
        }
    }
}

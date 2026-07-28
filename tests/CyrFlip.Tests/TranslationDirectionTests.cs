using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The four kinds of translation row, as a pure function of the two configured languages:
    ///   1 auto → UI language, 2 auto → target, 3 my language → target, 4 target → my language.
    ///
    /// Only 3 and 4 name a source, and even then only as a hint - the point of the pair is that the
    /// direction is known <b>before</b> the model answers, which is what lets the popup say where the
    /// text is going while it is still being written.
    /// </summary>
    public class TranslationDirectionTests
    {
        private static TranslationDirection Resolve(string token, string source = "ru", string target = "en")
            => TranslationLanguages.ResolveDirection(token, "Русский", "uk", source, target);

        [Fact]
        public void UiTokenTranslatesIntoTheInterfaceLanguageAndDetectsTheSource()
        {
            TranslationDirection d = Resolve(TranslationLanguages.UiToken);
            Assert.Equal("ru", d.TargetCode);
            Assert.Null(d.SourceCode);
        }

        [Fact]
        public void TargetTokenTranslatesIntoTheConfiguredTargetAndDetectsTheSource()
        {
            TranslationDirection d = Resolve(TranslationLanguages.TargetToken, target: "de");
            Assert.Equal("de", d.TargetCode);
            Assert.Null(d.SourceCode);
        }

        [Fact]
        public void SourceToTargetNamesBothEnds()
        {
            TranslationDirection d = Resolve(TranslationLanguages.SourceToTargetToken, source: "uk", target: "en");
            Assert.Equal("en", d.TargetCode);
            Assert.Equal("uk", d.SourceCode);
        }

        /// <summary>The other half of the pair is the same two languages, swapped - nothing more.</summary>
        [Fact]
        public void TargetToSourceIsExactlyTheMirrorImage()
        {
            TranslationDirection forward = Resolve(TranslationLanguages.SourceToTargetToken, source: "ru", target: "en");
            TranslationDirection back = Resolve(TranslationLanguages.TargetToSourceToken, source: "ru", target: "en");

            Assert.Equal(forward.TargetCode, back.SourceCode);
            Assert.Equal(forward.SourceCode, back.TargetCode);
        }

        /// <summary>An unset "my language" follows the interface language, which is right for most people.</summary>
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void AnEmptyMyLanguageFallsBackToTheInterfaceLanguage(string? source)
        {
            TranslationDirection d = Resolve(TranslationLanguages.TargetToSourceToken, source: source!);
            Assert.Equal("ru", d.TargetCode); // the UI language is Русский
        }

        [Fact]
        public void AnEmptyTargetFallsBackToEnglish()
        {
            TranslationDirection d = Resolve(TranslationLanguages.TargetToken, target: "");
            Assert.Equal("en", d.TargetCode);
        }

        /// <summary>A plain language code still means what it always meant - old rows keep working.</summary>
        [Fact]
        public void AConcreteCodeIsUnchangedAndStillAutoDetects()
        {
            TranslationDirection d = Resolve("de");
            Assert.Equal("de", d.TargetCode);
            Assert.Null(d.SourceCode);
        }

        [Fact]
        public void ActiveTokenStillFollowsTheLayout()
        {
            TranslationDirection d = Resolve(TranslationLanguages.ActiveToken);
            Assert.Equal("uk", d.TargetCode);
            Assert.Null(d.SourceCode);
        }

        [Fact]
        public void EveryRowKindHasItsOwnLabel()
        {
            string[] tokens =
            {
                TranslationLanguages.UiToken, TranslationLanguages.TargetToken,
                TranslationLanguages.SourceToTargetToken, TranslationLanguages.TargetToSourceToken,
            };
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (string token in tokens)
            {
                string label = TranslationLanguages.Label(token, "English");
                Assert.NotEmpty(label);
                Assert.DoesNotContain(">", label);          // the raw token never reaches the user
                Assert.True(seen.Add(label), "two row kinds share the label " + label);
            }
        }

        /// <summary>
        /// The source hint is an expectation, not an assertion: the prompt has to leave the model room
        /// to translate out of whatever the text is really in, or pressing the wrong half of the pair
        /// produces nonsense instead of a translation.
        /// </summary>
        [Fact]
        public void TheSourceHintLeavesTheModelAWayOut()
        {
            string hint = TranslationService.SourceHintLine("Russian");
            Assert.Contains("expected to be in Russian", hint);
            Assert.Contains("if it plainly is not", hint);

            Assert.Equal("", TranslationService.SourceHintLine(null));
            Assert.Equal("", TranslationService.SourceHintLine(""));
        }

        [Fact]
        public void AnAutoDetectingRowSendsNoSourceLineAtAll()
        {
            string prompt = TranslationService.BuildPrompt("German", "текст");
            Assert.DoesNotContain("expected to be in", prompt);
            Assert.Contains("into German", prompt);
        }
    }
}

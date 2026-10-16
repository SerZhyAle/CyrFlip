using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// A language change reaches every long-lived window with no restart, direction and script font
    /// included (APP-BEHAVIOUR rules 7 and 8, ticket S0060 P7-a). The history search window used to be
    /// missed by two of the three paths that change the language; the symbols palette and the traffic
    /// light board never learned the direction at all.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class LiveLanguageTests
    {
        private static readonly string Source = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "CyrFlip"));

        private static string Endonym(string code) => Localization.Names[Array.IndexOf(Localization.Codes, code)];

        private static void OnSta(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "the UI thread of the test hung");
            if (failure != null) throw new Exception("on the STA thread", failure);
        }

        // ---- the gate over the context ----

        /// <summary>The text of a method's body, found by name; braces inside strings and comments do not count.</summary>
        internal static string MethodBody(string source, string name)
        {
            Match head = Regex.Match(source, @"\b(?:private|internal|public)\s+(?:static\s+)?[\w<>?\[\], ]+\s+" + Regex.Escape(name) + @"\s*\(");
            Assert.True(head.Success, "method " + name + " not found");
            int open = source.IndexOf('{', head.Index);
            Assert.True(open > 0);
            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                char c = source[i];
                if (c == '"')
                {
                    for (i++; i < source.Length && source[i] != '"'; i++) if (source[i] == '\\') i++;
                }
                else if (c == '\'')
                {
                    for (i++; i < source.Length && source[i] != '\''; i++) if (source[i] == '\\') i++;
                }
                else if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    while (i < source.Length && source[i] != '\n') i++;
                }
                else if (c == '{') depth++;
                else if (c == '}' && --depth == 0) return source.Substring(open, i - open + 1);
            }
            throw new InvalidOperationException("unbalanced braces in " + name);
        }

        private static List<FieldInfo> LongLivedWindowFields()
            => typeof(CyrFlipContext)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(f => typeof(Form).IsAssignableFrom(f.FieldType)
                    && f.FieldType != typeof(SettingsForm) // gets its language through the Reload callback
                    && f.FieldType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Any(m => m.Name == "ApplyLanguage"))
                .ToList();

        [Fact]
        public void EveryLongLivedWindowTheContextHoldsIsReachedByALanguageChange()
        {
            List<FieldInfo> windows = LongLivedWindowFields();
            // The discovery has to find the windows it claims to, or an empty list would pass.
            Assert.True(windows.Count >= 7, "found only " + windows.Count + " windows with an ApplyLanguage");
            Assert.Contains(windows, f => f.Name == "_clipboardHistorySearchWindow");

            string context = File.ReadAllText(Path.Combine(Source, "CyrFlipContext.cs"));
            // The three things every language change calls: the tray texts (which re-read the notes, the
            // palette, the board and the hover popup), the follower for the rest, and the launcher surfaces.
            string reached = MethodBody(context, "UpdateTrayTexts")
                + MethodBody(context, "FollowUiLanguageInWindows")
                + MethodBody(context, "RefreshLauncherSurfaces");

            var missed = windows.Where(f => !Regex.IsMatch(reached, @"\b" + Regex.Escape(f.Name) + @"\b")).Select(f => f.Name).ToList();
            Assert.True(missed.Count == 0,
                "A window the context keeps is not told when the UI language changes: " + string.Join(", ", missed));
        }

        [Fact]
        public void TheDropDownTheTourAndAnImportAllEndInTheSameFollower()
        {
            string context = File.ReadAllText(Path.Combine(Source, "CyrFlipContext.cs"));
            string transfer = File.ReadAllText(Path.Combine(Source, "CyrFlipContext.SettingsTransfer.cs"));

            string set = MethodBody(context, "SetUiLanguage");
            Assert.Contains("FollowUiLanguageInWindows()", set);
            Assert.Contains("UpdateTrayTexts()", set);
            Assert.Contains("RefreshLauncherSurfaces()", set);

            string import = MethodBody(transfer, "RefreshImportedSettings");
            Assert.Contains("FollowUiLanguageInWindows()", import);
            Assert.Contains("UpdateTrayTexts()", import);

            // The tour changes the language through the same SetUiLanguage the drop-down uses.
            Assert.Contains("new WelcomeDialog(facts, SetUiLanguage,", context);
            Assert.Contains("SetHistoryOpacity, SetUiLanguage,", context);
        }

        // ---- the one implementation of direction and script font ----

        [Fact]
        public void DirectionAndFontFollowTheLanguageBothWays()
        {
            OnSta(() =>
            {
                using var form = new Form();
                Font? own = null;
                Font built = form.Font;

                // A language that needs nothing leaves the built font alone.
                Assert.False(WindowScript.Apply(form, "English", ref own));
                Assert.Same(built, form.Font);
                Assert.Equal(RightToLeft.No, form.RightToLeft);

                // Arabic: mirrored, no script font needed.
                Assert.False(WindowScript.Apply(form, Endonym("ar"), ref own));
                Assert.Equal(RightToLeft.Yes, form.RightToLeft);
                Assert.True(form.RightToLeftLayout);

                // Hindi: its own family (when the machine has it), left-to-right again.
                bool hasNirmala = FontFamily.Families.Any(f => f.Name == "Nirmala UI");
                bool replaced = WindowScript.Apply(form, Endonym("hi"), ref own);
                Assert.Equal(RightToLeft.No, form.RightToLeft);
                Assert.False(form.RightToLeftLayout);
                if (hasNirmala)
                {
                    Assert.True(replaced);
                    Assert.Equal("Nirmala UI", form.Font.FontFamily.Name);
                    Assert.Same(own, form.Font);
                }

                // Back to English: the family goes back too, or Nirmala UI stays for good.
                Font? hindi = own;
                WindowScript.Apply(form, "English", ref own);
                if (hasNirmala)
                {
                    Assert.NotEqual("Nirmala UI", form.Font.FontFamily.Name);
                    Assert.NotSame(hindi, own);
                }
                own?.Dispose();
            });
        }

        // ---- the windows themselves ----

        [Fact]
        public void TheSymbolsPaletteMirrorsAndComesBack()
        {
            OnSta(() =>
            {
                var config = new AppConfig { UiLanguage = "English", EnableSymbols = true };
                using var window = new SymbolsWindow(config, "English", (_, done) => done(true, 1));
                Assert.Equal(RightToLeft.No, window.RightToLeft);

                window.ApplyLanguage(Endonym("ar"));
                Assert.Equal(RightToLeft.Yes, window.RightToLeft);
                Assert.True(window.RightToLeftLayout);

                window.ApplyLanguage("English");
                Assert.Equal(RightToLeft.No, window.RightToLeft);
                Assert.False(window.RightToLeftLayout);
            });
        }

        [Fact]
        public void ThePaletteBuiltInArabicIsMirroredFromTheStart()
        {
            OnSta(() =>
            {
                var config = new AppConfig { UiLanguage = Endonym("ur") };
                using var window = new SymbolsWindow(config, Endonym("ur"), (_, done) => done(true, 1));
                Assert.Equal(RightToLeft.Yes, window.RightToLeft);
            });
        }

        [Fact]
        public void TheTrafficLightBoardMirrorsAndComesBack()
        {
            OnSta(() =>
            {
                var config = new AppConfig { UiLanguage = "English", EnableTrafficLight = true };
                using var window = new TrafficLightWindow(config, new TrafficLightBoardActions(), "English");
                Assert.Equal(RightToLeft.No, window.RightToLeft);

                window.ApplyLanguage(Endonym("ar"));
                Assert.Equal(RightToLeft.Yes, window.RightToLeft);
                // The board paints from its own ambient direction: it follows the window.
                Assert.Equal(RightToLeft.Yes, window.Board.RightToLeft);

                window.ApplyLanguage("English");
                Assert.Equal(RightToLeft.No, window.RightToLeft);
                Assert.Equal(RightToLeft.No, window.Board.RightToLeft);
            });
        }

        [Fact]
        public void TheHistorySearchWindowFollowsALiveChange()
        {
            OnSta(() =>
            {
                string folder = Path.Combine(Path.GetTempPath(), "cyrflip-test-" + Guid.NewGuid().ToString("N"));
                using var service = new ClipboardHistoryService(enabled: false, paused: false, folder);
                using var window = new ClipboardHistorySearchWindow(service, "English");
                Assert.Equal(RightToLeft.No, window.RightToLeft);
                string english = window.Text;

                window.ApplyLanguage(Endonym("ar"));
                Assert.Equal(RightToLeft.Yes, window.RightToLeft);
                Assert.NotEqual(english, window.Text);

                window.ApplyLanguage("English");
                Assert.Equal(RightToLeft.No, window.RightToLeft);
                Assert.Equal(english, window.Text);
            });
        }
    }
}

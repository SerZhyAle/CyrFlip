using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The About tab's version line. It is filled by <c>ApplyLanguage</c> rather than built as a
    /// static caption (a caption carrying the version number would be captured as a Russian string
    /// and "translated" on every language change), and a label nobody fills is simply blank - which
    /// looks exactly like a tab that never had a version on it. Hence this guard.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]   // builds the real window - see SharedGdiCollection
    public class AboutVersionTests
    {
        [Fact]
        public void The_about_tab_shows_the_build_version()
        {
            string text = "";
            string version = SupportBundle.AppVersion();
            var thread = new Thread(() => text = VersionLabelText("Русский"));
            thread.SetApartmentState(ApartmentState.STA);   // WinForms
            thread.Start();
            thread.Join();

            Assert.False(string.IsNullOrWhiteSpace(text), "the version label is blank");
            Assert.Contains(version, text);
            // The stamp is a date (YY.M.D.HHmm), so a build that could not read its own version -
            // "unknown" - is a failure rather than a cosmetic detail.
            Assert.NotEqual("unknown", version);
        }

        [Fact]
        public void The_version_line_is_translated_with_the_rest_of_the_window()
        {
            string russian = "";
            string english = "";
            var thread = new Thread(() =>
            {
                russian = VersionLabelText("Русский");
                english = VersionLabelText("English");
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.True(russian.StartsWith("Версия ", StringComparison.Ordinal), "russian was: '" + russian + "'");
            Assert.True(english.StartsWith("Version ", StringComparison.Ordinal), "english was: '" + english + "'");
            // Same build either way - only the word around the number is translated.
            Assert.Contains(SupportBundle.AppVersion(), russian);
            Assert.Contains(SupportBundle.AppVersion(), english);
        }

        /// <summary>Builds the real settings window in one language and reads its version label.</summary>
        private static string VersionLabelText(string language)
        {
            Type type = typeof(AppConfig).Assembly.GetType("CyrFlip.SettingsForm", true)!;
            var config = new AppConfig { UiLanguage = language };
            Action<bool> b = _ => { };
            Action noop = () => { };
            Action<int> i = _ => { };
            Action<string> s = _ => { };
            var launcherStore = new LauncherScenarioStore(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "CyrFlipTests", Guid.NewGuid().ToString("N")));
            object form = Activator.CreateInstance(type, new object[]
            {
                config, b, b, b, b, b, b, b, b, b, i, s, noop, noop, noop, noop, noop, b, b, b, b, b, b,
                launcherStore, b,
            })!;
            try
            {
                object label = type.GetField("_version", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(form)!;
                string text = (string)label.GetType().GetProperty("Text")!.GetValue(label)!;

                // It also has to be *in* the window, not merely constructed: a label left out of the
                // control tree would carry the right text and show nothing.
                Assert.True(IsInTree(form, label), "the version label is not part of the settings window");
                return text;
            }
            finally { (form as IDisposable)?.Dispose(); }
        }

        private static bool IsInTree(object control, object needle)
        {
            if (ReferenceEquals(control, needle)) return true;
            if (control.GetType().GetProperty("Controls")?.GetValue(control) is IEnumerable children)
                foreach (object child in children)
                    if (IsInTree(child, needle)) return true;
            return false;
        }
    }
}

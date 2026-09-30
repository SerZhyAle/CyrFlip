using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Ticket S0007 WL-3: every settings handler that writes Windows state takes the one-time backup
    /// first. Read off the source, method by method - a handler added later without the call fails
    /// here rather than on a user's machine the first time they want to put Windows back.
    /// </summary>
    public class SettingsBackupInventoryTests
    {
        private static readonly string Source = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "CyrFlip", "SettingsForm.cs"));

        /// <summary>The calls that change Windows' own input settings.</summary>
        private static readonly Regex Writer = new Regex(
            @"\b(InputLayouts\.(Add|Remove|Move|MakeDefault|Persist)|LanguageHotkeys\.(Assign|Clear|Remove|SetToggle))\(");

        private static readonly Regex MethodStart = new Regex(@"^        (private|public|internal|protected)[^=;]*\(", RegexOptions.Multiline);

        [Fact]
        public void EveryHandlerThatWritesWindowsStateBacksUpFirst()
        {
            Assert.True(File.Exists(Source), "missing " + Source);
            string text = File.ReadAllText(Source);

            var starts = new List<int>();
            foreach (Match m in MethodStart.Matches(text)) starts.Add(m.Index);
            starts.Add(text.Length);

            var problems = new List<string>();
            int writers = 0;
            for (int i = 0; i + 1 < starts.Count; i++)
            {
                string body = text.Substring(starts[i], starts[i + 1] - starts[i]);
                Match write = Writer.Match(body);
                if (!write.Success) continue;
                writers++;
                int backup = body.IndexOf("EnsureSystemBackups()", StringComparison.Ordinal);
                if (backup < 0 || backup > write.Index)
                    problems.Add(body.Substring(0, body.IndexOf('\n')).Trim() + " -> " + write.Value);
            }

            Assert.True(problems.Count == 0, "Writes Windows state without a backup first:\n" + string.Join("\n", problems));
            // Add, Remove, Move, MakeDefault, popular set, toggle, assign, clear, orphan remove.
            Assert.True(writers >= 9, "Only " + writers + " writing handlers were found - the scan is looking at the wrong thing");
        }
    }

    /// <summary>Ticket S0007 ST-4 and CF-3, on the real settings window.</summary>
    [Collection(SharedGdiCollection.Name)]
    public class SettingsFormStateTests
    {
        private static Form Build(AppConfig config) => TestForms.NewSettings(config);

        private static T Field<T>(Form form, string name)
            => (T)form.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;

        private static void OnUiThread(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() => { try { body(); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException(failure.ToString());
        }

        [Fact]
        public void TheOpacityLabelKeepsTheSliderValueAcrossARetranslation()
        {
            OnUiThread(() =>
            {
                using Form form = Build(new AppConfig { UiLanguage = "English", EnableClipboardHistory = true });
                var slider = Field<TrackBar>(form, "_opacity");
                var label = Field<Label>(form, "_opacityValue");

                slider.Value = 55;
                form.GetType().GetMethod("ApplyLanguage", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null);

                Assert.Equal("55%", label.Text);
            });
        }

        [Fact]
        public void AnUnknownUiLanguageShowsTheLanguageTheUiFallsBackTo()
        {
            OnUiThread(() =>
            {
                using Form form = Build(new AppConfig { UiLanguage = "Klingon" });
                var picker = Field<ComboBox>(form, "_uiLanguage");

                Assert.Equal("English", picker.SelectedItem);
            });
        }
    }
}

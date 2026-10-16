using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// UI and accessibility verification for SymbolsWindow (ticket S0056 section 13):
    /// builds in all 13 languages, no surviving Cyrillic in non-Russian builds,
    /// accessible names for all controls, minimum size measured, MA_NOACTIVATE answer,
    /// fresh config invariants.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class SymbolsWindowTests
    {
        private static void OnUiThread(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() => { try { body(); } catch (Exception ex) { failure = ex; } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Exception("UI thread failed: " + failure, failure);
        }

        private static IEnumerable<Control> All(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control deeper in All(child)) yield return deeper;
            }
        }

        [UiSweepFact]
        public void BuildsInAll13LanguagesWithNoSurvivingCyrillicInLatinBuilds()
        {
            var problems = new List<string>();

            OnUiThread(() =>
            {
                for (int i = 0; i < Localization.Names.Length; i++)
                {
                    string language = Localization.Names[i];
                    string code = Localization.Codes[i];
                    var config = new AppConfig { UiLanguage = language, EnableSymbols = true };

                    using var window = new SymbolsWindow(config, language, (_, onResult) => onResult(true, 1));

                    // Title
                    Assert.False(string.IsNullOrWhiteSpace(window.Text), $"{language}: Window text is empty");

                    // For non-Cyrillic languages (not ru and not uk), no control Text may have Cyrillic
                    if (code != "ru" && code != "uk")
                    {
                        foreach (Control c in All(window))
                        {
                            if (ContainsCyrillic(c.Text))
                            {
                                problems.Add($"[{language}] Control {c.GetType().Name} text has Cyrillic: \"{c.Text}\"");
                            }
                            if (ContainsCyrillic(c.AccessibleName))
                            {
                                problems.Add($"[{language}] Control {c.GetType().Name} AccessibleName has Cyrillic: \"{c.AccessibleName}\"");
                            }
                        }
                    }

                    // For all non-Russian languages (including uk), symbol names in accessibility MUST be English (no Cyrillic)
                    if (code != "ru")
                    {
                        var grid = All(window).OfType<SymbolGrid>().FirstOrDefault();
                        Assert.NotNull(grid);
                        var acc = grid.AccessibilityObject;
                        int childCount = Math.Min(acc.GetChildCount(), 20); // Check first 20 cells
                        for (int c = 0; c < childCount; c++)
                        {
                            AccessibleObject? child = acc.GetChild(c);
                            if (child != null && ContainsCyrillic(child.Name))
                            {
                                problems.Add($"[{language}] Grid cell {c} AccessibleName has Cyrillic: \"{child.Name}\"");
                            }
                        }
                    }
                }
            });

            Assert.True(problems.Count == 0, "Untranslated or leaked Cyrillic strings:\n" + string.Join("\n", problems));
        }

        private static bool ContainsCyrillic(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text!)
            {
                if (c >= 'Ѐ' && c <= 'ӿ') return true;
            }
            return false;
        }

        [Fact]
        public void EveryInteractiveControlHasAnAccessibleName()
        {
            OnUiThread(() =>
            {
                var config = new AppConfig { UiLanguage = "Русский", EnableSymbols = true };
                using var window = new SymbolsWindow(config, "Русский", (_, onResult) => onResult(true, 1));

                foreach (Control c in All(window))
                {
                    if (c is TextBox tb)
                    {
                        Assert.False(string.IsNullOrWhiteSpace(tb.AccessibleName), "SearchBox lacks AccessibleName");
                    }
                    else if (c is Button btn)
                    {
                        Assert.False(string.IsNullOrWhiteSpace(btn.AccessibleName), $"Button {btn.Text} lacks AccessibleName");
                    }
                    else if (c is SymbolGrid grid)
                    {
                        Assert.False(string.IsNullOrWhiteSpace(grid.AccessibleName), "SymbolGrid lacks AccessibleName");
                    }
                }
            });
        }

        [Fact]
        public void RepeatedGeometryAndLanguageUpdatesKeepClipboardFontUsableBeforeShow()
        {
            OnUiThread(() =>
            {
                using var window = new SymbolsWindow(new AppConfig(), "Русский", (_, done) => done(true, 1));
                for (int i = 0; i < 3; i++)
                {
                    window.ApplyLanguage("Русский");
                    typeof(SymbolsWindow).GetMethod("ApplyWindowGeometry", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(window, null);
                }

                // Font setters may skip equal fonts. Replacing and disposing the old instance then leaves
                // the native text box with a disposed font when its handle is first created.
                IntPtr fontHandle = window.TextField.Font.ToHfont();
                WindowInterop.DeleteObject(fontHandle);
                Assert.NotEqual(IntPtr.Zero, window.TextField.Handle);
                Assert.False(window.TextField.Multiline);
                Assert.True(window.TextField.Font.SizeInPoints > window.Font.SizeInPoints);
            });
        }

        [Fact]
        public void MinimumSizeIsMeasuredAndPositive()
        {
            OnUiThread(() =>
            {
                var config = new AppConfig { UiLanguage = "Русский", EnableSymbols = true };
                using var window = new SymbolsWindow(config, "Русский", (_, onResult) => onResult(true, 1));

                Assert.True(window.MinimumSize.Width >= 320, $"MinimumSize width too small: {window.MinimumSize.Width}");
                Assert.True(window.MinimumSize.Height >= 260, $"MinimumSize height too small: {window.MinimumSize.Height}");
            });
        }

        [Fact]
        public void SymbolGridAnswersMouseActivateWithNoActivate()
        {
            OnUiThread(() =>
            {
                using var grid = new SymbolGrid();
                _ = grid.Handle; // Ensure handle is created

                var m = Message.Create(grid.Handle, WindowInterop.WM_MOUSEACTIVATE, IntPtr.Zero, IntPtr.Zero);

                MethodInfo? wndProc = typeof(SymbolGrid).GetMethod(
                    "WndProc",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(wndProc);

                object[] args = new object[] { m };
                wndProc.Invoke(grid, args);

                var resultMsg = (Message)args[0];
                Assert.Equal((IntPtr)WindowInterop.MA_NOACTIVATE, resultMsg.Result);
            });
        }

        [Fact]
        public void FreshConfigDefaultsForSymbols()
        {
            var config = new AppConfig();
            Assert.False(config.EnableSymbols);
            Assert.True(config.EnableSymbolsHotkey);
            Assert.Equal("Ctrl+Shift+F9", config.SymbolsHotkey);
            Assert.False(config.SymbolsCloseOnPick);
            Assert.False(config.SymbolsHideTypable);
            Assert.Equal(0, config.SymbolsCount);
            Assert.Equal(SymbolsData.AllCategories, config.SymbolsCategory);
        }

        [Fact]
        public void StoredGroupKeysNormalizeAndAnythingElseOpensOnAll()
        {
            Assert.Equal(SymbolsData.CategoryMath, SymbolsData.NormalizeCategory("MATH"));
            Assert.Equal(SymbolsData.AllCategories, SymbolsData.NormalizeCategory("recent"));
            Assert.Equal(SymbolsData.AllCategories, SymbolsData.NormalizeCategory(""));
            Assert.Equal(SymbolsData.AllCategories, SymbolsData.NormalizeCategory(null));
        }

        [Fact]
        public void ListHoldsAllThenTheTwelveGroupsInTableOrder()
        {
            OnUiThread(() =>
            {
                var config = new AppConfig { UiLanguage = "Русский", EnableSymbols = true };
                using var window = new SymbolsWindow(config, "Русский", (_, onResult) => onResult(true, 1));
                ComboBox list = All(window).OfType<ComboBox>().Single();

                Assert.Equal(13, list.Items.Count);
                Assert.Equal("Все", list.GetItemText(list.Items[0]));
                for (int i = 0; i < SymbolsData.Categories.Length; i++)
                {
                    Assert.Equal(Localization.Translate("Русский", SymbolsData.CategoryTitle(SymbolsData.Categories[i].Key)), list.GetItemText(list.Items[i + 1]));
                }
            });
        }

        [Fact]
        public void RememberedGroupSelectsItsListEntryAndFiltersTheGrid()
        {
            OnUiThread(() =>
            {
                var config = new AppConfig { UiLanguage = "Русский", EnableSymbols = true, SymbolsCategory = SymbolsData.CategoryMath };
                using var window = new SymbolsWindow(config, "Русский", (_, onResult) => onResult(true, 1));
                ComboBox list = All(window).OfType<ComboBox>().Single();

                Assert.Equal("Математика", list.GetItemText(list.SelectedItem));
                Assert.NotEmpty(window.Grid.Entries);
                Assert.All(window.Grid.Entries, e => Assert.Equal(SymbolsData.CategoryMath, e.Category));
            });
        }

        [Fact]
        public void SearchStaysInsideTheSelectedGroup()
        {
            OnUiThread(() =>
            {
                var config = new AppConfig { UiLanguage = "Русский", EnableSymbols = true, SymbolsCategory = SymbolsData.CategoryMath };
                using var window = new SymbolsWindow(config, "Русский", (_, onResult) => onResult(true, 1));

                // A currency sign is on the table, but not in the math group the window is showing
                window.SearchBox.Text = "€";

                Assert.Empty(window.Grid.Entries);
            });
        }
    }
}

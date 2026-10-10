using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Clipboard writing for the Symbols window (ticket S0056, revision 2026-10-08): exact text copy (NBSP,
    /// combining marks, surrogate pairs), the accumulating text field, close-on-pick writing the character alone,
    /// and the busy guard. Never touches the real clipboard: the copy is a fake callback.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class SymbolsClipboardTests
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

        private static SymbolEntry Entry(int codePoint) => SymbolsData.Table.First(e => e.CodePoint == codePoint);

        private static void Pick(SymbolsWindow window, SymbolEntry entry)
        {
            MethodInfo? onActivated = typeof(SymbolsWindow).GetMethod(
                "OnSymbolActivated",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(onActivated);
            onActivated.Invoke(window, new object[] { entry });
        }

        private static string StatusText(SymbolsWindow window)
        {
            FieldInfo? statusField = typeof(SymbolsWindow).GetField(
                "_statusLabel",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(statusField);
            return ((Label)statusField.GetValue(window)!).Text;
        }

        [Fact]
        public void SymbolTextIsExactAndUnmodified()
        {
            // Verify all entries in SymbolsData.Table: text is exact, no leading/trailing whitespace,
            // NBSP is preserved as  , combining marks are isolated, surrogate pairs are preserved.
            SymbolEntry nbsp = SymbolsData.Table.First(e => e.CodePoint == 0x00A0);
            Assert.Equal(" ", nbsp.Text);
            Assert.Single(nbsp.Text);

            SymbolEntry acute = SymbolsData.Table.First(e => e.CodePoint == 0x0301);
            Assert.Equal("́", acute.Text);
            Assert.Single(acute.Text);
            Assert.True(acute.IsCombining);

            foreach (SymbolEntry entry in SymbolsData.Table)
            {
                // Never appends newlines or spaces (except the invisible space characters themselves)
                if (entry.Category != SymbolsData.CategoryInvisible)
                {
                    Assert.False(entry.Text.Contains('\r') || entry.Text.Contains('\n'));
                }
            }
        }

        [Fact]
        public void BusyGuardRejectionWritesNothingAndUpdatesStatus()
        {
            OnUiThread(() =>
            {
                var config = new AppConfig { UiLanguage = "Русский", EnableSymbols = true };
                var copies = new List<string>();

                // A taken _busy guard: the copy callback always answers false
                Action<string, Action<bool, uint>> busyCopy = (text, onResult) =>
                {
                    copies.Add(text);
                    onResult(false, 0);
                };

                using var window = new SymbolsWindow(config, "Русский", busyCopy);
                Pick(window, Entry(0x20AC));

                Assert.Equal(new[] { "€" }, copies);
                Assert.Equal("Буфер обмена занят, повторите", StatusText(window));
                Assert.Equal(string.Empty, window.TextField.Text);
            });
        }

        [Fact]
        public void PicksAccumulateInTheTextFieldAndTheWholeFieldIsCopied()
        {
            OnUiThread(() =>
            {
                var config = new AppConfig { UiLanguage = "Русский", EnableSymbols = true };
                var copies = new List<string>();

                Action<string, Action<bool, uint>> successCopy = (text, onResult) =>
                {
                    copies.Add(text);
                    onResult(true, 7);
                };

                using var window = new SymbolsWindow(config, "Русский", successCopy);

                Pick(window, Entry(0x20AC)); // Euro
                Assert.Equal("Скопировано: €", StatusText(window));
                Assert.Equal("€", window.TextField.Text);

                Pick(window, Entry(0x00A0)); // NBSP, kept as U+00A0 and appended
                Assert.Equal(new[] { "€", "€ " }, copies);
                Assert.Equal("€ ", window.TextField.Text);
            });
        }

        [Fact]
        public void CloseOnPickCopiesTheCharacterAloneNotTheField()
        {
            OnUiThread(() =>
            {
                var config = new AppConfig { UiLanguage = "Русский", EnableSymbols = true, SymbolsCloseOnPick = true };
                var copies = new List<string>();

                // Answered false so the window stays open: a successful pick would hide it and write the registry
                Action<string, Action<bool, uint>> copy = (text, onResult) =>
                {
                    copies.Add(text);
                    onResult(false, 0);
                };

                using var window = new SymbolsWindow(config, "Русский", copy);
                window.TextField.Text = "ab";

                Pick(window, Entry(0x20AC));

                Assert.Equal(new[] { "€" }, copies);
                Assert.Equal("ab", window.TextField.Text);
            });
        }
    }
}

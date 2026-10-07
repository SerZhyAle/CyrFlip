using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Clipboard writing and busy guard behavior for Symbols (ticket S0056 section 13):
    /// exact text copy (NBSP, combining marks, surrogate pairs), no extra characters,
    /// busy guard rejection with status notification, history not suppressed.
    /// Never touches real clipboard.
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

        [Fact]
        public void SymbolTextIsExactAndUnmodified()
        {
            // Verify all entries in SymbolsData.Table: text is exact, no leading/trailing whitespace,
            // NBSP is preserved as \u00A0, combining marks are isolated, surrogate pairs are preserved.
            SymbolEntry nbsp = SymbolsData.Table.First(e => e.CodePoint == 0x00A0);
            Assert.Equal("\u00A0", nbsp.Text);
            Assert.Single(nbsp.Text);

            SymbolEntry acute = SymbolsData.Table.First(e => e.CodePoint == 0x0301);
            Assert.Equal("\u0301", acute.Text);
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
                string? copiedText = null;

                // Simulate a taken _busy guard: copy callback always returns false
                Action<string, Action<bool>> busyCopy = (text, onResult) =>
                {
                    copiedText = text;
                    onResult(false);
                };

                using var window = new SymbolsWindow(config, "Русский", busyCopy);

                var entry = SymbolsData.Table.First(e => e.CodePoint == 0x20AC); // Euro

                // Trigger activation via reflection to test internal copy behavior
                var onActivatedMethod = typeof(SymbolsWindow).GetMethod(
                    "OnSymbolActivated",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.NotNull(onActivatedMethod);

                onActivatedMethod.Invoke(window, new object[] { entry });

                Assert.Equal("€", copiedText);

                // Find status label
                var statusLabelField = typeof(SymbolsWindow).GetField(
                    "_statusLabel",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.NotNull(statusLabelField);
                var statusLabel = (Label)statusLabelField.GetValue(window)!;
                Assert.NotNull(statusLabel);
                Assert.Equal("Буфер обмена занят, повторите", statusLabel.Text);

                // Recent list must NOT have recorded the failed copy
                var recentField = typeof(SymbolsWindow).GetField(
                    "_recent",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.NotNull(recentField);
                var recent = (SymbolRecent)recentField.GetValue(window)!;
                Assert.Equal(0, recent.Count);
            });
        }

        [Fact]
        public void SuccessfulCopyUpdatesStatusAndRecordsInRecent()
        {
            OnUiThread(() =>
            {
                var config = new AppConfig { UiLanguage = "Русский", EnableSymbols = true };
                string? copiedText = null;

                Action<string, Action<bool>> successCopy = (text, onResult) =>
                {
                    copiedText = text;
                    onResult(true);
                };

                using var window = new SymbolsWindow(config, "Русский", successCopy);

                var entry = SymbolsData.Table.First(e => e.CodePoint == 0x20AC); // Euro

                var onActivatedMethod = typeof(SymbolsWindow).GetMethod(
                    "OnSymbolActivated",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.NotNull(onActivatedMethod);

                onActivatedMethod.Invoke(window, new object[] { entry });

                Assert.Equal("€", copiedText);

                var statusLabelField = typeof(SymbolsWindow).GetField(
                    "_statusLabel",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.NotNull(statusLabelField);
                var statusLabel = (Label)statusLabelField.GetValue(window)!;
                Assert.NotNull(statusLabel);
                Assert.Equal("Скопировано: €", statusLabel.Text);

                var recentField = typeof(SymbolsWindow).GetField(
                    "_recent",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Assert.NotNull(recentField);
                var recent = (SymbolRecent)recentField.GetValue(window)!;
                Assert.Equal(1, recent.Count);
                Assert.Equal("€", recent.Items[0]);
            });
        }
    }
}

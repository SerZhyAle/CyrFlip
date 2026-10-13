using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace CyrFlip.Tests
{
    [Collection(SharedGdiCollection.Name)]
    public class ClipboardHistoryWindowTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [Fact]
        public void KeyboardReachesTheEntireHistoryAndKeepsTheSelectionVisible() => WithWindow((window, service) =>
        {
            int page = Property(window, "PageSize");
            for (int i = 0; i <= page; i++) Key(window, Keys.Down);
            Assert.Equal(page, Field(window, "_cursor"));
            Assert.Equal(1, Field(window, "_firstVisible"));
            Assert.Equal(service.Entries[1].Preview, window.AccessibilityObject.GetChild(2).Description);

            Key(window, Keys.End);
            Assert.Equal(service.Entries.Count - 1, Field(window, "_cursor"));
            Assert.Equal(service.Entries.Count - page, Field(window, "_firstVisible"));
            string selected = service.Entries.Last().Uuid;
            Key(window, Keys.Delete);
            Assert.DoesNotContain(service.Entries, e => e.Uuid == selected);
            Assert.Equal(service.Entries.Count - 1, Field(window, "_cursor"));
            Key(window, Keys.Up);
            Assert.Equal(service.Entries.Count - 2, Field(window, "_cursor"));
            Key(window, Keys.Home);
            Assert.Equal(0, Field(window, "_firstVisible"));
        });

        [Fact]
        public void WheelScrollsAndMouseAndAccessibilityUseTheScrolledRows() => WithWindow((window, service) =>
        {
            if (SystemInformation.MouseWheelScrollLines == 0) return;
            Wheel(window, -60);
            Assert.Equal(0, Field(window, "_firstVisible"));
            Wheel(window, -60);
            int first = Field(window, "_firstVisible");
            Assert.True(first > 0);
            Assert.Equal(service.Entries[first].Preview, window.AccessibilityObject.GetChild(2).Description);
            ClipboardHistoryEntry item = service.Entries[first];
            int header = Property(window, "HeaderHeight");
            typeof(ClipboardHistoryWindow).GetMethod("OnMouseDown", Private, null, new[] { typeof(object), typeof(MouseEventArgs) }, null)!.Invoke(window,
                new object?[] { window, new MouseEventArgs(MouseButtons.Right, 1, window.Width / 2, header + 10, 0) });
            Assert.True(item.IsPinned);

            Wheel(window, -12000);
            Assert.Equal(service.Entries.Count - Property(window, "PageSize"), Field(window, "_firstVisible"));
            window.Height += Property(window, "CellHeight");
            Assert.Equal(service.Entries.Count - Property(window, "PageSize"), Field(window, "_firstVisible"));
            Wheel(window, 12000);
            Assert.Equal(0, Field(window, "_firstVisible"));
        });

        [Fact]
        public void DpiChangeUsesWindowsSuggestedBoundsAndScalesThePaintedSurface() => WithWindow((window, service) =>
        {
            // The OS never sizes a window past MaxWindowTrackSize (screen + borders): a rect
            // written for a tall monitor bottoms out there on a short one. Ask for the largest
            // rect this session accepts; the assertion is that the window took the suggestion.
            int suggestedBottom = Math.Min(1254, SystemInformation.MaxWindowTrackSize.Height);
            var bounds = new WindowInterop.RECT { Left = 100, Top = 100, Right = 616, Bottom = suggestedBottom };
            IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf(bounds));
            try
            {
                Marshal.StructureToPtr(bounds, pointer, false);
                Message message = Message.Create(window.Handle, 0x02E0, (IntPtr)((192 << 16) | 192), pointer);
                typeof(ClipboardHistoryWindow).GetMethod("WndProc", Private)!.Invoke(window, new object[] { message });
                Assert.Equal(new Rectangle(100, 100, 516, suggestedBottom - 100), window.Bounds);
                Assert.Equal(168, Property(window, "CellHeight"));
                Assert.Equal(72, Property(window, "HeaderHeight"));
                Key(window, Keys.End);
                Assert.Equal(service.Entries.Count - Property(window, "PageSize"), Field(window, "_firstVisible"));
            }
            finally { Marshal.FreeHGlobal(pointer); }
        });

        [Theory]
        [InlineData(96)]
        [InlineData(144)]
        [InlineData(192)]
        public void TextStaysInsideItsColumnAndFootersStayInsideRowsAtEachDpi(int dpi) => WithWindow((window, service) =>
        {
            // Exercise the same metrics reset used on a monitor DPI change.
            typeof(ClipboardHistoryWindow).GetMethod("SetDpi", Private)!.Invoke(window, new object[] { dpi });
            window.Size = new Size(258 * dpi / 96, 577 * dpi / 96);
            int header = Property(window, "HeaderHeight");
            int row = Property(window, "CellHeight");
            int button = Property(window, "CellButtonWidth");
            Assert.Equal(84 * dpi / 96, row);
            using var bitmap = new Bitmap(window.Width, window.Height);
            bitmap.SetResolution(dpi, dpi);
            using (Graphics graphics = Graphics.FromImage(bitmap))
                typeof(ClipboardHistoryWindow).GetMethod("OnPaint", Private, null,
                    new[] { typeof(object), typeof(PaintEventArgs) }, null)!.Invoke(window,
                    new object[] { window, new PaintEventArgs(graphics, window.ClientRectangle) });
            ThemePalette palette = ThemeApply.PaletteOf(window) ?? ThemePalette.Light;
            // Long preview text must never appear in the space reserved for row actions.
            for (int y = header; y < header + row; y++)
                for (int x = window.Width - button; x < window.Width - button + 4 * dpi / 96; x++)
                    Assert.NotEqual(palette.TextMuted.ToArgb(), bitmap.GetPixel(x, y).ToArgb());
            int footerPixels = 0;
            for (int y = header + row - 22 * dpi / 96; y < header + row; y++)
                for (int x = 0; x < window.Width - button; x++)
                    if (bitmap.GetPixel(x, y).ToArgb() == palette.Info.ToArgb()) footerPixels++;
            Assert.True(footerPixels > 0, "timestamp should be visible above the row boundary");
            // Used for local visual verification, only when explicitly requested by the test runner.
            string? output = Environment.GetEnvironmentVariable("CYRFLIP_HISTORY_PREVIEW_DIR");
            if (output != null)
            {
                Directory.CreateDirectory(output);
                bitmap.Save(Path.Combine(output, "clipboard-history-" + dpi + ".png"));
            }
        });

        private static int Property(ClipboardHistoryWindow window, string name)
            => (int)typeof(ClipboardHistoryWindow).GetProperty(name, Private)!.GetValue(window)!;
        private static int Field(ClipboardHistoryWindow window, string name)
            => (int)typeof(ClipboardHistoryWindow).GetField(name, Private)!.GetValue(window)!;
        private static void Key(ClipboardHistoryWindow window, Keys key)
        {
            object[] args = { new Message(), key };
            Assert.True((bool)typeof(ClipboardHistoryWindow).GetMethod("ProcessCmdKey", Private)!.Invoke(window, args)!);
        }
        private static void Wheel(ClipboardHistoryWindow window, int delta)
            => typeof(ClipboardHistoryWindow).GetMethod("OnMouseWheel", Private)!.Invoke(window,
                new object[] { new MouseEventArgs(MouseButtons.None, 0, 100, 100, delta) });

        private static void WithWindow(Action<ClipboardHistoryWindow, ClipboardHistoryService> body)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "CyrFlipTests", Guid.NewGuid().ToString("N"));
                try
                {
                    using var service = new ClipboardHistoryService(false, false, dir);
                    service.Import(Enumerable.Range(0, 20).Select(i =>
                    {
                        string text = @"P:\WINDOWS\FileDo\PLAN\SP-0125 very long preview with more text " + i;
                        return new ExchangeClipboardItem { Uuid = ClipboardHistoryService.Hash(text), Text = text,
                            CopiedAtUtc = DateTime.UtcNow.AddMinutes(-i), SourceApp = "Code" };
                    }).ToArray());
                    using var window = new ClipboardHistoryWindow(service,
                        new AppConfig { UiLanguage = "Русский", ClipboardHistoryWidth = 258, ClipboardHistoryHeight = 577 }, () => { });
                    IntPtr handle = window.Handle;
                    body(window, service);
                }
                catch (Exception ex) { failure = ex; }
                finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Exception("UI-thread failure", failure);
        }
    }
}

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
            // S0089: a right click on the text edits the scrolled row's entry; it no longer pins.
            MouseDown(window, MouseButtons.Right, window.Width / 2, header + 10);
            Assert.Same(item, Editing(window));
            Assert.False(item.IsPinned);
            Key(window, Keys.Escape);
            Assert.Null(Editing(window));

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
                // S0075: the header band is 44 logical px so it holds the help button's hit target (Scale(44) at 192 DPI).
                Assert.Equal(88, Property(window, "HeaderHeight"));
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

        [Fact]
        public void TheCaptionNamesTheWindowClipboard() => WithWindow((window, service) =>
        {
            Assert.Equal(Localization.Translate("Русский", "Clipboard"), window.Text);
            Assert.Equal("Буфер обмена", window.Text);
        });

        [Fact]
        public void RightClickEditsTheWholeTextAndEscapeDiscards() => WithWindow((window, service) =>
        {
            ClipboardHistoryEntry item = service.Entries[0];
            string original = item.Text;
            MouseDown(window, MouseButtons.Right, Scale(window, 40), Property(window, "HeaderHeight") + 10);
            Assert.Same(item, Editing(window));
            Assert.Equal(original, Editor(window).Text);   // the stored text, not the painted preview
            Assert.Equal(ClipboardHistoryService.MaxTextChars, Editor(window).MaxLength);

            Editor(window).Text = "discarded";
            Key(window, Keys.Escape);

            Assert.Null(Editing(window));
            Assert.Same(item, service.Entries[0]);
            Assert.Equal(original, item.Text);
        });

        [Fact]
        public void EnterSavesTheEditAndEveryOtherKeyIsTheEditors() => WithWindow((window, service) =>
        {
            int count = service.Entries.Count;
            ClipboardHistoryEntry item = service.Entries[0];
            Key(window, Keys.Down);
            Key(window, Keys.F2);
            Assert.Same(item, Editing(window));

            // While editing, Delete is the editor's key: the entry stays.
            object[] args = { new Message(), Keys.Delete };
            Assert.False((bool)typeof(ClipboardHistoryWindow).GetMethod("ProcessCmdKey", Private)!.Invoke(window, args)!);
            Assert.Contains(item, service.Entries);

            Editor(window).Text = "corrected text";
            Key(window, Keys.Enter);

            Assert.Null(Editing(window));
            Assert.Equal(count, service.Entries.Count);
            Assert.DoesNotContain(item, service.Entries);
            ClipboardHistoryEntry edited = service.Entries.Single(e => e.Text == "corrected text");
            Assert.Equal(ClipboardHistoryService.Hash("corrected text"), edited.Uuid);
            Assert.Equal(item.CreatedAt, edited.CreatedAt);
        });

        [Fact]
        public void AClickOutsideTheEditorSavesAndDoesNothingElse() => WithWindow((window, service) =>
        {
            int header = Property(window, "HeaderHeight"), row = Property(window, "CellHeight");
            ClipboardHistoryEntry item = service.Entries[0];
            ClipboardHistoryEntry bystander = service.Entries[2];
            MouseDown(window, MouseButtons.Right, Scale(window, 40), header + 10);
            Editor(window).Text = "saved by the click";

            // A left click on row 2's delete glyph: without the edit it would delete that row.
            int x = window.ClientSize.Width - Property(window, "CellButtonWidth") / 2;
            int y = header + 2 * row + row - 6;
            Message click = Message.Create(window.Handle, 0x0201, IntPtr.Zero, (IntPtr)((y << 16) | (x & 0xffff)));
            typeof(ClipboardHistoryWindow).GetMethod("WndProc", Private)!.Invoke(window, new object[] { click });

            Assert.Null(Editing(window));
            Assert.Contains(bystander, service.Entries);
            Assert.DoesNotContain(item, service.Entries);
            Assert.Contains(service.Entries, e => e.Text == "saved by the click");
        });

        private static ClipboardHistoryEntry? Editing(ClipboardHistoryWindow window)
            => (ClipboardHistoryEntry?)typeof(ClipboardHistoryWindow).GetField("_editing", Private)!.GetValue(window);
        private static TextBox Editor(ClipboardHistoryWindow window)
            => (TextBox)typeof(ClipboardHistoryWindow).GetField("_editor", Private)!.GetValue(window)!;
        private static int Scale(ClipboardHistoryWindow window, int pixels)
            => (int)typeof(ClipboardHistoryWindow).GetMethod("Scale", Private, null, new[] { typeof(int) }, null)!.Invoke(window, new object[] { pixels })!;
        private static void MouseDown(ClipboardHistoryWindow window, MouseButtons button, int x, int y)
            => typeof(ClipboardHistoryWindow).GetMethod("OnMouseDown", Private, null, new[] { typeof(object), typeof(MouseEventArgs) }, null)!.Invoke(window,
                new object?[] { window, new MouseEventArgs(button, 1, x, y, 0) });

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

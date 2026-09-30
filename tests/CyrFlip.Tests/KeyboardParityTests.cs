using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using CyrFlip;
using Microsoft.Win32;
using Xunit;
using static CyrFlip.WindowInterop;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The keyboard routes ticket S0045 added for <c>INPUT-PARITY</c> rules 1-2 - the parts that are
    /// pure enough to hold without a desktop. The windows themselves still want a human at the keyboard.
    /// </summary>
    public class KeyboardParityTests
    {
        private const uint VkF6 = 0x75;
        private const uint VkLControl = 0xA2;

        private static KeyboardHook NewHook()
        {
            var hook = new KeyboardHook(_ => (IntPtr)1, _ => true, new PhysicalModifiers());
            hook.Install(Hotkey.CaseDefault, Hotkey.Parse("Ctrl+Shift+F9"), Hotkey.Parse(AppConfig.DefaultQuickNotesHotkey),
                deferInRemoteClient: false, enabled: true, caseEnabled: true, historyEnabled: true, quickNotesEnabled: false);
            return hook;
        }

        private static KBDLLHOOKSTRUCT Key(uint vk, uint time) => new KBDLLHOOKSTRUCT { vkCode = vk, time = time };

        // ---- K1: F6 into the translation popup ------------------------------------------------

        [Fact]
        public void F6IsTheUsersUntilThePopupIsWatchingForIt()
        {
            KeyboardHook hook = NewHook();
            int raised = 0;
            hook.FocusKeyPressed += (_, _) => raised++;

            Assert.False(hook.Decide(Key(VkF6, 10), down: true));
            Assert.False(hook.Decide(Key(VkF6, 20), down: false));
            Assert.Equal(0, raised);
        }

        [Fact]
        public void AWatchedF6IsSwallowedWithItsRepeatsAndItsRelease()
        {
            KeyboardHook hook = NewHook();
            int raised = 0;
            hook.FocusKeyPressed += (_, _) => raised++;
            hook.UpdateFocusKeyWatch(true);

            Assert.True(hook.Decide(Key(VkF6, 10), down: true));
            Assert.True(hook.Decide(Key(VkF6, 40), down: true));  // auto-repeat: no second toggle
            Assert.True(hook.Decide(Key(VkF6, 60), down: false));
            Assert.Equal(1, raised);

            Assert.True(hook.Decide(Key(VkF6, 100), down: true)); // the next press toggles back
            Assert.True(hook.Decide(Key(VkF6, 120), down: false));
            Assert.Equal(2, raised);
        }

        [Fact]
        public void CtrlF6KeepsItsMeaningInTheUsersWindow()
        {
            KeyboardHook hook = NewHook();
            int raised = 0;
            hook.FocusKeyPressed += (_, _) => raised++;
            hook.UpdateFocusKeyWatch(true);

            hook.Decide(Key(VkLControl, 10), down: true);
            Assert.False(hook.Decide(Key(VkF6, 20), down: true));
            Assert.False(hook.Decide(Key(VkF6, 30), down: false));
            Assert.Equal(0, raised);
        }

        [Fact]
        public void AReleaseThatNeverCameCostsAtMostTheRepeatWindow()
        {
            KeyboardHook hook = NewHook();
            hook.UpdateFocusKeyWatch(true);
            Assert.True(hook.Decide(Key(VkF6, 10), down: true)); // its up lost to an elevated window
            hook.UpdateFocusKeyWatch(false);

            // The chord matcher's lost-up rule (S0033 KC2-2): a press long after is a fresh one.
            Assert.False(hook.Decide(Key(VkF6, 10000), down: true));
            Assert.False(hook.Decide(Key(VkF6, 10020), down: false));
        }

        // ---- K2: the text menu's keyboard chord ------------------------------------------------

        [Fact]
        public void TheTextMenuChordFiresOnlyWhileBound()
        {
            KeyboardHook hook = NewHook();
            int raised = 0;
            hook.TextMenuHotkeyPressed += (_, _) => raised++;
            hook.UpdateTextMenuHotkey(Hotkey.Parse(AppConfig.DefaultTextMenuHotkey));
            const uint vkLShift = 0xA0, vkLMenu = 0xA4, vkM = 0x4D;

            hook.Decide(Key(VkLControl, 10), down: true);
            hook.Decide(Key(vkLShift, 20), down: true);
            hook.Decide(Key(vkLMenu, 25), down: true);
            Assert.False(hook.Decide(Key(vkM, 30), down: true)); // off until the context binds it
            hook.Decide(Key(vkM, 40), down: false);
            Assert.Equal(0, raised);

            hook.UpdateTextMenuEnabled(true);
            Assert.True(hook.Decide(Key(vkM, 50), down: true));
            Assert.True(hook.Decide(Key(vkM, 60), down: false));
            Assert.Equal(1, raised);
        }

        private sealed class MemoryKey : IConfigKey
        {
            public readonly Dictionary<string, object> Values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            public object? GetValue(string name) => Values.TryGetValue(name, out object? value) ? value : null;
            public void SetValue(string name, object value, RegistryValueKind kind) => Values[name] = value;
        }

        [Fact]
        public void TheTextMenuChordDefaultsOnAndRoundTrips()
        {
            var key = new MemoryKey();
            AppConfig cfg = AppConfig.LoadFrom(key, out _);
            Assert.Equal("Ctrl+Shift+Alt+M", cfg.TextMenuHotkey);
            Assert.True(cfg.EnableTextMenuHotkey);
            Assert.False(cfg.EnableContextMenu); // the chord is live only with the menu, which stays opt-in

            cfg.TextMenuHotkey = "Alt+Ctrl+Shift+K";
            cfg.EnableTextMenuHotkey = false;
            cfg.SaveTo(key);
            AppConfig back = AppConfig.LoadFrom(key, out _);
            Assert.Equal("Ctrl+Shift+Alt+K", back.TextMenuHotkey); // INPUT-CHORD rule 2: canonical on load
            Assert.False(back.EnableTextMenuHotkey);
        }

        [Fact]
        public void TheTextMenuChordIsOwnedInTheRegistry()
        {
            AppConfig cfg = AppConfig.LoadFrom(new MemoryKey(), out _);
            ChordRegistry registry = ChordRegistry.Build(cfg, new LauncherScenario[0], new LanguageHotkeys.Entry[0]);
            Hotkey chord = Hotkey.Parse(AppConfig.DefaultTextMenuHotkey);
            ChordOwner? owner = registry.CyrFlipOwnerOf(chord, ChordKind.Case);
            Assert.NotNull(owner);
            Assert.Equal(ChordKind.TextMenu, owner!.Kind);
            Assert.Null(registry.CyrFlipOwnerOf(chord, ChordKind.TextMenu));
            Assert.Equal(HotkeyRules.Refusal.None, HotkeyRules.Check(chord, new IntPtr[0], (_, _) => null).Refusal); // not an editing chord
        }

        // ---- K4: the region capture from the keyboard -----------------------------------------

        private static readonly Rectangle Monitor = new Rectangle(0, 0, 1920, 1080);
        private static readonly Rectangle Virtual = new Rectangle(-1280, 0, 3200, 1080);

        [Fact]
        public void AnArrowWithNoSelectionStartsAThirdOfTheMonitorInItsMiddle()
        {
            Rectangle start = RegionSelectionOverlay.Nudge(Rectangle.Empty, Keys.Right, Monitor, Virtual);
            Assert.Equal(new Rectangle(640, 360, 640, 360), start);
        }

        [Theory]
        [InlineData(Keys.Left, -10, 0)]
        [InlineData(Keys.Right, 10, 0)]
        [InlineData(Keys.Up, 0, -10)]
        [InlineData(Keys.Down, 0, 10)]
        [InlineData(Keys.Left | Keys.Control, -1, 0)]
        [InlineData(Keys.Down | Keys.Control, 0, 1)]
        public void AnArrowMovesTheSelection(Keys key, int dx, int dy)
        {
            var selection = new Rectangle(100, 100, 50, 40);
            Assert.Equal(new Rectangle(100 + dx, 100 + dy, 50, 40), RegionSelectionOverlay.Nudge(selection, key, Monitor, Virtual));
        }

        [Fact]
        public void ShiftArrowResizesFromTheRightAndBottomAndNeverBelowOnePixel()
        {
            var selection = new Rectangle(100, 100, 5, 5);
            Assert.Equal(new Rectangle(100, 100, 15, 5), RegionSelectionOverlay.Nudge(selection, Keys.Right | Keys.Shift, Monitor, Virtual));
            Assert.Equal(new Rectangle(100, 100, 5, 6), RegionSelectionOverlay.Nudge(selection, Keys.Down | Keys.Shift | Keys.Control, Monitor, Virtual));
            Assert.Equal(new Rectangle(100, 100, 1, 5), RegionSelectionOverlay.Nudge(selection, Keys.Left | Keys.Shift, Monitor, Virtual));
        }

        [Fact]
        public void TheSelectionStaysInsideTheVirtualScreen()
        {
            var atLeftEdge = new Rectangle(-1280, 0, 50, 40);
            Assert.Equal(atLeftEdge, RegionSelectionOverlay.Nudge(atLeftEdge, Keys.Left, Monitor, Virtual));
            Assert.Equal(atLeftEdge, RegionSelectionOverlay.Nudge(atLeftEdge, Keys.Up, Monitor, Virtual));
            var atRight = new Rectangle(1915, 1075, 5, 5);
            Assert.Equal(new Rectangle(1915, 1075, 5, 5), RegionSelectionOverlay.Nudge(atRight, Keys.Right | Keys.Shift, Monitor, Virtual));
            Assert.Equal(new Rectangle(1915, 1075, 5, 5), RegionSelectionOverlay.Nudge(atRight, Keys.Down, Monitor, Virtual));
        }

        // ---- K9: the launcher's taskbar menu ---------------------------------------------------

        private static readonly Rectangle Screen = new Rectangle(0, 0, 1920, 1080);

        [Fact]
        public void AClickOnTheTaskbarOpensTheMenuAtThePointer()
        {
            var working = new Rectangle(0, 0, 1920, 1032);
            var pointer = new Point(700, 1060);
            Assert.Equal(pointer, LauncherTaskbarWindow.MenuAnchor(pointer, Screen, working, Screen, working));
        }

        [Theory]
        [InlineData(0, 0, 1920, 1032, 960, 1031)]   // bottom taskbar
        [InlineData(0, 48, 1920, 1032, 960, 48)]    // top
        [InlineData(62, 0, 1858, 1080, 62, 540)]    // left
        [InlineData(0, 0, 1858, 1080, 1857, 540)]   // right
        [InlineData(0, 0, 1920, 1080, 960, 1079)]   // auto-hide: no band at all
        public void AKeyboardOpenPutsTheMenuOnTheTaskbarsInnerEdge(int x, int y, int w, int h, int ax, int ay)
        {
            var working = new Rectangle(x, y, w, h);
            Assert.Equal(new Point(ax, ay),
                LauncherTaskbarWindow.MenuAnchor(new Point(500, 500), Screen, working, Screen, working));
        }

        // ---- K6: the focus survives a row rebuild ----------------------------------------------

        [Fact]
        public void TheFocusGoesToTheSameColumnOrTheNearestFocusableOne()
        {
            using var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000) };
            var rows = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true };
            form.Controls.Add(rows);
            Button[] Row(bool upEnabled)
            {
                var row = new FlowLayoutPanel { AutoSize = true };
                var label = new Label { Text = "x" };
                var up = new Button { Text = "↑", Enabled = upEnabled };
                var down = new Button { Text = "↓" };
                row.Controls.Add(label); row.Controls.Add(up); row.Controls.Add(down);
                rows.Controls.Add(row);
                return new[] { up, down };
            }
            Button[] first = Row(upEnabled: false);
            Button[] second = Row(upEnabled: true);
            form.Show();

            Assert.Same(second[0], RowFocus.Find(rows, 1, 1));   // the same column
            Assert.Same(first[1], RowFocus.Find(rows, 0, 1));    // ↑ disabled on the top row: the next one
            Assert.Same(second[0], RowFocus.Find(rows, 5, 1));   // past the end: the last row
            Assert.Null(RowFocus.Find(new Panel(), 0, 0));
        }
    }
}

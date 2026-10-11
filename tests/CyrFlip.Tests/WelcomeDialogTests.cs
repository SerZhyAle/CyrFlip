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
    [Collection(SharedGdiCollection.Name)]
    public class WelcomeDialogTests
    {
        private static void Sta(Action action)
        {
            Exception? failure = null;
            var thread = new Thread(() => { try { Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException, true); action(); } catch (Exception e) { failure = e; } });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
            if (failure != null) throw failure;
        }

        private static IEnumerable<Control> Walk(Control root)
        {
            yield return root;
            foreach (Control child in root.Controls)
                foreach (Control nested in Walk(child)) yield return nested;
        }

        private static AppConfig NewConfig(string language)
        {
            var config = new AppConfig { UiLanguage = language };
            config.LayoutConversionProfiles.Add(AppConfig.FlipRow(null, true));
            return config;
        }

        private static WelcomeDialog New(string language, bool packaged = false)
            => New(NewConfig(language), packaged);

        private static WelcomeDialog New(AppConfig config, bool packaged = false, Action<WelcomeSwitch, string?, bool>? apply = null)
            => new WelcomeDialog(new WelcomeFacts(config, new[] { new InputLayouts.Installed { Klid = "00000409" } },
                packaged, true, true), _ => { }, _ => { }, () => false, apply);

        private static ToggleSwitch SwitchNamed(Control root, string name)
            => Assert.Single(Walk(root).OfType<ToggleSwitch>(), s => s.AccessibleName == name);

        private static void CheckCaptions(Control root)
        {
            foreach (var control in Walk(root).Where(c => c.Visible && (c is Label || c is Button || c is CheckBox)))
            {
                var wanted = control.GetPreferredSize(Size.Empty);
                Assert.True(control.Width >= wanted.Width && control.Height >= wanted.Height,
                    control.Text + ": needs " + wanted + ", got " + control.Size);
            }
        }

        [Fact]
        public void AllPagesAllLanguagesAllThemesKeepTheirSizeAndCaptions()
        {
            Sta(() =>
            {
                foreach (string language in Localization.Names)
                    foreach (var palette in new[] { ThemePalette.Light, ThemePalette.Dark, ThemePalette.HighContrast })
                    {
                        using var dialog = New(language);
                        dialog.Show(); ThemeApply.Apply(dialog, palette); dialog.PerformLayout();
                        Size size = dialog.Size;
                        for (int page = 0; page < 5; page++)
                        {
                            dialog.Navigate(page); dialog.PerformLayout();
                            Assert.True(size == dialog.Size, language + "/" + palette.Kind + "/" + page + ": " + size + " became " + dialog.Size); CheckCaptions(dialog);
                            Assert.Empty(Walk(dialog).OfType<TextBox>()); // observed S0055 fallback
                            Assert.Equal(Localization.IsRightToLeft(language), dialog.RightToLeftLayout);
                            if (language != "Русский" && language != "Українська")
                                foreach (Control c in Walk(dialog).Where(c => c.Visible && c is not ComboBox))
                                    Assert.DoesNotMatch("[А-Яа-яЁё]", c.Text);
                        }
                        dialog.Close();
                    }
            });
        }

        [Fact]
        public void EveryPageOpensWithOneSolidGlyphPlate()
        {
            Sta(() =>
            {
                using var dialog = New("English"); dialog.Show();
                for (int page = 0; page < 5; page++)
                {
                    dialog.Navigate(page); dialog.PerformLayout();
                    Assert.Single(Walk(dialog).OfType<GlyphBadge>(), b => b.Solid && IsShown(b));
                    Assert.Contains(Walk(dialog).OfType<GlyphBadge>(), b => !b.Solid && IsShown(b));
                }
            });
        }

        private static bool IsShown(Control control)
        {
            for (Control? c = control; c != null; c = c.Parent) if (!c.Visible) return false;
            return true;
        }

        [Fact]
        public void PaintedPiecesFollowTheirPalette()
        {
            Sta(() =>
            {
                foreach (var palette in new[] { ThemePalette.Light, ThemePalette.Dark })
                {
                    using var plate = new GlyphBadge(AppGlyphs.Info, null, 52, true);
                    plate.ApplyTheme(palette);
                    using var bitmap = new Bitmap(52, 52);
                    plate.DrawToBitmap(bitmap, new Rectangle(0, 0, 52, 52));
                    // Near the top edge of the disc, clear of the glyph: the plate's own fill.
                    Assert.Equal(palette.Accent.ToArgb(), bitmap.GetPixel(26, 4).ToArgb());

                    // The knob rests at the far end when on, so the near end of the track shows its fill.
                    foreach (bool on in new[] { true, false })
                    {
                        using var toggle = new ToggleSwitch(on);
                        toggle.ApplyTheme(palette);
                        Size size = toggle.GetPreferredSize(Size.Empty);
                        toggle.Size = size;
                        using var switchBitmap = new Bitmap(size.Width, size.Height);
                        toggle.DrawToBitmap(switchBitmap, new Rectangle(Point.Empty, size));
                        Color track = switchBitmap.GetPixel(on ? size.Width * 18 / 100 : size.Width * 75 / 100, size.Height / 2);
                        Assert.Equal((on ? palette.Accent : palette.SurfaceRaised).ToArgb(), track.ToArgb());
                    }
                }
            });
        }

        [Fact]
        public void SwitchesAreLiveAndWriteTheConfiguration()
        {
            Sta(() =>
            {
                AppConfig config = NewConfig("English");
                using var dialog = New(config); dialog.Show();
                Assert.True(config.EnableCaretOverlay);
                SwitchNamed(dialog, Localization.Translate("English", "Метка у каретки")).Toggle();
                Assert.False(config.EnableCaretOverlay);
                SwitchNamed(dialog, Localization.Translate("English", "Курсор мыши")).Toggle();
                Assert.True(config.EnableCursorChange);
                SwitchNamed(dialog, "EN ⇄ RU").Toggle();
                Assert.False(config.LayoutConversionProfiles[0].Enabled);
                SwitchNamed(dialog, Localization.Translate("English", "Быстрые заметки")).Toggle();
                Assert.True(config.EnableQuickNotes);
                Assert.True(SwitchNamed(dialog, Localization.Translate("English", "Быстрые заметки")).Checked);
            });
        }

        [Fact]
        public void ARefusedSwitchPutsItsKnobBack()
        {
            Sta(() =>
            {
                AppConfig config = NewConfig("English");
                var asked = new List<(WelcomeSwitch, bool)>();
                using var dialog = New(config, apply: (which, _, on) => asked.Add((which, on)));
                dialog.Show();
                ToggleSwitch caret = SwitchNamed(dialog, Localization.Translate("English", "Метка у каретки"));
                caret.Toggle();
                Assert.Equal((WelcomeSwitch.CaretMarker, false), Assert.Single(asked));
                Assert.True(caret.Checked, "the owner changed nothing, so the switch must show the real state");
            });
        }

        [Fact]
        public void TurningAChordOnTurnsTheMasterSwitchOn()
        {
            AppConfig config = NewConfig("English");
            config.EnableHotkeys = false;
            var facts = new WelcomeFacts(config, Array.Empty<InputLayouts.Installed>(), false, false, false);
            Assert.False(facts.IsOn(WelcomeSwitch.CaseFlip));
            facts.Set(WelcomeSwitch.CaseFlip, null, true);
            Assert.True(config.EnableHotkeys);
            Assert.True(facts.IsOn(WelcomeSwitch.CaseFlip));
            facts.Set(WelcomeSwitch.CaseFlip, null, false);
            Assert.False(facts.IsOn(WelcomeSwitch.CaseFlip));
            Assert.True(config.EnableHotkeys, "switching one chord off must not switch the others off with it");
        }

        [Fact]
        public void NavigationButtonsCarryTheirGlyphs()
        {
            Sta(() =>
            {
                foreach (string language in new[] { "English", "العربية" })
                {
                    using var dialog = New(language); dialog.Show();
                    var buttons = Walk(dialog).OfType<GlyphButton>().ToList();
                    Assert.Equal(3, buttons.Count);
                    // The glyph is filled from the shared path in the button's own Paint and never stored as
                    // an Image (ButtonGlyphs): what a button carries is an attached, drawable vocabulary glyph.
                    Assert.All(buttons, b =>
                    {
                        Assert.True(ButtonGlyphs.IsAttached(b), b.Text);
                        Assert.True(GlyphRenderer.Has(b.Glyph), b.Glyph);
                    });
                    Assert.Equal(new[] { AppGlyphs.Back, AppGlyphs.Forward, AppGlyphs.Cancel }, buttons.Select(b => b.Glyph));
                    dialog.Navigate(4);
                    Assert.Contains(buttons, b => b.Glyph == AppGlyphs.Settings);
                }
            });
        }

        [Fact]
        public void KeyCapsSplitAChordIntoItsKeys()
        {
            Assert.Equal(new[] { "Ctrl", "Shift", "F12" }, KeyCaps.Split("Ctrl+Shift+F12"));
            Assert.Equal(new[] { "Ctrl", "Shift", "Alt", "PrtSc" }, KeyCaps.Split("Ctrl+Shift+Alt+PrintScreen"));
            Assert.Empty(KeyCaps.Split(""));
            Sta(() =>
            {
                using var caps = new KeyCaps("Ctrl+Shift+F12");
                using var one = new KeyCaps("F12");
                Assert.True(caps.GetPreferredSize(Size.Empty).Width > 2 * one.GetPreferredSize(Size.Empty).Width);
            });
        }

        [Fact]
        public void CaptionGuardDetectsClipping()
        {
            Sta(() =>
            {
                using var form = new Form();
                form.Controls.Add(new Label { Text = "A caption that cannot fit", Size = new Size(8, 8) });
                form.Show(); Assert.ThrowsAny<Exception>(() => CheckCaptions(form));
            });
        }

        [Theory]
        [InlineData("escape")]
        [InlineData("skip")]
        [InlineData("close")]
        public void AllCancelPathsLeaveNoSettingsRequest(string path)
        {
            Sta(() =>
            {
                using var dialog = New("English"); dialog.Show();
                if (path == "close") dialog.Close();
                else if (path == "skip") ((Button)dialog.CancelButton!).PerformClick();
                else typeof(Form).GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, new object[] { Keys.Escape });
                Assert.False(dialog.Visible); Assert.Null(dialog.RequestedPage);
            });
        }

        [Fact]
        public void LanguageChangeWhileModalRebuildsAndKeepsTheTourOpen()
        {
            Sta(() =>
            {
                using var dialog = New("English");
                bool observed = false;
                int stage = 0;
                using var timer = new System.Windows.Forms.Timer { Interval = 500 };
                timer.Tick += (_, _) =>
                {
                    if (stage++ == 0)
                    {
                        Walk(dialog).OfType<ComboBox>().Single().SelectedIndex = Localization.IndexOf("ar");
                        return;
                    }
                    timer.Stop();
                    observed = dialog.Visible && dialog.RightToLeftLayout && Walk(dialog).OfType<ComboBox>().Any();
                    dialog.Close();
                };
                dialog.Shown += (_, _) =>
                {
                    timer.Start();
                    dialog.BeginInvoke(new Action(() => Walk(dialog).OfType<ComboBox>().Single().SelectedIndex = Localization.IndexOf("zh")));
                };
                dialog.ShowDialog();
                Assert.True(observed, "The modal language switch must keep a populated, mirrored dialog open.");
            });
        }

        [Fact]
        public void EnterAdvancesAndThenRequestsGeneralAndPackagedHidesStartup()
        {
            Sta(() =>
            {
                using var dialog = New("English", true); dialog.Show();
                for (int page = 0; page < 5; page++)
                {
                    Assert.Equal(page, dialog.PageIndex);
                    typeof(Form).GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, new object[] { Keys.Enter });
                }
                Assert.Equal(SettingsPageKey.General, dialog.RequestedPage);
                string startup = Localization.Translate("English", "Запускать CyrFlip вместе с Windows");
                Assert.DoesNotContain(Walk(dialog).OfType<ToggleSwitch>(), s => s.AccessibleName == startup);
                using var portable = New("English", false); portable.Show();
                SwitchNamed(portable, startup);
            });
        }
    }
}

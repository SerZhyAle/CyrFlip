using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// WINDOWS-UI section 11 / ICON-RENDER section 14, held as checks (the owner's instruction of
    /// 2026-10-11): every command button carries its vocabulary glyph before its caption, the answer
    /// pair of a dialog is green and red and nothing else is coloured, an irreversible confirmation
    /// keeps its safe default and its single red face, and the numbers the contract leaves as reference
    /// values (16 px, 6 px, 12 percent) are measured against the contrast the contract demands.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class ButtonGlyphTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "cyrflip-buttons-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private sealed class FakeCipher : IQuickNotesCipher
        {
            public string Protect(string plain) => "B64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
            public string? Unprotect(string cipher) =>
                cipher.StartsWith("B64:", StringComparison.Ordinal) ? Encoding.UTF8.GetString(Convert.FromBase64String(cipher.Substring(4))) : null;
        }

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

        private static IEnumerable<Button> Buttons(Control root) => Walk(root).OfType<Button>();

        // ---- The contract's numbers, measured ----

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TheAnswerGlyphAndCaptionHoldTheirContrastOnTheNeutralFaceInEveryState(bool dark)
        {
            ThemePalette palette = ThemePalette.For(dark ? ThemeKind.Dark : ThemeKind.Light); ThemeKind kind = palette.Kind;
            var report = new List<string>();
            foreach (ButtonLook look in new[] { ButtonLook.Affirmative, ButtonLook.Dismissing })
            {
                Assert.Null(ButtonGlyphs.TintOf(look));
                Color ink = ButtonGlyphs.InkFor(look, palette, true);
                var faces = new Dictionary<string, Color>
                {
                    ["rest"] = palette.Control,
                    ["hover"] = ButtonGlyphs.HoverBase(palette),
                    ["pressed"] = palette.ControlPressed,
                };
                foreach (KeyValuePair<string, Color> face in faces)
                {
                    Color painted = face.Value;
                    double glyph = ThemePalette.Contrast(ink, painted), text = ThemePalette.Contrast(palette.TextPrimary, painted);
                    report.Add($"{kind} {look} {face.Key}: glyph {glyph:0.00}:1, caption {text:0.00}:1 on #{painted.R:X2}{painted.G:X2}{painted.B:X2}");
                    Assert.True(glyph >= 3.0, "glyph below 3:1 - " + report.Last());
                    Assert.True(text >= 4.5, "caption below 4.5:1 - " + report.Last());
                }
            }
            Assert.Equal(6, report.Count);   // a loop that visited nothing would pass just as quietly
        }

        [Fact]
        public void TheDangerFaceKeepsItsInkAndTheNeutralGlyphTheTextColour()
        {
            foreach (ThemePalette palette in new[] { ThemePalette.Light, ThemePalette.Dark })
            {
                Assert.True(ThemePalette.Contrast(ButtonGlyphs.InkFor(ButtonLook.Acting, palette, true), palette.Danger) >= 4.5);
                Assert.Equal(palette.TextPrimary, ButtonGlyphs.InkFor(ButtonLook.Neutral, palette, true));
                // The dismissing answer of an irreversible confirmation sits on the ordinary control face.
                Assert.True(ThemePalette.Contrast(ButtonGlyphs.InkFor(ButtonLook.DismissingSafe, palette, true), palette.Control) >= 3.0);
            }
        }

        [Fact]
        public void AnUnavailableButtonMutesGlyphAndCaptionAndShowsNoHue()
        {
            foreach (ThemePalette palette in new[] { ThemePalette.Light, ThemePalette.Dark })
                foreach (ButtonLook look in Enum.GetValues(typeof(ButtonLook)))
                    Assert.Equal(palette.TextDisabled, ButtonGlyphs.InkFor(look, palette, false));
        }

        [Fact]
        public void HighContrastReplacesBothHueAndTint()
        {
            ThemePalette hc = ThemePalette.HighContrast;
            foreach (ButtonLook look in new[] { ButtonLook.Neutral, ButtonLook.Affirmative, ButtonLook.Dismissing, ButtonLook.DismissingSafe })
                Assert.Equal(hc.TextPrimary, ButtonGlyphs.InkFor(look, hc, true));
            Assert.Equal(hc.Control, ThemePalette.AnswerInk.Face(hc.Control, AnswerHue.Ok, hc));
        }

        // ---- The geometry ----

        [Fact]
        public void TheGlyphReservesSixteenPlusEightLogicalPixelsOnItsReadingStartSide()
        {
            Sta(() =>
            {
                using var form = new Form();
                var ltr = new Button { Text = "OK", AutoSize = true };
                var next = new Button { Text = "Next", AutoSize = true };
                var rtl = new Button { Text = "OK", AutoSize = true, RightToLeft = RightToLeft.Yes };
                form.Controls.AddRange(new Control[] { ltr, next, rtl });
                ButtonGlyphs.Attach(ltr, AppGlyphs.Confirm);
                ButtonGlyphs.Attach(next, AppGlyphs.Forward, ButtonLook.Neutral, glyphAfterText: true);
                ButtonGlyphs.Attach(rtl, AppGlyphs.Confirm);

                double scale = ButtonGlyphs.Dpi(ltr) / 96.0;
                int reserve = (int)Math.Round((ButtonGlyphs.GlyphLogical + ButtonGlyphs.GapLogical) * scale);
                Assert.Equal(new Padding(reserve, 0, 0, 0), ltr.Padding);
                Assert.Equal(new Padding(0, 0, reserve, 0), next.Padding);   // a "next": the glyph follows the caption
                Assert.Equal(new Padding(0, 0, reserve, 0), rtl.Padding);    // right-to-left: the reading start is the right
                Assert.Equal(24, ButtonGlyphs.GlyphLogical + ButtonGlyphs.GapLogical);
            });
        }

        [Fact]
        public void TheReservationIsAbsoluteSoARepeatedOrChangedCaptionNeverAddsItTwice()
        {
            Sta(() =>
            {
                using var form = new Form();
                var button = new Button { Text = "Export", Padding = new Padding(10, 4, 10, 4), AutoSize = true };
                form.Controls.Add(button);
                ButtonGlyphs.Attach(button, AppGlyphs.Export);
                Padding once = button.Padding;
                button.Text = "Exportieren..."; button.Text = "Export"; button.Font = new Font(button.Font.FontFamily, 11f);
                button.Visible = false; button.Visible = true;
                Assert.Equal(once.Left, button.Padding.Left);
                Assert.True(button.Padding.Left > 10 && button.Padding.Right == 10);
                ButtonGlyphs.Attach(button, AppGlyphs.Export);   // attached twice: still once
                Assert.Equal(once, button.Padding);
            });
        }

        [Fact]
        public void AGlyphOnlyButtonReservesNothing()
        {
            Sta(() =>
            {
                using var form = new Form();
                var button = new Button { Text = "" };
                form.Controls.Add(button);
                ButtonGlyphs.Attach(button, AppGlyphs.Close);
                Assert.Equal(Padding.Empty, button.Padding);
            });
        }

        // ---- The live look ----

        [Fact]
        public void TheAnswerInkFollowsALiveThemeChangeAndTheFaceStaysNeutral()
        {
            Sta(() =>
            {
                using var form = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(40, 40), Opacity = 0, ShowInTaskbar = false };
                var ok = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel, Left = 150 };
                form.Controls.AddRange(new Control[] { ok, cancel });
                ButtonGlyphs.Answer(ok); ButtonGlyphs.Answer(cancel);
                form.Show();

                foreach (ThemePalette palette in new[] { ThemePalette.Light, ThemePalette.Dark, ThemePalette.Light, ThemePalette.Dark })
                {
                    ThemeApply.Apply(form, palette);
                    Assert.Equal(palette.Control, ok.BackColor);
                    Assert.Equal(palette.Control, cancel.BackColor);
                    Assert.Equal(FlatStyle.Flat, ok.FlatStyle);

                    // The pixel: a point inside the filled disc of the cross-in-a-circle is exactly its ink.
                    using var bitmap = new Bitmap(cancel.Width, cancel.Height);
                    cancel.DrawToBitmap(bitmap, new Rectangle(0, 0, cancel.Width, cancel.Height));
                    Rectangle box;
                    using (Graphics g = cancel.CreateGraphics()) box = ButtonGlyphs.GlyphBox(cancel, g);
                    Color sample = bitmap.GetPixel(box.X + box.Width / 7 + 1, box.Y + box.Height / 2);
                    Color expected = palette.TextPrimary;
                    Assert.True(Math.Abs(sample.R - expected.R) + Math.Abs(sample.G - expected.G) + Math.Abs(sample.B - expected.B) < 24,
                        $"{palette.Kind}: the red disc is #{sample.R:X2}{sample.G:X2}{sample.B:X2}, expected #{expected.R:X2}{expected.G:X2}{expected.B:X2}");
                }
            });
        }

        [Fact]
        public void AnUnavailableAnswerRetainsTheNeutralFaceWhenReenabled()
        {
            Sta(() =>
            {
                using var form = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(40, 40), Opacity = 0, ShowInTaskbar = false };
                var ok = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK };
                form.Controls.Add(ok);
                ButtonGlyphs.Answer(ok);
                form.Show();
                ThemeApply.Apply(form, ThemePalette.Dark);

                ok.Enabled = false;
                Assert.Equal(ThemePalette.Dark.Control, ok.BackColor);   // no hue while unavailable
                ok.Enabled = true;
                Assert.Equal(ThemePalette.Dark.Control, ok.BackColor);
            });
        }

        [Fact]
        public void HighContrastPutsTheNativeFaceBackOnAnAnswer()
        {
            Sta(() =>
            {
                using var form = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(40, 40), Opacity = 0, ShowInTaskbar = false };
                var ok = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK };
                form.Controls.Add(ok);
                ButtonGlyphs.Answer(ok);
                form.Show();
                ThemeApply.Apply(form, ThemePalette.HighContrast);
                Assert.True(ok.BackColor.IsSystemColor, "high contrast leaves only system colours");
                Assert.NotEqual(FlatStyle.Flat, ok.FlatStyle);
            });
        }

        // ---- The dialogs ----

        [Fact]
        public void ConfirmationKeepsItsSafeDefaultAndItsSingleRedFace()
        {
            Sta(() =>
            {
                foreach (MessageBoxButtons set in new[] { MessageBoxButtons.YesNo, MessageBoxButtons.OKCancel })
                {
                    using var danger = new ConfirmDialog("English", "Delete?", set, MessageBoxIcon.Warning, danger: true, owned: true);
                    Button acting = danger.Buttons[0], safe = danger.Buttons[1];
                    Assert.Equal(ButtonLook.Acting, ButtonGlyphs.LookOf(acting));
                    Assert.Equal(ButtonLook.DismissingSafe, ButtonGlyphs.LookOf(safe));
                    Assert.Equal(AppGlyphs.Confirm, ButtonGlyphs.GlyphOf(acting));
                    Assert.Equal(AppGlyphs.Cancel, ButtonGlyphs.GlyphOf(safe));
                    // Item 7: Enter declines, Escape leaves, and a hue never moves the default focus.
                    Assert.Same(safe, danger.AcceptButton);
                    Assert.Same(safe, danger.CancelButton);
                    // The only red face is the acting one; the safe answer keeps the ordinary control face.
                    Assert.Equal(ThemePalette.Light.Danger, acting.BackColor);
                    Assert.Equal(ThemePalette.Light.Control, safe.BackColor);
                    Assert.Equal(ThemePalette.Light.TextPrimary, safe.ForeColor);
                    Assert.Null(ButtonGlyphs.TintOf(ButtonGlyphs.LookOf(safe)));

                    using var plain = new ConfirmDialog("English", "Continue?", set, MessageBoxIcon.Question, danger: false, owned: true);
                    Assert.Equal(ButtonLook.Affirmative, ButtonGlyphs.LookOf(plain.Buttons[0]));
                    Assert.Equal(ButtonLook.Dismissing, ButtonGlyphs.LookOf(plain.Buttons[1]));
                    Assert.Same(plain.Buttons[0], plain.AcceptButton);
                    Assert.Same(plain.Buttons[1], plain.CancelButton);
                }
            });
        }

        [Fact]
        public void EveryCommandButtonOfEveryWindowCarriesAGlyphOrIsDeclared()
        {
            var problems = new List<string>();
            int inspected = 0, withGlyph = 0, glyphOnlyCount = 0, declaredCount = 0;
            Sta(() =>
            {
                var config = new AppConfig { UiLanguage = "English" };
                Directory.CreateDirectory(_root);
                var forms = new List<KeyValuePair<string, Form>>();
                void Add(string name, Func<Form> make) { try { forms.Add(new KeyValuePair<string, Form>(name, make())); } catch (Exception e) { problems.Add(name + " could not be built: " + e.Message); } }

                foreach (MessageBoxButtons set in new[] { MessageBoxButtons.OK, MessageBoxButtons.YesNo, MessageBoxButtons.OKCancel })
                    Add("ConfirmDialog(" + set + ")", () => new ConfirmDialog("English", "?", set, MessageBoxIcon.Question, false, true));
                Add("HotkeyDialog", () => new HotkeyDialog("Ctrl+Shift+F12", "T", "English"));
                Add("YtDlpLinkDialog", () => new YtDlpLinkDialog("English"));
                Add("TranslationDialog", () => new TranslationDialog(null, "English"));
                Add("LauncherScenarioDialog", () => new LauncherScenarioDialog(null, "English"));
                Add("ExchangeExportDialog", () => new ExchangeExportDialog("English", notesAvailable: true, notesWarningOff: false));
                Add("SettingsTransferDialog", () => SettingsTransferDialogTests.Make("English"));
                Add("TranslationResultWindow", () => new TranslationResultWindow(config, "English"));
                Add("WelcomeDialog", () => new WelcomeDialog(new WelcomeFacts(config, Array.Empty<InputLayouts.Installed>(), false, false, false), _ => { }, _ => { }, () => false));
                Add("SettingsForm", () => TestForms.NewSettings(config, Path.Combine(_root, "scenarios")));
                var layouts = new List<InputLayouts.Installed>
                {
                    new InputLayouts.Installed { Klid = "00000409", LangId = 0x0409, LanguageName = "English", DisplayName = "US", IsDefault = true },
                    new InputLayouts.Installed { Klid = "00000419", LangId = 0x0419, LanguageName = "Russian", DisplayName = "Russian" },
                };
                var available = new List<InputLayouts.Available>
                {
                    new InputLayouts.Available { Klid = "00000422", LangId = 0x0422, LanguageName = "Ukrainian", DisplayName = "Ukrainian" },
                };
                var bundle = new SupportBundle.Result
                {
                    ArchivePath = @"C:\x\reports\CyrFlip-logs.zip", ArchiveBytes = 1000,
                    Entries = new List<SupportBundle.Entry> { new SupportBundle.Entry { Name = "report.txt", Bytes = 100 } },
                    Dropped = new List<string>(),
                };
                Add("LayoutPickerDialog", () => new LayoutPickerDialog(new string[0], "English", available));
                Add("LayoutConversionDialog", () => new LayoutConversionDialog(layouts, null, "English"));
                Add("SupportBundleDialog", () => new SupportBundleDialog(bundle, "English"));
                Add("ClipboardHistorySearchWindow", () => new ClipboardHistorySearchWindow(
                    new ClipboardHistoryService(enabled: false, paused: false, Path.Combine(_root, Guid.NewGuid().ToString("N")), new FakeCipher()), "English", (_, _) => { }));
                Add("QuickNotesWindow", () => new QuickNotesWindow(
                    new QuickNotesService(new QuickNotesStore(Path.Combine(_root, Guid.NewGuid().ToString("N") + ".log"), new FakeCipher())),
                    config, () => { }, () => { }, (_, _) => { }));

                foreach (KeyValuePair<string, Form> pair in forms)
                {
                    using (pair.Value)
                    {
                        pair.Value.CreateControl();
                        foreach (Button button in Buttons(pair.Value))
                        {
                            inspected++;
                            bool glyphOnly = button.Image != null && !string.IsNullOrEmpty(button.AccessibleName);
                            bool declared = button.GetType().Name == "HeaderButton"   // the group's own chevron (CollapsibleGroup)
                                // The quick notes' "Transfer..." opens a menu of both directions of the exchange file: the
                                // vocabulary has no record for it yet (PROPOSAL-2026-10-11-cyrflip-button-meanings).
                                || (pair.Key == "QuickNotesWindow" && button.Parent != null && TransferButtonOf(pair.Value) == button);
                            if (ButtonGlyphs.IsAttached(button)) withGlyph++;
                            else if (glyphOnly) glyphOnlyCount++;
                            else if (declared) declaredCount++;
                            else problems.Add($"{pair.Key}: the button \"{button.Text}\" has no glyph");
                        }
                    }
                }
            });
            Assert.True(problems.Count == 0, "Buttons without a glyph (WINDOWS-UI section 11 item 1):\n" + string.Join("\n", problems));
            Assert.True(inspected > 80, "only " + inspected + " buttons were inspected");
            // The guard can fail: the walk really met buttons of each kind it tells apart.
            Assert.True(withGlyph > 60 && glyphOnlyCount > 0 && declaredCount > 0,
                $"with glyph {withGlyph}, glyph-only {glyphOnlyCount}, declared {declaredCount}");
        }

        private static Button? TransferButtonOf(Form quickNotes)
            => (Button?)typeof(QuickNotesWindow).GetField("_transfer", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(quickNotes);

        [Fact]
        public void TheAnswerPairOfEveryDialogRetainsItsSemanticRoles()
        {
            Sta(() =>
            {
                var checkedDialogs = new List<Form>
                {
                    new HotkeyDialog("Ctrl+Shift+F12", "T", "English"),
                    new YtDlpLinkDialog("English"),
                    new TranslationDialog(null, "English"),
                    new LauncherScenarioDialog(null, "English"),
                    new ExchangeExportDialog("English", notesAvailable: true, notesWarningOff: false),
                };
                foreach (Form dialog in checkedDialogs)
                {
                    using (dialog)
                    {
                        var affirmative = Buttons(dialog).Where(b => b.DialogResult == DialogResult.OK).ToList();
                        var dismissing = Buttons(dialog).Where(b => b.DialogResult == DialogResult.Cancel).ToList();
                        Assert.Single(affirmative);
                        Assert.Single(dismissing);
                        Assert.Equal(ButtonLook.Affirmative, ButtonGlyphs.LookOf(affirmative[0]));
                        Assert.Equal(ButtonLook.Dismissing, ButtonGlyphs.LookOf(dismissing[0]));
                        Assert.Equal(AppGlyphs.Cancel, ButtonGlyphs.GlyphOf(dismissing[0]));
                        // Colour is spent on answers only (item 5): every other command is the mono look.
                        foreach (Button other in Buttons(dialog).Except(affirmative).Except(dismissing))
                            Assert.Equal(ButtonLook.Neutral, ButtonGlyphs.LookOf(other));
                        // What the glyph work must not touch: where Escape and Enter go (APP-BEHAVIOUR rule 1).
                        if (dialog.CancelButton != null) Assert.Same(dismissing[0], dialog.CancelButton);
                        if (dialog.AcceptButton != null) Assert.Same(affirmative[0], dialog.AcceptButton);
                    }
                }
            });
        }
    }
}

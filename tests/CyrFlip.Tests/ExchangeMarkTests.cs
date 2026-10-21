using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The exchange server's mark (<c>EXCHANGE-MARK</c> 0.3, ticket S0079): the artwork is vendored byte for byte and
    /// drawn nowhere else (rules 1 and 6), the program takes the held PNG step of the size it needs and never scales
    /// one up (rule 5), the star is never mirrored (rule 2), gives way to the one-tone glyph in high contrast (rule 3)
    /// and is named by the term (rule 14); and it stands at the places of rule 8 that this program has.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class ExchangeMarkTests
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", ".."));

        /// <summary>SHA-256 of each vendored file, copied from the catalog's <c>artwork/MANIFEST.json</c> (0.3): a clone has no catalog.</summary>
        internal static readonly Dictionary<string, string> Pinned = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mark-16.png"] = "032138cc7785d733c204d19c4f5505c361455ce7fc7e1d655651eafb0cb0d8b4",
            ["mark-20.png"] = "22e67199cda5d3d8f93f43a91a303756cbd52f27800a9363040314ef14b570ee",
            ["mark-24.png"] = "f3eb06c8ec0c73276c960bf7de155b652ae3a4f3f02d7eea92d7e436cf6afd35",
            ["mark-28.png"] = "925f6d16e23a1d7b420a0cf2aa53dc133d2751c46dd3548b991b4e5c69b7aa85",
            ["mark-30.png"] = "7a51151c4bd12a5376cfd7dc7a0d339a4cebd41e8891b6cf6f3b2049dee7c9e4",
            ["mark-32.png"] = "612ac17f99d07144a613e087d96fb3f9fed328b069d88207e3a3c825f8303e9e",
            ["mark-36.png"] = "00e66f2cffda2f312b87797d3b57326a6167ce5f5d4d02ac4138b5b04331418e",
            ["mark-40.png"] = "d739bf42e2a0437c9b2b78188a8d173497af8cd1d4b1f54bdc9360fdc62fa589",
            ["mark-42.png"] = "d5c4fcbe294b7184512764acf61e831b5b565174085ff1165633dffe1550b331",
            ["mark-48.png"] = "0083264a11d69a0b97aec5ea96144ad6ea29ed1abdf59ca2c09c27b239b60e69",
            ["mark-56.png"] = "2536533f866c576e8d0b2939ce8bce2c81a216658c3ebf8dda4cd7f05acc67d4",
            ["mark-64.png"] = "ad8e63098713b5e87e4458b7e8e24a21175985f361b18782112633756aacd379",
            ["mark-96.png"] = "a912b47c8941d4442eeb6d7bca6f8533ef4e9ddb91b683df37af948720c96187",
            ["mark-128.png"] = "d18a419580f878a2b9924818fa2aeaceeb1b2621a5977a54fecd4c065d30399b",
            ["mark-256.png"] = "8704ac443db595040889726ed2a1945c15e84f8b14ce4361e595d4dd52825479",
            ["exchange-mark.svg"] = "8b1f37b5bb19efbbafc431c0b54f8a650cb7f95f428b0d81b25fc9999bac517a",
            ["exchange-mark-small.svg"] = "2e1e685017a553e48cdfaf1f7529b559d0cce96d9240df987057e284b80fe7cc",
            ["exchange-mark-mono.svg"] = "124cc6f3136351e2c1bd064bd8c69aaf398f578a836d6565c1831abab3399b89",
            ["exchange-mark-full.svg"] = "420351bf93ea109af614c601d8a8ed026eb9ffa514ce59e46b51db7b5d495ef9",
        };

        private static readonly string[] Svgs = { "exchange-mark.svg", "exchange-mark-small.svg", "exchange-mark-mono.svg", "exchange-mark-full.svg" };

        /// <summary>The five rays, clockwise from the topmost (the contract's section 2).</summary>
        private static readonly Color[] Rays =
        {
            Color.FromArgb(0xFF, 0xB0, 0x20), Color.FromArgb(0xF4, 0x51, 0x3F), Color.FromArgb(0xA2, 0x4B, 0xE8),
            Color.FromArgb(0x2F, 0x82, 0xF2), Color.FromArgb(0x1A, 0xBA, 0x89),
        };

        private static string Sha(byte[] data)
        {
            using SHA256 sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(data).Select(b => b.ToString("x2")));
        }

        private static void OnUiThread(Action body)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("EXCEPTION: " + failure);
        }

        private static AppConfig Config(params ExchangeAccountEntry[] accounts)
        {
            var config = new AppConfig { UiLanguage = "English", SettingsGroupsState = "", SettingsViewport = "" };
            config.ExchangeClipboardAccounts = accounts.ToList();
            return config;
        }

        private static IEnumerable<ExchangeMarkPicture> MarksIn(Control root)
        {
            if (root is ExchangeMarkPicture self) yield return self;
            foreach (Control child in root.Controls)
                foreach (ExchangeMarkPicture mark in MarksIn(child)) yield return mark;
        }

        // ---- rungs 1 and 2: one picture, copied, never redrawn ----

        [Fact]
        public void EveryVendoredFileIsTheCatalogsByteForByteAndNothingElseLivesInTheFolders()
        {
            foreach (string folder in new[] { "assets/exchange-mark", "docs/assets/exchange-mark", "docs/help/exchange-mark" })
            {
                string[] files = Directory.GetFiles(Path.Combine(Root, folder.Replace('/', Path.DirectorySeparatorChar)));
                Assert.NotEmpty(files);
                foreach (string file in files)
                {
                    string name = Path.GetFileName(file);
                    Assert.True(Pinned.TryGetValue(name, out string? expected), folder + "/" + name + " is not a file of the manifest");
                    Assert.True(expected == Sha(File.ReadAllBytes(file)), folder + "/" + name + " is not the catalog's bytes (altered, or a line-ending conversion)");
                }
            }
            // the site and the help set carry the four SVG masters; the app carries the PNG steps and the glyph's master
            foreach (string folder in new[] { "docs/assets/exchange-mark", "docs/help/exchange-mark" })
                Assert.Equal(Svgs.OrderBy(n => n), Directory.GetFiles(Path.Combine(Root, folder.Replace('/', Path.DirectorySeparatorChar))).Select(Path.GetFileName).OrderBy(n => n));
            foreach (int size in ExchangeMark.HeldSizes)
                Assert.True(File.Exists(Path.Combine(Root, "assets", "exchange-mark", "mark-" + size + ".png")), "mark-" + size + ".png");
        }

        [Fact]
        public void TheGitAttributesKeepTheVendoredBytesFromAnyLineEndingConversion()
        {
            string attributes = File.ReadAllText(Path.Combine(Root, ".gitattributes"));
            foreach (string folder in new[] { "assets/exchange-mark", "docs/assets/exchange-mark", "docs/help/exchange-mark" })
                Assert.Matches(new Regex("^" + Regex.Escape(folder) + @"/\*\* -text\s*$", RegexOptions.Multiline), attributes);
        }

        [Fact]
        public void EveryEmbeddedStepIsTheVendoredFileByteForByte()
        {
            foreach (int size in ExchangeMark.HeldSizes)
            {
                byte[]? embedded = ExchangeMark.ResourceBytes(size);
                Assert.True(embedded != null, "the build does not embed mark-" + size + ".png");
                Assert.Equal(Pinned["mark-" + size + ".png"], Sha(embedded!));
            }
        }

        [Fact]
        public void TheGlyphRecordIsTheCatalogsOneTonePathUnchangedAndTheServerBaysAreGone()
        {
            string svg = File.ReadAllText(Path.Combine(Root, "assets", "exchange-mark", "exchange-mark-mono.svg"));
            string d = Regex.Match(svg, @" d=""([^""]+)""").Groups[1].Value;
            Assert.NotEmpty(d);
            GlyphRecord record = Glyphs.All.Single(r => r.Id == AppGlyphs.ExchangeServer);
            Assert.Equal(d, record.PathData);
            Assert.Null(record.Transform);
            Assert.False(record.EvenOdd);
            // Material 'dns', the two server bays, was the row's picture until the owner's order of 2026-10-19
            Assert.DoesNotContain("M20 13H4c-.55", record.PathData);
        }

        [Fact]
        public void NoSecondDrawingOfTheStarLivesInTheSource()
        {
            string source = Path.Combine(Root, "src", "CyrFlip");
            string[] ink = Rays.Select(c => c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2")).ToArray();
            string monoPath = Regex.Match(File.ReadAllText(Path.Combine(Root, "assets", "exchange-mark", "exchange-mark-mono.svg")), @" d=""([^""]+)""").Groups[1].Value;
            var hits = new List<string>();
            foreach (string file in Directory.GetFiles(source, "*.cs"))
            {
                string name = Path.GetFileName(file);
                string text = File.ReadAllText(file);
                // the generated vocabulary carries the glyph's path (the check finds it inline) - and only there
                if (name != "Glyphs.g.cs" && text.Contains(monoPath)) hits.Add(name + ": the star's path");
                foreach (string hex in ink)
                    if (Regex.IsMatch(text, "(?i)#" + hex + @"\b|0x" + hex + @"\b|FromArgb\(\s*0x" + hex.Substring(0, 2) + @"\s*,\s*0x" + hex.Substring(2, 2) + @"\s*,\s*0x" + hex.Substring(4, 2)))
                        hits.Add(name + ": the ray colour " + hex);
            }
            Assert.True(hits.Count == 0, "A second drawing of the star (rule 1):\n" + string.Join("\n", hits));
        }

        // ---- rule 5: the held step of the size, never scaled up ----

        [Fact]
        public void TheSetHoldsEveryStepOfTheThreeLogicalSizesAtOneHundredToTwoHundredPercent()
        {
            foreach (int logical in new[] { 16, 24, 32 })
                foreach (int percent in new[] { 100, 125, 150, 175, 200 })
                {
                    int device = (int)Math.Round(logical * percent / 100.0);
                    Assert.Contains(device, ExchangeMark.HeldSizes);
                    Assert.Equal(device, ExchangeMark.HeldSizeFor(device));
                }
        }

        [Fact]
        public void ASizeBetweenTwoStepsComesFromTheNextLargerNeverFromASmallerOne()
        {
            for (int device = 1; device <= 256; device++)
                Assert.True(ExchangeMark.HeldSizeFor(device) >= device, "device " + device);
            Assert.Equal(28, ExchangeMark.HeldSizeFor(26));
            Assert.Equal(36, ExchangeMark.HeldSizeFor(33));
            Assert.Equal(128, ExchangeMark.HeldSizeFor(100));
        }

        [Fact]
        public void AHeldStepComesBackAsTheFileAndAnyOtherSizeComesBackAtItsOwn()
        {
            foreach (int size in new[] { 16, 24, 32, 48 })
            {
                using Bitmap rendered = ExchangeMark.Render(size)!;
                using var stream = new MemoryStream(ExchangeMark.ResourceBytes(size)!);
                using var file = new Bitmap(stream);
                Assert.Equal(new Size(size, size), rendered.Size);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        Assert.True(file.GetPixel(x, y).ToArgb() == rendered.GetPixel(x, y).ToArgb(), "step " + size + " differs at " + x + "," + y);
            }
            foreach (int odd in new[] { 18, 26, 34, 45, 72 })
            {
                using Bitmap scaled = ExchangeMark.Render(odd)!;
                Assert.Equal(new Size(odd, odd), scaled.Size);
                Assert.True(CountNear(scaled, Rays[3]) > 0, odd + " px keeps the blue ray");
            }
        }

        [Fact]
        public void TheStarKeepsAllFiveColoursFlatUpToThirtyTwoPixelsAndFacetedFromThirtyThree()
        {
            foreach (int size in new[] { 16, 24, 32 })
            {
                using Bitmap mark = ExchangeMark.Render(size)!;
                foreach (Color ray in Rays) Assert.True(CountNear(mark, ray) > 0, size + " px lacks " + ray);
            }
            // the faceted drawing shades each ray in two facets, so a ray is found by its hue, not by its flat fill
            foreach (int size in new[] { 36, 48, 64 })
            {
                using Bitmap mark = ExchangeMark.Render(size)!;
                foreach (Color ray in Rays) Assert.True(CountNearHue(mark, ray) > 0, size + " px lacks the hue of " + ray);
            }
        }

        private static int CountNearHue(Bitmap bitmap, Color color)
        {
            int count = 0;
            for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                {
                    Color p = bitmap.GetPixel(x, y);
                    if (p.A < 200 || p.GetSaturation() < 0.35f) continue;
                    float delta = Math.Abs(p.GetHue() - color.GetHue());
                    if (Math.Min(delta, 360f - delta) <= 12f) count++;
                }
            return count;
        }

        private static int CountNear(Bitmap bitmap, Color color)
        {
            int count = 0;
            for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                {
                    Color p = bitmap.GetPixel(x, y);
                    if (p.A > 200 && Math.Abs(p.R - color.R) <= 32 && Math.Abs(p.G - color.G) <= 32 && Math.Abs(p.B - color.B) <= 32) count++;
                }
            return count;
        }

        // ---- the control: device size, never mirrored, one tone in high contrast, named by the term ----

        [Fact]
        public void ThePictureTakesTheDeviceSizeOfItsWindowsDpi()
        {
            OnUiThread(() =>
            {
                using var picture = new ExchangeMarkPicture(24);
                Assert.Equal(AccessibleRole.Graphic, picture.AccessibleRole);
                foreach ((int dpi, int expected) in new[] { (96, 24), (120, 30), (144, 36), (168, 42), (192, 48) })
                {
                    ControlDpi.Set(picture, dpi);
                    picture.Rescale();
                    Assert.Equal(new Size(expected, expected), picture.Size);
                    Assert.Contains(expected, ExchangeMark.HeldSizes);
                }
            });
        }

        private static Bitmap Paint(ExchangeMarkPicture picture, bool rightToLeft, ThemePalette palette)
        {
            // a plain host gives the transparent picture a ground, the way every real parent does
            using var host = new Panel { Size = picture.Size, BackColor = palette.SurfaceWindow, RightToLeft = rightToLeft ? RightToLeft.Yes : RightToLeft.No };
            host.Controls.Add(picture);
            picture.Location = Point.Empty;
            picture.ApplyTheme(palette);
            var bitmap = new Bitmap(host.Width, host.Height);
            host.DrawToBitmap(bitmap, new Rectangle(Point.Empty, host.Size));
            host.Controls.Remove(picture);
            return bitmap;
        }

        [Fact]
        public void ThePictureIsNeverMirroredInARightToLeftWindow()
        {
            OnUiThread(() =>
            {
                using var picture = new ExchangeMarkPicture(32);
                using Bitmap ltr = Paint(picture, false, ThemePalette.Light);
                using Bitmap rtl = Paint(picture, true, ThemePalette.Light);
                for (int y = 0; y < ltr.Height; y++)
                    for (int x = 0; x < ltr.Width; x++)
                        Assert.True(ltr.GetPixel(x, y).ToArgb() == rtl.GetPixel(x, y).ToArgb(), "mirrored at " + x + "," + y);
                Assert.True(CountNear(ltr, Rays[0]) > 0 && CountNear(ltr, Rays[4]) > 0);
            });
        }

        [Fact]
        public void InHighContrastTheStarGivesWayToTheOneToneGlyphInTheThemesTextColour()
        {
            OnUiThread(() =>
            {
                using var picture = new ExchangeMarkPicture(32);
                Assert.False(picture.ShowsOneTone);
                using Bitmap coloured = Paint(picture, false, ThemePalette.Dark);
                Assert.True(CountNear(coloured, Rays[3]) > 0, "dark keeps the coloured star");

                using Bitmap mono = Paint(picture, false, ThemePalette.HighContrast);
                Assert.True(picture.ShowsOneTone);
                Color ink = ThemePalette.HighContrast.TextPrimary;
                int solid = 0;
                for (int y = 0; y < mono.Height; y++)
                    for (int x = 0; x < mono.Width; x++)
                    {
                        Color p = mono.GetPixel(x, y);
                        if (p.ToArgb() == ThemePalette.HighContrast.SurfaceWindow.ToArgb()) continue;
                        // a fully covered pixel carries the ink exactly; an edge pixel is a blend of ink and ground
                        if (Math.Abs(p.R - ink.R) + Math.Abs(p.G - ink.G) + Math.Abs(p.B - ink.B) <= 6) solid++;
                    }
                Assert.True(solid > 40, "the glyph painted " + solid + " pixels in the text colour");
                foreach (Color ray in Rays) Assert.Equal(0, CountNear(mono, ray));
            });
        }

        // ---- rung 3: the places of rule 8 that this program has ----

        [Fact]
        public void TheBandShowsTheMarkOnlyOnTheExchangePageAndStaysOneHeight()
        {
            OnUiThread(() =>
            {
                using SettingsForm form = TestForms.NewSettings(Config());
                // shown the way the help generator shows it: inside a host panel, far off screen, so the tab strip
                // raises its selection events and nothing flashes on the user's display
                using var host = new Panel { Size = new Size(2000, 2000) };
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-30000, -30000);
                form.ShowInTaskbar = false;
                form.TopLevel = false;
                host.Controls.Add(form);
                form.Show();
                Application.DoEvents();

                ExchangeMarkPicture band = MarksIn(form).Single(m => m.Name == "headerExchangeMark");
                Assert.Equal(24, band.LogicalSize);
                Assert.Equal("Exchange server", band.AccessibleName);
                Control header = band.Parent!.Parent!;

                form.SelectPage(SettingsPageKey.General);
                Application.DoEvents();
                Assert.False(band.Visible);
                int plain = header.Height;

                form.SelectPage(SettingsPageKey.ExchangeServer);
                Application.DoEvents();
                Assert.True(band.Visible);
                Assert.Equal(plain, header.Height);
                Assert.True(band.Height <= header.Height, "the star fits the band");

                form.SelectPage(SettingsPageKey.Hotkeys);
                Application.DoEvents();
                Assert.False(band.Visible);
                form.Hide();
            });
        }

        [Fact]
        public void EveryAccountEntryWearsTheMarkBeforeItsLabel()
        {
            OnUiThread(() =>
            {
                var first = new ExchangeAccountEntry { Host = "nas.example.org", Login = "alice" };
                var second = new ExchangeAccountEntry { Host = "relay.example.org", Login = "bob", SignIn = ExchangeSignInState.SignedOut };
                using SettingsForm form = TestForms.NewSettings(Config(first, second));
                var _ = form.Handle;
                // the band's, the About credit's and one for each entry
                Assert.Equal(2 + 2, MarksIn(form).Count());
                foreach (ExchangeMarkPicture mark in MarksIn(form).Where(m => m.LogicalSize == 24 && m.Name != "headerExchangeMark"))
                {
                    Assert.Equal("Exchange server", mark.AccessibleName);
                    Control head = mark.Parent!;
                    Assert.Same(mark, head.Controls[0]);   // before the label, then the buttons
                    Assert.IsType<Label>(head.Controls[1]);
                }
            });
        }

        [Fact]
        public void TheAboutCreditNamesTheServerByItsFullNameBesideTheMark()
        {
            OnUiThread(() =>
            {
                using SettingsForm form = TestForms.NewSettings(Config());
                var _ = form.Handle;
                LinkLabel credit = Descendants(form).OfType<LinkLabel>().Single(l => l.Name == "resource." + AboutResources.ExchangeServerId);
                Assert.Equal("Media Exchange Server", credit.Text);
                ExchangeMarkPicture mark = Assert.IsType<ExchangeMarkPicture>(credit.Parent!.Controls[0]);
                Assert.Equal(16, mark.LogicalSize);
                Assert.Equal("Exchange server", mark.AccessibleName);
            });
        }

        [Fact]
        public void TheSignInDialogWearsTheMarkAtTheTopOfItsBody()
        {
            OnUiThread(() =>
            {
                using var dialog = new ExchangeAccountDialog("English", new List<ExchangeAccountEntry>());
                ExchangeMarkPicture mark = Assert.Single(MarksIn(dialog));
                Assert.Equal(32, mark.LogicalSize);
                Assert.Equal("Exchange server", mark.AccessibleName);
                Assert.Equal("Add an account on an exchange server", dialog.Text);   // the caption bar is the dialog's own words
                Assert.IsType<Label>(mark.Parent!.Controls[1]);   // beside the intro
            });
        }

        // ---- rung 4: the pages that open with it ----

        private static string TermIn(string language) => Localization.Translate(language, ExchangeMark.TermKey);

        [Fact]
        public void TheServersHelpPagesOpenWithTheFullFormOnceInEveryLanguageAndAboutCarriesTheInlineMark()
        {
            string help = Path.Combine(Root, "docs", "help");
            var withMark = new Dictionary<string, string>
            {
                ["settings.ExchangeServer"] = "settings-exchangeserver",
                ["exchange-account-dialog"] = "exchange-account-dialog",
                ["settings.About"] = "settings-about",
            };
            foreach (string locale in Localization.Codes)
            {
                string language = Localization.Names[Localization.IndexOf(locale)];
                foreach (KeyValuePair<string, string> page in withMark)
                {
                    string text = File.ReadAllText(Path.Combine(help, locale, page.Value + ".html"));
                    string main = text.Substring(text.IndexOf("<main>", StringComparison.Ordinal));
                    HelpArtifacts.ExchangeMarkFor(page.Key, language, out string before, out string beside);
                    Assert.Single(Regex.Matches(main, @"<img\b[^>]*exchange-mark/"));
                    if (page.Key == "settings.About")
                    {
                        // rule 11: the surface shows the mark but is not about the server - inline beside the title, image-only, the term as its alt
                        Assert.StartsWith("<main><div class=\"title-row\">" + beside + "<h1>", main);
                        Assert.Contains("exchange-mark-small.svg\" width=\"24\" height=\"24\"", beside);
                        Assert.Contains("alt=\"" + System.Web.HttpUtility.HtmlEncode(TermIn(language)) + "\"", beside);
                    }
                    else
                    {
                        // rule 10: the head of a page about the server - the full form, 128 px, once, first in <main>, the full name as its alt (rule 14)
                        Assert.StartsWith("<main>" + before + "<h1>", main);
                        Assert.Contains("exchange-mark-full.svg\" width=\"128\" height=\"128\" alt=\"Media Exchange Server\"", before);
                    }
                    Assert.True(File.Exists(Path.Combine(help, "exchange-mark", before.Length > 0 ? "exchange-mark-full.svg" : "exchange-mark-small.svg")));
                }

                // nothing else gets it: a page that merely mentions the server, and the open exchange file (S0023, M32), show nothing
                foreach (string file in Directory.GetFiles(Path.Combine(help, locale), "*.html"))
                    if (!withMark.ContainsValue(Path.GetFileNameWithoutExtension(file)))
                        Assert.DoesNotContain("exchange-mark/", File.ReadAllText(file));
            }
        }

        [Fact]
        public void TheGuideChapterAndTheReadmeHeadingsCarryTheInlineMarkWithTheTermAndTheStripRowDrawsTheStar()
        {
            string monoPath = Regex.Match(File.ReadAllText(Path.Combine(Root, "assets", "exchange-mark", "exchange-mark-mono.svg")), @" d=""([^""]+)""").Groups[1].Value;
            foreach ((string file, string code, string prefix) in new[]
            {
                ("docs/guide.html", "en", ""), ("docs/ru/guide.html", "ru", "../"), ("docs/uk/guide.html", "uk", "../"),
            })
            {
                string guide = File.ReadAllText(Path.Combine(Root, file.Replace('/', Path.DirectorySeparatorChar)));
                string term = TermIn(Localization.Names[Localization.IndexOf(code)]);
                string image = "<img class=\"mark-inline\" src=\"" + prefix + "assets/exchange-mark/exchange-mark-small.svg\" width=\"32\" height=\"32\" alt=\"" + term + "\">";
                Assert.Matches(new Regex("<h2[^>]*>" + Regex.Escape(image)), guide);
                Assert.True(File.Exists(Path.Combine(Root, "docs", "assets", "exchange-mark", "exchange-mark-small.svg")));
                // the page-strip table: the star replaced the two server bays, with the glyph's path data unchanged (C6)
                Assert.Contains("<path d=\"" + monoPath + "\"/></svg>" + term + "</td>", guide);
                Assert.DoesNotContain("M20 13H4c-.55", guide);
                Assert.Single(Regex.Matches(guide, "exchange-mark/"));
            }
            foreach ((string file, string code) in new[] { ("README.md", "en"), ("README_RU.md", "ru"), ("README_UK.md", "uk") })
            {
                string readme = File.ReadAllText(Path.Combine(Root, file));
                string term = TermIn(Localization.Names[Localization.IndexOf(code)]);
                string image = "<img src=\"docs/assets/exchange-mark/exchange-mark-small.svg\" width=\"32\" height=\"32\" alt=\"" + term + "\">";
                Assert.Matches(new Regex("(?m)^## " + Regex.Escape(image) + @" \S"), readme);
            }
        }

        [UiSweepFact]
        public void EveryMarkIsNamedByTheTermInEachInterfaceLanguageAndTheDialogStaysWhole()
        {
            OnUiThread(() =>
            {
                foreach (string language in Localization.Names)
                {
                    string term = Localization.Translate(language, ExchangeMark.TermKey);
                    var entry = new ExchangeAccountEntry { Host = "nas.example.org", Login = "alice" };
                    AppConfig config = Config(entry);
                    config.UiLanguage = language;
                    using (SettingsForm form = TestForms.NewSettings(config))
                    {
                        var _ = form.Handle;
                        foreach (ExchangeMarkPicture mark in MarksIn(form))
                            Assert.True(mark.AccessibleName == term, language + ": " + mark.Name + " is named '" + mark.AccessibleName + "', expected '" + term + "'");
                    }
                    using (var dialog = new ExchangeAccountDialog(language, new List<ExchangeAccountEntry>()))
                    {
                        ExchangeMarkPicture mark = Assert.Single(MarksIn(dialog));
                        Assert.Equal(term, mark.AccessibleName);
                        Assert.True(mark.Width >= 32);
                    }
                }
            });
        }

        private static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control grand in Descendants(child)) yield return grand;
            }
        }
    }
}

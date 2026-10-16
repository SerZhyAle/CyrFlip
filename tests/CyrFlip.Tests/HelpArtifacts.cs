using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using CyrFlip;

namespace CyrFlip.Tests
{
    /// <summary>Operator-invoked renderer, not an xUnit sweep. Never loads the user's configuration.</summary>
    public static class HelpArtifacts
    {
        public static void Generate(string repository, string onlyLocale, string onlySurface)
        {
            Application.EnableVisualStyles();
            var map = ContextHelp.ReadMap(File.ReadAllText(Path.Combine(repository, "help-map.json")));
            string output = Path.Combine(repository, "docs", "help");
            Directory.CreateDirectory(output);
            var titles = new Dictionary<string, Dictionary<string, string>>();
            var manifest = new List<object>();
            var originalRegistry = RuntimeRegistry.UserOverride.Value;
            // All settings writes land in memory. A preview cannot change the developer's registry.
            RuntimeRegistry.UserOverride.Value = new RuntimeDataSafetyTests.FakeRegistry();
            try
            {
                foreach (string locale in Localization.Codes.Where(c => onlyLocale.Length == 0 || onlyLocale.Split(',').Contains(c)))
                {
                    string language = Localization.Names[Localization.IndexOf(locale)];
                    ContextHelp.ApplicationLanguage = () => language;
                    string folder = Path.Combine(output, locale);
                    Directory.CreateDirectory(folder);
                    int captureStart = manifest.Count;
                    using var fixture = new ThemeCoverageTests();
                    foreach (var factory in fixture.Windows(language))
                    {
                        if (onlySurface.Length > 0 && !map.surfaces.Any(s => s.id == onlySurface && s.type == "CyrFlip." + factory.Item1)) continue;
                        using Form form = factory.Item2();
                        using var demoIcon = Icon.ExtractAssociatedIcon(typeof(AppConfig).Assembly.Location);
                        if (demoIcon != null) form.Icon = demoIcon;
                        ContextHelp.Surface? surface = map.surfaces.FirstOrDefault(s => s.type == form.GetType().FullName);
                        if (surface == null || surface.form != "page") continue;
                        if (form is ThemedForm themed) themed.HelpLanguage = language;
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(-30000, -30000);
                        form.TopMost = false;
                        form.ShowInTaskbar = false;
                        // Path-like demo values are explicit, including the displayed screenshot folder.
                        foreach (Control control in HelpElements.Walk(form))
                        {
                            if (control is Label label)
                            {
                                label.Text = label.Text.Replace(new string('m', 180), "aya-expanse:8b");
                                label.Text = System.Text.RegularExpressions.Regex.Replace(label.Text, @"\d{4}-\d{2}-\d{2}T[0-9:]+Z", "2026-01-01T12:00:00Z");
                            }
                            if (control is TextBoxBase box && box.Text.IndexOf(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase) >= 0)
                                box.Text = @"C:\Demo\CyrFlip";
                        }
                        using var hiddenHost = new Panel { Size = new Size(2000, 2000) };
                        form.TopLevel = false;
                        hiddenHost.Controls.Add(form);
                        ContextHelpUi.Attach(form);
                        form.Show();
                        form.Location = new Point(-30000, -30000);
                        Application.DoEvents();
                        if (onlySurface.Length > 0) Console.WriteLine("Help: " + form.Controls["contextHelp"]?.Bounds + "; visible=" + form.Controls["contextHelp"]?.Visible + "; client=" + form.ClientSize);
                        Render(surface, form, null);
                        if (form is SettingsForm settings)
                        {
                            foreach (SettingsPageKey key in SettingsPageKeys.DisplayOrder)
                            {
                                settings.SelectPage(key);
                                typeof(SettingsForm).GetMethod("SetPageGroupsExpanded", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(settings, new object[] { true });
                                Application.DoEvents();
                                var pageRow = map.surfaces.Single(s => s.id == "settings." + key);
                                Render(pageRow, form, settings.Tabs!.SelectedTab!.Text);
                            }
                        }
                        if (form is WelcomeDialog tour)
                        {
                            for (int page = 0; page < WelcomeDialog.PageCount; page++)
                            {
                                typeof(WelcomeDialog).GetMethod("Navigate", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(tour, new object[] { page });
                                Application.DoEvents();
                                string title = HelpElements.Walk(tour).OfType<Label>().FirstOrDefault(l => l.Visible && l.Font.SizeInPoints >= 14)?.Text ?? form.Text;
                                Render(map.surfaces.Single(s => s.id == "welcome." + page), form, title);
                            }
                        }
                        form.Hide();
                    }
                    fixture.DisposeFixtures();
                    if (onlySurface.Length > 0)
                    {
                        string infoPath = Path.Combine(folder, "capture-info.json");
                        if (File.Exists(infoPath))
                        {
                            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                            var info = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(infoPath));
                            var old = serializer.ConvertToType<object[]>(info["captures"])
                                .Where(o => (string)((Dictionary<string, object>)o)["surface"] != onlySurface).ToList();
                            old.AddRange(serializer.Deserialize<object[]>(serializer.Serialize(manifest.Skip(captureStart).ToArray())));
                            info["captures"] = old;
                            File.WriteAllText(infoPath, serializer.Serialize(info), new UTF8Encoding(false));
                            string titlePath = Path.Combine(folder, "titles.json");
                            var oldTitles = serializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(titlePath));
                            foreach (var item in titles) oldTitles[item.Key] = item.Value[locale];
                            File.WriteAllText(titlePath, serializer.Serialize(oldTitles), new UTF8Encoding(false));
                        }
                        continue;
                    }
                    Console.WriteLine("Captured " + locale);
                    File.WriteAllText(Path.Combine(folder, "capture-info.json"), new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(new
                    {
                        locale, version = SupportBundle.AppVersion(), captured = DateTime.UtcNow.ToString("yyyy-MM-dd"), demo = true,
                        captures = manifest.Skip(captureStart).ToArray(),
                    }), new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(folder, "titles.json"), new JavaScriptSerializer().Serialize(titles.ToDictionary(p => p.Key, p => p.Value[locale])), new UTF8Encoding(false));

                    void Render(ContextHelp.Surface surface, Form form, string? title)
                    {
                        title = title ?? form.Text;
                        if (title.Length == 0) title = Localization.Translate(language, surface.name);
                        if (!titles.TryGetValue(surface.id, out var translated)) titles[surface.id] = translated = new Dictionary<string, string>();
                        translated[locale] = title;
                        var elements = HelpElements.Describe(form);
                        foreach (string theme in new[] { "light", "dark" })
                        {
                            ThemeApply.Apply(form, theme == "dark" ? ThemePalette.Dark : ThemePalette.Light);
                            form.PerformLayout();
                            Application.DoEvents();
                            elements = HelpElements.Describe(form);
                            using var image = new Bitmap(form.Width, form.Height);
                            form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                            int border = (form.Width - form.ClientSize.Width) / 2;
                            Point origin = new Point(border, form.Height - form.ClientSize.Height - border);
                            using (Graphics g = Graphics.FromImage(image))
                            using (var font = new Font("Segoe UI", 9, FontStyle.Bold))
                            using (var background = new SolidBrush(Color.FromArgb(15, 70, 140)))
                            using (var ink = new SolidBrush(Color.White))
                            {
                                g.SmoothingMode = SmoothingMode.AntiAlias;
                                foreach (var entry in elements)
                                {
                                    Rectangle bounds = entry.Bounds;
                                    if (form.RightToLeftLayout) bounds.X = form.ClientSize.Width - bounds.Right;
                                    if (!form.ClientRectangle.IntersectsWith(bounds)) continue;
                                    int x = Math.Max(0, Math.Min(image.Width - 26, bounds.Left + origin.X));
                                    int y = Math.Max(0, Math.Min(image.Height - 26, bounds.Top + origin.Y));
                                    g.FillEllipse(background, x, y, 26, 26);
                                    string number = entry.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                                    g.DrawString(number, font, ink, new RectangleF(x, y, 26, 26), new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                                }
                            }
                            string filename = surface.help + "-" + theme + ".png";
                            image.Save(Path.Combine(folder, filename), ImageFormat.Png);
                            manifest.Add(new { surface = surface.id, locale, theme, file = locale + "/" + filename,
                                markers = elements.Select(e => new { id = e.Anchor, x = e.Bounds.X, y = e.Bounds.Y, width = e.Bounds.Width, height = e.Bounds.Height }).ToArray() });
                        }
                        string purpose = HelpElements.Walk(form).OfType<Label>().FirstOrDefault(l => l.Visible && Equals(l.Tag, SettingsForm.PageDescriptionTag))?.Text
                            ?? Localization.Translate(language, "Read the numbered elements, choose the values you need, then use the window’s command to continue.");
                        File.WriteAllText(Path.Combine(folder, surface.help + ".html"), Page(surface, title, purpose, elements, locale, language, map), new UTF8Encoding(false));
                    }
                }
                foreach (string locale in Localization.Codes.Where(c => onlySurface.Length == 0 && (onlyLocale.Length == 0 || onlyLocale.Split(',').Contains(c))))
                {
                    string language = Localization.Names[Localization.IndexOf(locale)];
                    var index = map.surfaces.Single(s => s.id == "index");
                    string links = string.Join("\n", map.surfaces.Where(s => s.form == "page" && s.id != "index")
                        .Select(s => "<li><a href=\"" + s.help + ".html\">" + H(titles[s.id][locale]) + "</a></li>"));
                    string content = "<p>" + H(Localization.Translate(language, "Help is available offline. Press F1 in a CyrFlip window or use its help button.")) + "</p><ul>" + links + "</ul>";
                    File.WriteAllText(Path.Combine(output, locale, "index.html"), Shell(Localization.Translate(language, "Help"), locale, content), new UTF8Encoding(false));
                }
                long size = Directory.GetFiles(output, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
                if (size > 128L * 1024 * 1024) throw new InvalidOperationException("Help exceeded its 128 MiB uncompressed budget.");
                Console.WriteLine("Generated help: " + manifest.Count + " screenshots, " + (size / 1024 / 1024) + " MiB.");
            }
            finally { RuntimeRegistry.UserOverride.Value = originalRegistry; }
        }

        private static string H(string value) => HttpUtility.HtmlEncode(value);
        private static string Shell(string title, string locale, string body)
            => "<!doctype html>\n<html lang=\"" + locale + "\" dir=\"" + (locale == "ar" || locale == "ur" ? "rtl" : "ltr") + "\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><meta name=\"referrer\" content=\"no-referrer\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src 'self' data:; style-src 'self'; base-uri 'none'; form-action 'none'\"><title>" + H(title) + " - CyrFlip</title><link rel=\"stylesheet\" href=\"../sza-kit.css\"><link rel=\"stylesheet\" href=\"../help.css\"><link rel=\"stylesheet\" href=\"../app-theme.css\"></head><body><main><h1>" + H(title) + "</h1>" + body + "</main></body></html>\n";

        private static string Page(ContextHelp.Surface surface, string title, string purpose, List<HelpElements.Element> elements, string locale, string language, ContextHelp.Map map)
        {
            string T(string key) => H(Localization.Translate(language, key));
            var html = new StringBuilder();
            html.Append("<section id=\"purpose\"><h2>" + T("What this is for") + "</h2><p>" + H(purpose) + "</p></section>");
            html.Append("<figure><img class=\"shot light\" src=\"" + surface.help + "-light.png\" alt=\"" + H(title) + "\"><img class=\"shot dark\" src=\"" + surface.help + "-dark.png\" alt=\"" + H(title) + "\"></figure>");
            html.Append("<section id=\"order\"><h2>" + T("In this order") + "</h2><p>" + T(surface.id.StartsWith("settings.", StringComparison.Ordinal) ? "Settings apply immediately. There is no required order; use the groups below." : "Read the numbered elements, choose the values you need, then use the window’s command to continue.") + "</p></section>");
            html.Append("<section id=\"elements\"><h2>" + T("The elements") + "</h2>");
            foreach (var element in elements)
                html.Append("<details id=\"" + element.Anchor + "\"><summary>" + element.Number + ". " + H(element.Name) + "</summary><p>" + H(element.Explanation) + "</p><a href=\"settings-hotkeys.html\">" + T("Your shortcuts") + "</a></details>");
            html.Append("</section><nav id=\"related\"><h2>" + T("Related") + "</h2><a href=\"index.html\">" + T("Help") + "</a> · <a href=\"settings-hotkeys.html\">" + T("Your shortcuts") + "</a> · <a href=\"https://serzhyale.github.io/CyrFlip/" + (locale == "ru" || locale == "uk" ? locale + "/" : "") + "guide.html\">" + T("Guide") + "</a></nav>");
            html.Append("<footer>CyrFlip " + H(SupportBundle.AppVersion()) + " · " + T("Captured") + " " + DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + " · <a href=\"index.html\">" + T("Help") + "</a> · <a href=\"mailto:sza@ukr.net\">" + T("Support") + "</a></footer>");
            return Shell(title, locale, html.ToString());
        }
    }
}


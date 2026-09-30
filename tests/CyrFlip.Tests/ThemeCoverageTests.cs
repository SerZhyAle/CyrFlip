using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// "Every window respects the theme", as a check rather than an intention (ticket S0020 section 8,
    /// test 1). Every window of the app is built and walked:
    ///
    /// <list type="bullet">
    /// <item><b>dark</b> - no control is left with a light system colour, and every colour on screen is
    /// one of the dark palette's (so an unknown literal cannot hide either);</item>
    /// <item><b>high contrast</b> - nothing but system colours;</item>
    /// <item><b>light after both</b> - every recorded property is back to what the constructor built,
    /// which is what "light looks exactly like the app always did" has to mean.</item>
    /// </list>
    ///
    /// <para>The list of windows is a whitelist twice over: every <see cref="Form"/> in the assembly must
    /// be a <see cref="ThemedForm"/> or declared out of theme with its reason, and every
    /// <see cref="ThemedForm"/> must be built here. A window added tomorrow fails one of the two.</para>
    ///
    /// <para>Palettes are applied directly - <see cref="ThemeManager"/> is never initialized in a test,
    /// so nothing here depends on the theme of the machine running it.</para>
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class ThemeCoverageTests : IDisposable
    {
        /// <summary>Forms deliberately outside the theme, each with the reason a reader will ask for.</summary>
        private static readonly Dictionary<string, string> OutOfTheme = new Dictionary<string, string>
        {
            ["CyrFlip.CaretOverlay+OverlayForm"] =
                "the caret badge: layout colours drawn over the user's own text (LAYOUT-PALETTE, APP-STYLE rule 5)",
            ["CyrFlip.RegionSelectionOverlay+MonitorWindow"] =
                "the region capture's overlay: a frozen picture of the user's own screen under a fixed veil (S0026 4.4)",
            ["CyrFlip.LauncherTaskbarWindow"] =
                "a 1x1 window at Opacity 0 that exists only to own a taskbar button - there is nothing to paint",
        };

        private readonly string _root = Path.Combine(Path.GetTempPath(), "CyrFlipThemeTests", Guid.NewGuid().ToString("N"));
        // The services the windows are built over - disposed on the UI thread that made them.
        private readonly List<IDisposable> _owned = new List<IDisposable>();

        public void Dispose()
        {
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private sealed class FakeCipher : IQuickNotesCipher
        {
            public string Protect(string plain) => Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
            public string? Unprotect(string cipher)
            {
                try { return Encoding.UTF8.GetString(Convert.FromBase64String(cipher)); }
                catch (FormatException) { return null; }
            }
        }

        [Fact]
        public void EveryFormIsThemedOrDeclared()
        {
            var problems = new List<string>();
            foreach (Type type in typeof(AppConfig).Assembly.GetTypes())
            {
                if (!typeof(Form).IsAssignableFrom(type) || type == typeof(ThemedForm)) continue;
                if (typeof(ThemedForm).IsAssignableFrom(type)) continue;
                if (!OutOfTheme.ContainsKey(type.FullName!)) problems.Add(type.FullName!);
            }
            Assert.True(problems.Count == 0,
                "Neither a ThemedForm nor declared out of theme:\n" + string.Join("\n", problems));
            // A declaration that names nothing any more is a stale excuse.
            foreach (string declared in OutOfTheme.Keys)
                Assert.NotNull(typeof(AppConfig).Assembly.GetType(declared));
        }

        [Fact]
        public void EveryWindowFollowsDarkHighContrastAndLight()
        {
            var problems = new List<string>();
            var built = new HashSet<Type>();
            OnUiThread(() =>
            {
                foreach ((string name, Func<Form> make) in Windows())
                {
                    using Form form = make();
                    built.Add(form.GetType());
                    Check(form, name, problems);
                }
                foreach (IDisposable owned in _owned) owned.Dispose();
            }, problems);

            Assert.True(problems.Count == 0, "Theme problems:\n" + string.Join("\n", problems));

            var missing = typeof(AppConfig).Assembly.GetTypes()
                .Where(t => typeof(ThemedForm).IsAssignableFrom(t) && t != typeof(ThemedForm) && !built.Contains(t))
                .Select(t => t.FullName).ToList();
            Assert.True(missing.Count == 0, "Themed windows this test never builds:\n" + string.Join("\n", missing));
        }

        /// <summary>The walk above is only worth having if it can fail.</summary>
        [Fact]
        public void TheWalkReportsWhatTheThemeMissed()
        {
            var problems = new List<string>();
            OnUiThread(() =>
            {
                // A window nobody themed: its light system colours must be reported.
                using (var plain = new Form())
                {
                    plain.Controls.Add(new Label { Text = "x" });
                    DarkProblems(plain, "unthemed", problems);
                }
                Assert.NotEmpty(problems);
                problems.Clear();

                // A literal that is not in the palette survives the walk and must be reported too.
                using (var odd = new Form())
                {
                    odd.Controls.Add(new Label { Text = "x", ForeColor = Color.FromArgb(1, 2, 3) });
                    ThemeApply.Apply(odd, ThemePalette.Dark);
                    DarkProblems(odd, "literal", problems);
                }
            }, problems);

            string report = Assert.Single(problems);
            Assert.Contains("Label", report);
        }

        /// <summary>
        /// A row the settings window builds after the window was themed (every table is rebuilt on every
        /// change) arrives dark, and a container emptied and refilled keeps doing so.
        /// </summary>
        [Fact]
        public void AControlAddedLaterArrivesInTheTheme()
        {
            var problems = new List<string>();
            OnUiThread(() =>
            {
                using var form = new Form();
                var rows = new FlowLayoutPanel();
                form.Controls.Add(rows);
                ThemeApply.Apply(form, ThemePalette.Dark);

                var row = new FlowLayoutPanel();
                row.Controls.Add(new Label { Text = "x", ForeColor = ThemePalette.Light.TextMuted });
                row.Controls.Add(new Button { Text = "y" });
                rows.Controls.Add(row);
                DarkProblems(form, "late row", problems);
                if (((Label)row.Controls[0]).ForeColor != ThemePalette.Dark.TextMuted) problems.Add("the muted label is not muted");
            }, problems);
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        // ---- The windows ----

        private IEnumerable<(string, Func<Form>)> Windows()
        {
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
                // A row the dialog builds in text.muted - the list-item colour the walk must follow (S0036 UI-6).
                Dropped = new List<string> { "clipboard-flip.log" },
            };
            var config = new AppConfig { UiLanguage = "English" };
            Directory.CreateDirectory(_root);

            yield return ("SettingsForm", () => NewSettings(config));
            yield return ("HotkeyDialog", () => new HotkeyDialog("Ctrl+Shift+F12", "T", "English"));
            yield return ("LayoutPickerDialog", () => new LayoutPickerDialog(new string[0], "English", available));
            yield return ("LayoutConversionDialog", () => new LayoutConversionDialog(layouts, null, "English"));
            yield return ("LauncherScenarioDialog", () => new LauncherScenarioDialog(null, "English"));
            yield return ("YtDlpLinkDialog", () => new YtDlpLinkDialog("English"));
            yield return ("TranslationDialog", () => new TranslationDialog(null, "English"));
            yield return ("TranslationResultWindow", () => new TranslationResultWindow(new AppConfig { UiLanguage = "English" }, "English"));
            yield return ("SupportBundleDialog", () => new SupportBundleDialog(bundle, "English"));
            yield return ("ExchangeExportDialog", () => new ExchangeExportDialog("English", notesAvailable: true, notesWarningOff: false));
            yield return ("ConfirmDialog", () => new ConfirmDialog("English", "Delete?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true, owned: true));
            yield return ("BusyDialog", () => new BusyDialog("English", System.Threading.Tasks.Task.CompletedTask));
            yield return ("ClipboardHistoryWindow",() => new ClipboardHistoryWindow(History(), config, () => { }));
            yield return ("ClipboardHistorySearchWindow", () => new ClipboardHistorySearchWindow(History(), "English", (_, _) => { }));
            yield return ("QuickNotesWindow", () => new QuickNotesWindow(Notes(), config, () => { }, () => { }, (_, _) => { }));
        }

        private ClipboardHistoryService History()
            => Own(new ClipboardHistoryService(enabled: false, paused: false, Path.Combine(_root, Guid.NewGuid().ToString("N")), new FakeCipher()));

        private QuickNotesService Notes()
            => Own(new QuickNotesService(new QuickNotesStore(Path.Combine(_root, Guid.NewGuid().ToString("N") + ".log"), new FakeCipher())));

        private T Own<T>(T disposable) where T : IDisposable { _owned.Add(disposable); return disposable; }

        private SettingsForm NewSettings(AppConfig config)
            => TestForms.NewSettings(config, Path.Combine(_root, "scenarios"));

        // ---- The checks ----

        private static void Check(Form form, string name, List<string> problems)
        {
            Dictionary<string, string> built = Snapshot(form);

            ThemeApply.Apply(form, ThemePalette.Dark);
            DarkProblems(form, name, problems);

            ThemeApply.Apply(form, ThemePalette.HighContrast);
            SystemOnlyProblems(form, name, problems);

            ThemeApply.Apply(form, ThemePalette.Light);
            Dictionary<string, string> back = Snapshot(form);
            foreach (KeyValuePair<string, string> pair in built)
                if (!back.TryGetValue(pair.Key, out string? now) || now != pair.Value)
                    problems.Add($"{name}: light did not restore {pair.Key}: built {pair.Value}, now {now}");
        }

        // By what a colour is for, not merely by value: the dark text colour is exactly the light "control"
        // grey, and dark ink is exactly black - a check by value alone would pass an unthemed window.
        private static readonly HashSet<int> DarkBackColors = Values(ThemeRole.SurfaceWindow, ThemeRole.SurfaceRaised,
            ThemeRole.SurfaceSunken, ThemeRole.SurfaceSelected, ThemeRole.SurfaceAlternate, ThemeRole.Control,
            ThemeRole.ControlHover, ThemeRole.ControlPressed, ThemeRole.Danger, ThemeRole.Accent);
        private static readonly HashSet<int> DarkForeColors = Values(ThemeRole.TextPrimary, ThemeRole.TextMuted,
            ThemeRole.TextDisabled, ThemeRole.Accent, ThemeRole.AccentInk, ThemeRole.Link, ThemeRole.Info,
            ThemeRole.Warning, ThemeRole.Danger, ThemeRole.Success);

        private static HashSet<int> Values(params ThemeRole[] roles)
            => new HashSet<int>(roles.Select(r => ThemePalette.Dark[r].ToArgb()));

        private static void DarkProblems(Control root, string name, List<string> problems)
        {
            foreach ((string path, Control control) in Walk(root, name))
            {
                // A tab control reports a fixed BackColor whatever it paints; in dark it paints itself.
                if (!(control is TabControl) && (control.BackColor.IsSystemColor || !DarkBackColors.Contains(control.BackColor.ToArgb())))
                    problems.Add($"{path}: BackColor {Describe(control.BackColor)} is not a dark palette colour");
                // A track bar reports a fixed ForeColor and draws its channel and thumb in the system style -
                // one of the declared exceptions of S0020 section 7; its background does follow.
                if (!(control is TrackBar) && (control.ForeColor.IsSystemColor || !DarkForeColors.Contains(control.ForeColor.ToArgb())))
                    problems.Add($"{path}: ForeColor {Describe(control.ForeColor)} is not a dark palette colour");
                switch (control)
                {
                    case Button button when button.FlatStyle != FlatStyle.Flat:
                        problems.Add($"{path}: the button keeps its light visual-style face");
                        break;
                    case LinkLabel link when link.LinkColor.ToArgb() != ThemePalette.Dark.Link.ToArgb():
                        problems.Add($"{path}: link colour {Describe(link.LinkColor)}");
                        break;
                    case TabPage page when page.UseVisualStyleBackColor:
                        problems.Add($"{path}: the page keeps its visual-style background");
                        break;
                    // The checklist editor keeps its native (empty) header: owner-draw and check boxes
                    // do not mix in a list view. Declared in S0020 section 7.
                    case ListView list when list.View == View.Details && list.HeaderStyle != ColumnHeaderStyle.None
                                            && !list.CheckBoxes && !list.OwnerDraw:
                        problems.Add($"{path}: the list header is not owner-drawn");
                        break;
                }
                // A row's own colour is painted by the native list, outside every control's colours (UI-6).
                if (control is ListView rows && !rows.VirtualMode)
                    for (int i = 0; i < rows.Items.Count; i++)
                    {
                        Color fore = rows.Items[i].ForeColor;
                        if (fore != rows.ForeColor && (fore.IsSystemColor || !DarkForeColors.Contains(fore.ToArgb())))
                            problems.Add($"{path}/item {i}: ForeColor {Describe(fore)} is not a dark palette colour");
                    }
                switch (control)
                {
                    case ThemeTabControl tabs when !IsUserPainted(tabs):
                        problems.Add($"{path}: the page list is left to the light native paint");
                        break;
                }
            }
        }

        private static void SystemOnlyProblems(Control root, string name, List<string> problems)
        {
            foreach ((string path, Control control) in Walk(root, name))
            {
                if (!control.BackColor.IsSystemColor)
                    problems.Add($"{path}: high contrast BackColor {Describe(control.BackColor)} is not a system colour");
                if (!control.ForeColor.IsSystemColor)
                    problems.Add($"{path}: high contrast ForeColor {Describe(control.ForeColor)} is not a system colour");
            }
        }

        private static bool IsUserPainted(Control control)
            => (bool)typeof(Control).GetMethod("GetStyle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(control, new object[] { ControlStyles.UserPaint })!;

        /// <summary>Every property the theme may change, per control, as text - the "built state" to compare.</summary>
        private static Dictionary<string, string> Snapshot(Form form)
        {
            var result = new Dictionary<string, string>();
            foreach ((string path, Control control) in Walk(form, form.GetType().Name))
            {
                result[path + ".BackColor"] = Describe(control.BackColor);
                result[path + ".ForeColor"] = Describe(control.ForeColor);
                switch (control)
                {
                    case Button button:
                        result[path + ".FlatStyle"] = button.FlatStyle.ToString();
                        result[path + ".UseVisualStyleBackColor"] = button.UseVisualStyleBackColor.ToString();
                        result[path + ".FlatBorder"] = Describe(button.FlatAppearance.BorderColor);
                        result[path + ".FlatOver"] = Describe(button.FlatAppearance.MouseOverBackColor);
                        break;
                    case ComboBox combo:
                        result[path + ".FlatStyle"] = combo.FlatStyle.ToString();
                        break;
                    case LinkLabel link:
                        result[path + ".LinkColor"] = Describe(link.LinkColor);
                        break;
                    case TabPage page:
                        result[path + ".UseVisualStyleBackColor"] = page.UseVisualStyleBackColor.ToString();
                        break;
                    case ListView list:
                        result[path + ".OwnerDraw"] = list.OwnerDraw.ToString();
                        if (!list.VirtualMode)
                            for (int i = 0; i < list.Items.Count; i++)
                                result[path + "/item " + i + ".ForeColor"] = Describe(list.Items[i].ForeColor);
                        break;
                }
            }
            return result;
        }

        private static IEnumerable<(string, Control)> Walk(Control control, string path)
        {
            yield return (path, control);
            for (int i = 0; i < control.Controls.Count; i++)
            {
                Control child = control.Controls[i];
                foreach ((string, Control) inner in Walk(child, path + "/" + i + ":" + child.GetType().Name))
                    yield return inner;
            }
        }

        private static string Describe(Color color)
            => color.IsNamedColor ? color.Name : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

        private static void OnUiThread(Action body, List<string> problems)
        {
            var thread = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { problems.Add("EXCEPTION: " + ex); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }
    }
}

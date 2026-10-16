using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// S0061 (LK / ER): a control must never hold a font that its owner has disposed. The one crash on
    /// record (crash.log of 2026-10-09, S0048 section 5.1 item 8, "if it recurs, crash.log carries the
    /// full stack") is exactly that stack: <c>ArgumentException: Parameter is not valid</c> out of
    /// <c>Font.ToLogFont</c> from <c>TextBox.OnHandleCreated</c> - a window handle created for a control
    /// whose <c>Font</c> had been disposed. It ended the process (Program.Fatal), and with it both hooks.
    ///
    /// The settings window swaps its own, bold and title fonts when the language changes to a script the
    /// default face cannot draw (<c>ApplyScript</c>), and several rows assign the form's own <c>Font</c>
    /// instance to a control explicitly. This walks the whole tree after every language of the product,
    /// there and back across the font families, asks every control's font for a handle and then forces
    /// every control to create its own window handle - the step that threw in the field.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class FontLifetimeTests
    {
        private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr handle);

        private static IEnumerable<Control> Walk(Control root)
        {
            yield return root;
            foreach (Control child in root.Controls)
                foreach (Control inner in Walk(child))
                    yield return inner;
        }

        /// <summary>
        /// A disposed <see cref="Font"/> answers <c>ToHfont</c> with the very exception the crash log
        /// carries. Null = the font is usable.
        /// </summary>
        private static string? FontProblem(Font font)
        {
            try
            {
                IntPtr handle = font.ToHfont();
                DeleteObject(handle);
                return null;
            }
            catch (Exception ex)
            {
                return ex.GetType().Name + ": " + ex.Message;
            }
        }

        private static string Describe(Control c)
            => c.GetType().Name + (string.IsNullOrEmpty(c.Name) ? "" : " '" + c.Name + "'")
               + (c is TextBoxBase ? "" : (c.Text.Length > 0 && c.Text.Length < 40 ? " \"" + c.Text + "\"" : ""));

        private static void OnSta(Action body)
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
            if (failure != null) throw new Exception("on the STA thread", failure);
        }

        [UiSweepFact]
        public void NoControlOfTheSettingsWindowHoldsADisposedFontAfterAnyLanguageChange()
        {
            OnSta(() =>
            {
                var config = new AppConfig { UiLanguage = "English" };
                using SettingsForm form = TestForms.NewSettings(config);
                MethodInfo apply = typeof(SettingsForm).GetMethod("ApplyLanguage", Private)!;
                var problems = new SortedSet<string>();

                // There and back across the font families, each transition once: default -> Nirmala UI
                // (hi, bn) -> YaHei UI (zh) -> default -> mirrored (ar) -> YaHei -> Nirmala -> default.
                // Thirteen languages thrice would be the same set of transitions for five minutes of CI;
                // only a change of FAMILY replaces fonts (ApplyScript returns early otherwise).
                string[] order = { "English", "हिन्दी", "বাংলা", "中文", "English", "العربية", "中文", "हिन्दी", "Русский", "اردو", "Deutsch" };
                foreach (string name in order)
                    Assert.Contains(name, Localization.Names);

                foreach (string language in order)
                {
                    config.UiLanguage = language;
                    apply.Invoke(form, null);

                    foreach (Control c in Walk(form))
                    {
                        string? bad = FontProblem(c.Font);
                        if (bad != null) problems.Add(language + ": " + Describe(c) + " holds a disposed font (" + bad + ")");
                    }
                    // Creating the window handle asks the font for a LOGFONT - what threw in the field.
                    foreach (Control c in Walk(form))
                    {
                        try { _ = c.Handle; }
                        catch (Exception ex) { problems.Add(language + ": " + Describe(c) + " cannot create its handle (" + ex.GetType().Name + ": " + ex.Message + ")"); }
                    }
                }

                Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems.Take(25)));
            });
        }

        /// <summary>
        /// The second suspect for the same stack: the live theme switch. S0048 section 5.1 met the same
        /// "Fatal during the Windows light -> dark switch" once and the 2026-10-09 01:54 crash happened at
        /// night, with nobody at the machine - when Windows itself flips the theme on a schedule. Every
        /// window of the product is built, then taken Dark -> HighContrast -> Light -> Dark -> Light the
        /// way the manager does, and after each switch every control's font must be usable and every
        /// handle creatable (ThemeApply touches borders and styles, and a style change recreates the
        /// handle - which asks the control's font for a LOGFONT).
        /// </summary>
        [Fact]
        public void NoWindowHoldsADisposedFontAfterAThemeSwitch()
        {
            OnSta(() =>
            {
                using var windows = new ThemeCoverageTests();
                var problems = new SortedSet<string>();
                foreach ((string name, Func<Form> make) in windows.Windows())
                {
                    using Form form = make();
                    foreach (ThemePalette palette in new[] { ThemePalette.Dark, ThemePalette.HighContrast, ThemePalette.Light, ThemePalette.Dark, ThemePalette.Light })
                    {
                        ThemeApply.Apply(form, palette);
                        foreach (Control c in Walk(form))
                        {
                            string? bad = FontProblem(c.Font);
                            if (bad != null) problems.Add(name + ": " + Describe(c) + " holds a disposed font (" + bad + ")");
                        }
                        foreach (Control c in Walk(form))
                        {
                            try { _ = c.Handle; }
                            catch (Exception ex) { problems.Add(name + ": " + Describe(c) + " cannot create its handle (" + ex.GetType().Name + ": " + ex.Message + ")"); }
                        }
                    }
                }
                Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems.Take(25)));
            });
        }

        /// <summary>
        /// The guard fact: the assertion above can fail. A font disposed under a control that still holds
        /// it is reported by the very probe the real test uses - otherwise a green run would prove nothing.
        /// </summary>
        [Fact]
        public void TheProbeSeesADisposedFont()
        {
            OnSta(() =>
            {
                using var live = new Font("Segoe UI", 9f);
                Assert.Null(FontProblem(live));

                var doomed = new Font("Segoe UI", 9f);
                doomed.Dispose();
                Assert.NotNull(FontProblem(doomed));
            });
        }

        /// <summary>
        /// S0066 L-1, the half the walk above cannot see: the settings rows are built <b>unparented</b>
        /// and only then attached by their Reload, and the reloads run <i>after</i> ApplyScript in every
        /// ApplyLanguage - so a row that is not attached when the family swaps (one built by a caller
        /// between reloads) holds the font the swap used to dispose. The crash on record was a handle
        /// created under exactly such a font. The row built here is never attached: across the family
        /// changes its every control must keep a usable font and be able to create its handle.
        /// </summary>
        [Fact]
        public void ARowKeptOutOfTheTreeHoldsNoDisposedFontAfterAnyLanguageChange()
        {
            OnSta(() =>
            {
                var config = new AppConfig { UiLanguage = "English" };
                using SettingsForm form = TestForms.NewSettings(config);
                MethodInfo apply = typeof(SettingsForm).GetMethod("ApplyLanguage", Private)!;
                MethodInfo rowBuilder = typeof(SettingsForm).GetMethod("LanguageHotkeyRow", Private)!;
                Assert.NotNull(rowBuilder);

                List<IntPtr> layouts = new List<IntPtr>(LanguageHotkeys.InstalledLayouts());
                Assert.True(layouts.Count > 0, "the machine has at least one installed layout");

                // One row without a chord (the value label is the form's regular font) and one with
                // (the value label is the shared bold font): both fonts used to be disposed by the
                // family swap while this row sat outside the tree.
                var problems = new SortedSet<string>();
                var held = new List<Control>();
                foreach (object? entry in new object?[]
                {
                    null,
                    new LanguageHotkeys.Entry { TargetHkl = layouts[0] },
                })
                {
                    Control row = (Control)rowBuilder.Invoke(form, new[] { layouts[0], entry })!;
                    held.Add(row);
                }

                // There and back across the families, the same transitions as the walk above.
                foreach (string language in new[] { "हिन्दी", "中文", "Русский", "हिन्दी" })
                {
                    config.UiLanguage = language;
                    apply.Invoke(form, null);

                    foreach (Control row in held)
                        foreach (Control c in Walk(row))
                        {
                            string? bad = FontProblem(c.Font);
                            if (bad != null) problems.Add(language + ": " + Describe(c) + " holds a disposed font (" + bad + ")");
                        }
                    foreach (Control row in held)
                        foreach (Control c in Walk(row))
                        {
                            try { _ = c.Handle; }
                            catch (Exception ex) { problems.Add(language + ": " + Describe(c) + " cannot create its handle (" + ex.GetType().Name + ": " + ex.Message + ")"); }
                        }
                }

                foreach (Control row in held) row.Dispose();
                Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems.Take(25)));
            });
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Operator-invoked, off-screen walk of the places that wear the exchange server's mark (<c>EXCHANGE-MARK</c> rung 3,
    /// ticket S0079): the settings page with its header band and an account entry, the sign-in dialog, and the About page's
    /// credit - each in the light, the dark and the high-contrast theme, in English and in the two right-to-left languages
    /// (<c>ar</c>, <c>ur</c>), at the display's own scale. One PNG per place; the names carry the matrix, so a person can look
    /// at them side by side. Not an xUnit test: it shows real windows (inside a hidden host, far off screen) and the sweep
    /// gate keeps those out of an ordinary run. Run <c>tools/uitest/Walk-ExchangeMark.ps1</c>. Never loads the user's settings.
    /// </summary>
    public static class ExchangeMarkWalk
    {
        public static void Run(string folder)
        {
            Application.EnableVisualStyles();
            Directory.CreateDirectory(folder);
            var original = RuntimeRegistry.UserOverride.Value;
            RuntimeRegistry.UserOverride.Value = new RuntimeDataSafetyTests.FakeRegistry();
            int written = 0;
            try
            {
                var themes = new (string Name, ThemePalette Palette)[]
                {
                    ("light", ThemePalette.Light), ("dark", ThemePalette.Dark), ("highcontrast", ThemePalette.HighContrast),
                };
                foreach (string code in new[] { "en", "ar", "ur" })
                {
                    string language = Localization.Names[Localization.IndexOf(code)];
                    var config = new AppConfig { UiLanguage = language, SettingsGroupsState = "", SettingsViewport = "" };
                    config.ExchangeClipboardAccounts = new List<ExchangeAccountEntry>
                    {
                        new ExchangeAccountEntry { Host = "nas.example.org", Login = "alice", DisplayName = "Home server" },
                        new ExchangeAccountEntry { Host = "relay.example.org", Login = "bob", SignIn = ExchangeSignInState.SignedOut },
                    };

                    using (SettingsForm settings = TestForms.NewSettings(config))
                    {
                        using var host = new Panel { Size = new Size(2000, 2000) };
                        settings.StartPosition = FormStartPosition.Manual;
                        settings.Location = new Point(-30000, -30000);
                        settings.ShowInTaskbar = false;
                        settings.TopLevel = false;
                        host.Controls.Add(settings);
                        settings.Show();
                        // tall, so the account entries and the About credit are on the capture, not below its edge
                        settings.Size = new Size(1000, 2200);
                        Application.DoEvents();
                        MethodInfo expand = typeof(SettingsForm).GetMethod("SetPageGroupsExpanded", BindingFlags.NonPublic | BindingFlags.Instance)!;
                        foreach (var (key, place) in new[] { (SettingsPageKey.ExchangeServer, "settings-exchange"), (SettingsPageKey.About, "settings-about") })
                        {
                            settings.SelectPage(key);
                            expand.Invoke(settings, new object[] { true });
                            Application.DoEvents();
                            foreach (var theme in themes)
                                written += Capture(settings, theme.Palette, Path.Combine(folder, place + "-" + code + "-" + theme.Name + ".png"));
                        }
                        settings.Hide();
                    }

                    using (var dialog = new ExchangeAccountDialog(language, new List<ExchangeAccountEntry>()))
                    {
                        using var host = new Panel { Size = new Size(1200, 1200) };
                        dialog.StartPosition = FormStartPosition.Manual;
                        dialog.Location = new Point(-30000, -30000);
                        dialog.TopLevel = false;
                        host.Controls.Add(dialog);
                        dialog.Show();
                        Application.DoEvents();
                        foreach (var theme in themes)
                            written += Capture(dialog, theme.Palette, Path.Combine(folder, "dialog-" + code + "-" + theme.Name + ".png"));
                        dialog.Hide();
                    }
                }
            }
            finally { RuntimeRegistry.UserOverride.Value = original; }
            Console.WriteLine("Exchange mark walk: " + written + " captures in " + folder);
        }

        private static int Capture(Form form, ThemePalette palette, string file)
        {
            ThemeApply.Apply(form, palette);
            form.PerformLayout();
            Application.DoEvents();
            using var image = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
            image.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            return 1;
        }
    }
}

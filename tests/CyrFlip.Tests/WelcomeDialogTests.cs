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

        private static WelcomeDialog New(string language, bool packaged = false)
        {
            var config = new AppConfig { UiLanguage = language };
            config.LayoutConversionProfiles.Add(AppConfig.FlipRow(null, true));
            return new WelcomeDialog(new WelcomeFacts(config, new[] { new InputLayouts.Installed { Klid = "00000409" } },
                packaged, true, true), _ => { }, _ => { }, () => false);
        }

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
                Assert.Empty(Walk(dialog).OfType<CheckBox>());
            });
        }
    }
}

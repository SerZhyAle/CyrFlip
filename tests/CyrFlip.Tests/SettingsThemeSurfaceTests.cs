using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The theme surface of the settings window (ticket S0060, phase P3): a disabled or read-only
    /// editor on a dark form answers <c>WM_CTLCOLORSTATIC</c> with readable dark-theme ink instead of
    /// the framework's system grey on white (H5), and the page strip mirrors for a right-to-left
    /// language (H7). The judge is the DC the message is answered with: our handler writes
    /// <c>text.disabled</c> into it, the framework default leaves it untouched.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class SettingsThemeSurfaceTests
    {
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

        private static AppConfig Config(string language = "English")
            => new AppConfig { UiLanguage = language, SettingsGroupsState = "", SettingsViewport = "" };

        private static Control? FindEditor(Control root, Func<Control, bool> kind)
        {
            foreach (Control child in root.Controls)
            {
                if (kind(child)) return child;
                Control? deeper = FindEditor(child, kind);
                if (deeper != null) return deeper;
            }
            return null;
        }

        /// <summary>Ask the receiver the question a disabled edit's parent is asked; return the DC ink.</summary>
        private static int AskCtColorStatic(IntPtr receiver, Control editor)
        {
            IntPtr hdc = CreateCompatibleDC(IntPtr.Zero);
            try
            {
                SendMessageW(receiver, WindowInterop.WM_CTLCOLORSTATIC, hdc, editor.Handle);
                return WindowInterop.GetTextColor(hdc);
            }
            finally { DeleteDC(hdc); }
        }

        [Fact]
        public void DarkFormAnswersCtColorStaticForADisabledTextBox()
        {
            OnUiThread(() =>
            {
                using var form = TestForms.NewSettings(Config());
                var _ = form.Handle;
                var box = (TextBoxBase)FindEditor(form, c => c is TextBox)!;
                ThemeApply.Apply(form, ThemePalette.Dark);
                // The framework's own answer for a message we do not take - the baseline to differ
                // from. Taken with the box switched on: most editors arrive gated by their page's
                // master switch, and an already-disabled one would answer with our own ink.
                box.Enabled = true;
                box.ReadOnly = false;
                int frameworkDefault = AskCtColorStatic(form.Handle, box);

                box.Enabled = false;
                Assert.Equal(System.Drawing.ColorTranslator.ToWin32(ThemePalette.Dark.TextDisabled),
                    AskCtColorStatic(form.Handle, box));
                Assert.NotEqual(frameworkDefault, AskCtColorStatic(form.Handle, box));

                // A read-only but enabled box stays legible too - the screenshot-folder row lives here.
                box.Enabled = true;
                box.ReadOnly = true;
                Assert.Equal(System.Drawing.ColorTranslator.ToWin32(ThemePalette.Dark.TextMuted),
                    AskCtColorStatic(form.Handle, box));

                box.ReadOnly = false;
                Assert.Equal(frameworkDefault, AskCtColorStatic(form.Handle, box));   // enabled edit answers CTLCOLOREDIT
            });
        }

        [Fact]
        public void LightFormLeavesCtColorStaticToTheSystem()
        {
            OnUiThread(() =>
            {
                using var form = TestForms.NewSettings(Config());
                var _ = form.Handle;
                var box = (TextBoxBase)FindEditor(form, c => c is TextBox)!;
                ThemeApply.Apply(form, ThemePalette.Light);
                box.Enabled = false;
                int frameworkDefault = AskCtColorStatic(form.Handle, box);
                Assert.Equal(frameworkDefault, AskCtColorStatic(form.Handle, box));
            });
        }

        [Fact]
        public void DarkNumericAnswersThroughItsOwnSubclass()
        {
            OnUiThread(() =>
            {
                using var form = TestForms.NewSettings(Config());
                var _ = form.Handle;
                NumericUpDown editor = SettingsForm.NumericEditors(form).First();
                ThemeApply.Apply(form, ThemePalette.Dark);
                // The translator page's numeric arrives disabled by its master switch; ask with it
                // switched on first, so the baseline is a message we do not take (an enabled editor
                // answers CTLCOLOREDIT, and its DC ink stays untouched).
                editor.Enabled = true;
                int frameworkDefault = AskCtColorStatic(editor.Handle, editor);
                Assert.Equal(0, frameworkDefault);

                editor.Enabled = false;
                Assert.Equal(System.Drawing.ColorTranslator.ToWin32(ThemePalette.Dark.TextDisabled),
                    AskCtColorStatic(editor.Handle, editor));

                editor.Enabled = true;
                Assert.Equal(frameworkDefault, AskCtColorStatic(editor.Handle, editor));
            });
        }

        [Fact]
        public void RightToLeftStripSitsOnTheRight()
        {
            OnUiThread(() =>
            {
                using var form = TestForms.NewSettings(Config("العربية"));
                Assert.Equal(TabAlignment.Right, form.Tabs!.Alignment);
                Assert.Equal(RightToLeft.Yes, form.RightToLeft);
            });

            OnUiThread(() =>
            {
                using var form = TestForms.NewSettings(Config());
                Assert.Equal(TabAlignment.Left, form.Tabs!.Alignment);
            });
        }

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }
}

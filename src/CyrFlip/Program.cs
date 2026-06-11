using System;
using System.Threading;
using System.Windows.Forms;

namespace CyrFlip
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // Single instance per user session — a second copy (e.g. autostart + manual launch)
            // would install a second hook and fight over the system cursor. Hold the mutex for
            // the app's lifetime; the OS releases it if we die.
            using var single = new Mutex(initiallyOwned: true, @"Local\CyrFlipSingleInstance", out bool isFirst);
            if (!isFirst)
                return;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Route UI-thread exceptions through our handler instead of the default WinForms dialog,
            // and make sure the system cursor is restored on any fatal error.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => Fatal(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Fatal(e.ExceptionObject as Exception);

            try
            {
                AppConfig config = AppConfig.Load();
                using var context = new CyrFlipContext(config);
                Application.Run(context);
            }
            catch (Exception ex)
            {
                // e.g. the keyboard hook failed to install in the context constructor.
                Fatal(ex);
            }
        }

        private static void Fatal(Exception? ex)
        {
            LayoutCursor.ForceRestore(); // never leave the system cursor replaced
            try
            {
                MessageBox.Show(
                    "CyrFlip hit an unexpected error and will close:\n\n" + (ex?.Message ?? "Unknown error"),
                    "CyrFlip", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { /* ignore */ }
            Application.Exit();
        }
    }
}

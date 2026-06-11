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

            AppConfig config = AppConfig.Load();
            using var context = new CyrFlipContext(config);
            Application.Run(context);
        }
    }
}

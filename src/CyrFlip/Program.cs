using System;
using System.Windows.Forms;

namespace CyrFlip
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppConfig config = AppConfig.Load();
            using var context = new CyrFlipContext(config);
            Application.Run(context);
        }
    }
}

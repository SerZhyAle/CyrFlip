using System;
using System.Threading;
using System.Windows.Forms;

namespace CyrFlip
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // The only arguments CyrFlip ever treats as commands are the launcher protocol's
            // (/launcher-run:{guid}, /launcher-settings, /exit) - anything else is ignored.
            string? command = LauncherIpc.ParseCommand(args);

            // Single instance per user session - a second copy (e.g. autostart + manual launch)
            // would install a second hook and fight over the system cursor. Hold the mutex for
            // the app's lifetime; the OS releases it if we die.
            using var single = new Mutex(initiallyOwned: true, @"Local\CyrFlipSingleInstance", out bool isFirst);
            if (!isFirst)
            {
                // A Jump List task landed while CyrFlip is alive: hand the command to the live
                // instance over the named pipe and leave. A second hook or tray icon is never
                // created (spec §8.2); a plain double-click of the exe stays a silent no-op.
                if (command != null)
                    LauncherIpc.TrySend(command);
                return;
            }

            if (command == LauncherIpc.ExitCommand)
                return; // nothing is running - "Exit CyrFlip" from a stale Jump List is a no-op

            Guid? oneShotRun = command != null ? LauncherIpc.RunId(command) : null;
            if (oneShotRun != null)
            {
                // One-shot-and-exit (spec §7.2 / OneClickRunner parity): run the scenario without
                // constructing the hook, tray or indicator. Release the mutex first - a yt-dlp link
                // prompt can sit open for minutes and must never block a real CyrFlip launch.
                single.ReleaseMutex();
                RunScenarioOneShot(oneShotRun.Value);
                return;
            }

            // Keep the autostart path in sync with whichever exe is actually running.
            // If the user enabled "Start with Windows" from a different build location,
            // silently update the registry entry to this exe's path on every launch.
            if (Autostart.IsEnabled)
                Autostart.Set(true);

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
                // "Manage scenarios" from the Jump List while CyrFlip was not running: become the
                // normal (single) instance and open the settings window once the tray is up.
                using var context = new CyrFlipContext(config, openSettingsOnStart: command == LauncherIpc.SettingsCommand);
                Application.Run(context);
            }
            catch (Exception ex)
            {
                // e.g. the keyboard hook failed to install in the context constructor.
                Fatal(ex);
            }
        }

        /// <summary>
        /// The OneClickRunner-equivalent one-shot launch: load the store fresh, run the scenario,
        /// surface a failure as a blocking dialog (there is no tray to show a balloon), exit. A
        /// disabled launcher ignores the command entirely - a stale Jump List task must not execute
        /// anything (spec §4).
        /// </summary>
        private static void RunScenarioOneShot(Guid id)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                AppConfig config = AppConfig.Load();
                if (!config.EnableScenarioLauncher)
                {
                    LauncherLog.Log("One-shot: launcher disabled, ignoring /launcher-run");
                    return;
                }

                var store = new LauncherScenarioStore();
                LauncherScenario? scenario = store.Find(id);
                string T(string ru) => Localization.Translate(config.UiLanguage, ru);
                if (scenario == null)
                {
                    LauncherLog.Log("One-shot: scenario not found: " + id);
                    MessageBox.Show(T("Сценарий не найден — обновите Jump List, открыв CyrFlip."),
                        "CyrFlip", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                LauncherLaunchResult result = LauncherExecution.Launch(scenario, T, () =>
                {
                    using var prompt = new YtDlpLinkDialog(config.UiLanguage);
                    return prompt.ShowDialog() == DialogResult.OK ? prompt.Link : null;
                });
                if (!result.Success && !result.Cancelled)
                    MessageBox.Show(string.Format(T("Не удалось запустить «{0}»: {1}"), scenario.Name, result.ErrorMessage),
                        "CyrFlip", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                LauncherLog.Log("One-shot: unexpected error: " + ex.Message);
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

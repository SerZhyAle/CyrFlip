using System;
using System.Threading;
using System.Threading.Tasks;
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
                // constructing the hook, tray or indicator. Give the mutex up first - a yt-dlp link
                // prompt can sit open for minutes and must never block a real CyrFlip launch.
                // Releasing ownership is not enough: `createdNew` asks whether the named object
                // exists, not whether anyone owns it, so the handle has to be closed as well.
                single.ReleaseMutex();
                single.Dispose();
                RunScenarioOneShot(oneShotRun.Value);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Route UI-thread exceptions through containment instead of the default WinForms dialog
            // (D3, S0061: contain per handler, exit only on a run of faults - S0065 E-2): one fault
            // in one handler costs that handler, not the tray, the indicator and both hooks. What
            // reaches the UI thread from outside it - an unhandled exception on a worker - is still
            // terminal, so that stays Fatal.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => FaultContainment.Contain(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Fatal(e.ExceptionObject as Exception);
            // A task tail nobody observed used to vanish without a line anywhere (S0065 E-10):
            // the fault goes to crash.log - in the log bundle since E-11 - and is marked observed,
            // because an unobserved exception's default end is the process.
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                try
                {
                    DiagnosticLog.Append(DiagnosticLog.Path("crash.log"),
                        Stamp(DateTime.Now) + " - unobserved task: " + (e.Exception?.ToString() ?? "null"));
                }
                catch { /* diagnostics must never affect the app */ }
                e.SetObserved();
            };

            // Keep the autostart entry pointing at whichever exe is actually running (the user may
            // have enabled "Start with Windows" from another build location). After the handlers,
            // and never throwing: a Run key the policy denies must not stop CyrFlip from starting.
            Autostart.SyncToThisExe();

            try
            {
                AppConfig config = AppConfig.Load();
                // No installer puts the portable build in the Start menu or on the desktop - the first
                // run does, and later runs keep those links pointing at this exe. Never throws.
                AppShortcuts.SyncOnStartup(config);
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

        /// <summary>One time stamp in the format crash.log lines start with.</summary>
        internal static string Stamp(DateTime moment)
            => moment.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);

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
                // The link prompt and the error box of this process follow the theme too (S0020).
                ThemeManager.Initialize(ThemeModes.Parse(config.Theme));

                var store = new LauncherScenarioStore();
                LauncherScenario? scenario = store.Find(id);
                string T(string ru) => Localization.Translate(config.UiLanguage, ru);
                if (scenario == null)
                {
                    LauncherLog.Log("One-shot: scenario not found: " + id);
                    ConfirmDialog.Show(config.UiLanguage, T("Сценарий не найден — обновите Jump List, открыв CyrFlip."),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                LauncherLaunchResult result = LauncherExecution.Launch(scenario, T, () =>
                {
                    using var prompt = new YtDlpLinkDialog(config.UiLanguage);
                    return prompt.ShowDialog() == DialogResult.OK ? prompt.Link : null;
                });
                if (!result.Success && !result.Cancelled)
                    ConfirmDialog.Show(config.UiLanguage, Localization.Format(T, "Не удалось запустить «{0}»: {1}", scenario.Name, result.ErrorMessage),
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                LauncherLog.Log("One-shot: unexpected error: " + ex.Message);
            }
            finally
            {
                // The process ends right after this: the log's background writer gets its lines out first.
                DiagnosticLog.Flush(TimeSpan.FromSeconds(1));
            }
        }

        internal static void Fatal(Exception? ex)
        {
            // The exception itself goes to the log before anything else runs: a crash report that
            // names only the log folder but not the failure makes every crash undiagnosable
            // (APP-BEHAVIOUR rule 6 - the raw exception belongs to the log, never to the user).
            try
            {
                DiagnosticLog.Append(DiagnosticLog.Path("crash.log"), Stamp(DateTime.Now) + " - fatal: "
                    + (ex?.ToString() ?? "null"));
            }
            catch { /* diagnostics must never affect the app */ }
            LayoutCursor.ForceRestore(); // never leave the system cursor replaced
            // Nor the layout files: the VS Code extension would keep drawing the last layout until
            // CyrFlip ran again (LAYOUT-SIGNAL rule 6). Only the primary instance gets here - the
            // forwarding and one-shot paths return before these handlers are installed.
            LayoutPublisher.Retract();
            DiagnosticLog.Flush(TimeSpan.FromMilliseconds(500));
            try
            {
                // The system box, deliberately, and the one left in the app (ThemeSourceGateTests): the
                // process is going down, and this is the dialog least likely to fail with it. What it
                // says changed with S0065 E-1: localized from the saved UI language, no data-folder
                // path (the account name), and the one thing the old text never said - that every
                // hotkey just went with the process, so CyrFlip has to be started again.
                MessageBox.Show(FatalBoxText(SavedUiLanguage()), "CyrFlip", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { /* ignore */ }
            Application.Exit();
        }

        /// <summary>The localized fatal sentence (S0065 E-1) - testable without closing anything.</summary>
        internal static string FatalBoxText(string language)
            => Localization.Translate(language,
                "CyrFlip столкнулся с непредвиденной ошибкой и закрывается. Все горячие клавиши отключены — запустите CyrFlip снова.");

        /// <summary>
        /// The saved UI language, which is what the fatal box speaks; the OS default when even the
        /// registry read fails. The old box was English-only in all thirteen.
        /// </summary>
        internal static string SavedUiLanguage()
        {
            try
            {
                string saved = AppConfig.Load().UiLanguage;
                if (saved.Length > 0) return saved;
            }
            catch { /* the box still needs a language */ }
            return Localization.DefaultLanguage();
        }
    }
}

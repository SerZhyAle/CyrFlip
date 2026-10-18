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
                // A second launch while CyrFlip is alive hands its request to the live instance over
                // the named pipe and leaves; a second hook or tray icon is never created (spec §8.2).
                // A Jump List task forwards its own command. A plain launch - the exe double-clicked,
                // the Start menu entry - asks for the settings window: a second launch must not vanish
                // (APP-ACTIVATION rule 7C, ticket S0060 P7-n), and a tray app has no other window to
                // raise. When nobody takes the request the user is told, once, in words - except for
                // /exit, which the build scripts send unattended and which must never wait for a click.
                string forwarded = SecondLaunchCommand(command);
                bool delivered = LauncherIpc.TrySend(forwarded);
                if (SecondLaunchOwesAnAnswer(forwarded, delivered))
                    TellAlreadyRunning();
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

            // Restore autostart when enabled in CyrFlip and keep it pointing at this exe (the user may
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
                using var context = new CyrFlipContext(config, openSettingsOnStart: command == LauncherIpc.SettingsCommand, initialCommand: command);
                Application.Run(context);
            }
            catch (Exception ex)
            {
                // e.g. the keyboard hook failed to install in the context constructor.
                Fatal(ex);
            }
        }

        /// <summary>
        /// What a second launch forwards to the live instance: the command it was started with, or - for
        /// a plain launch - the settings window. The command already exists in the protocol, so an older
        /// copy that is still running (the two versions side by side) understands it too.
        /// </summary>
        internal static string SecondLaunchCommand(string? parsedCommand)
            => parsedCommand ?? LauncherIpc.SettingsCommand;

        /// <summary>
        /// Whether a second launch whose request <paramref name="delivered"/> or not still owes the user
        /// an answer. Every request nobody took does, but <c>/exit</c>: build and release scripts send it
        /// to stop the tray, and a dialog there would hold a pipeline until somebody clicks it.
        /// </summary>
        internal static bool SecondLaunchOwesAnAnswer(string forwardedCommand, bool delivered)
            => !delivered && forwardedCommand != LauncherIpc.ExitCommand;

        /// <summary>
        /// The live instance did not take the request (hung, still starting, or running in another
        /// session): say so in the user's language and end. A second copy is not started in its place -
        /// two keyboard hooks would be worse than the lost click. The process has no tray and no hooks,
        /// like the one-shot launch, so the box is the only surface it can use.
        /// </summary>
        private static void TellAlreadyRunning()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                AppConfig config = AppConfig.Load();
                ThemeManager.Initialize(ThemeModes.Parse(config.Theme));
                ConfirmDialog.Show(config.UiLanguage,
                    Localization.Translate(config.UiLanguage,
                        "CyrFlip is already running but is not answering. Check the notification area, or end it in Task Manager."),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                LauncherLog.Log("Second launch: could not tell the user: " + ex.GetType().Name);
            }
            finally
            {
                DiagnosticLog.Flush(TimeSpan.FromSeconds(1));
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
                string T(string en) => Localization.Translate(config.UiLanguage, en);
                if (scenario == null)
                {
                    LauncherLog.Log("One-shot: scenario not found: " + id);
                    ConfirmDialog.Show(config.UiLanguage, T("The scenario was not found — open CyrFlip to refresh the Jump List."),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                LauncherLaunchResult result = LauncherExecution.Launch(scenario, T, () =>
                {
                    using var prompt = new YtDlpLinkDialog(config.UiLanguage);
                    return prompt.ShowDialog() == DialogResult.OK ? prompt.Link : null;
                });
                if (!result.Success && !result.Cancelled)
                    ConfirmDialog.Show(config.UiLanguage, Localization.Format(T, "Couldn't start \"{0}\": {1}", scenario.Name, result.ErrorMessage),
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
                "CyrFlip hit an unexpected error and is closing. Every hotkey is now off - start CyrFlip again.");

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

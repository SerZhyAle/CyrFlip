using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CyrFlip
{
    /// <summary>
    /// The second process of a command (APP-COMMANDS rules 6 to 10, ticket S0107): it never builds the tray, a hook
    /// or a window. It answers <c>commands</c> and <c>version</c> itself (item C, <c>forward: never</c>), carries every
    /// other verb to the running copy and exits with the answer's code, or - with nothing running - does what the
    /// verb's <c>coldStart</c> says. What it prints goes to the console that started it, and the two channels a caller
    /// can always rely on are the exit code and the <c>--result</c> file (rule 9).
    /// </summary>
    internal static class AppCommandRunner
    {
        /// <summary>How long a cold start waits for the started copy to take commands.</summary>
        internal const int StartReadyMs = 20000;

        // ------------------------------------------------------------------ entry points

        /// <summary>A command line that is not a command of this copy: exit 10, nothing executed (rule 3).</summary>
        public static int Refuse(AppCommandLineResult parsed)
        {
            Func<string, string> T = Translator();
            string message = parsed.MessageArgument.Length > 0
                ? Localization.Format(T, parsed.Message, parsed.MessageArgument)
                : T(parsed.Message);
            var response = AppCommandResponse.Refused(parsed.Reason, AppCommandExit.Usage, message);
            Print(response, json: false);
            return response.ExitCode;
        }

        /// <summary>A <c>forward: never</c> verb, answered by this process from what it holds.</summary>
        public static int RunLocal(AppCommandInvocation invocation)
        {
            bool json = invocation.Flag("json");
            AppCommandManifest manifest = AppCommandManifest.Shipped;
            if (invocation.Spec.Name == "commands")
            {
                if (json) WriteOut(manifest.Raw);
                else WriteOut(Encoding.UTF8.GetBytes(manifest.ReadableList()));
                return AppCommandExit.Done;
            }
            // version
            string version = SupportBundle.AppVersion();
            if (json)
                WriteOut(Encoding.UTF8.GetBytes(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(
                    new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["product"] = "CyrFlip", ["version"] = version, ["cliVersion"] = manifest.CliVersion,
                    }) + "\n"));
            else
                WriteOut(Encoding.UTF8.GetBytes("CyrFlip " + version + " (commands " + manifest.CliVersion + ")\n"));
            return AppCommandExit.Done;
        }

        /// <summary>The running copy runs elevated and this caller cannot open what it holds (rule 11, <c>elevation</c>).</summary>
        public static int Elevated(AppCommandInvocation invocation)
            => Finish(invocation, AppCommandResponse.Refused(AppCommandReasons.Elevation, AppCommandExit.Permission,
                Translator()("CyrFlip runs as administrator. Run this command as administrator too.")));

        /// <summary>A copy is running: hand it the command and wait for its answer.</summary>
        public static int Forward(AppCommandInvocation invocation)
            => Finish(invocation, Deliver(invocation));

        /// <summary>
        /// Nothing is running; the caller still holds the single-instance mutex it just created and must have released
        /// it before this is called. The verb's <c>coldStart</c> decides (rule 10).
        /// </summary>
        public static int ColdStart(AppCommandInvocation invocation)
        {
            Func<string, string> T = Translator();
            switch (invocation.Spec.ColdStart)
            {
                case "refuse":
                    return Finish(invocation, AppCommandResponse.Refused(AppCommandReasons.NotRunning, AppCommandExit.NotReady,
                        T("CyrFlip is not running.")));
                case "oneshot":
                    return Finish(invocation, RunOneShot(invocation, T), log: true);
                default:
                    return Finish(invocation, StartThenForward(invocation, T));
            }
        }

        // ------------------------------------------------------------------ the work

        private static AppCommandResponse RunOneShot(AppCommandInvocation invocation, Func<string, string> T)
        {
            if (invocation.Spec.Name == "quit")
                return AppCommandResponse.Done(T("CyrFlip was not running - nothing to close."));
            switch (invocation.Spec.Name)
            {
                case "layout list":
                    // The same call the running copy makes, without its tracked window: no "current" here.
                    return AppCommandsLayoutText.LayoutList(IndicatorLayoutHost.InstalledEntries(), null, running: false, T);
                case "text convert":
                    return AppCommandsLayoutText.TextConvert(invocation, IndicatorLayoutHost.InstalledEntries(),
                        AppCommandsLayoutText.ProductConverter, T);
                case "text flip-case":
                    return AppCommandsLayoutText.TextFlipCase(invocation, T);
                case "scenario list":
                case "chords list":
                case "diagnostics paths":
                case "diagnostics bundle":
                    return AppCommandsInspection.OneShot(invocation, Environment.CurrentDirectory, DateTime.Now);
                case "scenario run":
                case "autostart set":
                case "cursor restore":
                    break;
                default:
                    return AppCommandResponse.Refused(AppCommandReasons.UnknownVerb, AppCommandExit.Usage, T("CyrFlip is not running."));
            }

            AppConfig config = AppConfig.Load();
            T = en => Localization.Translate(config.UiLanguage, en);
            AppCommandResponse? refusal = AppCommandVerbs.RiskGate(invocation.Spec, config.EnableExternalCommands, legacy: false, T);
            if (refusal != null) return refusal;
            // The two oneshot state verbs (rows C43 and C44, ticket S0125): both writes-user-data, so the gate above is
            // what answers 12 with the setting off - the same gate the running copy runs before its dispatcher.
            if (invocation.Spec.Name == "autostart set")
                return AppCommandsStateSwitches.AutostartSet(invocation, OneShotAutostart, T);
            if (invocation.Spec.Name == "cursor restore")
                return AppCommandsStateSwitches.CursorRestore(LayoutCursor.ReloadScheme, T);
            if (!config.EnableScenarioLauncher)
                return AppCommandVerbs.ModuleOff(T("The scenario launcher is switched off. Turn it on in the settings of CyrFlip."));
            var store = new LauncherScenarioStore();
            refusal = AppCommandVerbs.PickScenario(store.All, invocation, T, out LauncherScenario? scenario);
            if (refusal != null) return refusal;
            LauncherLaunchResult result = LauncherExecution.Launch(scenario!, T, AppCommandVerbs.LinkFrom(invocation));
            return AppCommandVerbs.Launched(scenario!, result, T);
        }

        /// <summary>
        /// <c>coldStart: start</c>: start CyrFlip as the tray copy (with no arguments, as a person would), wait until it
        /// takes commands, then hand it the command like any second process. The caller gets the running copy's answer
        /// and exit code, and the tray copy goes on running after this process ends.
        /// </summary>
        /// <summary>
        /// The autostart verb's one-shot host: the page's own registry call - the Run key and the machine-local intent
        /// pair - with no tray item to follow (no tray exists in this process) and no dialog (a command never waits on a
        /// person, rule 4).
        /// </summary>
        private sealed class OneShotAutostartHost : IAutostartCommandsHost
        {
            public static readonly OneShotAutostartHost Instance = new OneShotAutostartHost();
            public bool AutostartManagedByWindows => Autostart.ManagedByWindows;
            public void SetAutostart(bool enabled) => Autostart.Set(enabled);
        }
        private static readonly OneShotAutostartHost OneShotAutostart = OneShotAutostartHost.Instance;

        private static AppCommandResponse StartThenForward(AppCommandInvocation invocation, Func<string, string> T)
        {
            try
            {
                using Process? started = Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = false });
                if (started == null)
                    return AppCommandResponse.Failed(AppCommandReasons.Error, AppCommandExit.Failed, T("CyrFlip could not be started."));
            }
            catch (Exception ex)
            {
                LauncherLog.Log("Command: cold start failed: " + ex.GetType().Name);
                return AppCommandResponse.Failed(AppCommandReasons.Error, AppCommandExit.Failed, T("CyrFlip could not be started."));
            }

            int began = Environment.TickCount;
            while (unchecked(Environment.TickCount - began) < StartReadyMs)
            {
                var probe = NewRequest("status", new string[0]);
                AppCommandDelivery delivery = LauncherIpc.SendRequest(probe, 2000, out _, deliveryMs: 500);
                if (delivery == AppCommandDelivery.Answered) return Deliver(invocation);
                Thread.Sleep(200);
            }
            return AppCommandResponse.Failed(AppCommandReasons.Timeout, AppCommandExit.NotProven, T("CyrFlip did not get ready in time."));
        }

        /// <summary>Send the command and read what came of it (rules 7 and 8, item K).</summary>
        internal static AppCommandResponse Deliver(AppCommandInvocation invocation, string? pipeName = null)
        {
            Func<string, string> T = Translator();
            AppCommandRequest request = NewRequest(invocation.Spec.Name, invocation.Args);
            int completion = invocation.Spec.TimeoutMs > 0 ? invocation.Spec.TimeoutMs : 10000;
            AppCommandDelivery delivery = LauncherIpc.SendRequest(request, completion, out AppCommandResponse? response, pipeName: pipeName);
            switch (delivery)
            {
                case AppCommandDelivery.Answered:
                    return response!;
                case AppCommandDelivery.OlderCopy:
                    // The old copy still does what it always did for a verb that has a frozen spelling; the end of
                    // that work is not seen, so the answer is "not proven" either way (item K paragraph 2).
                    string? legacy = AppCommandVerbs.LegacyFormOf(invocation);
                    if (legacy != null) LauncherIpc.TrySend(legacy, pipeName: pipeName);
                    return AppCommandResponse.Failed(AppCommandReasons.Error, AppCommandExit.NotProven,
                        T("The running CyrFlip is older and does not take commands. Restart CyrFlip, then try again."));
                case AppCommandDelivery.TimedOut:
                    return AppCommandResponse.Failed(AppCommandReasons.Timeout, AppCommandExit.NotProven,
                        T("CyrFlip did not answer in time. It may still be working."));
                case AppCommandDelivery.Unreadable:
                    return AppCommandResponse.Failed(AppCommandReasons.Error, AppCommandExit.NotProven,
                        T("CyrFlip did not give a readable answer."));
                case AppCommandDelivery.NotOurs:
                    return AppCommandResponse.Refused(AppCommandReasons.NotRunning, AppCommandExit.NotReady, T("CyrFlip is not running."));
                default:
                    // The copy holds the mutex but takes no connection in time: busy, never a hang (item J).
                    return AppCommandResponse.BusyNow(T("CyrFlip is busy: a window there waits for you. Try again when it is closed."));
            }
        }

        internal static AppCommandRequest NewRequest(string command, System.Collections.Generic.IReadOnlyList<string> args)
        {
            string dir;
            try { dir = Environment.CurrentDirectory; }
            catch (Exception) { dir = ""; } // a deleted current folder leaves a relative path unresolvable, nothing else
            var copy = new string[args.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = args[i];
            return new AppCommandRequest
            {
                Command = command, Args = copy, WorkingDir = dir, Origin = "cli",
                RequestId = Guid.NewGuid().ToString("D"),
            };
        }

        // ------------------------------------------------------------------ the answer

        /// <summary>Print, write <c>--result</c>, log when this process did the work, and return the exit code.</summary>
        private static int Finish(AppCommandInvocation invocation, AppCommandResponse response, bool log = false)
        {
            if (log)
            {
                using Process self = Process.GetCurrentProcess();
                AppCommandsLog.Log(invocation.Spec.Name, "cli", (uint)self.Id, response);
            }
            string? resultPath = invocation.Option("result");
            if (resultPath != null && !WriteResult(resultPath, response))
            {
                response = AppCommandResponse.Failed(AppCommandReasons.IoError, AppCommandExit.Failed,
                    Translator()("The answer could not be written to the file you named."));
            }
            Print(response, invocation.Flag("json"));
            DiagnosticLog.Flush(TimeSpan.FromSeconds(1));
            return response.ExitCode;
        }

        /// <summary>
        /// The response <c>data</c> into the file the caller named, whole or not at all (rule 9, case C11): a temporary
        /// file beside it, renamed over the target. A relative path is the caller's working directory's.
        /// </summary>
        internal static bool WriteResult(string path, AppCommandResponse response)
        {
            string temp = "";
            try
            {
                string target = Path.GetFullPath(path);
                temp = target + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
                File.WriteAllText(temp, AppCommandProtocol.DataJson(response), new UTF8Encoding(false));
                if (File.Exists(target)) File.Replace(temp, target, null);
                else File.Move(temp, target);
                return true;
            }
            catch (Exception ex)
            {
                LauncherLog.Log("Command: --result not written: " + ex.GetType().Name);
                try { if (temp.Length > 0 && File.Exists(temp)) File.Delete(temp); }
                catch (Exception cleanup) { LauncherLog.Log("Command: --result temp left: " + cleanup.GetType().Name); }
                return false;
            }
        }

        private static void Print(AppCommandResponse response, bool json)
        {
            string text = json ? AppCommandProtocol.Serialize(response) : response.Message;
            if (text.Length > 0) WriteOut(Encoding.UTF8.GetBytes(text + "\n"));
        }

        /// <summary>
        /// Standard output of a window-subsystem process: the handle a caller redirected, else the console of the
        /// parent (<c>AttachConsole</c>), else nowhere - the exit code still carries the answer.
        /// </summary>
        private static void WriteOut(byte[] bytes)
        {
            try
            {
                IntPtr handle = WindowInterop.GetStdHandle(WindowInterop.STD_OUTPUT_HANDLE);
                if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                    WindowInterop.AttachConsole(WindowInterop.ATTACH_PARENT_PROCESS);
                using Stream output = Console.OpenStandardOutput();
                output.Write(bytes, 0, bytes.Length);
                output.Flush();
            }
            catch (Exception ex)
            {
                LauncherLog.Log("Command: nothing printed: " + ex.GetType().Name);
            }
        }

        /// <summary>
        /// The interface language for the sentences this process says itself. Read as the one value, not through
        /// <see cref="AppConfig.Load"/>: a second process decides its role before it loads the settings (rule 6).
        /// </summary>
        private static Func<string, string> Translator()
        {
            string language = "";
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\CyrFlip");
                language = key?.GetValue("UiLanguage") as string ?? "";
            }
            catch (Exception ex) { LauncherLog.Log("Command: language not read: " + ex.GetType().Name); }
            if (language.Length == 0) language = Localization.DefaultLanguage();
            return en => Localization.Translate(language, en);
        }
    }
}

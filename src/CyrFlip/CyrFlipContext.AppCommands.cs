using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The running copy's side of the command line (APP-COMMANDS 0.2, ticket S0107). A request arrives on the
    /// listener's thread; it is reparsed with the same parser a cold start uses, gated, and run on the UI thread.
    /// The UI thread has <see cref="PickUpMs"/> to take it - shorter than the caller's delivery bound - or the answer
    /// is <c>busy</c> and the command is not run later (item J). A modal window or the first-run tour makes every
    /// command <c>busy</c> (rule 10). Each command taken is one line of <c>app-commands.log</c> (rule 16).
    /// </summary>
    internal sealed partial class CyrFlipContext
    {
        /// <summary>How long the UI thread has to take a command before the answer is <c>busy</c>.</summary>
        internal const int PickUpMs = 800;

        /// <summary>Called on the listener's thread for every profile request; never throws.</summary>
        private AppCommandResponse OnAppCommandRequest(AppCommandRequest request, uint callerPid)
        {
            AppCommandResponse response = AnswerAppCommand(request);
            AppCommandsLog.Log(request.Command.Length <= 40 ? request.Command : "?", request.Origin, callerPid, response);
            return response;
        }

        /// <summary>
        /// One request, answered. <paramref name="remote"/> is a request another device of the exchange account sent
        /// (REMOTE-CONTROL, ticket CYRFLP-0140): rules 8 to 10 there have already decided it, so the local setting of
        /// APP-COMMANDS rule 13 is not asked (REMOTE-CONTROL rule 13).
        /// </summary>
        private AppCommandResponse AnswerAppCommand(AppCommandRequest request, bool remote = false)
        {
            AppCommandSpec? spec = AppCommandManifest.Shipped.Find(request.Command);
            // A verb this copy does not know - also what a newer second process meets in an older copy (section 3).
            if (spec == null || spec.Forward == "never")
                return AppCommandResponse.Refused(AppCommandReasons.UnknownVerb, AppCommandExit.Usage,
                    Localization.Format(T, "Unknown command \"{0}\". Run \"CyrFlip.exe commands\" to see the list.",
                        request.Command.Length <= 40 ? request.Command : request.Command.Substring(0, 40) + ".."));
            AppCommandLineResult parsed = AppCommandLine.ParseOptions(spec, request.Args);
            if (parsed.Kind != AppCommandLineKind.Command || parsed.Invocation == null)
                return AppCommandResponse.Refused(parsed.Reason, AppCommandExit.Usage,
                    parsed.MessageArgument.Length > 0 ? Localization.Format(T, parsed.Message, parsed.MessageArgument) : T(parsed.Message));
            AppCommandInvocation invocation = parsed.Invocation;

            SynchronizationContext? ui = _ui;
            if (ui == null || _disposed)
                return AppCommandResponse.BusyNow(T("CyrFlip is busy: a window there waits for you. Try again when it is closed."));

            // 0 waiting, 1 taken by the UI thread, 2 given up by the listener: exactly one of the two wins.
            int state = 0;
            Task<AppCommandResponse>? work = null;
            using var taken = new ManualResetEventSlim(false);
            ui.Post(_ =>
            {
                if (Interlocked.CompareExchange(ref state, 1, 0) != 0) return;
                try { work = RunAppCommandOnUi(invocation, request.Legacy, request.WorkingDir, remote); }
                catch (Exception ex)
                {
                    LauncherLog.Log("Command: " + spec.Name + " failed: " + ex.GetType().Name);
                    work = Task.FromResult(AppCommandResponse.Failed(AppCommandReasons.Error, AppCommandExit.Failed,
                        T("The command failed. The log bundle on the About page has the details.")));
                }
                finally { taken.Set(); }
            }, null);

            if (!taken.Wait(PickUpMs) && Interlocked.CompareExchange(ref state, 2, 0) == 0)
                return AppCommandResponse.BusyNow(T("CyrFlip is busy: a window there waits for you. Try again when it is closed."));
            taken.Wait();
            Task<AppCommandResponse> result = work!;
            int bound = spec.TimeoutMs > 0 ? spec.TimeoutMs : 10000;
            if (!result.Wait(bound))
                // The caller stops waiting at the same bound and says "not proven"; this line is only for a caller
                // that reads it itself.
                return AppCommandResponse.Failed(AppCommandReasons.Timeout, AppCommandExit.NotProven,
                    T("CyrFlip did not answer in time. It may still be working."));
            return result.Result;
        }

        /// <summary>A modal window of ours, the first-run tour or the backup question is open: rule 10's <c>busy</c>.</summary>
        private bool AppCommandsBusy()
        {
            if (_welcomePending || _welcome != null) return true;
            foreach (Form form in Application.OpenForms)
                if (form.Visible && form.Modal) return true;
            return false;
        }

        /// <summary>
        /// One command, on the UI thread. The task completes when the verb's work has ended (or was accepted).
        /// <paramref name="workingDir"/> is the caller's current directory, which a verb that takes a path resolves a
        /// relative one against (rule 7, <c>workingDir</c>).
        /// </summary>
        private Task<AppCommandResponse> RunAppCommandOnUi(AppCommandInvocation invocation, bool legacy, string workingDir, bool remote = false)
        {
            AppCommandSpec spec = invocation.Spec;
            if (AppCommandsBusy())
                return Done(AppCommandResponse.BusyNow(T("CyrFlip is busy: a window there waits for you. Try again when it is closed.")));
            AppCommandResponse? refusal = remote ? null : AppCommandVerbs.RiskGate(spec, _config.EnableExternalCommands, legacy, T);
            if (refusal != null) return Done(refusal);

            switch (spec.Name)
            {
                case "status":
                    return Done(AppCommandResponse.Done(Localization.Format(T, "CyrFlip {0} is running and ready.", SupportBundle.AppVersion()),
                        StatusData()));
                case "show":
                    ShowSettings(AppCommandVerbs.PageOf(invocation.Option("page")));
                    return Done(AppCommandResponse.Done(T("Done.")));
                case "quit":
                    AppCommandResponse closing = AppCommandResponse.Done(T("CyrFlip is closing."));
                    // Only once the answer has reached the caller: closing first would take the pipe with it.
                    closing.AfterSent = () => _ui?.Post(_ => ExitThread(), null);
                    return Done(closing);
                case "clipboard send":
                case "clipboard get":
                    return RunClipboardExchangeCommand(invocation, workingDir);
                case "clipboard-history list":
                case "clipboard-history put":
                case "clipboard-history pause":
                    return RunHistoryCommand(invocation);
                case "scenario list":
                case "chords list":
                case "diagnostics paths":
                case "diagnostics bundle":
                    return RunInspectionCommand(invocation, workingDir);
                case "scenario run":
                    return RunScenarioCommand(invocation);
                case "note add":
                case "note list":
                case "note get":
                case "note export":
                    return RunNoteCommand(invocation, workingDir);
                case "layout list":
                case "layout current":
                case "layout switch":
                case "text convert":
                case "text flip-case":
                    return RunLayoutTextCommand(invocation);
                case "screenshot window":
                case "screenshot region":
                case "screenshot full":
                    return RunScreenshotCommand(invocation, workingDir);
                case "trafficlight status":
                case "trafficlight check":
                    return RunTrafficLightCommand(invocation);
                case "note show":
                case "clipboard-history show":
                case "symbols show":
                case "trafficlight show":
                    return Done(RunWindowCommand(invocation));
                case "keepawake set":
                case "theme set":
                case "autostart set":
                case "cursor restore":
                    return RunStateSwitchCommand(invocation);
                case "hotkeys pause":
                    return Done(RunHotkeysPause(invocation));
                case "translate text":
                    return RunTranslateCommand(invocation);
                case "selection convert":
                case "selection flip-case":
                case "selection note":
                case "selection translate":
                    return RunSelectionCommand(invocation);
                default:
                    return Done(AppCommandResponse.Refused(AppCommandReasons.UnknownVerb, AppCommandExit.Usage,
                        Localization.Format(T, "Unknown command \"{0}\". Run \"CyrFlip.exe commands\" to see the list.", spec.Name)));
            }
        }

        /// <summary>
        /// The four read-only verbs of ticket S0111. The bundle packs every log, which takes long enough to matter, so it runs on
        /// the pool (the caller's bound is the manifest's minute); the other three answer at once from what the UI thread holds.
        /// </summary>
        private Task<AppCommandResponse> RunInspectionCommand(AppCommandInvocation invocation, string workingDir)
        {
            AppConfig config = _config;
            Func<string, string> translate = T;
            DateTime stamp = DateTime.Now;
            if (invocation.Spec.Name == "diagnostics bundle")
                return Task.Run(() => AppCommandsInspection.Run(invocation, config, workingDir, stamp, translate, SupportBundle.LogDirectory));
            return Done(AppCommandsInspection.Run(invocation, config, workingDir, stamp, translate, SupportBundle.LogDirectory));
        }

        private Task<AppCommandResponse> RunScenarioCommand(AppCommandInvocation invocation)
        {
            if (!_config.EnableScenarioLauncher)
                return Done(AppCommandVerbs.ModuleOff(T("The scenario launcher is switched off. Turn it on in the settings of CyrFlip.")));
            // Re-read, as a Jump List task is: a command fired right after an edit runs the current version.
            _launcherStore.Reload();
            AppCommandResponse? refusal = AppCommandVerbs.PickScenario(_launcherStore.All, invocation, T, out LauncherScenario? scenario);
            if (refusal != null) return Done(refusal);
            LauncherScenario chosen = scenario!;
            return LauncherExecution.LaunchAsync(chosen, T, AppCommandVerbs.LinkFrom(invocation)).ContinueWith(task =>
            {
                LauncherLaunchResult result = task.Status == TaskStatus.RanToCompletion
                    ? task.Result
                    : LauncherLaunchResult.Fail(task.Exception != null ? FailureCause.Describe(task.Exception.GetBaseException(), _config.UiLanguage) : "");
                return AppCommandVerbs.Launched(chosen, result, T);
            }, TaskScheduler.Default);
        }

        /// <summary>
        /// <c>note add</c>, <c>note list</c>, <c>note get</c> and <c>note export</c> (tickets S0108 and S0120): the service alone,
        /// without the notes window, which the context menu, the chord or the tray create when a person asks for it. The token
        /// ends the work a second before the listener gives up (<c>timeoutMs</c>), so a verb that was answered "not proven"
        /// writes nothing afterwards. The export additionally carries the caller's working directory, the saver's file-system
        /// seam and budget (S0110's own, the one the tests fake) and the remote-path rule.
        /// </summary>
        private Task<AppCommandResponse> RunNoteCommand(AppCommandInvocation invocation, string workingDir)
        {
            int bound = invocation.Spec.TimeoutMs > 0 ? invocation.Spec.TimeoutMs : 10000;
            var cancel = new CancellationTokenSource(Math.Max(1000, bound - 1000));
            Task<AppCommandResponse> work;
            switch (invocation.Spec.Name)
            {
                case "note add":
                    work = AppCommandsNotes.NoteAdd(invocation, EnsureQuickNotesService, T, cancel.Token);
                    break;
                case "note get":
                    work = AppCommandsNotes.NoteGet(invocation, EnsureQuickNotesService, T, cancel.Token);
                    break;
                case "note export":
                    var saver = new ScreenshotSaver();
                    work = AppCommandsNotes.NoteExport(invocation, workingDir, EnsureQuickNotesService,
                        saver.FileSystem, saver.Budget, p => LaunchTargets.IsRemotePath(p), T, cancel.Token);
                    break;
                default:
                    work = AppCommandsNotes.NoteList(invocation, EnsureQuickNotesService, T, cancel.Token);
                    break;
            }
            work.ContinueWith(_ => cancel.Dispose(), TaskScheduler.Default);
            return work;
        }

        /// <summary>
        /// <c>trafficlight status</c> and <c>trafficlight check</c> (ticket S0117): the live engine and scheduler, never a window.
        /// The status answers at once from what the board knows; the check waits for its checks under a token that ends the work
        /// a second before the listener gives up (<c>timeoutMs</c>), so a verb that was answered "not proven" waits for nothing more.
        /// </summary>
        private Task<AppCommandResponse> RunTrafficLightCommand(AppCommandInvocation invocation)
        {
            if (invocation.Spec.Name == "trafficlight status")
                return Done(AppCommandsTrafficLight.Status(invocation, TrafficLightCommandHostOrNull, T));
            int bound = invocation.Spec.TimeoutMs > 0 ? invocation.Spec.TimeoutMs : 10000;
            var cancel = new CancellationTokenSource(Math.Max(1000, bound - 1000));
            Task<AppCommandResponse> work = AppCommandsTrafficLight.Check(invocation, TrafficLightCommandHostOrNull, T, cancel.Token);
            work.ContinueWith(_ => cancel.Dispose(), TaskScheduler.Default);
            return work;
        }

        // ---- The four window-open verbs (ticket S0119) ----

        /// <summary>
        /// <c>note show</c>, <c>clipboard-history show</c>, <c>symbols show</c> and <c>trafficlight show</c>: each handler
        /// answers at once once its window is on its way up, through the host seam the tests fake. Show, never hide.
        /// </summary>
        private AppCommandResponse RunWindowCommand(AppCommandInvocation invocation)
        {
            IWindowCommandsHost host = new WindowCommandsHost(this);
            switch (invocation.Spec.Name)
            {
                case "note show":
                    return AppCommandsWindows.NoteShow(invocation, host, T);
                case "clipboard-history show":
                    return AppCommandsWindows.HistoryShow(host, T);
                case "symbols show":
                    return AppCommandsWindows.SymbolsShow(host, T);
                default:
                    return AppCommandsWindows.TrafficLightShow(host, T);
            }
        }

        /// <summary>
        /// The show-only opening of the symbols palette the command uses: the chord's window without the chord's
        /// focus, so a script that opens the palette never takes the editor's caret away (ticket S0119).
        /// </summary>
        private void ShowSymbolsWithoutFocus()
        {
            if (!_config.EnableSymbols) return;
            EnsureSymbolsWindow()?.ShowPalette(_indicator.LastActiveWindow, active: false);
        }

        /// <summary>
        /// The live windows and modules behind the four verbs - one adapter, because each path already exists: the
        /// chord's notes path, the tray item's history show half, the settings' board presentation (S0084) and the
        /// palette's own no-focus opening. Every member is called on the UI thread the dispatcher already holds.
        /// </summary>
        private sealed class WindowCommandsHost : IWindowCommandsHost
        {
            private readonly CyrFlipContext _owner;

            public WindowCommandsHost(CyrFlipContext owner) { _owner = owner; }

            public bool QuickNotesOn => _owner._config.EnableQuickNotes;
            public bool ClipboardHistoryOn => _owner._config.EnableClipboardHistory;
            public bool SymbolsOn => _owner._config.EnableSymbols;
            public bool TrafficLightOn => _owner._config.EnableTrafficLight;

            public void ShowQuickNotes(bool startNew) => _owner.ShowQuickNotes(startNew);
            public void ShowClipboardHistory() => _owner._clipboardHistoryWindow.ShowStrip();
            public void ShowSymbols() => _owner.ShowSymbolsWithoutFocus();
            public void ShowTrafficLight() => _owner.ShowTrafficLightResult();
        }

        // ------------------------------------------------------------------ the three history verbs (ticket S0121)

        /// <summary>
        /// <c>clipboard-history list</c>, <c>clipboard-history put</c> and <c>clipboard-history pause</c>: the live
        /// service the strip owns, never a window - the provider answers null while the module is off, so an off
        /// history answers <c>module-off</c> before its journal is read. The token ends the work a second before the
        /// listener gives up (<c>timeoutMs</c>), so a verb answered "not proven" writes nothing afterwards. The
        /// restore runs through the service's own path; the two read and write verbs answer 12 with the setting off
        /// because the base's rule-13 gate runs before this switch.
        /// </summary>
        private Task<AppCommandResponse> RunHistoryCommand(AppCommandInvocation invocation)
        {
            if (invocation.Spec.Name == "clipboard-history pause")
                return Done(AppCommandsClipboardHistory.PauseHistory(invocation, new HistoryCommandsHost(this), T));
            int bound = invocation.Spec.TimeoutMs > 0 ? invocation.Spec.TimeoutMs : 10000;
            var cancel = new CancellationTokenSource(Math.Max(1000, bound - 1000));
            Func<ClipboardHistoryService?> provider = () => _config.EnableClipboardHistory ? _clipboardHistory : null;
            Task<AppCommandResponse> work = invocation.Spec.Name == "clipboard-history list"
                ? AppCommandsClipboardHistory.ListHistory(invocation, provider, T, cancel.Token)
                : AppCommandsClipboardHistory.PutHistory(invocation, provider, entry => provider()!.Restore(entry), T, cancel.Token);
            work.ContinueWith(_ => cancel.Dispose(), TaskScheduler.Default);
            return work;
        }

        // ------------------------------------------------------------------ the two exchange verbs and their option sets (ticket S0121)

        /// <summary>
        /// <c>clipboard send</c> and <c>clipboard get</c> (rows C32 and C33): without the new options each keeps its
        /// wave-1 behaviour byte for byte - the same busy and accepted sentences, the module's own transfer - and
        /// with <c>--file</c>/<c>--text</c> or <c>--out</c> the resolution happens before anything transfers, so a
        /// caller's mistake is answered while nothing has moved. The module gate is first, as the wave-1 dispatch had it.
        /// </summary>
        private Task<AppCommandResponse> RunClipboardExchangeCommand(AppCommandInvocation invocation, string workingDir)
        {
            bool send = invocation.Spec.Name == "clipboard send";
            if (!_config.EnableExchangeClipboard)
                return Done(AppCommandVerbs.ModuleOff(T("The clipboard on the server is switched off. Turn it on in the settings of CyrFlip.")));
            if (send && invocation.Option("file") == null && invocation.Option("text") == null)
            {
                bool started = SendExchangeClipboard();
                return Done(started
                    ? AppCommandResponse.Accepted(T("Accepted - CyrFlip shows how it ended in the notification area."))
                    : AppCommandResponse.BusyNow(T("CyrFlip is already sending or receiving the clipboard.")));
            }
            if (!send && invocation.Option("out") == null)
            {
                bool started = GetExchangeClipboard();
                return Done(started
                    ? AppCommandResponse.Accepted(T("Accepted - CyrFlip shows how it ended in the notification area."))
                    : AppCommandResponse.BusyNow(T("CyrFlip is already sending or receiving the clipboard.")));
            }

            int bound = invocation.Spec.TimeoutMs > 0 ? invocation.Spec.TimeoutMs : 10000;
            var cancel = new CancellationTokenSource(Math.Max(1000, bound - 1000));
            var saver = new ScreenshotSaver();
            Task<AppCommandResponse> work = send
                ? SendClipboardSourceCommand(invocation, workingDir, saver.Budget, cancel.Token)
                : GetExchangeClipboardIntoFile(invocation, workingDir, saver.FileSystem, saver.Budget, cancel.Token);
            work.ContinueWith(_ => cancel.Dispose(), TaskScheduler.Default);
            return work;
        }

        /// <summary>
        /// The send with a named source: the payload is built on the pool under the saver's budget (a dead share in
        /// <c>--file</c> must not eat the verb's bound) and only then does the module's own transfer start - its
        /// seconds run in the background and end in the tray balloons, so the answer is what the row asks for:
        /// <c>accepted</c> with the source's kind and size.
        /// </summary>
        private async Task<AppCommandResponse> SendClipboardSourceCommand(AppCommandInvocation invocation, string workingDir,
            TimeSpan budget, CancellationToken token)
        {
            try
            {
                SendSource source =
                    await AppCommandsClipboardExchange.ResolveSendSource(invocation, workingDir, budget,
                        AppCommandsClipboardExchange.StatOf, p => LaunchTargets.IsRemotePath(p), T, token);
                if (source.Refusal != null) return source.Refusal;
                ExchangeClipboardPayload payload = source.Payload!;
                bool started = SendExchangeClipboard(payload);
                return started
                    ? AppCommandResponse.Accepted(T("Accepted - CyrFlip shows how it ended in the notification area."),
                        new Dictionary<string, object> { ["kind"] = payload.Kind, ["sizeBytes"] = payload.ClearSize })
                    : AppCommandResponse.BusyNow(T("CyrFlip is already sending or receiving the clipboard."));
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                // The family's own rule: the caller always gets an answer, and only the fault's type is logged.
                LauncherLog.Log("Command: clipboard send failed: " + ex.GetType().Name);
                return AppCommandResponse.Failed(AppCommandReasons.Error, AppCommandExit.Failed,
                    T("The command failed. The log bundle on the About page has the details."));
            }
        }

        /// <summary>
        /// The pause host: the tray item's own check item carries the persisted state, the live service and the
        /// save - <c>CheckedChanged</c> is the one path every surface already shares (the settings page re-reads its
        /// value on <see cref="SettingsForm.Reload"/>, so the page, the item and the verb show one value).
        /// </summary>
        private sealed class HistoryCommandsHost : IHistoryCommandsHost
        {
            private readonly CyrFlipContext _owner;

            public HistoryCommandsHost(CyrFlipContext owner) { _owner = owner; }

            public bool ClipboardHistoryOn => _owner._config.EnableClipboardHistory;

            public void SetPaused(bool paused) => _owner.PauseHistoryFromCommand(paused);
        }

        /// <summary>One command's pause write: the tray item's own path, then the settings page re-read - never a second persisted value.</summary>
        private void PauseHistoryFromCommand(bool paused)
        {
            _historyPauseItem.Checked = paused;
            _settings?.RefreshState();
        }

        // ------------------------------------------------------------------ the four state-switch verbs (ticket S0125)

        /// <summary>
        /// <c>keepawake set</c>, <c>theme set</c>, <c>autostart set</c> and <c>cursor restore</c> (rows C41-C44): each
        /// answers at once, on the UI thread the dispatcher already holds - the live keep-awake request is bound to
        /// this thread, the theme repaints windows here, and every write goes through the seam the matching surface
        /// owns (the tray items' own toggle paths, the page's choose path, the page's own Run-key call, the exit
        /// path's own <c>SPI_SETCURSORS</c> seam), never a second persisted path.
        /// </summary>
        private Task<AppCommandResponse> RunStateSwitchCommand(AppCommandInvocation invocation)
        {
            var host = new StateSwitchCommandsHost(this);
            switch (invocation.Spec.Name)
            {
                case "keepawake set":
                    return Done(AppCommandsStateSwitches.KeepAwakeSet(invocation, host, T));
                case "theme set":
                    return Done(AppCommandsStateSwitches.ThemeSet(invocation, host, T));
                case "autostart set":
                    return Done(AppCommandsStateSwitches.AutostartSet(invocation, host, T));
                default:
                    return Done(AppCommandsStateSwitches.CursorRestore(LayoutCursor.ReloadScheme, T));
            }
        }

        /// <summary>The pending session-only keep-awake request's timer, on the tray UI thread; null when none runs.</summary>
        private System.Windows.Forms.Timer? _keepAwakeSessionTimer;

        /// <summary>
        /// Stops and disposes the pending session-only keep-awake timer, if any - called by the timer's own tick and by
        /// the next <c>keepawake set</c> (one session at a time; a process death clears the request itself, the way
        /// Windows clears a thread-bound execution-state request).
        /// </summary>
        private void StopKeepAwakeSession()
        {
            if (_keepAwakeSessionTimer == null) return;
            _keepAwakeSessionTimer.Stop();
            _keepAwakeSessionTimer.Dispose();
            _keepAwakeSessionTimer = null;
        }

        /// <summary>
        /// The live state switches behind the four verbs - one adapter over the seams the surfaces already own. Every
        /// member is called on the UI thread the dispatcher holds: the keep-awake request binds to the calling thread,
        /// the theme's set-mode repaints windows, and the tray items are WinForms controls.
        /// </summary>
        private sealed class StateSwitchCommandsHost : IKeepAwakeCommandsHost, IThemeCommandsHost, IAutostartCommandsHost
        {
            private readonly CyrFlipContext _owner;

            public StateSwitchCommandsHost(CyrFlipContext owner) { _owner = owner; }

            public void SetKeepAwakeSwitches(bool? system, bool? screen)
            {
                // The tray item's own CheckedChanged path is the one path: live call, config, save - a verb writes
                // exactly what a click on the item writes, and the settings window re-reads the live state.
                if (system.HasValue && _owner._keepAwakeItem.Checked != system.Value) _owner._keepAwakeItem.Checked = system.Value;
                if (screen.HasValue && _owner._keepScreenItem.Checked != screen.Value) _owner._keepScreenItem.Checked = screen.Value;
                _owner._settings?.RefreshState();
            }

            public DateTime BeginKeepAwakeSession(bool? system, bool? screen, int minutes)
            {
                // Session-only: the live call on this - the tray's - thread, the config and the check items untouched
                // (they keep showing the persisted pair), one timer on the same thread to end it.
                if (system.HasValue) KeepAwake.SetSystemAwake(system.Value);
                if (screen.HasValue) KeepAwake.SetScreenOn(screen.Value);
                DateTime endsUtc = DateTime.UtcNow.AddMinutes(minutes);
                _owner.StopKeepAwakeSession();
                var timer = new System.Windows.Forms.Timer { Interval = minutes * 60 * 1000 };
                timer.Tick += (_, _) =>
                {
                    _owner.StopKeepAwakeSession();
                    AppCommandsStateSwitches.EndSession(_owner._config.KeepSystemAwake, _owner._config.KeepScreenOn);
                    _owner.UpdateTrayTooltip();
                    _owner._settings?.RefreshState();
                };
                timer.Start();
                _owner._keepAwakeSessionTimer = timer;
                _owner.UpdateTrayTooltip();
                return endsUtc;
            }

            public void SetTheme(ThemeMode mode, string token)
            {
                // The page's own choose path: the token written and saved, then the live set-mode - no restart - and
                // the settings window re-reads its picker. High contrast keeps its precedence inside SetMode.
                _owner._config.Theme = token;
                _owner._config.Save();
                ThemeManager.SetMode(mode);
                _owner._settings?.RefreshState();
            }

            public bool AutostartManagedByWindows => Autostart.ManagedByWindows;

            public void SetAutostart(bool enabled)
            {
                // The page's own call, never its dialog: a command does not wait on a person (rule 4), so a registry
                // failure raises through to the handler's answer instead. The tray item follows through its own
                // CheckedChanged path, exactly as SetAutostartFromSettings drives it.
                Autostart.Set(enabled);
                if (_owner._autostartItem.Checked != enabled) _owner._autostartItem.Checked = enabled;
            }
        }

        // ------------------------------------------------------------------ the translator verb (ticket S0128)

        /// <summary>
        /// The one command translation at a time: 0 free, 1 claimed. Claimed on the UI thread before the work starts,
        /// released by the handler's own <c>finally</c>; a second <c>translate text</c> meets it busy and forms no queue.
        /// </summary>
        private int _commandTranslateBusy;

        /// <summary>
        /// <c>translate text</c> (row C38): the running instance's own translation service, with no popup, no synthesized key,
        /// no clipboard and no balloon - the token ends the work a second before the listener gives up (<c>timeoutMs</c>),
        /// so a verb answered "not proven" has already cancelled its model call. The provider answers null while the
        /// translator module is off, so an off translator answers <c>module-off</c> before its service is touched; the
        /// rule-13 gate has already answered 12 for the class above <c>safe</c> (this class is <c>leaves-machine</c>, so the
        /// answer of a missing instance is the base's 11 <c>not-running</c>). The chord path is not touched anywhere here:
        /// its popup, its supersedence token and its cancel-key watch keep their own rules, and a command translation
        /// never cancels a chord translation, nor the other way around.
        /// </summary>
        private Task<AppCommandResponse> RunTranslateCommand(AppCommandInvocation invocation)
        {
            int bound = invocation.Spec.TimeoutMs > 0 ? invocation.Spec.TimeoutMs : 10000;
            var cancel = new CancellationTokenSource(Math.Max(1000, bound - 1000));
            Func<ITranslateCommandsHost?> provider = () =>
                _config.EnableTranslate ? new TranslateCommandsHost(this) : null;
            Task<AppCommandResponse> work = AppCommandsTranslate.TranslateText(invocation, provider, T, cancel.Token);
            work.ContinueWith(_ => cancel.Dispose(), TaskScheduler.Default);
            return work;
        }

        /// <summary>
        /// The live service behind <c>translate text</c> - one adapter over the <see cref="TranslationService"/> the context
        /// already owns (built once, the chord's own), so the command and the chord share one server probe, one model
        /// resolution and one timeout ladder. The sink is null on this path: nothing paints.
        /// </summary>
        private sealed class TranslateCommandsHost : ITranslateCommandsHost
        {
            private readonly CyrFlipContext _owner;

            public TranslateCommandsHost(CyrFlipContext owner) { _owner = owner; }

            public string UiLanguage => _owner._config.UiLanguage;

            public bool BeginCommandTranslation()
                => Interlocked.CompareExchange(ref _owner._commandTranslateBusy, 1, 0) == 0;

            public void EndCommandTranslation()
                => Interlocked.Exchange(ref _owner._commandTranslateBusy, 0);

            public Task<TranslationResult> TranslateAsync(string text, string targetCode, string? sourceLanguageName,
                CancellationToken ct)
                => _owner._translation.TranslateAsync(text, targetCode, sourceLanguageName, null, ct);
        }

        // ------------------------------------------------------------------ the selection family (ticket S0129)

        /// <summary>
        /// The visible, titled top-level windows with the pid beside each - the one window list both families that
        /// name a window read (the selection verbs and <c>screenshot window</c>, tickets S0129 and S0130), so
        /// <c>--window-title</c> finds the same windows for both. GetWindowText never sends messages into another
        /// process's windows (it reads the cached title), so the walk cannot hang on a busy program.
        /// </summary>
        private static List<SelectionWindow> TopLevelWindows()
        {
            var windows = new List<SelectionWindow>();
            WindowInterop.EnumWindows((window, _) =>
            {
                if (WindowInterop.IsWindowVisible(window))
                {
                    var title = new System.Text.StringBuilder(512);
                    WindowInterop.GetWindowText(window, title, title.Capacity);
                    if (title.Length > 0)
                    {
                        WindowInterop.GetWindowThreadProcessId(window, out uint pid);
                        windows.Add(new SelectionWindow(window, pid, title.ToString()));
                    }
                }
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        /// <summary>
        /// The four selection verbs (rows C34-C37): they act on the selection of a window another program owns, so
        /// their work runs on the pool - the flip pipeline they ride sleeps through the capture and paste settles - under
        /// a token that ends the work a second before the listener gives up (<c>timeoutMs</c>), so a verb answered
        /// "not proven" has already cancelled its model call. The notes service is resolved here, on the UI thread the
        /// dispatcher holds, because owning the tray and the notes-window state is that thread's business; null is the
        /// module-off answer the handler gives. Nothing of a chord path is touched here: the flip paths, the translation
        /// phase A and the delivery keep their own rules, and a command's selection work is invisible to them and the
        /// other way around.
        /// </summary>
        private Task<AppCommandResponse> RunSelectionCommand(AppCommandInvocation invocation)
        {
            int bound = invocation.Spec.TimeoutMs > 0 ? invocation.Spec.TimeoutMs : 10000;
            var cancel = new CancellationTokenSource(Math.Max(1000, bound - 1000));
            CancellationToken token = cancel.Token;
            var host = new SelectionCommandsHost(this);
            QuickNotesService? notes = invocation.Spec.Name == "selection note" ? EnsureQuickNotesService() : null;
            Task<AppCommandResponse> work = Task.Run(() =>
            {
                switch (invocation.Spec.Name)
                {
                    case "selection convert":
                        return AppCommandsSelection.SelectionConvert(invocation, host, T, token);
                    case "selection flip-case":
                        return AppCommandsSelection.SelectionFlipCase(invocation, host, T, token);
                    case "selection note":
                        return AppCommandsSelection.SelectionNote(invocation, host, notes, T, token);
                    default:
                        return AppCommandsSelection.SelectionTranslate(invocation,
                            _config.EnableTranslate ? host : null, T, token);
                }
            });
            work.ContinueWith(_ => cancel.Dispose(), TaskScheduler.Default);
            return work;
        }

        /// <summary>
        /// The live machine behind the selection verbs - one adapter over the seams the chords and the earlier command
        /// verbs already own: the window list over the interop pinvokes, the raise through <see cref="ForegroundActivator"/>
        /// (built for "putting the user's own window back in the foreground before synthesizing input into it", with its
        /// hung-window guard), the flip pipeline over the context's own <see cref="ClipboardHandler"/>, the claim of the
        /// one <c>_busy</c> guard every chord uses with the history suppression bracketed inside it, and the translation
        /// service with no sink, sharing the claim with <c>translate text</c>. Every pipeline, service and suppress path
        /// is the one the chord flows use; the adapter owns nothing but the window list and the verify poll.
        /// </summary>
        private sealed class SelectionCommandsHost : ISelectionCommandsHost
        {
            private readonly CyrFlipContext _owner;

            /// <summary>How long the raise waits for the foreground to settle before it answers <c>window-not-foreground</c>.</summary>
            internal const int FocusSettleMs = 1500;

            public SelectionCommandsHost(CyrFlipContext owner) { _owner = owner; }

            public IReadOnlyList<SelectionWindow> TopLevelWindows() => CyrFlipContext.TopLevelWindows();

            public bool BringToFront(IntPtr window)
            {
                if (WindowInterop.GetForegroundWindow() == window) return true;
                ForegroundActivator.Activate(window);
                // SetForegroundWindow answers at once and Windows settles asynchronously: verify, and a window that
                // has not taken the focus by the settle cap answers window-not-foreground (never a silent wrong window).
                for (int waited = 0; waited < FocusSettleMs; waited += 50)
                {
                    if (WindowInterop.GetForegroundWindow() == window) return true;
                    Thread.Sleep(50);
                }
                return WindowInterop.GetForegroundWindow() == window;
            }

            /// <summary>The layout live in the window, as the translator's own code table gives it (S0109's own resolution).</summary>
            public string LayoutCodeOf(IntPtr window)
            {
                IntPtr hkl = CursorIndicator.HklOf(window);
                string klid = LayoutIdentity.KlidForHkl(hkl).ToUpperInvariant();
                return klid.Length == 0 ? "" : LayoutIdentity.CodeFor(unchecked((uint)(long)hkl), klid);
            }

            public string UiLanguage => _owner._config.UiLanguage;

            public IReadOnlyList<LayoutEntry> InstalledLayouts() => IndicatorLayoutHost.InstalledEntries();

            public bool StoredConvertSymbols => _owner._config.ConvertSymbols;
            public bool StoredFlipCapsLock => _owner._config.FlipCapsLockAfter;

            public bool BeginClipboardWork()
            {
                if (Interlocked.CompareExchange(ref _owner._busy, 1, 0) != 0) return false;
                _owner._clipboardHistory.SuppressBegin();
                return true;
            }

            public void EndClipboardWork()
            {
                _owner._clipboardHistory.SuppressEnd();
                Interlocked.Exchange(ref _owner._busy, 0);
            }

            public ClipboardHandler.FlipResult ConvertSelection(string fromKlid, string toKlid, bool convertSymbols)
                => _owner._clipboard.ConvertSelection(fromKlid, toKlid, convertSymbols);

            public ClipboardHandler.FlipResult FlipCaseSelection(bool syncCapsAfter)
                => _owner._clipboard.FlipCase(syncCapsAfter);

            public ClipboardHandler.CaptureResult TakeSelection(out string text, int maxChars)
                => _owner._clipboard.TakeSelection(out text, out _, maxChars);

            public bool BeginCommandTranslation()
                => Interlocked.CompareExchange(ref _owner._commandTranslateBusy, 1, 0) == 0;

            public void EndCommandTranslation()
                => Interlocked.Exchange(ref _owner._commandTranslateBusy, 0);

            public Task<TranslationResult> TranslateAsync(string text, string targetCode, CancellationToken ct)
                => _owner._translation.TranslateAsync(text, targetCode, null, null, ct);
        }

        private Dictionary<string, object> StatusData()
        {
            int pid;
            using (Process self = Process.GetCurrentProcess()) pid = self.Id;
            return new Dictionary<string, object>
            {
                ["version"] = SupportBundle.AppVersion(),
                ["pid"] = pid,
                ["acceptsRestrictedCommands"] = _config.EnableExternalCommands,
                // REMOTE-CONTROL rule 8: the state of "Listen to commands from my other devices" is part of status.
                ["remoteCommands"] = _config.RemoteCommandsListen,
                ["modules"] = new Dictionary<string, object>
                {
                    ["quickNotes"] = _config.EnableQuickNotes,
                    ["translate"] = _config.EnableTranslate,
                    ["scenarioLauncher"] = _config.EnableScenarioLauncher,
                    ["exchangeClipboard"] = _config.EnableExchangeClipboard,
                    ["trafficLight"] = _config.EnableTrafficLight,
                    ["symbols"] = _config.EnableSymbols,
                    ["clipboardHistory"] = _config.EnableClipboardHistory,
                    ["contextMenu"] = _config.EnableContextMenu,
                },
            };
        }

        private static Task<AppCommandResponse> Done(AppCommandResponse response) => Task.FromResult(response);
    }
}

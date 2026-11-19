using System;
using System.Collections.Generic;
using System.Globalization;

namespace CyrFlip
{
    /// <summary>
    /// What the keep-awake verbs need of the running copy (ticket S0125): the persisted pair through the two tray
    /// items' own toggle paths, and the session-only arm - the live apply (on the tray UI thread, because
    /// <see cref="KeepAwake"/>'s request is bound to the thread that made it) and the one timer that ends it.
    /// </summary>
    internal interface IKeepAwakeCommandsHost
    {
        /// <summary>The persisted form: each given switch written exactly as its tray item writes it - the one path.</summary>
        void SetKeepAwakeSwitches(bool? system, bool? screen);

        /// <summary>The session-only form: the given switches applied live, nothing written, the timer armed; answers the end time.</summary>
        DateTime BeginKeepAwakeSession(bool? system, bool? screen, int minutes);
    }

    /// <summary>The theme verb's seam: the page's own choose path - token, save, live set-mode - nothing else.</summary>
    internal interface IThemeCommandsHost
    {
        void SetTheme(ThemeMode mode, string token);
    }

    /// <summary>
    /// The autostart verb's seam: the page's own call (the Run key and the machine-local intent together, never one
    /// without the other) and the packaged-build refusal the page already words.
    /// </summary>
    internal interface IAutostartCommandsHost
    {
        /// <summary>The General page's honesty: on a packaged build Windows owns startup and the verb writes nothing.</summary>
        bool AutostartManagedByWindows { get; }

        /// <summary>The page's own write, raised through when the registry refuses so the verb can answer instead of a dialog.</summary>
        void SetAutostart(bool enabled);
    }

    /// <summary>
    /// The four state-switch verbs (ticket S0125, APP-COMMANDS 0.3, the research rows C41 to C44):
    /// <c>keepawake set</c>, <c>theme set</c>, <c>autostart set</c> and <c>cursor restore</c>. Each is a pure function
    /// over the parsed invocation and a host over the one seam the surface already owns, so the tests need no context
    /// and no window; <c>CyrFlipContext</c> supplies the tray items, the settings page's choose path, the Run-key pair
    /// and the tray's own timer, and a one-shot cold start supplies the autostart and cursor seams alone.
    ///
    /// <para>Every write goes through the same one-setter path the pages and the tray items use - never a second
    /// persisted path - so the verb, the tray, the settings window and the answer show one value. The two <c>safe</c>
    /// verbs never meet the rule-13 gate (item W: an answer that repeats the caller's own switch does not raise the
    /// class); the two <c>writes-user-data</c> verbs are gated by the base before any handler runs, live and oneshot
    /// both. The session-only <c>--minutes</c> request writes no config at all: the live state returns to what the
    /// pages own when the tray's timer ends, and a process death clears it the way Windows clears the bound request
    /// itself.</para>
    /// </summary>
    internal static class AppCommandsStateSwitches
    {
        /// <summary>The lowest <c>--minutes</c> a session-only keep-awake request may ask for.</summary>
        public const int MinSessionMinutes = 1;
        /// <summary>The highest: a workday, the bound the Assumed entry of the ticket weighed and took.</summary>
        public const int MaxSessionMinutes = 480;

        // ------------------------------------------------------------------ keepawake set

        /// <summary>
        /// The keep-awake pair (row C41). Without <c>--minutes</c> the given switches go through the tray items' own
        /// toggle paths - the live call, the config value, the save - so the items, the page and the verb hold one
        /// value. With <c>--minutes</c> the request is session-only: the given switches are applied live, the config is
        /// not written at all, the host's timer ends it, and the answer names <c>expiresUtc</c>. A request that names no
        /// switch is refused (10); a bound outside 1..480 is refused (10).
        /// </summary>
        public static AppCommandResponse KeepAwakeSet(AppCommandInvocation invocation, IKeepAwakeCommandsHost host,
            Func<string, string> T)
        {
            try
            {
                bool? system = OptionalBool(invocation, "system");
                bool? screen = OptionalBool(invocation, "screen");
                if (system == null && screen == null)
                    return AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                        T("Give --system or --screen, or both."));

                string? rawMinutes = invocation.Option("minutes");
                if (rawMinutes == null)
                {
                    host.SetKeepAwakeSwitches(system, screen);
                    return KeepAwakeAnswer(T, null, 0);
                }
                if (!int.TryParse(rawMinutes, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes)
                    || minutes < MinSessionMinutes || minutes > MaxSessionMinutes)
                    return AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                        Localization.Format(T, "The --minutes must be a whole number from {0} to {1}.",
                            MinSessionMinutes, MaxSessionMinutes));

                DateTime expiresUtc = host.BeginKeepAwakeSession(system, screen, minutes);
                return KeepAwakeAnswer(T, expiresUtc, minutes);
            }
            catch (Exception ex)
            {
                return Crashed("keepawake set", ex, T);
            }
        }

        /// <summary>The keep-awake answer: the combined live state, its one sentence, and - at a session-only run - the
        /// caller's own bound in words and the shared end time in the data, never the other way round.</summary>
        private static AppCommandResponse KeepAwakeAnswer(Func<string, string> T, DateTime? expiresUtc, int minutes)
        {
            bool system = KeepAwake.KeepSystemAwake;
            bool screen = KeepAwake.KeepScreenOn;
            string message = system && screen ? T("The computer will not sleep and the screen will not turn off.")
                : system ? T("The computer will not sleep.")
                : screen ? T("The screen will not turn off.")
                : T("Both keep-awake switches are off.");
            var data = new Dictionary<string, object>
            {
                ["system"] = system,
                ["screen"] = screen,
            };
            if (expiresUtc == null) return AppCommandResponse.Done(message, data);
            data["expiresUtc"] = CyrFlipExchangeWriter.Date(expiresUtc.Value);
            return AppCommandResponse.Done(message + " " + Localization.Format(T,
                "The switches return to their saved state after {0} minutes.", minutes), data);
        }

        /// <summary>
        /// The end of a session-only request: the live state returns to what the pages own - the persisted pair, in
        /// the startup's own one-call form, so no intermediate mask is ever sent. The tray's timer calls this on the UI
        /// thread; the pages and the tooltip re-read the state after it. Pure over <see cref="KeepAwake"/>, so the
        /// tests prove the return without a timer.
        /// </summary>
        internal static void EndSession(bool persistedSystem, bool persistedScreen)
            => KeepAwake.Restore(persistedSystem, persistedScreen);

        // ------------------------------------------------------------------ theme set

        /// <summary>
        /// The theme token (row C42): written and saved exactly as choosing it on the General page writes it, then the
        /// live set-mode - no restart, high contrast keeps its precedence because the verb writes the token, never the
        /// resolved kind. A token that is none of the three is the parser's own enum refusal (10, never folded to
        /// <c>system</c>); the answer repeats the stored token (item W), so the verb stays <c>safe</c>.
        /// </summary>
        public static AppCommandResponse ThemeSet(AppCommandInvocation invocation, IThemeCommandsHost host,
            Func<string, string> T)
        {
            try
            {
                ThemeMode mode = ThemeModes.Parse(invocation.Option("theme"));
                string token = ThemeModes.Token(mode);
                host.SetTheme(mode, token);
                return AppCommandResponse.Done(T("The theme is applied at once, exactly as on the settings page."),
                    new Dictionary<string, object> { ["theme"] = token });
            }
            catch (Exception ex)
            {
                return Crashed("theme set", ex, T);
            }
        }

        // ------------------------------------------------------------------ autostart set

        /// <summary>
        /// The Run-key pair and the machine-local intent together (row C43), through the page's own call - never half a
        /// switch. Exactly one of <c>--on</c>/<c>--off</c> is the parser's own <c>oneOf</c> refusal (10). On a packaged
        /// (Store, MSIX) build the verb answers 11 <c>managed-by-windows</c> and writes nothing, exactly as the settings
        /// page says; a registry failure answers the family's crash sentence instead of opening a dialog no command may
        /// wait on (rule 4). The answer repeats the new state (item W) and the verb stays <c>writes-user-data</c>: the
        /// rule-13 gate before the handler is what answers 12 with the setting off.
        /// </summary>
        public static AppCommandResponse AutostartSet(AppCommandInvocation invocation, IAutostartCommandsHost host,
            Func<string, string> T)
        {
            try
            {
                if (host.AutostartManagedByWindows)
                    return AppCommandResponse.Refused(AppCommandReasons.ManagedByWindows, AppCommandExit.NotReady,
                        T("Windows itself owns startup for the Store build of CyrFlip."));
                bool enabled = invocation.Option("on") != null;
                host.SetAutostart(enabled);
                return AppCommandResponse.Done(
                    T(enabled ? "CyrFlip will start with Windows." : "CyrFlip will not start with Windows."),
                    new Dictionary<string, object> { ["enabled"] = enabled });
            }
            catch (Exception ex)
            {
                return Crashed("autostart set", ex, T);
            }
        }

        // ------------------------------------------------------------------ cursor restore

        /// <summary>
        /// The branded-I-beam repair (row C44): the whole default scheme reloaded unconditionally - the same
        /// <c>SPI_SETCURSORS</c> reload the exit paths own, without their replaced-guard, because the kill scenario the
        /// verb exists for is a process that replaced nothing. Idempotent, safe on any thread and beside a live
        /// instance; the reload seam is injectable so the tests record instead of reloading the real scheme.
        /// </summary>
        public static AppCommandResponse CursorRestore(Action reloadScheme, Func<string, string> T)
        {
            try
            {
                reloadScheme();
                return AppCommandResponse.Done(T("The default system cursors are reloaded."),
                    new Dictionary<string, object> { ["restored"] = true });
            }
            catch (Exception ex)
            {
                return Crashed("cursor restore", ex, T);
            }
        }

        // ------------------------------------------------------------------ shared

        /// <summary>One of the pair's switches as given; null when the option is absent.</summary>
        internal static bool? OptionalBool(AppCommandInvocation invocation, string name)
        {
            string? value = invocation.Option(name);
            return value == null
                ? null
                : value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A fault after the host's work would otherwise fault the caller's wait: the caller always gets an answer. Only the type is logged.</summary>
        private static AppCommandResponse Crashed(string verb, Exception ex, Func<string, string> T)
        {
            LauncherLog.Log("Command: " + verb + " failed: " + ex.GetType().Name);
            return AppCommandResponse.Failed(AppCommandReasons.Error, AppCommandExit.Failed,
                T("The command failed. The log bundle on the About page has the details."));
        }
    }
}

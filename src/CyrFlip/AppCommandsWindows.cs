using System;

namespace CyrFlip
{
    /// <summary>
    /// What the four module-window commands ask of the running copy (ticket S0119): whether each module is on, and
    /// the show-only path of its window. The seam exists so the handlers are tested with a fake host; the running copy
    /// supplies a small adapter over its own chords, tray items and settings paths, each with the hide half taken out.
    /// </summary>
    internal interface IWindowCommandsHost
    {
        /// <summary>Whether the quick notes module is on; off answers <c>module-off</c> before the journal is even opened.</summary>
        bool QuickNotesOn { get; }

        /// <summary>Whether the clipboard history module is on; off answers <c>module-off</c> before the journal is read.</summary>
        bool ClipboardHistoryOn { get; }

        /// <summary>Whether the symbols window module is on; off answers <c>module-off</c> with no window object created.</summary>
        bool SymbolsOn { get; }

        /// <summary>Whether the traffic light module is on; off answers <c>module-off</c> with nothing created, read or started.</summary>
        bool TrafficLightOn { get; }

        /// <summary>The chord's own path: the notes window up on the note it was last left on - or, with
        /// <paramref name="startNew"/>, a fresh note with the caret already in the body.</summary>
        void ShowQuickNotes(bool startNew);

        /// <summary>The tray item's show half: the history strip up and brought forward, never hidden.</summary>
        void ShowClipboardHistory();

        /// <summary>The palette's own opening rule: up without taking the focus from the editor.</summary>
        void ShowSymbols();

        /// <summary>The settings' own path (S0084): the board up without the focus and raised above whatever covers it.</summary>
        void ShowTrafficLight();
    }

    /// <summary>
    /// The four verbs that open a module window (ticket S0119, APP-COMMANDS 0.2): <c>note show</c>,
    /// <c>clipboard-history show</c>, <c>symbols show</c> and <c>trafficlight show</c>, the research rows C26, C31, C39
    /// and C40. Each is a pure function over the parsed invocation and a host, so the tests need no context and no
    /// window; the answer is a sentence and an exit code, and no answer carries anything of what a window shows.
    ///
    /// <para><b>Show, never hide:</b> every verb shows and only shows - twice in a row the window is visible both
    /// times, brought forward where it is - unlike the chord's and the tray item's toggle, which stay as they are.
    /// The focus is what the window's own opening path already takes: the notes window as the chord opens it, the
    /// history strip as its tray item shows it, the palette without the editor's focus, the board without any.</para>
    /// <para>Order inside each verb: the module gate first (11 <c>module-off</c> with a sentence naming the setting,
    /// before anything of the module is created or read - the quick notes' journal in particular), then the show, then
    /// exit 0. The base answers the rest: the parser (10), <c>--result</c>, the busy answer, the log line and the
    /// cold start's refusal, all S0107's.</para>
    /// </summary>
    internal static class AppCommandsWindows
    {
        // ------------------------------------------------------------------ note show

        /// <summary>Exit 0 with the notes window on its way up; <paramref name="startNew"/> is the chord's fresh-note path.</summary>
        public static AppCommandResponse NoteShow(AppCommandInvocation invocation, IWindowCommandsHost host, Func<string, string> T)
        {
            try
            {
                if (!host.QuickNotesOn) return AppCommandVerbs.ModuleOff(T("The quick notes are switched off. Turn them on in the settings of CyrFlip."));
                host.ShowQuickNotes(invocation.Flag("new"));
                return AppCommandResponse.Done(T("Done."));
            }
            catch (Exception ex)
            {
                return Crashed("note show", ex, T);
            }
        }

        // ------------------------------------------------------------------ clipboard-history show

        /// <summary>Exit 0 with the history strip visible - and, run again while it is, still visible and brought forward.</summary>
        public static AppCommandResponse HistoryShow(IWindowCommandsHost host, Func<string, string> T)
        {
            try
            {
                if (!host.ClipboardHistoryOn) return AppCommandVerbs.ModuleOff(T("The clipboard history is switched off. Turn it on in the settings of CyrFlip."));
                host.ShowClipboardHistory();
                return AppCommandResponse.Done(T("Done."));
            }
            catch (Exception ex)
            {
                return Crashed("clipboard-history show", ex, T);
            }
        }

        // ------------------------------------------------------------------ symbols show

        /// <summary>Exit 0 with the symbols palette up, the editor's focus untouched.</summary>
        public static AppCommandResponse SymbolsShow(IWindowCommandsHost host, Func<string, string> T)
        {
            try
            {
                if (!host.SymbolsOn) return AppCommandVerbs.ModuleOff(T("The symbols window is switched off. Turn it on in the settings of CyrFlip."));
                host.ShowSymbols();
                return AppCommandResponse.Done(T("Done."));
            }
            catch (Exception ex)
            {
                return Crashed("symbols show", ex, T);
            }
        }

        // ------------------------------------------------------------------ trafficlight show

        /// <summary>Exit 0 with the board up without the focus, raised above whatever covers it.</summary>
        public static AppCommandResponse TrafficLightShow(IWindowCommandsHost host, Func<string, string> T)
        {
            try
            {
                if (!host.TrafficLightOn) return AppCommandVerbs.ModuleOff(T("The traffic light is switched off. Turn it on in the settings of CyrFlip."));
                host.ShowTrafficLight();
                return AppCommandResponse.Done(T("Done."));
            }
            catch (Exception ex)
            {
                return Crashed("trafficlight show", ex, T);
            }
        }

        // ------------------------------------------------------------------ shared

        /// <summary>A fault after the show would otherwise reach the pipe: the caller always gets an answer. Only the type is logged.</summary>
        private static AppCommandResponse Crashed(string verb, Exception ex, Func<string, string> T)
        {
            LauncherLog.Log("Command: " + verb + " failed: " + ex.GetType().Name);
            return AppCommandResponse.Failed(AppCommandReasons.Error, AppCommandExit.Failed,
                T("The command failed. The log bundle on the About page has the details."));
        }
    }
}

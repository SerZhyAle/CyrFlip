using System;
using System.IO;

namespace CyrFlip
{
    /// <summary>
    /// Maps a script scenario to the interpreter that runs it, so the launcher never depends on the
    /// machine's file associations. Windows deliberately gives .ps1 no "open" verb, which makes a
    /// .ps1 impossible to shell-execute by path; .bat/.cmd do have one, but it is machine state that
    /// other software can hijack or remove. Handing both to an explicit interpreter makes a scenario
    /// behave identically everywhere. Port of OneClickRunner's <c>ScriptInterpreter</c> - the quoting
    /// is load-bearing and must not be "simplified" (tech plan §2.1).
    /// </summary>
    internal static class LauncherScriptInterpreter
    {
        /// <summary>
        /// True when <paramref name="scriptPath"/> names a script the launcher runs itself, in which
        /// case <paramref name="interpreter"/>/<paramref name="arguments"/> describe how to start it.
        /// False for anything the shell should open as before (an .exe, a document, a URL).
        /// </summary>
        public static bool TryResolve(string? scriptPath, string? scenarioArguments,
            out string interpreter, out string arguments)
        {
            interpreter = string.Empty;
            arguments = string.Empty;

            string script = scriptPath?.Trim() ?? string.Empty;
            string extra = string.IsNullOrWhiteSpace(scenarioArguments) ? string.Empty : " " + scenarioArguments!.Trim();

            switch (ScriptExtension(script))
            {
                case ".ps1":
                    interpreter = PowerShellHost();
                    // -File must stay last: PowerShell treats every following token as a script
                    // argument. Bypass applies to this launch only and is what lets a scenario run
                    // under the default machine policy, which blocks unsigned scripts.
                    arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"{extra}";
                    return true;

                case ".bat":
                case ".cmd":
                    interpreter = "cmd.exe";
                    // The one cmd quoting form that survives BOTH a path with spaces and quoted
                    // arguments: /s makes cmd strip the outer pair and run the remainder verbatim.
                    // Without /s, arguments containing quotes push the line over cmd's two-quote rule
                    // and it strips the wrong ones, mangling the command.
                    arguments = $"/s /c \"\"{script}\"{extra}\"";
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>The extension to dispatch on; empty for anything that is not run as a local script.</summary>
        private static string ScriptExtension(string path)
        {
            // An http(s) target belongs to the browser even when its URL happens to end in .ps1.
            if (Uri.TryCreate(path, UriKind.Absolute, out Uri? uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                return string.Empty;

            try
            {
                return Path.GetExtension(path).ToLowerInvariant();
            }
            catch (ArgumentException)
            {
                return string.Empty; // invalid path characters: let the shell path report it
            }
        }

        /// <summary>
        /// PowerShell 7 when it is installed, Windows PowerShell otherwise. 7 is preferred because a
        /// script written for it can fail under 5.1, and its installer does not always put it on PATH,
        /// so its default location is probed too.
        /// </summary>
        internal static string PowerShellHost()
        {
            string? onPath = LauncherExecution.ResolveOnPath("pwsh.exe");
            if (onPath != null)
                return onPath;

            string installed = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
            if (File.Exists(installed))
                return installed;

            return LauncherExecution.ResolveOnPath("powershell.exe") ?? "powershell.exe";
        }
    }
}

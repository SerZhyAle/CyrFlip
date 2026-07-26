using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace CyrFlip
{
    /// <summary>Outcome of a launch attempt. Callers decide how to surface a failure.</summary>
    internal sealed class LauncherLaunchResult
    {
        public bool Success { get; private set; }
        /// <summary>True when the user deliberately cancelled (dismissed the yt-dlp prompt or a UAC prompt).</summary>
        public bool Cancelled { get; private set; }
        public string? ErrorMessage { get; private set; }

        public static LauncherLaunchResult Ok() => new LauncherLaunchResult { Success = true };
        public static LauncherLaunchResult UserCancelled() => new LauncherLaunchResult { Cancelled = true };
        public static LauncherLaunchResult Fail(string message) => new LauncherLaunchResult { ErrorMessage = message };
    }

    /// <summary>
    /// The single code path that turns a <see cref="LauncherScenario"/> into a running process - the
    /// port of OneClickRunner's <c>ScenarioLauncher</c>. Every entry point (settings editor, tray
    /// submenu, Jump List / one-shot process, per-scenario hotkey) routes through here, so behaviour
    /// cannot drift between them; elevation is decided solely by <see cref="LauncherScenario.RunAsAdmin"/>.
    ///
    /// Error messages go through the <c>translate</c> callback (Russian source strings, see
    /// <see cref="Localization"/>) because this service does not know the UI language.
    /// </summary>
    internal static class LauncherExecution
    {
        /// <summary>The environment variable that carries the yt-dlp link (never the command line - see BuildYtDlpStartInfo).</summary>
        public const string YtDlpLinkVariable = "CYRFLIP_YTDLP_LINK";

        /// <summary>
        /// Launch <paramref name="item"/>. <paramref name="promptForLink"/> is invoked (on the caller's
        /// thread, which must be the UI thread) when a yt-dlp scenario needs a link; it returns the
        /// link or null when the user cancelled. Executable scenarios never call it.
        /// </summary>
        public static LauncherLaunchResult Launch(LauncherScenario item, Func<string, string> translate,
            Func<string?>? promptForLink = null)
        {
            if (item.IsYtDlp)
                return LaunchYtDlp(item, translate, promptForLink);

            if (!TryResolveTarget(item.Path, item.WorkingDirectory, translate, out string? validationError))
            {
                LauncherLog.Log($"Launch blocked for '{item.Name}': {validationError}");
                return LauncherLaunchResult.Fail(validationError!);
            }

            try
            {
                // A script goes to its interpreter rather than being shell-executed: the shell can only
                // start what the machine has an association for, which .ps1 never has.
                bool isScript = LauncherScriptInterpreter.TryResolve(
                    item.Path, item.Arguments, out string interpreter, out string interpreterArgs);
                var startInfo = new ProcessStartInfo
                {
                    FileName = isScript ? interpreter : item.Path,
                    Arguments = isScript ? interpreterArgs : item.Arguments,
                    UseShellExecute = true,
                };
                if (item.RunAsAdmin)
                    startInfo.Verb = "runas";
                if (!string.IsNullOrWhiteSpace(item.WorkingDirectory))
                    startInfo.WorkingDirectory = item.WorkingDirectory;

                Process? process = Process.Start(startInfo);
                LauncherLog.Log($"Launched '{item.Name}' (pid={process?.Id}, admin={item.RunAsAdmin}): {startInfo.FileName} {startInfo.Arguments}");
                return LauncherLaunchResult.Ok();
            }
            catch (Win32Exception wex) when (wex.NativeErrorCode == 1223) // ERROR_CANCELLED - user declined UAC
            {
                LauncherLog.Log($"Elevation cancelled by user for '{item.Name}'");
                return LauncherLaunchResult.UserCancelled();
            }
            catch (Exception ex)
            {
                LauncherLog.Log($"Launch error for '{item.Name}': {ex.Message}");
                return LauncherLaunchResult.Fail(ex.Message);
            }
        }

        private static LauncherLaunchResult LaunchYtDlp(LauncherScenario item, Func<string, string> translate,
            Func<string?>? promptForLink)
        {
            if (promptForLink == null)
                return LauncherLaunchResult.Fail(translate("Сценарию yt-dlp нужен запрос ссылки, недоступный в этом контексте."));

            string? link = promptForLink();
            if (string.IsNullOrWhiteSpace(link))
            {
                LauncherLog.Log("yt-dlp launch cancelled by user");
                return LauncherLaunchResult.UserCancelled();
            }
            link = link!.Trim();

            string? linkError = ValidateYtDlpLink(link, translate);
            if (linkError != null)
                return LauncherLaunchResult.Fail(linkError);

            if (ResolveOnPath("yt-dlp") == null)
                return LauncherLaunchResult.Fail(translate("yt-dlp не найден в PATH. Установите его, чтобы команда yt-dlp работала в терминале."));

            string outputFolder = ResolveYtDlpOutputFolder(item);
            try
            {
                Directory.CreateDirectory(outputFolder);
            }
            catch (Exception ex)
            {
                return LauncherLaunchResult.Fail(string.Format(
                    translate("Не удалось использовать папку загрузки «{0}»: {1}"), outputFolder, ex.Message));
            }

            try
            {
                ProcessStartInfo startInfo = BuildYtDlpStartInfo(item, link, outputFolder);
                Process? process = Process.Start(startInfo);
                // The link itself is deliberately absent from the log (spec §9).
                LauncherLog.Log($"Started yt-dlp (pid={process?.Id}) in '{outputFolder}'");
                return LauncherLaunchResult.Ok();
            }
            catch (Exception ex)
            {
                LauncherLog.Log($"yt-dlp launch error: {ex.Message}");
                return LauncherLaunchResult.Fail(ex.Message);
            }
        }

        /// <summary>
        /// The untrusted link would let a '"' or control character break out of the quoting below and
        /// hand cmd the rest as a command - reject those outright; a valid URL never contains them.
        /// </summary>
        internal static string? ValidateYtDlpLink(string link, Func<string, string> translate)
        {
            if (link.IndexOf('"') >= 0 || ContainsControlChar(link))
                return translate("Ссылка содержит недопустимые символы (кавычки или управляющие).");
            return null;
        }

        internal static string ResolveYtDlpOutputFolder(LauncherScenario item)
            => string.IsNullOrWhiteSpace(item.YtDlpOutputFolder)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
                : item.YtDlpOutputFolder;

        /// <summary>
        /// Keep the persistent, watchable console window (cmd /k) but never put the raw link on the
        /// command line: it travels in an environment variable and is referenced quoted, so cmd treats
        /// '&amp;', '|', '&lt;', '&gt;' inside it as literal text rather than shell operators.
        /// </summary>
        internal static ProcessStartInfo BuildYtDlpStartInfo(LauncherScenario item, string link, string outputFolder)
        {
            string format = string.IsNullOrWhiteSpace(item.YtDlpFormat) ? string.Empty : item.YtDlpFormat.Trim() + " ";
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k yt-dlp {format}\"%{YtDlpLinkVariable}%\"",
                UseShellExecute = false, // required to pass the environment; also gives us the console window
                WorkingDirectory = outputFolder,
            };
            startInfo.EnvironmentVariables[YtDlpLinkVariable] = link;
            return startInfo;
        }

        /// <summary>
        /// Verify the target is resolvable before starting, so a missing file yields a clear message
        /// instead of an opaque Win32 exception. An existing file, an http(s) URL, a path relative to
        /// the working directory, or a PATH-resolvable bare command all pass.
        /// </summary>
        internal static bool TryResolveTarget(string? rawPath, string? workingDirectory,
            Func<string, string> translate, out string? error)
        {
            error = null;
            string path = rawPath?.Trim() ?? string.Empty;
            if (path.Length == 0)
            {
                error = translate("У сценария не задан путь.");
                return false;
            }

            if (File.Exists(path))
                return true;

            // A URL is launchable by the shell even though it is not a file.
            if (Uri.TryCreate(path, UriKind.Absolute, out Uri? uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                return true;

            // Relative to the scenario's working directory, if one is set.
            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                try
                {
                    if (File.Exists(Path.Combine(workingDirectory, path)))
                        return true;
                }
                catch { /* malformed working directory - fall through to the PATH check */ }
            }

            // A rooted path that does not exist is definitely broken.
            if (Path.IsPathRooted(path))
            {
                error = string.Format(translate("Файл не найден: {0}"), path);
                return false;
            }

            // A bare command (e.g. calc.exe) is fine if it resolves on PATH.
            if (ResolveOnPath(path) != null)
                return true;

            error = string.Format(translate("«{0}» не найден ни как файл, ни в PATH."), path);
            return false;
        }

        /// <summary>Resolve a bare command against PATH / PATHEXT. Returns the full path or null.</summary>
        internal static string? ResolveOnPath(string command)
        {
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv))
                return null;

            bool hasExtension = Path.HasExtension(command);
            string[] extensions = hasExtension
                ? new[] { string.Empty }
                : (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.BAT;.CMD;.COM")
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string dir in pathEnv!.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string ext in extensions)
                {
                    try
                    {
                        string candidate = Path.Combine(dir.Trim(), command + ext);
                        if (File.Exists(candidate))
                            return candidate;
                    }
                    catch { /* invalid PATH entry - skip */ }
                }
            }
            return null;
        }

        private static bool ContainsControlChar(string value)
        {
            foreach (char c in value)
                if (char.IsControl(c))
                    return true;
            return false;
        }
    }
}

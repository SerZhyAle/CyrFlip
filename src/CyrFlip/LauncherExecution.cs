using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

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
    /// <b>What is validated is what is started</b> (ticket S0008, LS-6): <see cref="TryResolveTarget"/>
    /// hands back the absolute path it found, and that path - not the scenario's text - goes to the
    /// shell or the interpreter. A script found only on PATH used to pass validation and then reach
    /// <c>pwsh -File tool.ps1</c>, which does not search PATH; and <c>runas</c> on a bare name
    /// elevated whatever ShellExecute's own search found rather than the file that was checked.
    ///
    /// Error messages go through the <c>translate</c> callback (Russian source strings, see
    /// <see cref="Localization"/>) because this service does not know the UI language.
    /// </summary>
    internal static class LauncherExecution
    {
        /// <summary>The environment variable that carries the yt-dlp link (never the command line - see BuildYtDlpStartInfo).</summary>
        public const string YtDlpLinkVariable = "CYRFLIP_YTDLP_LINK";

        /// <summary>Longest "extra yt-dlp parameters" string that is ever passed.</summary>
        public const int MaxYtDlpFormatLength = 200;

        /// <summary>
        /// One token of the extra yt-dlp parameters. No quote, no <c>%</c>, no <c>^</c>, <c>&amp;</c>
        /// or <c>|</c> - nothing cmd would read - so each token can travel quoted on the command line
        /// and <c>&lt;</c>/<c>&gt;</c> inside the quotes stay literal (format filters use them).
        /// </summary>
        private static readonly Regex YtDlpFormatToken = new Regex(@"^[A-Za-z0-9+/\[\]<>=*._,-]+$", RegexOptions.CultureInvariant);

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

            if (!TryResolveTarget(item.Path, item.WorkingDirectory, translate, out string? resolved, out string? validationError))
            {
                LauncherLog.Log(BlockedLine(item));
                return LauncherLaunchResult.Fail(validationError!);
            }

            string kind = KindOf(resolved!);
            try
            {
                ProcessStartInfo startInfo = BuildStartInfo(item, resolved!);
                Process? process = Process.Start(startInfo);
                LauncherLog.Log(LaunchedLine(item, kind, process?.Id));
                return LauncherLaunchResult.Ok();
            }
            catch (Win32Exception wex) when (wex.NativeErrorCode == 1223) // ERROR_CANCELLED - user declined UAC
            {
                LauncherLog.Log($"Elevation cancelled by user for {Describe(item)}");
                return LauncherLaunchResult.UserCancelled();
            }
            catch (Exception ex)
            {
                LauncherLog.Log(FailedLine(item, kind, ex));
                return LauncherLaunchResult.Fail(ex.Message);
            }
        }

        // ---- What launcher.log may say (ticket S0010 TD-1) ----
        //
        // launcher.log goes into the log bundle the user mails to the author, and a scenario's path
        // and arguments are the user's own data: "--token=..." and connection strings are routine
        // there. So a launch is recorded by the scenario's id and name, its kind, the process id and
        // the error - never by FileName or Arguments. The lines are built here, pure, so the tests
        // prove it without starting a process.

        /// <summary>"'Name' [guid]" - how every launch line names its scenario.</summary>
        internal static string Describe(LauncherScenario item) => $"'{item.Name}' [{item.Id:D}]";

        /// <summary>exe / script / url - the kind of the resolved target, never the target itself.</summary>
        internal static string KindOf(string resolvedPath)
        {
            if (Uri.TryCreate(resolvedPath, UriKind.Absolute, out Uri? uri) && uri != null
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                return "url";
            return LauncherScriptInterpreter.IsScript(resolvedPath) ? "script" : "exe";
        }

        internal static string BlockedLine(LauncherScenario item)
            => $"Launch blocked for {Describe(item)}: the target did not resolve";

        internal static string LaunchedLine(LauncherScenario item, string kind, int? pid)
            => $"Launched {Describe(item)} ({kind}, pid={pid}, admin={item.RunAsAdmin})";

        /// <summary>
        /// The exception's type and, for a Win32 failure, its error code - not its message, which
        /// for some failures quotes the file it could not start.
        /// </summary>
        internal static string FailedLine(LauncherScenario item, string kind, Exception ex)
            => $"Launch error for {Describe(item)} ({kind}): {ex.GetType().Name}"
                + (ex is Win32Exception w ? $" (Win32 error {w.NativeErrorCode})" : "");

        /// <summary>
        /// The start info for an executable scenario whose target resolved to <paramref name="resolvedPath"/>.
        /// A script goes to its interpreter rather than being shell-executed: the shell can only start
        /// what the machine has an association for, which .ps1 never has.
        /// </summary>
        internal static ProcessStartInfo BuildStartInfo(LauncherScenario item, string resolvedPath)
        {
            bool isScript = LauncherScriptInterpreter.TryResolve(
                resolvedPath, item.Arguments, out string interpreter, out string interpreterArgs);
            var startInfo = new ProcessStartInfo
            {
                FileName = isScript ? interpreter : resolvedPath,
                Arguments = isScript ? interpreterArgs : item.Arguments,
                UseShellExecute = true,
            };
            if (item.RunAsAdmin)
                startInfo.Verb = "runas";
            if (!string.IsNullOrWhiteSpace(item.WorkingDirectory))
                startInfo.WorkingDirectory = item.WorkingDirectory;
            return startInfo;
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

            string? ytDlp = ResolveOnPath("yt-dlp");
            // cmd expands a '%' even inside quotes, so a path carrying one is not handed to it.
            if (ytDlp == null || ytDlp.IndexOf('%') >= 0 || ytDlp.IndexOf('"') >= 0)
                return LauncherLaunchResult.Fail(translate("yt-dlp не найден в PATH. Установите его, чтобы команда yt-dlp работала в терминале."));

            if (!IsValidYtDlpFormat(item.YtDlpFormat))
                LauncherLog.Log($"yt-dlp: the stored extra parameters of {Describe(item)} are not passed (characters outside the allowed set)");

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
                ProcessStartInfo startInfo = BuildYtDlpStartInfo(item, link, outputFolder, ytDlp);
                Process? process = Process.Start(startInfo);
                // The link itself is deliberately absent from the log (spec §9).
                LauncherLog.Log(LaunchedLine(item, "yt-dlp", process?.Id));
                return LauncherLaunchResult.Ok();
            }
            catch (Exception ex)
            {
                LauncherLog.Log(FailedLine(item, "yt-dlp", ex));
                return LauncherLaunchResult.Fail(ex.Message);
            }
        }

        /// <summary>
        /// The untrusted link would let a '"' or control character break out of the quoting below and
        /// hand cmd the rest as a command - reject those outright; a valid URL never contains them.
        /// It must also be an absolute http(s) address (S0008 LS-4): a value starting with '-' is an
        /// <i>option</i> to yt-dlp, not a link, and the <c>--</c> in the command line is only the
        /// second line of defence.
        /// </summary>
        internal static string? ValidateYtDlpLink(string link, Func<string, string> translate)
        {
            if (link.IndexOf('"') >= 0 || ContainsControlChar(link))
                return translate("Ссылка содержит недопустимые символы (кавычки или управляющие).");
            if (!Uri.TryCreate(link, UriKind.Absolute, out Uri? uri) || uri == null
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return translate("Ссылка должна быть адресом http:// или https://.");
            return null;
        }

        /// <summary>
        /// Whether the extra yt-dlp parameters may be passed: empty, or space-separated tokens of
        /// <see cref="YtDlpFormatToken"/>, at most <see cref="MaxYtDlpFormatLength"/> characters in
        /// all (open decision 2 of S0008). The field is placed on a cmd command line, and a scenario
        /// XML can come from anywhere - so a stored value outside the set is <b>kept but not
        /// passed</b>, and the scenario dialog says why.
        /// </summary>
        internal static bool IsValidYtDlpFormat(string? format)
        {
            if (string.IsNullOrWhiteSpace(format)) return true;
            string trimmed = format!.Trim();
            if (trimmed.Length > MaxYtDlpFormatLength) return false;
            foreach (string token in trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (!YtDlpFormatToken.IsMatch(token)) return false;
            return true;
        }

        internal static string ResolveYtDlpOutputFolder(LauncherScenario item)
            => string.IsNullOrWhiteSpace(item.YtDlpOutputFolder)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
                : item.YtDlpOutputFolder;

        /// <summary>
        /// Keep the persistent, watchable console window (cmd /k) but never put the raw link on the
        /// command line: it travels in an environment variable and is referenced quoted, so cmd treats
        /// '&amp;', '|', '&lt;', '&gt;' inside it as literal text rather than shell operators.
        ///
        /// Three more rules (S0008 LS-4): yt-dlp is started by the <b>full path</b> that was resolved
        /// on PATH - cmd searches its current directory, the download folder, first - and
        /// <c>NoDefaultCurrentDirectoryInExePath</c> keeps it from doing so for anything it starts
        /// itself; <c>--</c> ends the options before the link; and the extra parameters are passed
        /// only when <see cref="IsValidYtDlpFormat"/> allows them, each token quoted. The outer quote
        /// pair with <c>/s</c> is the one cmd quoting that survives a quoted program path followed by
        /// quoted arguments (see <see cref="LauncherScriptInterpreter"/>).
        /// </summary>
        internal static ProcessStartInfo BuildYtDlpStartInfo(LauncherScenario item, string link, string outputFolder, string ytDlpPath)
        {
            var format = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(item.YtDlpFormat) && IsValidYtDlpFormat(item.YtDlpFormat))
                foreach (string token in item.YtDlpFormat.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    format.Append('"').Append(token).Append("\" ");

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/s /k \"\"{ytDlpPath}\" {format}-- \"%{YtDlpLinkVariable}%\"\"",
                UseShellExecute = false, // required to pass the environment; also gives us the console window
                WorkingDirectory = outputFolder,
            };
            startInfo.EnvironmentVariables[YtDlpLinkVariable] = link;
            startInfo.EnvironmentVariables["NoDefaultCurrentDirectoryInExePath"] = "1";
            return startInfo;
        }

        /// <summary>
        /// Verify the target is resolvable before starting, so a missing file yields a clear message
        /// instead of an opaque Win32 exception, and hand back the <b>absolute</b> path that passed -
        /// the one that is then started. An http(s) URL, an existing rooted file, a path relative to
        /// the working directory, a file relative to the current directory, or a PATH-resolvable bare
        /// command all pass. <paramref name="pathVariable"/> replaces PATH for tests.
        /// </summary>
        internal static bool TryResolveTarget(string? rawPath, string? workingDirectory,
            Func<string, string> translate, out string? resolved, out string? error, string? pathVariable = null)
        {
            resolved = null;
            error = null;
            string path = rawPath?.Trim() ?? string.Empty;
            if (path.Length == 0)
            {
                error = translate("У сценария не задан путь.");
                return false;
            }

            // A URL is launchable by the shell even though it is not a file.
            if (Uri.TryCreate(path, UriKind.Absolute, out Uri? uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                resolved = path;
                return true;
            }

            bool rooted;
            try { rooted = Path.IsPathRooted(path); }
            catch (ArgumentException) { rooted = false; }

            if (rooted)
            {
                if (TryFullPathOfFile(path, out resolved)) return true;
                // A rooted path that does not exist is definitely broken.
                error = string.Format(translate("Файл не найден: {0}"), path);
                return false;
            }

            // Relative to the scenario's working directory, if one is set.
            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                try
                {
                    if (TryFullPathOfFile(Path.Combine(workingDirectory, path), out resolved))
                        return true;
                }
                catch { /* malformed working directory - fall through to the next checks */ }
            }

            if (TryFullPathOfFile(path, out resolved)) return true;

            // A bare command (e.g. calc.exe) is fine if it resolves on PATH.
            resolved = ResolveOnPath(path, pathVariable);
            if (resolved != null)
                return true;

            error = string.Format(translate("«{0}» не найден ни как файл, ни в PATH."), path);
            return false;
        }

        /// <summary>Kept for callers that only need the verdict.</summary>
        internal static bool TryResolveTarget(string? rawPath, string? workingDirectory,
            Func<string, string> translate, out string? error)
            => TryResolveTarget(rawPath, workingDirectory, translate, out _, out error);

        private static bool TryFullPathOfFile(string path, out string? full)
        {
            full = null;
            try
            {
                if (!File.Exists(path)) return false;
                full = Path.GetFullPath(path);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Resolve a bare command against PATH / PATHEXT. Returns the full path or null. A relative
        /// PATH entry (".") is skipped: it names the current directory, which is exactly the search
        /// this resolution exists to avoid.
        /// </summary>
        internal static string? ResolveOnPath(string command, string? pathVariable = null)
        {
            string? pathEnv = pathVariable ?? Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv))
                return null;

            bool hasExtension = Path.HasExtension(command);
            string[] extensions = hasExtension
                ? new[] { string.Empty }
                : (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.BAT;.CMD;.COM")
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string rawDir in pathEnv!.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
            {
                string dir = rawDir.Trim().Trim('"');
                try
                {
                    if (dir.Length == 0 || !Path.IsPathRooted(dir)) continue;
                }
                catch { continue; }

                foreach (string ext in extensions)
                {
                    try
                    {
                        string candidate = Path.Combine(dir, command + ext);
                        if (File.Exists(candidate))
                            return Path.GetFullPath(candidate);
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

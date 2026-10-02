using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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

        /// <summary>The environment variable that carries the download folder, handed to yt-dlp as <c>-P</c>.</summary>
        public const string YtDlpFolderVariable = "CYRFLIP_YTDLP_FOLDER";

        /// <summary>The prefix of the variables that carry the user's own yt-dlp configs (<c>..1</c>, <c>..2</c>).</summary>
        public const string YtDlpConfigVariable = "CYRFLIP_YTDLP_CONFIG";

        /// <summary>cmd by its System32 path (S0034 LS2-7): a bare name is searched for, starting beside CyrFlip.exe.</summary>
        internal static string CmdPath => Path.Combine(Environment.SystemDirectory, "cmd.exe");

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
                return LauncherLaunchResult.Fail(FailureCause.Describe(ex, "Русский"));
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

        /// <summary>
        /// <see cref="Launch"/> for a caller on the UI thread - the thread both low-level hooks share
        /// (ticket S0030 HT-1). The yt-dlp link prompt is modal UI, so it still runs here, on the
        /// caller's thread; everything after it - validation, the PATH walk, <c>Process.Start</c>,
        /// the download folder - runs on the pool, because any of it can touch a network share and
        /// wait out an SMB connect while the whole machine's keyboard waits with it. The task
        /// completes on the pool; surfacing a failure is the caller's job, on its own thread.
        /// </summary>
        public static Task<LauncherLaunchResult> LaunchAsync(LauncherScenario item, Func<string, string> translate,
            Func<string?>? promptForLink = null)
        {
            LauncherScenario snapshot = item.Clone(); // an edit made meanwhile must not reach the worker half-way
            if (!snapshot.IsYtDlp)
                return Task.Run(() => Launch(snapshot, translate));

            LauncherLaunchResult? early = PromptYtDlpLink(translate, promptForLink, out string? link);
            if (early != null)
                return Task.FromResult(early);
            return Task.Run(() => StartYtDlp(snapshot, link!, translate));
        }

        private static LauncherLaunchResult LaunchYtDlp(LauncherScenario item, Func<string, string> translate,
            Func<string?>? promptForLink)
        {
            LauncherLaunchResult? early = PromptYtDlpLink(translate, promptForLink, out string? link);
            return early ?? StartYtDlp(item, link!, translate);
        }

        /// <summary>The UI half of a yt-dlp launch: the link, or the result that ends the launch here.</summary>
        private static LauncherLaunchResult? PromptYtDlpLink(Func<string, string> translate,
            Func<string?>? promptForLink, out string? link)
        {
            link = null;
            if (promptForLink == null)
                return LauncherLaunchResult.Fail(translate("Сценарию yt-dlp нужен запрос ссылки, недоступный в этом контексте."));

            string? answer = promptForLink();
            if (string.IsNullOrWhiteSpace(answer))
            {
                LauncherLog.Log("yt-dlp launch cancelled by user");
                return LauncherLaunchResult.UserCancelled();
            }
            link = answer!.Trim();
            return null;
        }

        /// <summary>The worker half of a yt-dlp launch: validate, resolve, create the folder, start.</summary>
        private static LauncherLaunchResult StartYtDlp(LauncherScenario item, string link, Func<string, string> translate)
        {
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
                return LauncherLaunchResult.Fail(Localization.Format(translate,
                    "Не удалось использовать папку загрузки «{0}»: {1}", outputFolder, FailureCause.Describe(ex, "Русский")));
            }

            try
            {
                ProcessStartInfo startInfo = BuildYtDlpStartInfo(item, link, outputFolder, ytDlp,
                    YtDlpConfigLocations(ytDlp, outputFolder, File.Exists, Environment.GetEnvironmentVariable,
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
                Process? process = Process.Start(startInfo);
                // The link itself is deliberately absent from the log (spec §9).
                LauncherLog.Log(LaunchedLine(item, "yt-dlp", process?.Id));
                return LauncherLaunchResult.Ok();
            }
            catch (Exception ex)
            {
                LauncherLog.Log(FailedLine(item, "yt-dlp", ex));
                return LauncherLaunchResult.Fail(FailureCause.Describe(ex, "Русский"));
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
        /// <para>Three more rules (S0008 LS-4): yt-dlp is started by the <b>full path</b> that was
        /// resolved on PATH, and <c>NoDefaultCurrentDirectoryInExePath</c> keeps cmd from searching its
        /// current directory for anything it starts; <c>--</c> ends the options before the link; and
        /// the extra parameters are passed only when <see cref="IsValidYtDlpFormat"/> allows them, each
        /// token quoted. The outer quote pair with <c>/s</c> is the one cmd quoting that survives a
        /// quoted program path followed by quoted arguments (see <see cref="LauncherScriptInterpreter"/>).</para>
        ///
        /// <para><b>No config is read from the download folder</b> (S0034 LS2-2). yt-dlp loads a
        /// "home" <c>yt-dlp.conf</c> from the folder given to <c>-P</c> - or, without <c>-P</c>, from its
        /// current directory, which used to be the download folder, Downloads by default: a file a web
        /// page can drop there, and a config can say <c>--exec</c>. So the command says
        /// <c>--ignore-config</c> and names the user's own configs - found where yt-dlp itself would
        /// look, <see cref="YtDlpConfigLocations"/> - with <c>--config-locations</c>. The folder is given
        /// as <c>-P</c> rather than as the working directory: cmd refuses a UNC current directory and
        /// falls back to <c>C:\Windows</c>, where the download failed while CyrFlip reported success.
        /// cmd itself starts in the user profile. The folder and the config paths travel in
        /// environment variables like the link - a path may hold a <c>%</c>.</para>
        /// </summary>
        internal static ProcessStartInfo BuildYtDlpStartInfo(LauncherScenario item, string link, string outputFolder,
            string ytDlpPath, System.Collections.Generic.IReadOnlyList<string>? configs = null)
        {
            var configArgs = new StringBuilder();
            var format = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(item.YtDlpFormat) && IsValidYtDlpFormat(item.YtDlpFormat))
                foreach (string token in item.YtDlpFormat.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    format.Append('"').Append(token).Append("\" ");

            var startInfo = new ProcessStartInfo
            {
                FileName = CmdPath,
                UseShellExecute = false, // required to pass the environment; also gives us the console window
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };
            int n = 0;
            foreach (string config in configs ?? Array.Empty<string>())
            {
                string variable = YtDlpConfigVariable + (++n).ToString(System.Globalization.CultureInfo.InvariantCulture);
                startInfo.EnvironmentVariables[variable] = config;
                configArgs.Append("--config-locations \"%").Append(variable).Append("%\" ");
            }
            startInfo.Arguments = $"/s /k \"\"{ytDlpPath}\" --ignore-config {configArgs}-P \"%{YtDlpFolderVariable}%\" "
                + $"{format}-- \"%{YtDlpLinkVariable}%\"\"";
            startInfo.EnvironmentVariables[YtDlpLinkVariable] = link;
            startInfo.EnvironmentVariables[YtDlpFolderVariable] = QuotableFolder(outputFolder);
            startInfo.EnvironmentVariables["NoDefaultCurrentDirectoryInExePath"] = "1";
            return startInfo;
        }

        /// <summary>
        /// A folder that can stand inside a quote pair: yt-dlp reads its arguments by the C runtime's
        /// rules, where <c>\"</c> is an escaped quote - so <c>"D:\"</c> would swallow the closing quote.
        /// A trailing separator becomes <c>\.</c>, the same folder.
        /// </summary>
        internal static string QuotableFolder(string folder)
            => folder.EndsWith("\\", StringComparison.Ordinal) || folder.EndsWith("/", StringComparison.Ordinal)
                ? folder + "."
                : folder;

        /// <summary>
        /// The user's own yt-dlp configs, as <c>--ignore-config</c> would otherwise drop them: the
        /// portable one beside the executable, then the first user config found in yt-dlp's own
        /// search order (<c>%XDG_CONFIG_HOME%</c> or <c>~\.config</c>, <c>%APPDATA%</c>, <c>~</c> - the
        /// order of yt-dlp's <c>get_user_config_dirs</c> and <c>_load_from_config_dirs</c>). Anything
        /// inside the download folder is left out - that is the folder the rule exists for.
        /// </summary>
        internal static System.Collections.Generic.List<string> YtDlpConfigLocations(string ytDlpPath, string outputFolder,
            Func<string, bool> fileExists, Func<string, string?> environment, string home)
        {
            var found = new System.Collections.Generic.List<string>(2);
            string download = NormalizeFolder(outputFolder);
            bool Usable(string path)
            {
                try
                {
                    return path.IndexOf('"') < 0 && fileExists(path)
                        && !string.Equals(NormalizeFolder(Path.GetDirectoryName(path) ?? ""), download, StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            }

            string? exeFolder = null;
            try { exeFolder = Path.GetDirectoryName(ytDlpPath); } catch { }
            if (!string.IsNullOrEmpty(exeFolder) && Usable(Path.Combine(exeFolder!, "yt-dlp.conf")))
                found.Add(Path.Combine(exeFolder!, "yt-dlp.conf"));

            var candidates = new System.Collections.Generic.List<string>(10);
            string xdg = environment("XDG_CONFIG_HOME") ?? "";
            if (xdg.Length == 0) xdg = Path.Combine(home, ".config");
            candidates.Add(Path.Combine(xdg, "yt-dlp.conf"));
            candidates.Add(Path.Combine(xdg, "yt-dlp", "config"));
            candidates.Add(Path.Combine(xdg, "yt-dlp", "config.txt"));
            string appData = environment("APPDATA") ?? "";
            if (appData.Length > 0)
            {
                candidates.Add(Path.Combine(appData, "yt-dlp.conf"));
                candidates.Add(Path.Combine(appData, "yt-dlp", "config"));
                candidates.Add(Path.Combine(appData, "yt-dlp", "config.txt"));
            }
            candidates.Add(Path.Combine(home, "yt-dlp.conf"));
            candidates.Add(Path.Combine(home, "yt-dlp.conf.txt"));
            candidates.Add(Path.Combine(home, ".yt-dlp", "config"));
            candidates.Add(Path.Combine(home, ".yt-dlp", "config.txt"));
            foreach (string candidate in candidates)
                if (Usable(candidate))
                {
                    found.Add(candidate);
                    break;
                }
            return found;
        }

        private static string NormalizeFolder(string folder)
        {
            try { return Path.GetFullPath(folder).TrimEnd('\\', '/'); }
            catch { return folder.TrimEnd('\\', '/'); }
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
                error = Localization.Format(translate, "Файл не найден: {0}", path);
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

            error = Localization.Format(translate, "«{0}» не найден ни как файл, ни в PATH.", path);
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

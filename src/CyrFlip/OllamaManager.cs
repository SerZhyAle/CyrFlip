using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CyrFlip
{
    /// <summary>
    /// Locates, starts and (on the portable build) installs the local Ollama runtime. The translator
    /// itself only speaks HTTP to <see cref="OllamaClient"/>; this module manages the process and the
    /// binary around it, so nothing here knows about prompts or profiles.
    ///
    /// <para><b>MSIX:</b> a packaged build never downloads or launches a third-party installer -
    /// <see cref="CanInstallInPlace"/> is false there and the UI degrades to opening ollama.com,
    /// because Store certification does not allow an app to fetch and run executable code.</para>
    /// </summary>
    internal static class OllamaManager
    {
        public const string DownloadUrl = "https://ollama.com/download/OllamaSetup.exe";
        public const string WebPage = "https://ollama.com/download";

        /// <summary>False in an MSIX package: there the install button only opens the website.</summary>
        public static bool CanInstallInPlace => !PackageInfo.IsPackaged;

        /// <summary>Path to ollama.exe if found (the usual install folders, then PATH), else "".</summary>
        public static string FindExe()
        {
            string[] candidates =
            {
                Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Ollama\ollama.exe"),
                Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA"), @"Ollama\ollama.exe"),
                Combine(Environment.GetEnvironmentVariable("ProgramFiles"), @"Ollama\ollama.exe"),
                Combine(Environment.GetEnvironmentVariable("ProgramW6432"), @"Ollama\ollama.exe"),
            };

            foreach (string candidate in candidates)
                if (candidate.Length > 0 && Exists(candidate)) return candidate;

            string? path = Environment.GetEnvironmentVariable("PATH");
            if (path == null) return "";
            foreach (string entry in path.Split(';'))
            {
                string dir = entry.Trim();
                if (dir.Length == 0) continue;
                string candidate = Combine(dir, "ollama.exe");
                if (candidate.Length > 0 && Exists(candidate)) return candidate;
            }
            return "";
        }

        public static bool IsInstalled() => FindExe().Length > 0;

        /// <summary>True when an Ollama process is alive; the server itself is probed over HTTP.</summary>
        public static bool IsProcessRunning()
        {
            return HasProcess("ollama") || HasProcess("ollama app");
        }

        /// <summary>Starts the server: the tray app when it is installed, otherwise a hidden "ollama serve".</summary>
        public static bool StartServer()
        {
            string exe = FindExe();
            if (exe.Length == 0) return false;
            try
            {
                string dir = Path.GetDirectoryName(exe) ?? "";
                string app = Path.Combine(dir, "ollama app.exe");
                if (File.Exists(app))
                    Process.Start(new ProcessStartInfo(app) { UseShellExecute = true })?.Dispose();
                else
                    Process.Start(new ProcessStartInfo(exe, "serve")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                    })?.Dispose();
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Downloads OllamaSetup.exe to %TEMP%, reporting a language-free progress string
        /// ("42% (300/700 MB)") the caller prefixes with its own localized label. Returns the file
        /// path, or "" when the download failed. Never call this on a packaged build.
        /// </summary>
        public static async Task<string> DownloadInstallerAsync(IProgress<string>? progress, CancellationToken ct)
        {
            if (!CanInstallInPlace) return "";
            string destination = Path.Combine(Path.GetTempPath(), "OllamaSetup.exe");
            try
            {
                EnableModernTls();
                using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
                using HttpResponseMessage response = await client
                    .GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return "";

                long total = response.Content.Headers.ContentLength ?? -1L;
                using Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var file = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920];
                long read = 0;
                int lastPercent = -1;
                while (true)
                {
                    int count = await source.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                    if (count <= 0) break;
                    await file.WriteAsync(buffer, 0, count, ct).ConfigureAwait(false);
                    read += count;
                    if (progress == null) continue;
                    if (total > 0)
                    {
                        int percent = (int)(read * 100L / total);
                        if (percent == lastPercent) continue;
                        lastPercent = percent;
                        progress.Report(percent + "% (" + Mb(read) + "/" + Mb(total) + " MB)");
                    }
                    else progress.Report(Mb(read) + " MB");
                }
                return destination;
            }
            catch (OperationCanceledException) { throw; }
            catch { return ""; }
        }

        public static void RunInstaller(string installerPath)
        {
            if (!CanInstallInPlace) return;
            Open(installerPath);
        }

        public static void OpenWebPage() => Open(WebPage);

        /// <summary>
        /// Open a model's own page in the user's browser. CyrFlip never fetches it: the request is the
        /// browser's, made because the user clicked - so the app still opens no socket of its own
        /// (see <see cref="TranslationLanguages.ModelPageUrl"/> for why we send people there at all).
        /// </summary>
        public static void OpenModelPage(string? model) => Open(TranslationLanguages.ModelPageUrl(model));

        /// <summary>
        /// net48 picks SSL3/TLS1.0 by default, which ollama.com rejects - without this the download
        /// fails with an opaque "connection closed" on an otherwise healthy machine.
        /// </summary>
        private static void EnableModernTls()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch { /* an older machine without TLS 1.2 support - let the request try anyway */ }
        }

        private static bool HasProcess(string name)
        {
            try
            {
                Process[] found = Process.GetProcessesByName(name);
                try { return found.Length > 0; }
                finally { foreach (Process process in found) process.Dispose(); }
            }
            catch { return false; }
        }

        private static void Open(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose(); }
            catch { /* no browser / blocked installer - the caller already told the user what to do */ }
        }

        private static bool Exists(string path)
        {
            try { return File.Exists(path); }
            catch { return false; }
        }

        private static string Combine(string? root, string tail)
        {
            if (string.IsNullOrEmpty(root)) return "";
            try { return Path.Combine(root!, tail); }
            catch { return ""; }
        }

        private static string Mb(long bytes) => (bytes / (1024 * 1024)).ToString();
    }
}

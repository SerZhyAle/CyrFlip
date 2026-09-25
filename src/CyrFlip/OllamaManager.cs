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

        /// <summary>What <see cref="DownloadInstallerAsync"/> came back with.</summary>
        internal enum InstallerStatus
        {
            Ok,
            /// <summary>The network failed, or the file arrived shorter than the server announced.</summary>
            DownloadFailed,
            /// <summary>The file is not validly signed by Ollama - it is deleted, never run.</summary>
            Untrusted,
        }

        internal sealed class InstallerDownload
        {
            public InstallerStatus Status = InstallerStatus.DownloadFailed;
            /// <summary>The verified installer; "" unless <see cref="Status"/> is Ok.</summary>
            public string Path = "";
        }

        /// <summary>
        /// Downloads OllamaSetup.exe, reporting a language-free progress string ("42% (300/700 MB)")
        /// the caller prefixes with its own localized label. Never call this on a packaged build.
        ///
        /// <para>Three checks stand between the download and <see cref="RunInstaller"/> (ticket S0010
        /// TD-5): the file goes into a folder of its own with a random name, not the predictable
        /// <c>%TEMP%\OllamaSetup.exe</c> another process could swap; its length must equal the
        /// <c>Content-Length</c> the server announced, since a cleanly closed short stream is a
        /// truncated exe; and it must carry a valid Authenticode signature by
        /// <see cref="AuthenticodeCheck.OllamaPublisher"/>. A file that fails any of them - or a
        /// download that is cancelled - is deleted.</para>
        /// </summary>
        public static async Task<InstallerDownload> DownloadInstallerAsync(IProgress<string>? progress, CancellationToken ct)
        {
            var result = new InstallerDownload();
            if (!CanInstallInPlace) return result;
            PruneStaleDownloads();
            string folder = Path.Combine(Path.GetTempPath(), DownloadFolderPrefix + Guid.NewGuid().ToString("N"));
            string destination = Path.Combine(folder, "OllamaSetup.exe");
            bool keep = false;
            try
            {
                Directory.CreateDirectory(folder);
                TlsPolicy.EnsureTls12();
                long total, read;
                using (var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
                using (HttpResponseMessage response = await client
                    .GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) return result;

                    total = response.Content.Headers.ContentLength ?? -1L;
                    using Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    using var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    var buffer = new byte[81920];
                    read = 0;
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
                }

                if (!IsComplete(read, total)) return result;
                if (!AuthenticodeCheck.IsSignedBy(destination, AuthenticodeCheck.OllamaPublisher))
                {
                    result.Status = InstallerStatus.Untrusted;
                    return result;
                }
                keep = true;
                result.Status = InstallerStatus.Ok;
                result.Path = destination;
                return result;
            }
            catch (OperationCanceledException) { throw; }
            catch { return result; }
            finally
            {
                if (!keep) TryDeleteFolder(folder);
            }
        }

        /// <summary>
        /// The download is whole: something arrived, and when the server announced a length, exactly
        /// that much (a short read that ended cleanly is otherwise indistinguishable from success).
        /// </summary>
        internal static bool IsComplete(long read, long announced) => read > 0 && (announced < 0 || read == announced);

        private const string DownloadFolderPrefix = "CyrFlip-ollama-";

        /// <summary>
        /// Earlier downloads: the installer has to outlive <see cref="RunInstaller"/> (ShellExecute
        /// returns before it has read its own file), so its folder is cleared on the next download
        /// once it is a day old.
        /// </summary>
        private static void PruneStaleDownloads()
        {
            try
            {
                foreach (string dir in Directory.GetDirectories(Path.GetTempPath(), DownloadFolderPrefix + "*"))
                    if (DateTime.UtcNow - Directory.GetCreationTimeUtc(dir) > TimeSpan.FromDays(1))
                        TryDeleteFolder(dir);
            }
            catch { /* housekeeping only */ }
        }

        private static void TryDeleteFolder(string folder)
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
            catch { /* still open (an installer running from it) - the next prune gets it */ }
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

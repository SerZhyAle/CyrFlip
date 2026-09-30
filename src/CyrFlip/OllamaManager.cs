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

        internal sealed class InstallerDownload : IDisposable
        {
            public InstallerStatus Status = InstallerStatus.DownloadFailed;
            /// <summary>The verified installer; "" unless <see cref="Status"/> is Ok.</summary>
            public string Path = "";

            /// <summary>
            /// The verified file held open from before the signature check until the installer runs
            /// (S0037 TR-4): read sharing only, so no other process can rewrite, rename or delete it
            /// between <see cref="AuthenticodeCheck.IsSignedBy"/> and the launch - each of which opens
            /// it by name again. Released by <see cref="RunInstaller"/> or <see cref="Dispose"/>.
            /// </summary>
            internal FileStream? Guard;

            public void Dispose()
            {
                Guard?.Dispose();
                Guard = null;
            }
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
                    int lastPercent = -1;
                    read = await CopyAsync(source, file, DownloadIdleTimeoutMs, done =>
                    {
                        if (progress == null) return;
                        if (total > 0)
                        {
                            int percent = (int)(done * 100L / total);
                            if (percent == lastPercent) return;
                            lastPercent = percent;
                            progress.Report(percent + "% (" + Mb(done) + "/" + Mb(total) + " MB)");
                        }
                        else progress.Report(Mb(done) + " MB");
                    }, ct).ConfigureAwait(false);
                }

                if (!IsComplete(read, total)) return result;
                // Held from here to the launch, so what is verified is what runs (TR-4).
                result.Guard = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!AuthenticodeCheck.IsSignedBy(destination, AuthenticodeCheck.OllamaPublisher))
                {
                    result.Dispose();
                    result.Status = InstallerStatus.Untrusted;
                    return result;
                }
                keep = true;
                result.Status = InstallerStatus.Ok;
                result.Path = destination;
                return result;
            }
            catch (OperationCanceledException) { result.Dispose(); throw; }
            catch { result.Dispose(); return result; }
            finally
            {
                if (!keep) TryDeleteFolder(folder);
            }
        }

        /// <summary>How long the installer download may go silent before it is given up (S0037 TR-1).</summary>
        internal const int DownloadIdleTimeoutMs = 60_000;

        /// <summary>
        /// Copy <paramref name="source"/> to <paramref name="destination"/>, ending on the caller's
        /// cancel <b>and</b> on silence (S0037 TR-1). On net48 a read pending on a response stream does
        /// not look at its token, and the client's timeout is infinite: a connection that went quiet
        /// after the headers (a Wi-Fi drop, a captive proxy) hung the download for good, and every
        /// translator button in the settings stayed disabled until CyrFlip was restarted. Disposing the
        /// stream is the one thing that ends such a read, so the token and an idle timer re-armed per
        /// chunk both do that - queued to the pool, the way <c>OllamaClient</c> ends a cancelled answer
        /// stream. A cancel comes back as <see cref="OperationCanceledException"/>, silence as
        /// <see cref="TimeoutException"/>.
        /// </summary>
        internal static async Task<long> CopyAsync(Stream source, Stream destination, int idleTimeoutMs,
            Action<long>? onChunk, CancellationToken ct)
        {
            using var idle = new CancellationTokenSource();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, idle.Token);
            using CancellationTokenRegistration abort = linked.Token.Register(() =>
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { source.Dispose(); }
                    catch { /* already closed */ }
                }));
            var buffer = new byte[81920];
            long read = 0;
            while (true)
            {
                idle.CancelAfter(idleTimeoutMs);   // re-armed for every chunk: silence, not a total
                int count;
                try
                {
                    count = await source.ReadAsync(buffer, 0, buffer.Length, linked.Token).ConfigureAwait(false);
                }
                catch (Exception) when (linked.IsCancellationRequested)
                {
                    ct.ThrowIfCancellationRequested();
                    throw new TimeoutException("The download went silent for " + idleTimeoutMs + " ms.");
                }
                if (count <= 0) break;
                await destination.WriteAsync(buffer, 0, count, ct).ConfigureAwait(false);
                read += count;
                onChunk?.Invoke(read);
            }
            return read;
        }

        /// <summary>
        /// The download is whole: something arrived, and when the server announced a length, exactly
        /// that much (a short read that ended cleanly is otherwise indistinguishable from success).
        /// </summary>
        internal static bool IsComplete(long read, long announced) => read > 0 && (announced < 0 || read == announced);

        private const string DownloadFolderPrefix = "CyrFlip-ollama-";

        /// <summary>
        /// Earlier downloads - a few hundred MB each. <see cref="RunInstaller"/> removes its own folder
        /// once the installer exits (S0037 TR-5); what is left (a CyrFlip closed meanwhile, an installer
        /// still holding it) goes here: on the next download, and when the translator page is shown.
        /// A folder younger than a day may belong to an installer running right now, so it is kept -
        /// unless Ollama is installed already, when nothing in them is needed any more (a folder whose
        /// installer still runs fails to delete and simply stays for the next time).
        /// </summary>
        /// <param name="tempFolder">The folder to look in; the user's %TEMP% when null (tests pass their own).</param>
        internal static void PruneStaleDownloads(bool evenRecent = false, string? tempFolder = null)
        {
            try
            {
                foreach (string dir in Directory.GetDirectories(tempFolder ?? Path.GetTempPath(), DownloadFolderPrefix + "*"))
                    if (evenRecent || DateTime.UtcNow - Directory.GetCreationTimeUtc(dir) > TimeSpan.FromDays(1))
                        TryDeleteFolder(dir);
            }
            catch { /* housekeeping only */ }
        }

        /// <summary>The translator page was shown: clear old installer downloads, off the caller's thread.</summary>
        public static void PruneDownloadsInBackground()
        {
            if (!CanInstallInPlace) return;
            Task.Run(() => PruneStaleDownloads(evenRecent: IsInstalled()));
        }

        private static void TryDeleteFolder(string folder)
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
            catch { /* still open (an installer running from it) - the next prune gets it */ }
        }

        /// <summary>
        /// Start the verified installer, then let go of the file it was held by (TR-4), and remove its
        /// folder once the installer has exited (TR-5) - it used to stay in %TEMP% for good, since a
        /// user who has installed Ollama never downloads again.
        /// </summary>
        public static void RunInstaller(InstallerDownload download)
        {
            try
            {
                if (!CanInstallInPlace || download.Status != InstallerStatus.Ok) return;
                Process? installer = null;
                try { installer = Process.Start(new ProcessStartInfo(download.Path) { UseShellExecute = true }); }
                catch { /* declined at the UAC prompt, or blocked - the folder is pruned later */ }
                download.Dispose();   // the process exists (or never will): the file may be released now
                string? folder = Path.GetDirectoryName(download.Path);
                if (installer == null || folder == null) return;
                Task.Run(() =>
                {
                    try { installer.WaitForExit(); }
                    catch { return; }
                    finally { installer.Dispose(); }
                    TryDeleteFolder(folder);
                });
            }
            finally { download.Dispose(); }
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

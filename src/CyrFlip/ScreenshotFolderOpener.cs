using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// Opens the screenshots folder in Explorer (S0057) - the one shared path for the chord and the
    /// settings button, so the two can never disagree about the target. The probe (a local
    /// <see cref="Directory.Exists"/>) runs on a pool worker: the UI thread is the keyboard hook's
    /// thread (S0030 HT-1), and a folder on a pulled drive or an offline share must never stall it;
    /// a remote path is not probed at all, but handed to the shell, which does its own,
    /// out-of-process, waiting. The open and the two balloons are posted back to the UI thread.
    ///
    /// <para>The feature never creates the folder: the saver creates it on the first save, and
    /// opening is a read (section 3.3). Pressing the chord twice opens two windows (D3).</para>
    /// </summary>
    internal sealed class ScreenshotFolderOpener
    {
        private readonly Func<string> _configured;
        private readonly Func<string?> _lastFallback;
        private readonly SynchronizationContext? _ui;
        private readonly Func<string, string> _translate;
        private readonly Action<int, string, ToolTipIcon> _balloon;

        /// <param name="configured">The configured folder value, read on the caller's (UI) thread.</param>
        /// <param name="lastFallback">The folder of this session's last fallback save (D4), or null.</param>
        /// <param name="ui">Where the open and the balloons are posted; the caller's own thread.</param>
        /// <param name="translate">One localization lookup.</param>
        /// <param name="balloon">The tray balloon: timeout, text, icon.</param>
        public ScreenshotFolderOpener(Func<string> configured, Func<string?> lastFallback,
            SynchronizationContext? ui, Func<string, string> translate,
            Action<int, string, ToolTipIcon> balloon)
        {
            _configured = configured;
            _lastFallback = lastFallback;
            _ui = ui;
            _translate = translate;
            _balloon = balloon;
        }

        /// <summary>Open the folder the captures are saved into - from the chord or the settings button.</summary>
        public void Open()
        {
            // Read on the caller's thread, decided on a pool worker, presented back on the UI thread.
            string configured = _configured();
            string? fallback = _lastFallback();
            Task.Run(() =>
            {
                ScreenshotFolderTarget.Resolution resolution;
                try
                {
                    var saver = new ScreenshotSaver();
                    resolution = ScreenshotFolderTarget.Resolve(configured, fallback,
                        saver.DisplayFolder, Directory.Exists, p => LaunchTargets.IsRemotePath(p));
                }
                catch
                {
                    // A resolver that cannot even run leaves the "not created yet" state - the
                    // honest answer when nothing is known about the folder.
                    resolution = new ScreenshotFolderTarget.Resolution
                        { Outcome = ScreenshotFolderTarget.Outcome.NotCreatedYet };
                }
                SynchronizationContext? ui = _ui;
                if (ui != null) ui.Post(_ => Present(resolution), null);
                else Present(resolution);
            });
        }

        private void Present(ScreenshotFolderTarget.Resolution resolution)
        {
            switch (resolution.Outcome)
            {
                case ScreenshotFolderTarget.Outcome.Open:
                    if (resolution.Folder != null) ShellOpen(resolution.Folder);
                    return;
                case ScreenshotFolderTarget.Outcome.Unavailable:
                    // The balloon eats a lone ampersand the way a label does; a path may carry one.
                    _balloon(4000, Localization.Format(_translate, "Папка для снимков недоступна: {0}",
                        (resolution.Configured ?? "").Replace("&", "&&")), ToolTipIcon.Warning);
                    return;
                default:
                    // The very line the settings button has always shown, so the two never word it
                    // differently (section 3.2.3).
                    _balloon(4000, _translate("Этой папки пока нет - она появится при первом сохранённом снимке."),
                        ToolTipIcon.Info);
                    return;
            }
        }

        /// <summary>explorer.exe "&lt;folder&gt;": the same shell-open the settings button always used.</summary>
        private static void ShellOpen(string folder)
        {
            try { Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })?.Dispose(); }
            catch { /* the shell refused - nothing this feature can add to it */ }
        }
    }
}

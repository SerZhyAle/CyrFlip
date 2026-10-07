using System;

namespace CyrFlip
{
    /// <summary>
    /// Which folder the "open the screenshots folder" chord opens (S0057 section 3.2) - a pure
    /// function over a probe seam: no disk, no clock, no shell. The one target is the folder
    /// <see cref="ScreenshotSaver"/> writes to - the same <see cref="ScreenshotSaver.DisplayFolder"/>
    /// rule the settings page shows - and never a second opinion about where screenshots live.
    ///
    /// <para>A <b>remote</b> path is never handed to the probe: the caller checks remoteness first
    /// (a UNC name or a mapped drive) and such a path is opened as written, because probing it
    /// opens an SMB session and waits out its timeout (S0030 HT-1) - the shell does its own,
    /// out-of-process, waiting instead.</para>
    /// </summary>
    internal static class ScreenshotFolderTarget
    {
        internal enum Outcome
        {
            /// <summary>Open <see cref="Resolution.Folder"/> - a probed local folder, or a remote one handed to the shell as written.</summary>
            Open,
            /// <summary>Nothing to open yet: the normal first-run state, told with the settings button's own line.</summary>
            NotCreatedYet,
            /// <summary>The configured folder is gone (a pulled USB stick): named in the balloon.</summary>
            Unavailable,
        }

        internal sealed class Resolution
        {
            public Outcome Outcome { get; set; }
            /// <summary>The folder to open; null unless the outcome is <see cref="Outcome.Open"/>.</summary>
            public string? Folder { get; set; }
            /// <summary>
            /// The configured folder as <see cref="ScreenshotSaver.DisplayFolder"/> writes it - the
            /// <see cref="Outcome.Unavailable"/> balloon's input, and never a second opinion.
            /// </summary>
            public string? Configured { get; set; }
        }

        /// <summary>
        /// Resolve the target. <paramref name="displayFolder"/> is <see cref="ScreenshotSaver.DisplayFolder"/>
        /// over the configured value, <paramref name="exists"/> the disk probe (run by the caller off
        /// the UI thread), <paramref name="isRemote"/> <see cref="LaunchTargets.IsRemotePath"/>.
        /// <paramref name="lastFallback"/> is the folder of the last fallback save of this session
        /// (D4): in memory only, null when the decision is off or nothing was remembered.
        /// </summary>
        internal static Resolution Resolve(string configured, string? lastFallback,
            Func<string, string?> displayFolder, Func<string, bool> exists, Func<string?, bool> isRemote)
        {
            string? shown = displayFolder(configured);
            if (!string.IsNullOrEmpty(shown))
            {
                // A remote path is never probed - it is opened as written (section 3.4).
                if (isRemote(shown!) || exists(shown!))
                    return new Resolution { Outcome = Outcome.Open, Folder = shown, Configured = shown };
            }
            if (!string.IsNullOrEmpty(lastFallback) && (isRemote(lastFallback) || exists(lastFallback!)))
                return new Resolution { Outcome = Outcome.Open, Folder = lastFallback, Configured = shown };

            // No folder to open. An explicitly configured folder that is not there says so and is
            // named; the default Screenshots known folder that simply does not exist yet is the
            // normal first-run state and gets the settings button's own line.
            return string.IsNullOrWhiteSpace(configured)
                ? new Resolution { Outcome = Outcome.NotCreatedYet, Configured = shown }
                : new Resolution { Outcome = Outcome.Unavailable, Configured = shown };
        }
    }
}

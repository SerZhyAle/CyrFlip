using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CyrFlip
{
    /// <summary>An icon source for a Jump List task: a file that carries an icon, plus the icon index.</summary>
    internal readonly struct LauncherIcon
    {
        public readonly string Path;
        public readonly int Index;
        public LauncherIcon(string path, int index) { Path = path; Index = index; }
    }

    /// <summary>
    /// The questions <see cref="LauncherIconResolver"/> asks the machine, each injectable so a test
    /// can count them (ticket S0030 HT-1). Only <see cref="FileExists"/> and
    /// <see cref="ResolveOnPath"/> touch a file system, and neither is ever asked about a path that
    /// <see cref="IsRemote"/> called remote.
    /// </summary>
    internal sealed class LauncherPathProbes
    {
        public Func<string, bool> FileExists { get; set; } = DefaultFileExists;
        public Func<string, string?> ResolveOnPath { get; set; } = command => LauncherExecution.ResolveOnPath(command);
        public Func<string, bool> IsRemote { get; set; } = path => LaunchTargets.IsRemotePath(path);
        public Func<string> PowerShellHost { get; set; } = LauncherScriptInterpreter.PowerShellHost;
        /// <summary>
        /// The icon file of a vocabulary glyph on its plate (<see cref="LauncherShortcutIcons"/>), or null when
        /// it could not be written. Writes a small file, so like the rest of this class it is pool-only.
        /// </summary>
        public Func<string, string?> VocabularyIcon { get; set; } = id => LauncherShortcutIcons.PathFor(id);

        public static LauncherPathProbes Real() => new LauncherPathProbes();

        private static bool DefaultFileExists(string path)
        {
            try { return File.Exists(path); }
            catch { return false; }
        }
    }

    /// <summary>
    /// Picks a non-blank icon per scenario for the Jump List and the settings table. Executables use
    /// their own icon; scripts borrow their interpreter's (PowerShell, cmd, Python, WScript); a yt-dlp
    /// download is the vocabulary's download and anything whose icon cannot be read is the vocabulary's
    /// apps (<c>ICON-EXTERNAL</c> rules 2 and 4, ticket S0022 A6) - never CyrFlip's own mark, which is the
    /// product and not a meaning, so no task ever appears blank or wears the wrong identity (and the
    /// OneClickRunner badge is never presented as this app's brand, tech plan §2.3).
    ///
    /// <para><b>It probes the file system, so it never runs on the UI thread</b> (ticket S0030 HT-1):
    /// the Jump List is built on the pool and the settings table fills its icons in as they arrive
    /// (<see cref="LauncherIconCache"/>). A remote target is not probed at all, even there - it
    /// takes the fallback icon "as written", because a <c>File.Exists</c> on <c>\\nas\tools\x.exe</c>
    /// waits out an SMB connect, and an icon is not worth that.</para>
    /// </summary>
    internal static class LauncherIconResolver
    {
        public static LauncherIcon Resolve(LauncherScenario item, LauncherPathProbes? probes = null)
        {
            probes ??= LauncherPathProbes.Real();
            if (item.IsYtDlp)
                return Vocabulary(AppGlyphs.Download, probes);

            string path = item.Path;
            if (string.IsNullOrWhiteSpace(path))
                return Vocabulary(AppGlyphs.Apps, probes);

            if (probes.IsRemote(path))
                return Vocabulary(AppGlyphs.Apps, probes);

            string extension;
            try { extension = Path.GetExtension(path).ToLowerInvariant(); }
            catch (ArgumentException) { return Vocabulary(AppGlyphs.Apps, probes); }

            switch (extension)
            {
                case ".exe":
                case ".com":
                case ".scr":
                    return probes.FileExists(path) ? new LauncherIcon(path, 0) : PathCommandOrFallback(path, probes);

                case ".ps1":
                    return InterpreterIcon(probes, probes.PowerShellHost());
                case ".bat":
                case ".cmd":
                    return InterpreterIcon(probes, "cmd.exe");
                case ".py":
                case ".pyw":
                    return InterpreterIcon(probes, "py.exe", "python.exe");
                case ".vbs":
                case ".vbe":
                case ".js":
                    return InterpreterIcon(probes, "wscript.exe");

                default:
                    // Some other file type: its own icon if it exists, otherwise the app's.
                    return probes.FileExists(path) ? new LauncherIcon(path, 0) : Vocabulary(AppGlyphs.Apps, probes);
            }
        }

        /// <summary>A bare command like "calc.exe" carries an icon once resolved through PATH.</summary>
        private static LauncherIcon PathCommandOrFallback(string command, LauncherPathProbes probes)
        {
            string? resolved = Found(probes.ResolveOnPath(command), probes);
            return resolved != null ? new LauncherIcon(resolved, 0) : Vocabulary(AppGlyphs.Apps, probes);
        }

        /// <summary>
        /// First resolvable interpreter's icon, or the app fallback. A candidate may be a bare command
        /// to look up on PATH or an already-resolved full path.
        /// </summary>
        private static LauncherIcon InterpreterIcon(LauncherPathProbes probes, params string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                string? resolved;
                if (!Path.IsPathRooted(candidate))
                    resolved = Found(probes.ResolveOnPath(candidate), probes);
                else if (probes.IsRemote(candidate))
                    resolved = null;
                else
                    resolved = probes.FileExists(candidate) ? candidate : null;
                if (resolved != null)
                    return new LauncherIcon(resolved, 0);
            }
            return Vocabulary(AppGlyphs.Apps, probes);
        }

        /// <summary>
        /// A PATH hit on a network folder is a real target, but reading its icon would go back over
        /// the network every time the icon is drawn - the app's icon stands in for it.
        /// </summary>
        private static string? Found(string? resolved, LauncherPathProbes probes)
            => resolved != null && !probes.IsRemote(resolved) ? resolved : null;

        /// <summary>
        /// The glyph of what the scenario stands for, on its plate. When the file cannot be written the path is
        /// empty: the task has no icon (the shell draws its generic one) and the Jump List is still built.
        /// </summary>
        internal static LauncherIcon Vocabulary(string glyphId, LauncherPathProbes probes)
            => new LauncherIcon(probes.VocabularyIcon(glyphId) ?? "", 0);
    }

    /// <summary>
    /// Small display-icon cache for the settings table: one extracted 16x16 image per scenario
    /// (keyed by its id <b>and</b> path, so an edited path is a new entry), so scrolling or filtering
    /// the list never re-extracts. Icons are display-only - a failed extraction never blocks a launch
    /// (tech plan Фаза 2.5). Dispose with the owning form.
    ///
    /// <para><b>Nothing here blocks the caller</b> (ticket S0030 HT-1): <see cref="Get"/> answers from
    /// the cache or returns null and resolves the icon on the pool; the result is handed back on the
    /// caller's synchronization context through the <c>ready</c> callback. Settings used to resolve
    /// every row's icon on the UI thread - the thread the keyboard hook shares - on every refresh,
    /// every checkbox and every character typed into the search box.</para>
    /// </summary>
    internal sealed class LauncherIconCache : IDisposable
    {
        private readonly Dictionary<string, Icon?> _icons = new Dictionary<string, Icon?>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<LauncherScenario, Icon?> _load;
        private SynchronizationContext? _context;
        private int _generation;
        private bool _disposed;

        public LauncherIconCache(Func<LauncherScenario, Icon?>? load = null)
        {
            _load = load ?? LoadIcon;
        }

        internal static string KeyOf(LauncherScenario item)
            => item.Id.ToString("N") + "|" + (item.IsYtDlp ? "yt-dlp" : item.Path);

        /// <summary>
        /// The scenario's icon when it is already known; otherwise null, and the icon is resolved on
        /// the pool and handed to <paramref name="ready"/> on this thread's synchronization context.
        /// Call on the UI thread.
        /// </summary>
        public Icon? Get(LauncherScenario item, Action<Guid, Icon> ready)
        {
            string key = KeyOf(item);
            if (_icons.TryGetValue(key, out Icon? cached))
                return cached;
            if (_disposed || _pending.Contains(key))
                return null;

            // Captured on first use rather than in the constructor: the owning form creates this in
            // a field initializer, i.e. before the WinForms context is installed on the thread.
            _context ??= SynchronizationContext.Current;
            SynchronizationContext? context = _context;
            if (context == null)
                return null;

            _pending.Add(key);
            int generation = _generation;
            LauncherScenario snapshot = item.Clone();
            Task.Run(() =>
            {
                Icon? icon = null;
                try { icon = _load(snapshot); }
                catch { /* display-only - the row simply shows no icon */ }
                try { context.Post(_ => Complete(key, generation, snapshot.Id, icon, ready), null); }
                catch { icon?.Dispose(); } // the owner's thread is gone
            });
            return null;
        }

        /// <summary>The scenario list changed: forget every icon and drop any still being resolved.</summary>
        public void Invalidate()
        {
            _generation++;
            foreach (Icon? icon in _icons.Values)
                icon?.Dispose();
            _icons.Clear();
            _pending.Clear();
        }

        public void Dispose()
        {
            _disposed = true;
            Invalidate();
        }

        private void Complete(string key, int generation, Guid id, Icon? icon, Action<Guid, Icon> ready)
        {
            if (_disposed || generation != _generation)
            {
                icon?.Dispose();
                return;
            }
            _pending.Remove(key);
            _icons[key] = icon;
            if (icon != null)
                ready(id, icon);
        }

        private static Icon? LoadIcon(LauncherScenario item)
        {
            LauncherIcon source = LauncherIconResolver.Resolve(item);
            if (string.IsNullOrEmpty(source.Path))
                return null;
            return Icon.ExtractAssociatedIcon(source.Path);
        }
    }
}

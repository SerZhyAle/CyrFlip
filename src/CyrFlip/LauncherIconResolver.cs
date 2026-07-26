using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

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
    /// Picks a non-blank icon per scenario for the Jump List and the settings table. Executables use
    /// their own icon; scripts borrow their interpreter's (PowerShell, cmd, Python, WScript);
    /// anything unresolved falls back to CyrFlip's own icon so no task ever appears blank - and the
    /// OneClickRunner badge is never presented as this app's brand (tech plan §2.3).
    /// </summary>
    internal static class LauncherIconResolver
    {
        public static LauncherIcon Resolve(LauncherScenario item)
        {
            if (item.IsYtDlp)
                return AppFallback();

            string path = item.Path;
            if (string.IsNullOrWhiteSpace(path))
                return AppFallback();

            string extension;
            try { extension = Path.GetExtension(path).ToLowerInvariant(); }
            catch (ArgumentException) { return AppFallback(); }

            switch (extension)
            {
                case ".exe":
                case ".com":
                case ".scr":
                    return File.Exists(path) ? new LauncherIcon(path, 0) : PathCommandOrFallback(path);

                case ".ps1":
                    return InterpreterIcon(LauncherScriptInterpreter.PowerShellHost());
                case ".bat":
                case ".cmd":
                    return InterpreterIcon("cmd.exe");
                case ".py":
                case ".pyw":
                    return InterpreterIcon("py.exe", "python.exe");
                case ".vbs":
                case ".vbe":
                case ".js":
                    return InterpreterIcon("wscript.exe");

                default:
                    // Some other file type: its own icon if it exists, otherwise the app's.
                    return File.Exists(path) ? new LauncherIcon(path, 0) : AppFallback();
            }
        }

        /// <summary>A bare command like "calc.exe" carries an icon once resolved through PATH.</summary>
        private static LauncherIcon PathCommandOrFallback(string command)
        {
            string? resolved = LauncherExecution.ResolveOnPath(command);
            return resolved != null ? new LauncherIcon(resolved, 0) : AppFallback();
        }

        /// <summary>
        /// First resolvable interpreter's icon, or the app fallback. A candidate may be a bare command
        /// to look up on PATH or an already-resolved full path.
        /// </summary>
        private static LauncherIcon InterpreterIcon(params string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                string? resolved = Path.IsPathRooted(candidate)
                    ? (File.Exists(candidate) ? candidate : null)
                    : LauncherExecution.ResolveOnPath(candidate);
                if (resolved != null)
                    return new LauncherIcon(resolved, 0);
            }
            return AppFallback();
        }

        private static LauncherIcon AppFallback() => new LauncherIcon(Application.ExecutablePath, 0);
    }

    /// <summary>
    /// Small display-icon cache for the settings table: one extracted 16x16 image per icon source
    /// file, so scrolling the list never re-extracts. Icons are display-only - a failed extraction
    /// never blocks a launch (tech plan Фаза 2.5). Dispose with the owning form.
    /// </summary>
    internal sealed class LauncherIconCache : IDisposable
    {
        private readonly Dictionary<string, Icon?> _icons = new Dictionary<string, Icon?>(StringComparer.OrdinalIgnoreCase);

        public Icon? Get(LauncherScenario item)
        {
            LauncherIcon source = LauncherIconResolver.Resolve(item);
            if (string.IsNullOrEmpty(source.Path))
                return null;
            if (_icons.TryGetValue(source.Path, out Icon? cached))
                return cached;

            Icon? icon = null;
            try { icon = Icon.ExtractAssociatedIcon(source.Path); }
            catch { /* display-only - the row simply shows no icon */ }
            _icons[source.Path] = icon;
            return icon;
        }

        public void Dispose()
        {
            foreach (Icon? icon in _icons.Values)
                icon?.Dispose();
            _icons.Clear();
        }
    }
}

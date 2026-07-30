using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CyrFlip
{
    /// <summary>
    /// The shared body of CyrFlip's three append-only diagnostic logs (<see cref="TranslateLog"/>,
    /// <see cref="TextMenuLog"/>, <see cref="LauncherLog"/>): one lock, the MSIX-aware folder, and -
    /// the reason this class exists - <b>rotation</b>.
    ///
    /// <para>These files never had a cap. <c>context-menu.log</c> alone writes a line per menu
    /// opening and per click, so on a machine that runs CyrFlip for years the logs grow without any
    /// bound at all. <see cref="SupportBundle"/> already reads only their tails, which is precisely
    /// the admission that the files themselves outgrow their usefulness.</para>
    ///
    /// <para><b>Rotation happens once per file per session</b>, on the first write - a log does not
    /// grow by megabytes within one run, so checking on every append would be pure overhead. The
    /// <b>tail</b> is what survives (the recent session is the interesting one), it starts on a line
    /// boundary, and it is introduced by a marker line: a silently shortened log reads as a complete
    /// one, which is how a reader concludes that something "never happened".</para>
    /// </summary>
    internal static class DiagnosticLog
    {
        /// <summary>Above this, the file is rotated on the session's first write.</summary>
        public const long MaxBytes = 2L * 1024 * 1024;

        /// <summary>How much of the tail survives rotation.</summary>
        public const int KeepBytes = 512 * 1024;

        private static readonly object Lock = new object();

        /// <summary>Files already considered for rotation in this process (see the class remarks).</summary>
        private static readonly HashSet<string> Rotated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The folder every CyrFlip log lives in: <c>%LOCALAPPDATA%\CyrFlip</c>, or
        /// <c>%ProgramData%\CyrFlip</c> when packaged (a write to %LOCALAPPDATA% is virtualized into
        /// the package container, where no outside reader would find it).
        /// </summary>
        public static string Path(string fileName) => System.IO.Path.Combine(
            Environment.GetFolderPath(PackageInfo.IsPackaged
                ? Environment.SpecialFolder.CommonApplicationData   // %ProgramData%
                : Environment.SpecialFolder.LocalApplicationData),  // %LOCALAPPDATA%
            "CyrFlip", fileName);

        /// <summary>
        /// Append one line, rotating the file first if this is the session's first write to it.
        /// Every failure is swallowed: diagnostics must never affect the app.
        /// </summary>
        public static void Append(string path, string line)
        {
            try
            {
                lock (Lock)
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                    if (Rotated.Add(path))
                        Rotate(path, MaxBytes, KeepBytes);
                    File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { /* diagnostics must never affect the app */ }
        }

        /// <summary>
        /// Cut <paramref name="path"/> down to its last <paramref name="keepBytes"/> bytes when it
        /// exceeds <paramref name="maxBytes"/>, keeping whole lines and prefixing a marker that says
        /// what was dropped. Internal (with the sizes as arguments) so the tests need not write 2 MB.
        /// </summary>
        internal static void Rotate(string path, long maxBytes, int keepBytes)
        {
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists || file.Length <= maxBytes) return;

                long length = file.Length;
                byte[] tail = new byte[keepBytes];
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    stream.Seek(length - keepBytes, SeekOrigin.Begin);
                    int read = 0;
                    while (read < tail.Length)
                    {
                        int chunk = stream.Read(tail, read, tail.Length - read);
                        if (chunk <= 0) break;
                        read += chunk;
                    }
                }

                // Start just past the first newline, so the file never opens on half a line.
                int start = Array.IndexOf(tail, (byte)'\n');
                start = start < 0 ? 0 : start + 1;
                long dropped = length - (keepBytes - start);

                byte[] marker = Encoding.UTF8.GetBytes(
                    "--- rotated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ": first " + dropped
                    + " bytes of " + length + " dropped, tail follows ---" + Environment.NewLine);

                // Rewritten in place rather than through a temp file that replaces it: replacing
                // means deleting, and a file another process holds open cannot be deleted - which is
                // exactly the case that matters, since SupportBundle reads these very logs (with
                // FileShare.ReadWrite) to build an archive, and the user may well have one open too.
                // A crash mid-rewrite leaves a truncated diagnostic log, which is an acceptable
                // trade for rotation that actually happens.
                using (var output = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                {
                    output.Seek(0, SeekOrigin.Begin);
                    output.Write(marker, 0, marker.Length);
                    output.Write(tail, start, keepBytes - start);
                    output.SetLength(output.Position);
                }
            }
            catch { /* a log we cannot rotate is a log we simply keep appending to */ }
        }

        /// <summary>Test seam: forget which files were already rotated in this process.</summary>
        internal static void ResetRotationState()
        {
            lock (Lock) Rotated.Clear();
        }
    }
}

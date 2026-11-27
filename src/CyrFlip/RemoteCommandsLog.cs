using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CyrFlip
{
    /// <summary>
    /// The target's log of REMOTE-CONTROL rule 11 (<c>remote-commands.log</c>, ticket CYRFLP-0140): one line per request that
    /// opened - the time, the controller's name and short id, the verb, its class, the outcome and the exit code - and one
    /// <c>sealed-failed</c> line, the clear <c>fromDeviceId</c> marked unverified, for a box that did not open. An argument
    /// is written by its name only, never its value. The External control window shows the last lines; nothing of it goes to a server.
    /// </summary>
    internal static class RemoteCommandsLog
    {
        public const string FileName = "remote-commands.log";
        /// <summary>The lines the External control window shows (section T5 of `WINDOW.md`: the last 20).</summary>
        public const int RecentLines = 20;

        private static readonly object Gate = new object();
        private static readonly LinkedList<string> Recent = new LinkedList<string>();

        /// <summary>Raised after a line was added, on the thread that wrote it.</summary>
        public static event Action? Written;

        public static string FilePath => DiagnosticLog.Path(FileName);

        /// <summary>The last lines, oldest first.</summary>
        public static IReadOnlyList<string> Last()
        {
            lock (Gate) return Recent.ToList();
        }

        public static void Request(string controllerName, string controllerId, string verb, string risk, string outcome, int exit,
            IEnumerable<string> argNames)
        {
            string args = string.Join(", ", argNames.Select(a => Clean(a, 32)));
            Write(Clean(controllerName, 40) + " (" + Short(controllerId) + ") | " + Clean(verb, 40) + " | " + risk + " | " + outcome
                  + " | exit " + exit.ToString(CultureInfo.InvariantCulture) + (args.Length > 0 ? " | args: " + args : ""));
        }

        public static void SealedFailed(string clearFrom, string failure)
            => Write("sealed-failed (" + Clean(failure, 24) + ") | from " + Short(clearFrom) + " (unverified)");

        /// <summary>A fault of the listener: the site and the exception's type only.</summary>
        public static void Fault(string site, Exception? ex)
        {
            try
            {
                DiagnosticLog.Append(FilePath, Stamp() + " | fault " + site + (ex != null ? " " + ex.GetType().Name : ""));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                // a log that cannot be written never stops a command
            }
        }

        /// <summary>Reads the tail of the file once, so the window shows the last lines after a restart too (call on the pool).</summary>
        public static void LoadRecentFromFile()
        {
            try
            {
                string path = FilePath;
                if (!File.Exists(path)) return;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                long start = Math.Max(0, stream.Length - 16384);
                stream.Seek(start, SeekOrigin.Begin);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                string[] lines = reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0 && !l.Contains(" | fault ")).ToArray();
                if (start > 0 && lines.Length > 0) lines = lines.Skip(1).ToArray(); // the first may be cut
                lock (Gate)
                {
                    if (Recent.Count > 0) return; // a request already arrived; its lines are newer
                    foreach (string line in lines.Skip(Math.Max(0, lines.Length - RecentLines))) Recent.AddLast(line);
                }
                Written?.Invoke();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                // nothing to show is the answer
            }
        }

        private static void Write(string body)
        {
            string line = Stamp() + " | " + body;
            lock (Gate)
            {
                Recent.AddLast(line);
                while (Recent.Count > RecentLines) Recent.RemoveFirst();
            }
            try { DiagnosticLog.Append(FilePath, line); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                // the in-memory lines still show in the window
            }
            Written?.Invoke();
        }

        private static string Stamp() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        private static string Short(string id) => string.IsNullOrEmpty(id) ? "?" : Clean(id.Substring(0, Math.Min(8, id.Length)), 8);

        /// <summary>One line of printable text: no control character, no separator that would split the line's fields.</summary>
        internal static string Clean(string? text, int max)
        {
            var sb = new StringBuilder();
            foreach (char c in text ?? "")
            {
                if (sb.Length >= max) break;
                if (c < 0x20 || c == 0x7F || c == '|' || char.GetUnicodeCategory(c) == UnicodeCategory.Format) continue;
                sb.Append(c);
            }
            string s = sb.ToString().Trim();
            return s.Length > 0 ? s : "?";
        }
    }
}

using System;
using System.IO;
using System.Text;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Rotation of the append-only diagnostic logs. The sizes are arguments rather than the
    /// production constants so a test never has to write two megabytes to prove a rule.
    /// </summary>
    public sealed class DiagnosticLogTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "CyrFlipLogTests-" + Guid.NewGuid().ToString("N"));

        public DiagnosticLogTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        private string Write(string name, params string[] lines)
        {
            string path = Path.Combine(_dir, name);
            File.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine, Encoding.UTF8);
            return path;
        }

        [Fact]
        public void A_file_below_the_cap_is_left_alone()
        {
            string path = Write("small.log", "one", "two", "three");
            string before = File.ReadAllText(path);

            DiagnosticLog.Rotate(path, maxBytes: 1024, keepBytes: 256);

            Assert.Equal(before, File.ReadAllText(path));
        }

        [Fact]
        public void An_absent_file_is_not_an_error()
        {
            string path = Path.Combine(_dir, "missing.log");

            DiagnosticLog.Rotate(path, maxBytes: 10, keepBytes: 5);

            Assert.False(File.Exists(path));
        }

        [Fact]
        public void An_oversized_file_keeps_its_tail_behind_a_marker()
        {
            var lines = new string[400];
            for (int i = 0; i < lines.Length; i++) lines[i] = "line " + i.ToString("D4") + " " + new string('x', 90);
            string path = Write("big.log", lines);
            long original = new FileInfo(path).Length;

            DiagnosticLog.Rotate(path, maxBytes: 4096, keepBytes: 2048);

            string[] rotated = File.ReadAllLines(path);
            Assert.StartsWith("--- rotated ", rotated[0]);
            Assert.Contains("dropped, tail follows ---", rotated[0]);
            // The tail starts on a line boundary, never mid-line: the second line has to be one of
            // the originals, whole.
            Assert.Contains(rotated[1], lines);
            // The last line written is always the last line kept.
            Assert.Equal(lines[lines.Length - 1], rotated[rotated.Length - 1]);
            Assert.True(new FileInfo(path).Length < original, "the file did not get smaller");
            Assert.True(new FileInfo(path).Length <= 2048 + 200, "the tail plus marker exceeded the budget");
        }

        [Fact]
        public void Rotation_reports_how_much_it_dropped()
        {
            var lines = new string[400];
            for (int i = 0; i < lines.Length; i++) lines[i] = new string('y', 100);
            string path = Write("counted.log", lines);
            long original = new FileInfo(path).Length;

            DiagnosticLog.Rotate(path, maxBytes: 4096, keepBytes: 2048);

            string marker = File.ReadAllLines(path)[0];
            // "first N bytes of M dropped" - both numbers are the real ones, so a reader can tell
            // what is missing rather than guessing. N is at least everything outside the kept
            // window, and a little more because the tail is cut forward to a line boundary.
            Assert.Contains(" bytes of " + original + " dropped", marker);
            int first = marker.IndexOf("first ", StringComparison.Ordinal) + "first ".Length;
            int end = marker.IndexOf(' ', first);
            long reported = long.Parse(marker.Substring(first, end - first));
            Assert.InRange(reported, original - 2048, original);
        }

        [Fact]
        public void Append_rotates_once_per_session_then_only_appends()
        {
            var lines = new string[400];
            for (int i = 0; i < lines.Length; i++) lines[i] = new string('z', 100);
            string path = Write("session.log", lines);
            DiagnosticLog.ResetRotationState();

            // The production entry point rotates on the first write of the session..
            DiagnosticLog.Rotate(path, maxBytes: 4096, keepBytes: 2048);
            long afterRotation = new FileInfo(path).Length;

            DiagnosticLog.Append(path, "first message");
            DiagnosticLog.Append(path, "second message");

            string[] result = File.ReadAllLines(path);
            Assert.Equal("second message", result[result.Length - 1]);
            Assert.Equal("first message", result[result.Length - 2]);
            // ..and the appends grew the file rather than rotating it again.
            Assert.True(new FileInfo(path).Length > afterRotation);
        }

        [Fact]
        public void Append_creates_the_file_and_its_folder()
        {
            string path = Path.Combine(_dir, "nested", "fresh.log");

            DiagnosticLog.Append(path, "hello");

            Assert.Equal(new[] { "hello" }, File.ReadAllLines(path));
        }

        [Fact]
        public void A_log_held_open_for_writing_is_still_rotated()
        {
            var lines = new string[400];
            for (int i = 0; i < lines.Length; i++) lines[i] = new string('w', 100);
            string path = Write("locked.log", lines);

            // A reader that demanded exclusive access would fail on exactly the file we care about,
            // since these logs are ours and we hold them open.
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                DiagnosticLog.Rotate(path, maxBytes: 4096, keepBytes: 2048);

            Assert.StartsWith("--- rotated ", File.ReadAllLines(path)[0]);
        }
    }
}

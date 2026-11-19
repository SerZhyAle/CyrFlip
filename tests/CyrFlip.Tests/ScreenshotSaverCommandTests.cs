using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The caller-named write of the screenshot verbs (ticket S0110): <see cref="ScreenshotSaver.SaveTo"/> over an in-memory
    /// file system. The path is used exactly as given, an overwrite is one rename over the target, nothing is created
    /// that the plan did not name, the temporary file never survives, and the PNG carries the <c>tIME</c> chunk of the
    /// freeze instant in both forms (CAPTURE-OUTPUT rule 16 as 0.5 states it).
    /// </summary>
    public class ScreenshotSaverCommandTests
    {
        private static readonly DateTime Instant = new DateTime(2026, 11, 15, 9, 8, 7, DateTimeKind.Local);

        internal sealed class CommandFs : IScreenshotFileSystem
        {
            private readonly object _gate = new object();
            public readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> Folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\work", @"C:\x", @"C:\shots" };
            public readonly List<string> Created = new List<string>();
            public readonly List<string> Log = new List<string>();
            /// <summary>Thrown by <c>WriteAllBytes</c> after a fragment is already on disk, as a full volume would.</summary>
            public Exception? WriteFailure;
            public Exception? ReplaceFailure;
            /// <summary>Holds <c>WriteAllBytes</c> until opened - a share that does not answer.</summary>
            public ManualResetEvent? WriteGate;
            /// <summary>Holds <c>DirectoryExists</c> until opened.</summary>
            public ManualResetEvent? ProbeGate;

            public bool DirectoryExists(string path)
            {
                ProbeGate?.WaitOne();
                lock (_gate) return Folders.Contains(path);
            }

            public bool FileExists(string path)
            {
                lock (_gate) return Files.ContainsKey(path) || Folders.Contains(path);
            }

            public void CreateDirectory(string path)
            {
                lock (_gate) { Folders.Add(path); Created.Add(path); }
            }

            public void WriteAllBytes(string path, byte[] bytes)
            {
                WriteGate?.WaitOne();
                lock (_gate)
                {
                    string parent = Path.GetDirectoryName(path) ?? "";
                    if (!Folders.Contains(parent)) throw new DirectoryNotFoundException();
                    if (Files.ContainsKey(path)) throw new IOException("exists");
                    Files[path] = new byte[] { 0 };
                    if (WriteFailure != null) throw WriteFailure;
                    Files[path] = (byte[])bytes.Clone();
                    Log.Add("write " + path);
                }
            }

            public bool MoveNoReplace(string from, string to)
            {
                lock (_gate)
                {
                    if (Files.ContainsKey(to)) return false;
                    Files[to] = Files[from];
                    Files.Remove(from);
                    Log.Add("move " + to);
                    return true;
                }
            }

            public bool MoveReplace(string from, string to)
            {
                lock (_gate)
                {
                    if (ReplaceFailure != null) throw ReplaceFailure;
                    Files[to] = Files[from];
                    Files.Remove(from);
                    Log.Add("replace " + to);
                    return true;
                }
            }

            public void DeleteFile(string path)
            {
                lock (_gate) Files.Remove(path);
            }

            public bool AnyTemp()
            {
                lock (_gate) return Files.Keys.Any(k => Path.GetFileName(k).StartsWith("~", StringComparison.Ordinal));
            }

            public byte[]? Read(string path)
            {
                lock (_gate) return Files.TryGetValue(path, out byte[]? bytes) ? bytes : null;
            }
        }

        internal static byte[] TinyPng()
        {
            using var bitmap = new Bitmap(4, 3);
            return ClipboardImage.EncodePng(bitmap);
        }

        private static ScreenshotSaver Saver(CommandFs fs, int budgetMs = 5000)
            => new ScreenshotSaver(fs, () => null, () => null, TimeSpan.FromMilliseconds(budgetMs));

        private static OutputPlan Plan(CommandFs fs, string arg, bool overwrite = false)
        {
            OutputResolution resolution = OutputPlan.Resolve(arg, @"C:\work", overwrite, fs);
            Assert.True(resolution.Plan != null, arg + " -> " + resolution.Problem);
            return resolution.Plan!;
        }

        private static int ReadBigEndian(byte[] bytes, int offset)
            => (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

        /// <summary>The <c>tIME</c> chunk right after IHDR, read back from the bytes: signature 8 + IHDR 25 = 33.</summary>
        internal static DateTime TimeChunk(byte[] file)
        {
            Assert.Equal(7, ReadBigEndian(file, 33));
            Assert.Equal("tIME", Encoding.ASCII.GetString(file, 37, 4));
            return new DateTime((file[41] << 8) | file[42], file[43], file[44], file[45], file[46], file[47]);
        }

        // ------------------------------------------------------------------ a named file

        [Fact]
        public void TheFileLandsAtExactlyThePathWithNoOrdinalAndNoCleaning()
        {
            var fs = new CommandFs();
            byte[] png = TinyPng();
            OutputPlan plan = Plan(fs, @"C:\x\my shot (2) é.png");

            ScreenshotSaver.SaveToResult result = Saver(fs).SaveTo(png, Instant, plan);

            Assert.Equal(ScreenshotSaver.SaveToOutcome.Saved, result.Outcome);
            Assert.Equal(@"C:\x\my shot (2) é.png", result.FilePath);
            Assert.Equal(PngTime.Inject(png, Instant), fs.Read(@"C:\x\my shot (2) é.png"));
            Assert.False(fs.AnyTemp());
            Assert.Single(fs.Files);
            Assert.Empty(fs.Created);
        }

        [Fact]
        public void TheTimeChunkIsTheFreezeInstantInTheFileForm()
        {
            var fs = new CommandFs();
            Saver(fs).SaveTo(TinyPng(), Instant, Plan(fs, @"C:\x\a.png"));
            Assert.Equal(Instant, TimeChunk(fs.Read(@"C:\x\a.png")!));
        }

        [Fact]
        public void TheTimeChunkIsTheFreezeInstantInTheFolderFormToo()
        {
            var fs = new CommandFs();
            ScreenshotSaver.SaveToResult result = Saver(fs).SaveTo(TinyPng(), Instant, Plan(fs, @"C:\shots\"));
            Assert.Equal(Instant, TimeChunk(fs.Read(result.FilePath!)!));
        }

        [Fact]
        public void OverwriteReplacesTheFileWholeAndLeavesNoTemporaryFile()
        {
            var fs = new CommandFs();
            fs.Files[@"C:\x\a.png"] = new byte[] { 1, 2, 3 };
            byte[] png = TinyPng();
            OutputPlan plan = Plan(fs, @"C:\x\a.png", overwrite: true);

            ScreenshotSaver.SaveToResult result = Saver(fs).SaveTo(png, Instant, plan);

            Assert.Equal(ScreenshotSaver.SaveToOutcome.Saved, result.Outcome);
            Assert.Equal(PngTime.Inject(png, Instant), fs.Read(@"C:\x\a.png"));
            Assert.False(fs.AnyTemp());
            Assert.Contains(fs.Log, line => line.StartsWith("replace ", StringComparison.Ordinal));
        }

        [Fact]
        public void AFileThatAppearedAfterThePlanSurvivesUntouchedWithoutOverwrite()
        {
            var fs = new CommandFs();
            OutputPlan plan = Plan(fs, @"C:\x\a.png");
            fs.Files[@"C:\x\a.png"] = new byte[] { 7, 7, 7 };

            ScreenshotSaver.SaveToResult result = Saver(fs).SaveTo(TinyPng(), Instant, plan);

            Assert.Equal(ScreenshotSaver.SaveToOutcome.Exists, result.Outcome);
            Assert.Equal(new byte[] { 7, 7, 7 }, fs.Read(@"C:\x\a.png"));
            Assert.False(fs.AnyTemp());
        }

        [Fact]
        public void AFailedWriteLeavesTheOldFileAndNoTemporaryFile()
        {
            var fs = new CommandFs { WriteFailure = new IOException("boom") };
            fs.Files[@"C:\x\a.png"] = new byte[] { 1, 2, 3 };
            OutputPlan plan = Plan(fs, @"C:\x\a.png", overwrite: true);

            ScreenshotSaver.SaveToResult result = Saver(fs).SaveTo(TinyPng(), Instant, plan);

            Assert.Equal(ScreenshotSaver.SaveToOutcome.Failed, result.Outcome);
            Assert.Equal("IOException", result.Reason);
            Assert.Equal(new byte[] { 1, 2, 3 }, fs.Read(@"C:\x\a.png"));
            Assert.False(fs.AnyTemp());
        }

        [Fact]
        public void AFailedRenameOverTheTargetLeavesTheOldFileAndNoTemporaryFile()
        {
            var fs = new CommandFs { ReplaceFailure = new IOException("access denied", 5) };
            fs.Files[@"C:\x\a.png"] = new byte[] { 1, 2, 3 };
            OutputPlan plan = Plan(fs, @"C:\x\a.png", overwrite: true);

            ScreenshotSaver.SaveToResult result = Saver(fs).SaveTo(TinyPng(), Instant, plan);

            Assert.Equal(ScreenshotSaver.SaveToOutcome.Failed, result.Outcome);
            Assert.Equal(new byte[] { 1, 2, 3 }, fs.Read(@"C:\x\a.png"));
            Assert.False(fs.AnyTemp());
        }

        // ------------------------------------------------------------------ what failed

        [Theory]
        [InlineData(0x80070070L)] // ERROR_DISK_FULL as an HRESULT
        [InlineData(112L)]        // ERROR_DISK_FULL
        [InlineData(39L)]         // ERROR_HANDLE_DISK_FULL
        public void AFullVolumeIsToldByTheErrorCode(long code)
        {
            var fs = new CommandFs { WriteFailure = new IOException("full", unchecked((int)code)) };
            ScreenshotSaver.SaveToResult result = Saver(fs).SaveTo(TinyPng(), Instant, Plan(fs, @"C:\x\a.png"));

            Assert.Equal(ScreenshotSaver.SaveToOutcome.DiskFull, result.Outcome);
            Assert.Null(result.FilePath);
            Assert.False(fs.AnyTemp());
        }

        [Fact]
        public void AFolderThatVanishedAfterThePlanIsMissingAndNeverCreated()
        {
            var fs = new CommandFs();
            OutputPlan plan = Plan(fs, @"C:\x\a.png");
            fs.Folders.Remove(@"C:\x");

            ScreenshotSaver.SaveToResult result = Saver(fs).SaveTo(TinyPng(), Instant, plan);

            Assert.Equal(ScreenshotSaver.SaveToOutcome.FolderMissing, result.Outcome);
            Assert.Empty(fs.Created);
            Assert.Empty(fs.Files);
        }

        [Fact]
        public void APathNotFoundErrorCodeIsAMissingFolderToo()
        {
            var fs = new CommandFs { WriteFailure = new IOException("gone", 3) };
            ScreenshotSaver.SaveToResult result = Saver(fs).SaveTo(TinyPng(), Instant, Plan(fs, @"C:\x\a.png"));
            Assert.Equal(ScreenshotSaver.SaveToOutcome.FolderMissing, result.Outcome);
        }

        [Fact]
        public void TheReasonOfAFailureIsAnExceptionTypeNeverAPath()
        {
            var fs = new CommandFs { WriteFailure = new UnauthorizedAccessException(@"Access to the path 'C:\x\secret-4711.png' is denied") };
            ScreenshotSaver.SaveToResult result = Saver(fs).SaveTo(TinyPng(), Instant, Plan(fs, @"C:\x\secret-4711.png"));

            Assert.Equal(ScreenshotSaver.SaveToOutcome.Failed, result.Outcome);
            Assert.Equal("UnauthorizedAccessException", result.Reason);
        }

        [Fact]
        public void AnOverrunOfTheBudgetIsATimeoutAndTheAbandonedAttemptCleansUp()
        {
            using var gate = new ManualResetEvent(false);
            var fs = new CommandFs { WriteGate = gate };
            OutputPlan plan = Plan(fs, @"C:\x\a.png");

            ScreenshotSaver.SaveToResult result = Saver(fs, budgetMs: 150).SaveTo(TinyPng(), Instant, plan);
            Assert.Equal(ScreenshotSaver.SaveToOutcome.Timeout, result.Outcome);

            gate.Set(); // the share answers at last
            Assert.True(SpinWait.SpinUntil(() => !fs.AnyTemp() && fs.Read(@"C:\x\a.png") == null, 3000),
                "the abandoned attempt deletes its temporary file and never renames it into place");
        }

        // ------------------------------------------------------------------ folder mode

        [Fact]
        public void AFolderGetsTheRuleThreeNameAtTheFreezeInstant()
        {
            var fs = new CommandFs();
            ScreenshotSaver.SaveToResult result = Saver(fs).SaveTo(TinyPng(), Instant, Plan(fs, @"C:\shots\"));

            Assert.Equal(ScreenshotSaver.SaveToOutcome.Saved, result.Outcome);
            Assert.Equal(@"C:\shots\screenshot_261115_090807.png", result.FilePath);
            Assert.False(fs.AnyTemp());
        }

        [Fact]
        public void ATakenAutomaticNameGetsRuleFivesOrdinalAndNeverReplacesTheOldFile()
        {
            var fs = new CommandFs();
            fs.Files[@"C:\shots\screenshot_261115_090807.png"] = new byte[] { 5 };
            fs.Files[@"C:\shots\screenshot_261115_090807 (2).png"] = new byte[] { 6 };
            // Overwrite given with a folder: it has no effect - an automatic name is never replaced.
            ScreenshotSaver.SaveToResult result = Saver(fs).SaveTo(TinyPng(), Instant, Plan(fs, @"C:\shots\", overwrite: true));

            Assert.Equal(@"C:\shots\screenshot_261115_090807 (3).png", result.FilePath);
            Assert.Equal(new byte[] { 5 }, fs.Read(@"C:\shots\screenshot_261115_090807.png"));
            Assert.Equal(new byte[] { 6 }, fs.Read(@"C:\shots\screenshot_261115_090807 (2).png"));
        }

        [Fact]
        public void AFolderThatVanishedAfterThePlanIsNeverCreatedInFolderMode()
        {
            var fs = new CommandFs();
            OutputPlan plan = Plan(fs, @"C:\shots\");
            fs.Folders.Remove(@"C:\shots");

            ScreenshotSaver.SaveToResult result = Saver(fs).SaveTo(TinyPng(), Instant, plan);

            Assert.Equal(ScreenshotSaver.SaveToOutcome.FolderMissing, result.Outcome);
            Assert.Empty(fs.Created);
        }

        // ------------------------------------------------------------------ the existing save is unchanged

        [Fact]
        public void TheChordsSaveStillCreatesItsFolderAndFallsBack()
        {
            var fs = new CommandFs();
            var saver = new ScreenshotSaver(fs, () => @"C:\fresh\shots", () => @"C:\fresh\downloads", TimeSpan.FromSeconds(5));

            ScreenshotSaver.Result result = saver.Save(TinyPng(), Instant, @"C:\fresh\chosen");

            Assert.Equal(ScreenshotSaver.Outcome.Saved, result.Outcome);
            Assert.Contains(@"C:\fresh\chosen", fs.Created);
            Assert.Equal(@"C:\fresh\chosen\screenshot_261115_090807.png", result.FilePath);
        }
    }
}

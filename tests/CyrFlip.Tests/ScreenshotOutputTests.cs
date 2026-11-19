using System;
using System.Collections.Generic;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// <c>--out</c> of the two screenshot verbs (ticket S0110, APP-COMMANDS 0.2 rule 9 and item P): a path a program names
    /// is used exactly as given. Everything runs on a fake file system - no disk, no Win32 - because
    /// <see cref="OutputPlan.Resolve"/> is pure over the saver's own seam.
    /// </summary>
    public class ScreenshotOutputTests
    {
        private const string Work = @"C:\work";

        private sealed class Fs : IScreenshotFileSystem
        {
            public readonly HashSet<string> Folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Work, @"C:\x", @"C:\" };
            public readonly HashSet<string> Files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> Asked = new List<string>();

            public bool DirectoryExists(string path) { Asked.Add(path); return Folders.Contains(path); }
            public bool FileExists(string path) { Asked.Add(path); return Files.Contains(path) || Folders.Contains(path); }
            public void CreateDirectory(string path) => throw new InvalidOperationException("a command never creates a folder");
            public void WriteAllBytes(string path, byte[] bytes) => throw new InvalidOperationException("resolution writes nothing");
            public bool MoveNoReplace(string from, string to) => throw new InvalidOperationException("resolution writes nothing");
            public bool MoveReplace(string from, string to) => throw new InvalidOperationException("resolution writes nothing");
            public void DeleteFile(string path) => throw new InvalidOperationException("resolution writes nothing");
        }

        private static OutputResolution Resolve(string arg, Fs fs, bool overwrite = false, string workingDir = Work)
            => OutputPlan.Resolve(arg, workingDir, overwrite, fs);

        private static OutputPlan Plan(string arg, Fs fs, bool overwrite = false, string workingDir = Work)
        {
            OutputResolution r = Resolve(arg, fs, overwrite, workingDir);
            Assert.True(r.Plan != null, arg + " -> " + r.Problem);
            return r.Plan!;
        }

        private static void Refused(string arg, Fs fs, OutputProblem problem, string reason, int exit, string status)
        {
            OutputResolution r = Resolve(arg, fs);
            Assert.Null(r.Plan);
            Assert.Equal(problem, r.Problem);
            Assert.Equal(reason, r.Reason);
            Assert.Equal(exit, r.ExitCode);
            Assert.Equal(status, r.Status);
        }

        // ------------------------------------------------------------------ files

        [Fact]
        public void ARelativeFileNameIsTheCallersWorkingDirectorys()
        {
            OutputPlan plan = Plan("a.png", new Fs());
            Assert.Equal(OutputMode.File, plan.Mode);
            Assert.Equal(@"C:\work\a.png", plan.Path);
            Assert.False(plan.Overwrite);
        }

        [Fact]
        public void ARelativePathWalksFromThatDirectory()
        {
            Assert.Equal(@"C:\a.png", Plan(@"..\a.png", new Fs()).Path);
            Assert.Equal(@"C:\work\a.png", Plan(@".\a.png", new Fs()).Path);
        }

        [Fact]
        public void ARootRelativePathIsOnTheWorkingDirectorysDrive()
        {
            var fs = new Fs();
            fs.Folders.Add(@"D:\x");
            Assert.Equal(@"D:\x\a.png", Plan(@"\x\a.png", fs, workingDir: @"D:\work").Path);
        }

        [Theory]
        [InlineData(@"C:\x\a.png")]
        [InlineData("C:/x/a.png")]
        [InlineData(@"C:\x\a.PNG")]
        [InlineData(@"C:\x\a.Png")]
        public void AFullFileNameInAnExistingFolderIsUsedAsGiven(string arg)
        {
            OutputPlan plan = Plan(arg, new Fs());
            Assert.Equal(OutputMode.File, plan.Mode);
            Assert.Equal(arg.Replace('/', '\\'), plan.Path);
        }

        [Fact]
        public void ANameWithSpacesAndLettersOfAnyScriptIsKeptAsTyped()
        {
            Assert.Equal(@"C:\x\my shot (2) ё.png", Plan(@"C:\x\my shot (2) ё.png", new Fs()).Path);
        }

        [Fact]
        public void AFileAtADriveRootNeedsOnlyTheRoot()
        {
            Assert.Equal(@"C:\a.png", Plan(@"C:\a.png", new Fs()).Path);
        }

        [Fact]
        public void ANetworkFileResolvesLikeAnyOtherWhenItsFolderAnswers()
        {
            var fs = new Fs();
            fs.Folders.Add(@"\\srv\share");
            Assert.Equal(@"\\srv\share\a.png", Plan(@"\\srv\share\a.png", fs).Path);
        }

        [Fact]
        public void AnExistingFileIsRefusedWithoutOverwriteAndTheNameIsLeftAlone()
        {
            var fs = new Fs();
            fs.Files.Add(@"C:\x\a.png");
            Refused(@"C:\x\a.png", fs, OutputProblem.Exists, AppCommandReasons.Exists, 11, "refused");
        }

        [Fact]
        public void OverwriteLetsAnExistingFileBeReplaced()
        {
            var fs = new Fs();
            fs.Files.Add(@"C:\x\a.png");
            OutputPlan plan = Plan(@"C:\x\a.png", fs, overwrite: true);
            Assert.True(plan.Overwrite);
            Assert.Equal(@"C:\x\a.png", plan.Path);
        }

        [Fact]
        public void AFolderNamedLikeTheFileIsInTheWayWhateverOverwriteSays()
        {
            var fs = new Fs();
            fs.Folders.Add(@"C:\x\a.png");
            Assert.Equal(OutputProblem.Exists, Resolve(@"C:\x\a.png", fs, overwrite: true).Problem);
            Assert.Equal(OutputProblem.Exists, Resolve(@"C:\x\a.png", fs, overwrite: false).Problem);
        }

        [Fact]
        public void AFolderThatIsNotThereIsMissingAndNeverCreated()
        {
            // Fs.CreateDirectory throws: the resolver must not even try.
            Refused(@"C:\nowhere\a.png", new Fs(), OutputProblem.MissingFolder, AppCommandReasons.TargetMissing, 11, "failed");
        }

        // ------------------------------------------------------------------ folders

        [Theory]
        [InlineData(@"C:\x\", @"C:\x")]
        [InlineData("C:/x/", @"C:\x")]
        [InlineData(@"C:\", @"C:\")]
        public void APathEndingInASeparatorIsAFolderTheFileIsNamedIn(string arg, string folder)
        {
            OutputPlan plan = Plan(arg, new Fs());
            Assert.Equal(OutputMode.Folder, plan.Mode);
            Assert.Equal(folder, plan.Path);
        }

        [Fact]
        public void ARelativeFolderWithASeparatorIsTheWorkingDirectorys()
        {
            var fs = new Fs();
            fs.Folders.Add(@"C:\work\shots");
            OutputPlan plan = Plan(@"shots\", fs);
            Assert.Equal(OutputMode.Folder, plan.Mode);
            Assert.Equal(@"C:\work\shots", plan.Path);
        }

        [Fact]
        public void AnExistingFolderWithoutTheSeparatorIsAFolderToo()
        {
            OutputPlan plan = Plan(@"C:\x", new Fs());
            Assert.Equal(OutputMode.Folder, plan.Mode);
            Assert.Equal(@"C:\x", plan.Path);
        }

        [Fact]
        public void ADotIsTheWorkingDirectoryItself()
        {
            Assert.Equal(@"C:\work", Plan(".", new Fs()).Path);
        }

        [Fact]
        public void OverwriteHasNoEffectInFolderMode()
        {
            Assert.False(Plan(@"C:\x\", new Fs(), overwrite: true).Overwrite);
            Assert.False(Plan(@"C:\x", new Fs(), overwrite: true).Overwrite);
        }

        [Fact]
        public void AFolderWithASeparatorThatIsNotThereIsMissing()
        {
            Refused(@"C:\nowhere\", new Fs(), OutputProblem.MissingFolder, AppCommandReasons.TargetMissing, 11, "failed");
        }

        // ------------------------------------------------------------------ names that are not .png files

        [Theory]
        [InlineData(@"C:\x\a.jpg")]
        [InlineData(@"C:\x\a")]
        [InlineData(@"C:\x\a.png.txt")]
        [InlineData(@"C:\x\.png")]
        [InlineData(@"C:\nowhere")]
        [InlineData("a")]
        public void ANameThatIsNotAPngFileOrAFolderIsRefusedAsUsage(string arg)
        {
            Refused(arg, new Fs(), OutputProblem.NotPng, AppCommandReasons.BadArgument, 10, "refused");
        }

        // ------------------------------------------------------------------ paths Windows refuses

        [Theory]
        [InlineData("CON")]
        [InlineData("con.png")]
        [InlineData("nul.png")]
        [InlineData("AUX.png")]
        [InlineData("COM1.png")]
        [InlineData("LPT9.txt")]
        [InlineData(@"C:\x\COM3\a.png")]
        [InlineData("a .png")]
        [InlineData("a.png.")]
        [InlineData("a.png ")]
        [InlineData("a<b.png")]
        [InlineData("a>b.png")]
        [InlineData("a|b.png")]
        [InlineData("a*b.png")]
        [InlineData("a?b.png")]
        [InlineData("a\"b.png")]
        [InlineData("a\u0001b.png")]
        [InlineData("a:b.png")]
        [InlineData("C:a.png")]
        [InlineData("C:")]
        [InlineData(@"C:\x\a:b.png")]
        [InlineData(@"1:\a.png")]
        [InlineData(@"\\?\C:\a.png")]
        [InlineData(@"\\.\NUL")]
        [InlineData("//?/C:/a.png")]
        [InlineData("")]
        public void APathWindowsRefusesIsBadUsageAndNothingIsProbedForIt(string arg)
        {
            var fs = new Fs();
            Refused(arg, fs, OutputProblem.BadPath, AppCommandReasons.BadArgument, 10, "refused");
            Assert.Empty(fs.Asked);
        }

        [Fact]
        public void AnOverLongPathIsBadUsage()
        {
            var fs = new Fs();
            Refused(@"C:\x\" + new string('a', 33000) + ".png", fs, OutputProblem.BadPath, AppCommandReasons.BadArgument, 10, "refused");
            Assert.Empty(fs.Asked);
        }

        [Fact]
        public void APathThatWindowsCannotNormalizeIsBadUsageNotACrash()
        {
            // 300 characters in one segment is over the classic path bound: GetFullPath throws, the resolver answers.
            OutputResolution r = Resolve(@"C:\x\" + new string('a', 300) + ".png", new Fs());
            // Whether the runtime takes a long path or not, the answer is a plan or a problem - never an exception.
            Assert.Equal(r.Plan == null, r.Problem != OutputProblem.None);
            Assert.True(r.Problem == OutputProblem.BadPath || r.Problem == OutputProblem.None, r.Problem.ToString());
        }

        [Theory]
        [InlineData("")]
        [InlineData("relative")]
        [InlineData("C:work")]
        public void ARelativePathWithNoUsableWorkingDirectoryIsBadUsage(string workingDir)
        {
            Assert.Equal(OutputProblem.BadPath, Resolve("a.png", new Fs(), workingDir: workingDir).Problem);
        }

        [Fact]
        public void TheResolverNeverAsksForMoreThanTheFolderAndTheFile()
        {
            var fs = new Fs();
            Plan(@"C:\x\a.png", fs);
            Assert.All(fs.Asked, path => Assert.StartsWith(@"C:\x", path));
            Assert.True(fs.Asked.Count <= 3, string.Join(" | ", fs.Asked));
        }

        [Fact]
        public void ThePureHalfHoldsNoWin32NoRegistryAndNoConfiguration()
        {
            string source = System.IO.File.ReadAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "src", "CyrFlip", "ScreenshotOutput.cs")));
            Assert.DoesNotContain("DllImport", source);
            Assert.DoesNotContain("Registry", source);
            Assert.DoesNotContain("AppConfig", source);
        }
    }
}

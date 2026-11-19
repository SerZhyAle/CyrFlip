using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The open trace (ticket S0135, phase 0): off by default and free, one fixed-shape row per span when on, never a failure
    /// for the window it measures. No window is built here - the trace is a process-wide switch, so these facts share the
    /// collection of the timing test that also turns it on and are serialized with it.
    /// </summary>
    [Collection(SharedGdiCollection.Name)]
    public class OpenTraceTests : IDisposable
    {
        private readonly string _file = Path.Combine(Path.GetTempPath(), "CyrFlipOpenTrace", Guid.NewGuid().ToString("N") + ".csv");

        public OpenTraceTests()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            OpenTrace.Stop();   // an environment variable of the test run must not leak into a fact
        }

        public void Dispose()
        {
            OpenTrace.Stop();
            try { File.Delete(_file); } catch (IOException) { }
        }

        /// <summary>The rows this fact wrote: other windows built by other tests may append rows under their own names.</summary>
        private string[] RowsOf(string window)
            => (File.Exists(_file) ? File.ReadAllLines(_file) : new string[0]).Where(r => r.Contains("," + window + ",")).ToArray();

        [Fact]
        public void OffByDefaultCostsNothingAndWritesNothing()
        {
            Assert.False(OpenTrace.Enabled);
            Assert.Equal(0, OpenTrace.Begin());
            OpenTrace.End("TraceOff", "span", 0);
            OpenTrace.End("TraceOff", "span", 12345);   // a stamp from before the trace was switched off ends nothing
            Assert.False(File.Exists(_file));
        }

        [Fact]
        public void OneRowPerSpanWithFiveFixedFields()
        {
            OpenTrace.Record(_file);
            long start = OpenTrace.Begin();
            Assert.NotEqual(0, start);
            OpenTrace.End("TraceWindow", "page:Hotkeys", start);

            string row = Assert.Single(RowsOf("TraceWindow"));
            string[] fields = row.Split(',');
            Assert.Equal(5, fields.Length);
            Assert.True(DateTime.TryParse(fields[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _));
            Assert.True(int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int processId) && processId > 0);
            Assert.Equal("TraceWindow", fields[2]);
            Assert.Equal("page:Hotkeys", fields[3]);
            Assert.True(double.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double ms) && ms >= 0);
        }

        [Fact]
        public void TheMillisecondsAreWrittenWithTheInvariantCultureWhateverTheThreadSpeaks()
        {
            CultureInfo before = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");   // a decimal comma
                OpenTrace.Record(_file);
                OpenTrace.End("TraceCulture", "span", OpenTrace.Begin());
            }
            finally { CultureInfo.CurrentCulture = before; }

            string[] fields = Assert.Single(RowsOf("TraceCulture")).Split(',');
            Assert.Equal(5, fields.Length);   // a decimal comma would have made it six
            Assert.Matches(@"^\d+\.\d{3}$", fields[4]);
        }

        [Fact]
        public void ANameCanNeitherAddAColumnNorBreakARowNorCarryText()
        {
            Assert.Equal("a_b__c_d", OpenTrace.Clean("a,b\r\nc d"));
            Assert.Equal("SettingsForm", OpenTrace.Clean("SettingsForm"));
            Assert.Equal("page:Hotkeys", OpenTrace.Clean("page:Hotkeys"));
            Assert.Equal("first-reload", OpenTrace.Clean("first-reload"));
            Assert.Equal("____", OpenTrace.Clean("\"';,"));   // quotes, semicolons and commas are all replaced
        }

        [Fact]
        public void AFileThatCannotBeWrittenTurnsTheTraceOffInsteadOfThrowing()
        {
            OpenTrace.Record(Path.Combine(_file + ".missing-folder", "trace.csv"));   // the folder does not exist
            long start = OpenTrace.Begin();
            Assert.NotEqual(0, start);

            OpenTrace.End("TraceBroken", "span", start);   // must not throw

            Assert.False(OpenTrace.Enabled);
            Assert.Equal(0, OpenTrace.Begin());
        }
    }
}

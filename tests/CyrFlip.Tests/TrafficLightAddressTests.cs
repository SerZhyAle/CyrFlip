using System.Collections.Generic;
using System.Linq;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>Address validation per kind, the accepted-status-code text, and the persisted list (ticket S0069).</summary>
    public class TrafficLightAddressTests
    {
        private const string Pin = "SHA256:Ul2rC03A44auS8B7ibLjJaAApHooIx/4R+SSgDTXUtY";

        [Theory]
        [InlineData("ping", "8.8.8.8")]
        [InlineData("ping", "router")]
        [InlineData("ping", "nas.local")]
        [InlineData("ping", "fe80::1")]
        [InlineData("ping", "2001:db8::7")]
        [InlineData("ping", "ПК-БУХГАЛТЕРИЯ")]
        [InlineData("tcp", "nas:445")]
        [InlineData("tcp", "192.168.1.5:3389")]
        [InlineData("tcp", "[2001:db8::7]:22")]
        [InlineData("sftp", "nas.example.org:2022")]
        [InlineData("exchange", "exchange.example.org:22022")]
        [InlineData("http", "https://nas.local:5000/status")]
        [InlineData("http", "http://192.168.1.1")]
        [InlineData("folder", @"C:\Data")]
        [InlineData("folder", @"\\SZAMEDIA\COMMON")]
        [InlineData("folder", @"\\nas\share\sub dir\x")]
        [InlineData("drive", "E:")]
        public void ValidAddressesPass(string kind, string address)
        {
            Assert.Equal(AddressIssue.None, TrafficLightAddress.Validate(kind, address, ""));
        }

        [Theory]
        [InlineData("ping", "", "Empty")]
        [InlineData("ping", "999.1.1.1", "BadHost")]
        [InlineData("ping", "12345", "BadHost")]
        [InlineData("ping", "-bad.example", "BadHost")]
        [InlineData("ping", "a b", "BadHost")]
        [InlineData("ping", "fe80::1%eth0", "BadHost")]
        [InlineData("tcp", "nas", "BadPort")]
        [InlineData("tcp", "nas:0", "BadPort")]
        [InlineData("tcp", "nas:65536", "BadPort")]
        [InlineData("tcp", "nas:ab", "BadPort")]
        [InlineData("tcp", "2001:db8::7", "BadPort")]
        [InlineData("http", "nas.local", "BadUrl")]
        [InlineData("http", "ftp://nas.local", "BadUrl")]
        [InlineData("http", "https://user:pw@nas.local", "BadUrl")]
        [InlineData("folder", "Data", "BadPath")]
        [InlineData("folder", @"C:\Data\*.txt", "BadPath")]
        [InlineData("folder", @"\\?\C:\x", "BadPath")]
        [InlineData("folder", @"\\server", "BadPath")]
        [InlineData("drive", "E:\\", "BadDrive")]
        [InlineData("drive", "EE:", "BadDrive")]
        [InlineData("floppy", "A:", "UnknownKind")]
        public void BadAddressesAreRefusedWithAReason(string kind, string address, string issue)
        {
            Assert.Equal(issue, TrafficLightAddress.Validate(kind, address, "").ToString());
        }

        [Fact]
        public void AFingerprintBelongsToTheExchangeKindAndHasAShape()
        {
            Assert.Equal(AddressIssue.None, TrafficLightAddress.Validate("exchange", "x.example.org:22022", Pin));
            Assert.Equal(AddressIssue.BadFingerprint, TrafficLightAddress.Validate("exchange", "x.example.org:22022", "SHA256:short"));
            Assert.Equal(AddressIssue.BadFingerprint, TrafficLightAddress.Validate("exchange", "x.example.org:22022", Pin + "="));
            Assert.Equal(AddressIssue.BadFingerprint, TrafficLightAddress.Validate("sftp", "x.example.org:22", Pin));
        }

        [Theory]
        [InlineData("200-399", 200, true)]
        [InlineData("200-399", 399, true)]
        [InlineData("200-399", 400, false)]
        [InlineData("200-399,401", 401, true)]
        [InlineData("200-399, 401 ; 403", 403, true)]
        [InlineData("404", 404, true)]
        [InlineData("404", 200, false)]
        public void AcceptedStatusCodesAreRangesAndLists(string text, int status, bool accepted)
        {
            Assert.True(TrafficLightStatusCodes.TryParse(text, out TrafficLightStatusCodes? codes));
            Assert.Equal(accepted, codes!.Contains(status));
        }

        [Theory]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData("99")]
        [InlineData("600")]
        [InlineData("399-200")]
        [InlineData("200,,300")]
        [InlineData("200-")]
        public void BrokenStatusCodeTextIsRefused(string text)
        {
            Assert.False(TrafficLightStatusCodes.TryParse(text, out _));
        }

        [Fact]
        public void StatusCodesAreWrittenBackSortedAndMerged()
        {
            Assert.True(TrafficLightStatusCodes.TryParse("401,200-299,300-399,404", out TrafficLightStatusCodes? codes));
            Assert.Equal("200-399,401,404", codes!.ToString());
        }

        // ---- the persisted list ----

        [Fact]
        public void TheListRoundTripsThroughJsonWithItsOrdinals()
        {
            var targets = new List<TrafficLightTarget>
            {
                new TrafficLightTarget { Id = "a", Label = "Router", Kind = "ping", Address = "192.168.1.1", Ordinal = 1, Group = "Home" },
                new TrafficLightTarget { Id = "b", Label = "Site", Kind = "http", Address = "https://example.org", Ordinal = 0, IntervalSeconds = 3600, Notify = true, RecordHistory = true },
            };
            List<TrafficLightTarget> back = TrafficLightTargetList.Read(TrafficLightTargetList.Serialize(targets));

            Assert.Equal(2, back.Count);
            TrafficLightTarget site = back.Single(t => t.Id == "b");
            Assert.Equal(3600, site.IntervalSeconds);
            Assert.True(site.Notify);
            Assert.True(site.RecordHistory);
            Assert.Equal(new[] { "b", "a" }, TrafficLightTargetList.InOrder(back).Select(t => t.Id).ToArray());
        }

        [Fact]
        public void ARepairedListHasUniqueIdsAndNumbersInRange()
        {
            var rows = new List<TrafficLightTarget>
            {
                new TrafficLightTarget { Id = "x", IntervalSeconds = 1, TimeoutMs = 99999, FailuresBeforeDown = 0 },
                new TrafficLightTarget { Id = "x", IntervalSeconds = -5 },
                new TrafficLightTarget { Id = "", IntervalSeconds = 0 },
            };
            Assert.True(TrafficLightTargetList.Sanitize(rows));

            Assert.Equal(3, rows.Select(r => r.Id).Distinct().Count());
            Assert.Equal(TrafficLightTarget.MinIntervalSeconds, rows[0].IntervalSeconds);
            Assert.Equal(TrafficLightTarget.MaxTimeoutMs, rows[0].TimeoutMs);
            Assert.Equal(1, rows[0].FailuresBeforeDown);
            Assert.Equal(0, rows[1].IntervalSeconds);   // manual only is a legal value
            Assert.Equal(0, rows[2].IntervalSeconds);
        }

        [Fact]
        public void GroupsFollowTheirFirstMemberAndMovingChangesOnlyThePosition()
        {
            var targets = new List<TrafficLightTarget>
            {
                new TrafficLightTarget { Id = "1", Group = "Office", Ordinal = 0 },
                new TrafficLightTarget { Id = "2", Group = "Home", Ordinal = 1 },
                new TrafficLightTarget { Id = "3", Group = "Office", Ordinal = 2 },
                new TrafficLightTarget { Id = "4", Group = "", Ordinal = 3 },
            };
            var groups = TrafficLightTargetList.Grouped(targets);
            Assert.Equal(new[] { "Office", "Home", "" }, groups.Select(g => g.Key).ToArray());
            Assert.Equal(new[] { "1", "3" }, groups[0].Value.Select(t => t.Id).ToArray());

            Assert.True(TrafficLightTargetList.Move(targets, "4", -1));
            Assert.Equal(new[] { "1", "2", "4", "3" }, TrafficLightTargetList.InOrder(targets).Select(t => t.Id).ToArray());
            Assert.Equal("", targets.Single(t => t.Id == "4").Group); // the group did not change
            Assert.False(TrafficLightTargetList.Move(targets, "1", -1)); // already first
        }

        [Fact]
        public void AMalformedValueIsAnEmptyListAndNotAnException()
        {
            Assert.Empty(TrafficLightTargetList.Read("not json"));
            Assert.Empty(TrafficLightTargetList.Read(""));
            Assert.Empty(TrafficLightTargetList.Read(null));
        }

        [Fact]
        public void ADefaultTimeoutFollowsTheKindUntilTheUserSetsOne()
        {
            var ping = new TrafficLightTarget { Kind = "ping" };
            Assert.Equal(1500, ping.EffectiveTimeoutMs);
            ping.Kind = "http";
            Assert.Equal(5000, ping.EffectiveTimeoutMs);
            ping.TimeoutMs = 800;
            Assert.Equal(800, ping.EffectiveTimeoutMs);
            ping.TimeoutMs = 60000;
            Assert.Equal(10000, ping.EffectiveTimeoutMs);
        }
    }
}

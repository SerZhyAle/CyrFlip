using System.Linq;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The one-field smart add (ticket S0069 D24): every row of the specification's table, the owner's own
    /// examples first. The recognizer is pure - no test here, and no path through it, touches the network.
    /// </summary>
    public class TargetRecognizerTests
    {
        private static TargetCandidate One(string text)
        {
            RecognizedLine line = TargetRecognizer.RecognizeLine(text);
            Assert.True(line.IsRecognized, "'" + text + "' was " + line.Status);
            Assert.Single(line.Candidates);
            return line.Candidates[0];
        }

        // ---- the owner's examples ----

        [Theory]
        [InlineData(@"D:\", "D:")]
        [InlineData("D:", "D:")]
        [InlineData("d:/", "D:")]
        public void ADriveIsKeptAsTheLetter(string text, string address)
        {
            TargetCandidate c = One(text);
            Assert.Equal(TrafficLightKinds.Drive, c.Kind);
            Assert.Equal(address, c.Address);
            Assert.Equal(RecognizedAs.Drive, c.As);
        }

        [Theory]
        [InlineData(@"\\SZAMEDIA", "SZAMEDIA")]
        [InlineData("//mycomp", "mycomp")]
        public void AMachineIsAddedAsPingAndFlaggedForTheSmbFallback(string text, string host)
        {
            TargetCandidate c = One(text);
            Assert.Equal(TrafficLightKinds.Ping, c.Kind);
            Assert.Equal(host, c.Address);
            Assert.True(c.IsMachineName);
            Assert.Equal(RecognizedAs.Machine, c.As);
        }

        [Theory]
        [InlineData(@"\\SZAMEDIA\COMMON", @"\\SZAMEDIA\COMMON", "COMMON")]
        [InlineData("//nas/share/sub", @"\\nas\share\sub", "sub")]
        public void ANetworkFolderIsKeptAsTheUncPath(string text, string address, string label)
        {
            TargetCandidate c = One(text);
            Assert.Equal(TrafficLightKinds.Folder, c.Kind);
            Assert.Equal(address, c.Address);
            Assert.Equal(label, c.Label);
            Assert.False(c.IsMachineName);
        }

        [Theory]
        [InlineData("192,168,1,100", "192.168.1.100", true)]
        [InlineData("192.168.1.100", "192.168.1.100", false)]
        [InlineData("8.8.8.8", "8.8.8.8", false)]
        [InlineData("10,0,0,1", "10.0.0.1", true)]
        public void AnIpAddressIsPingWithTheDotsRestored(string text, string address, bool commaRead)
        {
            RecognizedLine line = TargetRecognizer.RecognizeLine(text);
            TargetCandidate c = line.Candidates.Single();
            Assert.Equal(TrafficLightKinds.Ping, c.Kind);
            Assert.Equal(address, c.Address);
            Assert.Equal(commaRead ? address : "", line.CommaReadAs);
        }

        [Theory]
        [InlineData("http://www.google.com", "http://www.google.com", "google.com")]
        [InlineData("https://nas.local:5000/", "https://nas.local:5000/", "nas.local")]
        [InlineData("www.google.com", "https://www.google.com", "google.com")]
        [InlineData("google.com", "https://google.com", "google.com")]
        public void AWebsiteIsHttpWithAScheme(string text, string address, string label)
        {
            TargetCandidate c = One(text);
            Assert.Equal(TrafficLightKinds.Http, c.Kind);
            Assert.Equal(address, c.Address);
            Assert.Equal(label, c.Label);
            Assert.Equal(RecognizedAs.Website, c.As);
        }

        // ---- the rest of the table ----

        [Theory]
        [InlineData("SZAMEDIA")]
        [InlineData("router")]
        [InlineData("nas.local")]
        [InlineData("fe80::1")]
        public void ANameOrAnIpv6AddressIsPing(string text)
        {
            Assert.Equal(TrafficLightKinds.Ping, One(text).Kind);
        }

        [Theory]
        [InlineData("nas:445", "nas:445")]
        [InlineData("192.168.1.5:3389", "192.168.1.5:3389")]
        [InlineData("[::1]:22", "[::1]:22")]
        public void AHostAndAPortIsTcp(string text, string address)
        {
            TargetCandidate c = One(text);
            Assert.Equal(TrafficLightKinds.Tcp, c.Kind);
            Assert.Equal(address, c.Address);
        }

        [Fact]
        public void ALocalFolderIsKeptAsWrittenWithBackslashes()
        {
            TargetCandidate c = One(@"C:/Data/Reports/");
            Assert.Equal(TrafficLightKinds.Folder, c.Kind);
            Assert.Equal(@"C:\Data\Reports", c.Address);
            Assert.Equal("Reports", c.Label);
        }

        [Fact]
        public void AFileUrlIsAFolder()
        {
            TargetCandidate c = One("file:///C:/Data");
            Assert.Equal(TrafficLightKinds.Folder, c.Kind);
            Assert.Equal(@"C:\Data", c.Address);
        }

        [Fact]
        public void AnSftpLinkKeepsTheHostAndThePortAndNothingElse()
        {
            RecognizedLine line = TargetRecognizer.RecognizeLine("sftp://canaryuser:canarypw@nas.example.org:2022/secret/path");
            TargetCandidate c = line.Candidates.Single();
            Assert.Equal(TrafficLightKinds.Sftp, c.Kind);
            Assert.Equal("nas.example.org:2022", c.Address);
            Assert.True(line.SecretDropped);
            Assert.DoesNotContain("canary", c.Address + c.Label + c.Fingerprint);
            Assert.DoesNotContain("secret", c.Address + c.Label);
        }

        [Fact]
        public void AnSftpLinkWithNoPortUses22()
        {
            Assert.Equal("nas.local:22", One("sftp://nas.local/data").Address);
        }

        [Fact]
        public void AnHttpLinkWithALoginKeepsNoLogin()
        {
            RecognizedLine line = TargetRecognizer.RecognizeLine("https://canaryuser:canarypw@intranet.example.org/status");
            Assert.True(line.SecretDropped);
            Assert.DoesNotContain("canary", line.Candidates.Single().Address);
        }

        [Fact]
        public void AnExchangeStringGivesTheServerItsPinAndItsName()
        {
            RecognizedLine line = TargetRecognizer.RecognizeLine(
                "FMSEXCH1:x=exchange.example.org%3A22022&fp=SHA256%3AUl2rC03A44auS8B7ibLjJaAApHooIx%2F4R%2BSSgDTXUtY&n=Home+NAS&u=alice");
            TargetCandidate c = line.Candidates.Single();
            Assert.Equal(TrafficLightKinds.Exchange, c.Kind);
            Assert.Equal("exchange.example.org:22022", c.Address);
            Assert.Equal("SHA256:Ul2rC03A44auS8B7ibLjJaAApHooIx/4R+SSgDTXUtY", c.Fingerprint);
            Assert.Equal("Home NAS", c.Label);
            Assert.True(line.SecretDropped);
        }

        [Fact]
        public void ABrokenContractStringIsRefusedWholeNotGuessed()
        {
            RecognizedLine line = TargetRecognizer.RecognizeLine("FMSEXCH1:x=example.org%3A0");
            Assert.Equal(LineStatus.Refused, line.Status);
            Assert.Equal("bad-x", line.Reason);
            Assert.Empty(line.Candidates);
        }

        [Fact]
        public void AnSftpCodeGivesOneSftpPerHostAndOneExchange()
        {
            RecognizedLine line = TargetRecognizer.RecognizeLine(
                "FMSSFTP2:h=192.168.1.23,100.64.0.7&p=2022&u=fms&pw=k7PmQ2wXr9TzS4vGnHb3JdLe&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk&x=relay.example.net%3A44022&sid=q3Vb7YtK0xP2mN9sLfR4wA&drv=1");
            Assert.Equal(new[] { "192.168.1.23:2022", "100.64.0.7:2022", "relay.example.net:44022" },
                line.Candidates.Select(c => c.Address).ToArray());
            Assert.Equal(new[] { "sftp", "sftp", "exchange" }, line.Candidates.Select(c => c.Kind).ToArray());
            Assert.All(line.Candidates, c => Assert.Equal("", c.Fingerprint));
        }

        // ---- what is not recognized ----

        [Theory]
        [InlineData("hello world")]
        [InlineData("12345")]
        [InlineData("999.1.1.1")]
        [InlineData("mailto:a@b.c")]
        [InlineData("ftp://host/x")]
        [InlineData(@"\\?\C:\x")]
        public void AnythingElseIsNotRecognized(string text)
        {
            RecognizedLine line = TargetRecognizer.RecognizeLine(text);
            Assert.Equal(LineStatus.NotRecognized, line.Status);
            Assert.Empty(line.Candidates);
        }

        [Fact]
        public void AFmscfgDocumentIsRefusedUnreadAndKeepsNothing()
        {
            const string document = "{\n  \"schemaVersion\": 2,\n  \"resourceName\": \"Phone\",\n  \"password\": \"canarypassword\"\n}";
            RecognitionResult result = TargetRecognizer.Recognize(document);
            RecognizedLine line = Assert.Single(result.Lines);
            Assert.Equal(LineStatus.FmsCfgFile, line.Status);
            Assert.DoesNotContain("canary", line.Text);
            Assert.Empty(line.Candidates);
        }

        // ---- what outlives the parse (D21) ----

        [Theory]
        [InlineData("sftp://canaryuser:canarypassword@host.example:2022/path")]
        [InlineData("https://canaryuser:canarypassword@nas.example/")]
        [InlineData("FMSSFTP2:h=192.168.1.23&p=2022&u=canaryuser&pw=canarypassword&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk&x=relay.example.net%3A44022&sid=q3Vb7YtK0xP2mN9sLfR4wA")]
        [InlineData("FMSEXCH1:x=exchange.example.org%3A22022&u=canary.login&n=Office")]
        public void ARecognizedLineKeepsNoRawTextAndNoSecret(string text)
        {
            RecognizedLine line = TargetRecognizer.RecognizeLine(text);
            Assert.True(line.IsRecognized);
            Assert.True(line.SecretDropped);
            Assert.Equal("", line.Text);
            foreach (TargetCandidate c in line.Candidates)
                Assert.DoesNotContain("canary", c.Kind + "|" + c.Address + "|" + c.Fingerprint + "|" + c.Label);
        }

        [Theory]
        [InlineData("FMSSFTP3:h=host&p=22&u=canaryuser&pw=canarypassword")]
        [InlineData("FMSEXCH9:x=host%3A1&u=canary.login")]
        [InlineData("fmscfg9:{\"password\":\"canarypassword\"}")]
        public void AContractStringOfAnUnknownVersionIsNeitherRecognizedNorEchoed(string text)
        {
            RecognizedLine line = TargetRecognizer.RecognizeLine(text);
            Assert.Contains(line.Status, new[] { LineStatus.NotRecognized, LineStatus.Refused });
            Assert.Empty(line.Candidates);
            Assert.Equal("", line.Text);
        }

        [Fact]
        public void AnUnrecognizedTextOfAnyOtherShapeIsHandedToTheDetailsPanelAsTyped()
        {
            RecognizedLine line = TargetRecognizer.RecognizeLine("hello world");
            Assert.Equal(LineStatus.NotRecognized, line.Status);
            Assert.Equal("hello world", line.Text);
        }

        // ---- the paste ----

        [Fact]
        public void EachNonEmptyLineIsItsOwnCandidate()
        {
            RecognitionResult result = TargetRecognizer.Recognize("google.com\r\n\r\n  8.8.8.8  \n\"D:\\\"\n");
            Assert.Equal(new[] { "https://google.com", "8.8.8.8", "D:" },
                result.Lines.Select(l => l.Candidates.Single().Address).ToArray());
            Assert.False(result.Truncated);
        }

        [Fact]
        public void AFiftyOneLinePasteIsCutAtFifty()
        {
            string text = string.Join("\n", Enumerable.Range(1, 51).Select(i => "host" + i));
            RecognitionResult result = TargetRecognizer.Recognize(text);
            Assert.Equal(50, result.Lines.Count);
            Assert.True(result.Truncated);
            Assert.Equal(51, result.TotalLines);
        }

        [Fact]
        public void QuotesAndByteOrderMarkAreStripped()
        {
            Assert.Equal("mycomp", One("\uFEFF\"//mycomp\"").Address);
        }
    }
}

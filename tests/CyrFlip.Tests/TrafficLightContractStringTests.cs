using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Ticket S0069 D21 / section 9 item 3: CyrFlip reads the <c>FMSEXCH1</c> connection string and the
    /// <c>FMSSFTP1/2</c> descriptor by the catalog's own conformance vectors (byte-exact copies under
    /// <c>Fixtures/</c>). The reading half only - a string that breaks a rule is refused whole, and no secret
    /// survives the parse.
    /// </summary>
    public class TrafficLightContractStringTests
    {
        private static readonly string Fixtures = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Fixtures"));

        private static Dictionary<string, object> Load(params string[] path)
        {
            string file = Path.Combine(Fixtures, Path.Combine(path));
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                .Deserialize<Dictionary<string, object>>(File.ReadAllText(file, Encoding.UTF8));
        }

        private static string InputOf(Dictionary<string, object> testCase)
        {
            if (testCase.TryGetValue("inputHex", out object? hex) && hex is string h)
            {
                var bytes = new byte[h.Length / 2];
                for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(h.Substring(i * 2, 2), 16);
                return new UTF8Encoding(false, false).GetString(bytes);
            }
            return testCase.TryGetValue("input", out object? input) ? (string)input : "";
        }

        // The catalog is not on a CI runner, so the contract's own bytes are pinned here as literals (the way the
        // SCENARIO-FILE vectors are): a re-issue of a vector file updates its literal in the same change.
        [Theory]
        [InlineData("DeviceExchange", "connection-string-v1.json", "4e75c0da11515f4b4e77a46e24751b55f874d699a0362de26cd591df79a8d26d")]
        [InlineData("AnywhereAccess", "descriptor-v2.json", "04354e86fa5700c1fafd9fac7842949ea9a8824cb4b09587d9c849c29e9d89b6")]
        public void TheVectorFilesAreByteIdenticalToTheCatalogsCopies(string folder, string name, string sha256)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(Fixtures, folder, name));
            using var hash = System.Security.Cryptography.SHA256.Create();
            string actual = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            Assert.Equal(sha256, actual);
        }

        [Fact]
        public void EveryTextVectorOfTheConnectionStringIsReadAsTheContractSays()
        {
            Dictionary<string, object> file = Load("DeviceExchange", "connection-string-v1.json");
            var cases = (ArrayList)file["cases"];
            int run = 0;
            var failures = new List<string>();
            foreach (Dictionary<string, object> testCase in cases)
            {
                if ((string)testCase["kind"] != "text") continue; // files are not what a paste is
                run++;
                string name = (string)testCase["name"];
                string verdict = (string)testCase["verdict"];
                ExchangeConnectionParse parsed = ExchangeConnectionString.Parse(InputOf(testCase));

                if (verdict == "refused")
                {
                    string reason = (string)testCase["reason"];
                    if (parsed.Ok) failures.Add(name + ": accepted, the contract refuses it (" + reason + ")");
                    else if (parsed.Reason != reason) failures.Add(name + ": refused as " + parsed.Reason + ", the contract says " + reason);
                    else if (testCase.TryGetValue("looks", out object? looks) && parsed.Looks != (string)looks)
                        failures.Add(name + ": looks " + parsed.Looks + ", the contract says " + looks);
                    continue;
                }

                if (!parsed.Ok) { failures.Add(name + ": refused as " + parsed.Reason); continue; }
                var fields = (Dictionary<string, object>)testCase["fields"];
                if (parsed.Host != (string)fields["host"]) failures.Add(name + ": host " + parsed.Host);
                if (parsed.Port != (int)fields["port"]) failures.Add(name + ": port " + parsed.Port);
                string fp = fields.TryGetValue("fp", out object? pin) ? (string)pin : "";
                if (parsed.Fingerprint != fp) failures.Add(name + ": fp " + parsed.Fingerprint);
                string n = fields.TryGetValue("n", out object? label) ? (string)label : "";
                if (parsed.Name != n) failures.Add(name + ": n " + parsed.Name);
                if (parsed.LoginDropped != fields.ContainsKey("u")) failures.Add(name + ": u dropped " + parsed.LoginDropped);
            }

            Assert.True(run > 60, "the vector file was read (" + run + " text cases)");
            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        [Fact]
        public void NoSecretIsKeptFromAConnectionString()
        {
            ExchangeConnectionParse parsed = ExchangeConnectionString.Parse(
                "FMSEXCH1:x=exchange.example.org%3A22022&u=canary.login&n=Office");
            Assert.True(parsed.Ok);
            Assert.True(parsed.LoginDropped);
            foreach (string value in new[] { parsed.Host, parsed.Fingerprint, parsed.Name, parsed.Reason })
                Assert.DoesNotContain("canary", value);
        }

        [Fact]
        public void EveryDescriptorVectorIsReadAsTheContractSays()
        {
            Dictionary<string, object> file = Load("AnywhereAccess", "descriptor-v2.json");
            var failures = new List<string>();

            foreach (Dictionary<string, object> valid in (ArrayList)file["valid"])
            {
                string name = (string)valid["name"];
                SftpDescriptorParse parsed = SftpDescriptor.Parse((string)valid["code"]);
                if (!parsed.Ok) { failures.Add(name + ": refused as " + parsed.Reason); continue; }
                var hosts = new List<string>();
                foreach (object host in (ArrayList)valid["hosts"]) hosts.Add((string)host);
                if (string.Join(",", parsed.Hosts) != string.Join(",", hosts)) failures.Add(name + ": hosts " + string.Join(",", parsed.Hosts));
                if (parsed.Port != (int)valid["port"]) failures.Add(name + ": port " + parsed.Port);
                string exchange = valid["exchange"] == null ? "" : (string)valid["exchange"];
                string actual = parsed.ExchangePort > 0 ? TrafficLightAddress.JoinHostPort(parsed.ExchangeHost, parsed.ExchangePort) : "";
                if (actual != exchange) failures.Add(name + ": exchange " + actual);
            }

            foreach (Dictionary<string, object> refused in (ArrayList)file["refused"])
            {
                SftpDescriptorParse parsed = SftpDescriptor.Parse((string)refused["code"]);
                if (parsed.Ok) failures.Add("accepted though refused: " + (string)refused["reason"]);
                else Assert.Empty(parsed.Hosts); // refused whole - nothing partly read
            }

            foreach (Dictionary<string, object> v1 in (ArrayList)file["v1IgnoresV2Keys"])
            {
                SftpDescriptorParse parsed = SftpDescriptor.Parse((string)v1["code"]);
                if (!parsed.Ok) failures.Add("v1 code with v2 keys refused: " + parsed.Reason);
                else if (parsed.ExchangePort != 0 || parsed.ExchangeHost.Length != 0) failures.Add("v1 read a v2 key");
            }

            Assert.True(failures.Count == 0, string.Join("\n", failures));
        }

        [Fact]
        public void NoSecretIsKeptFromAnSftpCode()
        {
            const string code = "FMSSFTP2:h=192.168.1.23&p=2022&u=canaryuser&pw=canarypassword&fp=SHA256%3A8f6TQvCbXjDMOyu4A9JzKcWlEHmR5pNsGgVaU2wYqhk&x=relay.example.net%3A44022&sid=q3Vb7YtK0xP2mN9sLfR4wA";
            RecognizedLine line = TargetRecognizer.RecognizeLine(code);

            Assert.True(line.IsRecognized);
            Assert.True(line.SecretDropped);
            Assert.Equal(2, line.Candidates.Count); // one sftp per host, one exchange for x
            foreach (TargetCandidate candidate in line.Candidates)
            {
                string everything = candidate.Kind + "|" + candidate.Address + "|" + candidate.Fingerprint + "|" + candidate.Label;
                Assert.DoesNotContain("canary", everything);
                Assert.DoesNotContain("SHA256", everything);   // the share's host key is not the exchange server's pin
                Assert.DoesNotContain("q3Vb7", everything);
            }
        }
    }
}

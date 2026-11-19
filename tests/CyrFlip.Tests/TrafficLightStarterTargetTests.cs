using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CyrFlip;
using Microsoft.Win32;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// A brand-new profile starts the traffic light's table with one working example, www.google.com, so the page
    /// is not blank the first time the module is switched on. It is a first-run seed only: the list is written on
    /// every save, so on an existing profile an empty table cannot be told from one the user emptied, and an
    /// upgrade must never get a row it did not have. No test here touches the network.
    /// </summary>
    public class TrafficLightStarterTargetTests
    {
        private sealed class Key : IConfigKey
        {
            public readonly Dictionary<string, object> Values = new Dictionary<string, object>();
            public object? GetValue(string name) => Values.TryGetValue(name, out var value) ? value : null;
            public void SetValue(string name, object value, RegistryValueKind kind) => Values[name] = value;
        }

        private static string NoLegacyFile() => Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");

        [Fact]
        public void AFreshProfileStartsWithOneGoogleTargetAndSavesIt()
        {
            AppConfig cfg = AppConfig.LoadFrom(null, out bool mustSave, NoLegacyFile());

            TrafficLightTarget target = Assert.Single(cfg.TrafficLightTargets);
            Assert.True(mustSave);                                     // the seeded row reaches the registry
            Assert.Equal(TrafficLightKinds.Http, target.Kind);
            Assert.Equal("https://www.google.com", target.Address);
            Assert.True(target.Enabled);
            Assert.Equal(0, target.Ordinal);
            Assert.False(cfg.EnableTrafficLight);                      // the module itself stays off: no socket, no timer
        }

        [Fact]
        public void AListTheUserEmptiedIsNeverRefilled()
        {
            var key = new Key();
            AppConfig cfg = AppConfig.LoadFrom(null, out _, NoLegacyFile());
            cfg.SaveTo(key);

            AppConfig second = AppConfig.LoadFrom(key, out _);
            Assert.Equal("https://www.google.com", Assert.Single(second.TrafficLightTargets).Address);

            second.TrafficLightTargets.Clear();
            second.SaveTo(key);

            // An emptied list is the user's decision: it stays empty and is never refilled.
            Assert.Empty(AppConfig.LoadFrom(key, out _).TrafficLightTargets);
        }

        [Fact]
        public void AnExistingProfileNeverGetsTheExample()
        {
            var withoutTheValue = new Key();                           // written before the traffic light existed
            AppConfig older = AppConfig.LoadFrom(withoutTheValue, out _);
            Assert.Empty(older.TrafficLightTargets);

            var withEmptyList = new Key();                             // an update of a build that wrote "[]"
            withEmptyList.Values["TrafficLightTargets"] = "[]";
            Assert.Empty(AppConfig.LoadFrom(withEmptyList, out _).TrafficLightTargets);
        }

        [Fact]
        public void AnExistingProfileKeepsItsOwnTargetsUntouched()
        {
            var key = new Key();
            key.Values["TrafficLightTargets"] =
                "[{\"Id\":\"a\",\"Kind\":\"ping\",\"Address\":\"1.1.1.1\",\"Label\":\"Router\"}]";

            TrafficLightTarget only = Assert.Single(AppConfig.LoadFrom(key, out _).TrafficLightTargets);
            Assert.Equal("1.1.1.1", only.Address);
        }

        [Fact]
        public void TheExampleIsWhatTypingTheAddressIntoTheAddFieldWouldAdd()
        {
            TrafficLightTarget starter = TrafficLightTargetList.StarterTarget();

            RecognizedLine line = TargetRecognizer.RecognizeLine("www.google.com");
            Assert.True(line.IsRecognized);
            TargetCandidate typed = Assert.Single(line.Candidates);

            Assert.Equal(typed.Kind, starter.Kind);
            Assert.Equal(typed.Address, starter.Address);
            // Everything else is the kind's own defaults, i.e. exactly what Enter adds.
            TrafficLightTarget byEnter = TrafficLightTarget.Create(typed.Kind, typed.Address, starter.Label, 0);
            Assert.Equal(byEnter.IntervalSeconds, starter.IntervalSeconds);
            Assert.Equal(byEnter.FailuresBeforeDown, starter.FailuresBeforeDown);
            Assert.Equal(byEnter.AcceptedStatusCodes, starter.AcceptedStatusCodes);
            Assert.Equal(byEnter.Notify, starter.Notify);
            Assert.Equal(byEnter.RecordHistory, starter.RecordHistory);
        }

        [Fact]
        public void TheExampleIsATargetTheCommandLineMayNameAndALabelThatIsNotAnAddress()
        {
            TrafficLightTarget starter = TrafficLightTargetList.StarterTarget();

            // "google.com" would read as an address and make `trafficlight status` refuse the whole board.
            Assert.True(TrafficLightLabels.IsSafe(starter));
            Assert.False(string.IsNullOrWhiteSpace(starter.Label));
            Assert.True(starter.Label.Length <= TrafficLightTargetList.MaxLabelLength);
            Assert.True(TrafficLightAddress.IsValidHttpUrl(starter.Address));

            var rows = new List<TrafficLightTarget> { starter };
            Assert.False(TrafficLightTargetList.Sanitize(rows));      // already in the form the list repairs towards
            Assert.Equal(1, rows.Count(r => r.Id == starter.Id));
        }
    }
}

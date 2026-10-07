using System;
using System.Collections.Generic;
using System.IO;
using CyrFlip;
using Microsoft.Win32;
using Xunit;

namespace CyrFlip.Tests
{
    public class FirstRunTests
    {
        private sealed class Key : IConfigKey
        {
            public readonly Dictionary<string, object> Values = new Dictionary<string, object>();
            public object? GetValue(string name) => Values.TryGetValue(name, out var value) ? value : null;
            public void SetValue(string name, object value, RegistryValueKind kind) => Values[name] = value;
        }

        [Theory]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, true)]
        public void MarkerIncludesExplicitReplayOverride(bool first, bool shown, bool expected)
            => Assert.Equal(expected, FirstRun.ShouldShow(first, shown));

        [Fact]
        public void NewProfileAndMarkerRoundTrip()
        {
            var cfg = AppConfig.LoadFrom(null, out bool save, Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
            Assert.True(save); Assert.True(cfg.IsFirstRun); Assert.False(cfg.WelcomeShown);
            var key = new Key(); cfg.WelcomeShown = true; cfg.SaveTo(key);
            var again = AppConfig.LoadFrom(key, out _);
            Assert.True(again.WelcomeShown); Assert.False(again.IsFirstRun);
            Assert.False(FirstRun.ShouldShow(again.IsFirstRun, again.WelcomeShown));
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"hotkey\":\"Ctrl+Shift+F12\"}")]
        [InlineData("damaged legacy JSON")]
        public void LegacyProfileNeverShowsTour(string contents)
        {
            string file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            try
            {
                File.WriteAllText(file, contents);
                var cfg = AppConfig.LoadFrom(null, out _, file);
                Assert.False(cfg.IsFirstRun); Assert.True(cfg.WelcomeShown);
            }
            finally { File.Delete(file); }
        }

        [Fact]
        public void ExistingMissingCorruptAndZeroMarkers()
        {
            var key = new Key();
            Assert.True(AppConfig.LoadFrom(key, out _).WelcomeShown);
            key.Values["WelcomeShown"] = new byte[] { 0 };
            var corrupt = AppConfig.LoadFrom(key, out _);
            Assert.True(corrupt.WelcomeShown); Assert.Contains("WelcomeShown", corrupt.UnreadableValues);
            key.Values["WelcomeShown"] = "0";
            var wrongKind = AppConfig.LoadFrom(key, out _);
            Assert.True(wrongKind.WelcomeShown); Assert.Contains("WelcomeShown", wrongKind.UnreadableValues);
            key.Values["WelcomeShown"] = 0;
            var replay = AppConfig.LoadFrom(key, out _);
            Assert.False(replay.IsFirstRun); Assert.False(replay.WelcomeShown);
            Assert.True(FirstRun.ShouldShow(replay.IsFirstRun, replay.WelcomeShown));
        }
    }
}

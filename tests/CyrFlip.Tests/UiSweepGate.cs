using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

// The 13-language UI sweeps (the [UiSweepFact] methods) rebuild real windows in every UI
// language, and GDI serializes them - the dominant cost of a test run (measured 2026-10-15:
// ~0.7-2.8 s per SettingsForm build, 286 s for one hotkeys-page sweep, ~30 min for the lot).
// They also put real windows on the user's screen, one flash per language per window. The
// strings and the settings window are edited all day, so any rule of the form "run again when
// those sources changed", and equally "run in Release", re-runs them on nearly every test run
// (both were tried: 2026-10-15 the first, then the build configuration - a Release run swept,
// so build.ps1 -Tests and every hand-run Release `dotnet test` walked every window in every
// language, S0072 included). The gate therefore asks for one thing and keeps no state:
//
//   - a sweep runs only when CYRFLIP_SWEEP=full is set, in ANY configuration. release.ps1's
//     preflight, release.yml and ci.yml set it on their `dotnet test` step, so the full walk is
//     the release's own validation path and nothing else starts it;
//   - everything else - Debug or Release, `dotnet test`, build.ps1 -Tests - skips every sweep;
//   - a change to one window sweeps that window only: `CYRFLIP_SWEEP=full dotnet test
//     --filter SymbolsWindowTests`. Never the whole interface for one window's change.
//
// Mark a new 13-language window loop with [UiSweepFact] instead of [Fact] - that is the whole
// registration. Cheap pure-string loops over Localization.Names stay [Fact] on purpose:
// they are milliseconds, show no window and keep their always-on signal.

namespace CyrFlip.Tests
{
    /// <summary>Marks a test method that builds real windows in every UI language: gateable.</summary>
    [XunitTestCaseDiscoverer("CyrFlip.Tests.UiSweepFactDiscoverer", "CyrFlip.Tests")]
    public sealed class UiSweepFactAttribute : FactAttribute
    {
    }

    internal sealed class UiSweepFactDiscoverer : FactDiscoverer
    {
        public UiSweepFactDiscoverer(IMessageSink diagnosticMessageSink) : base(diagnosticMessageSink) { }

        protected override IXunitTestCase CreateTestCase(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo factAttribute)
            => new UiSweepTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod);
    }

    /// <summary>A stock fact case that reports skipped when the gate says this configuration does not sweep.</summary>
    internal sealed class UiSweepTestCase : XunitTestCase
    {
        [Obsolete("Called by the deserializer", true)]
        public UiSweepTestCase() { }

        public UiSweepTestCase(IMessageSink diagnosticMessageSink, TestMethodDisplay defaultMethodDisplay, TestMethodDisplayOptions defaultMethodDisplayOptions, ITestMethod testMethod)
            : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod) { }

        public override async Task<RunSummary> RunAsync(IMessageSink diagnosticMessageSink, IMessageBus messageBus, object[] constructorArguments, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource)
        {
            if (UiSweepGate.ShouldSkip(out var reason))
            {
                var test = new XunitTest(this, DisplayName);
                messageBus.QueueMessage(new TestSkipped(test, reason));
                messageBus.QueueMessage(new TestFinished(test, 0m, string.Empty));
                return new RunSummary { Total = 1, Skipped = 1 };
            }
            return await base.RunAsync(diagnosticMessageSink, messageBus, constructorArguments, aggregator, cancellationTokenSource);
        }
    }

    internal static class UiSweepGate
    {
        public const string EnvVar = "CYRFLIP_SWEEP";

        /// <summary>The one per-case decision: true means report this sweep as skipped.</summary>
        public static bool ShouldSkip(out string reason)
        {
            var mode = Environment.GetEnvironmentVariable(EnvVar)?.Trim().ToLowerInvariant();
            if (mode == "full")
            {
                reason = "";
                return false;
            }
            reason = "UI language sweep skipped: only a release run sweeps (release.ps1, release.yml, ci.yml set "
                + EnvVar + "=full). To sweep one window: " + EnvVar + "=full dotnet test --filter <ThatWindowTests>.";
            return true;
        }
    }
}

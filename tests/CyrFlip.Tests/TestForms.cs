using System;
using System.IO;
using System.Windows.Forms;
using CyrFlip;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The one place the tests build the real settings window (ticket S0029 RB-1). It calls the
    /// constructor directly rather than through reflection: <c>Activator.CreateInstance</c> binds no
    /// optional parameters, so adding one to <see cref="SettingsForm"/> turned six guards into
    /// MissingMethodExceptions that each reported as a different, misleading failure. Compiled, the next
    /// signature change breaks the build instead of the run.
    /// </summary>
    internal static class TestForms
    {
        /// <param name="scenarioRoot">Folder for the launcher store; null = a fresh per-call temp folder
        /// (created only if something writes, which nothing here does).</param>
        /// <param name="withExchange">Build the exchange-file rows (ticket S0023), so a walk sees their captions.</param>
        public static SettingsForm NewSettings(AppConfig config, string? scenarioRoot = null, bool withExchange = true)
        {
            Action<bool> b = _ => { };
            Action noop = () => { };
            var store = new LauncherScenarioStore(scenarioRoot
                ?? Path.Combine(Path.GetTempPath(), "CyrFlipTests", Guid.NewGuid().ToString("N")));
            Action<IWin32Window, bool>? exchange = withExchange ? (_, _) => { } : null;
            return new SettingsForm(config, b, b, b, b, b, b, b, b, b, _ => { }, _ => { }, noop, noop, noop, noop, noop,
                b, b, b, b, b, b, store, b, noop, noop, noop, (_, _) => "", exchange);
        }
    }
}

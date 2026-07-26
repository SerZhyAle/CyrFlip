using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The conversion table absorbed the built-in EN ⇄ RU flip, so every config written before that
    /// has to arrive in the new world with its chord intact - and a user who emptied the table on
    /// purpose must not find it refilled on the next launch. The seeding decision is pure (it reads
    /// the raw registry values), so it is testable without touching HKCU.
    /// </summary>
    public class ConversionTableSeedTests
    {
        [Fact]
        public void AFreshInstallGetsTheEnRuRowOnTheDefaultChord()
        {
            Assert.True(AppConfig.NeedsFlipRow(null, legacyValuesPresent: false, rowCount: 0));

            LayoutConversionProfile row = AppConfig.FlipRow(null, true);
            Assert.Equal("00000409", row.SourceKlid);
            Assert.Equal("00000419", row.TargetKlid);
            Assert.Equal(AppConfig.DefaultFlipHotkey, row.Hotkey);
            Assert.True(row.Enabled);
            Assert.True(row.IsUsable);
        }

        [Fact]
        public void AnUpgradeCarriesOverTheChordAndSwitchTheFlipUsedToHave()
        {
            LayoutConversionProfile row = AppConfig.FlipRow("Ctrl+Alt+F9", false);

            Assert.Equal("Ctrl+Alt+F9", row.Hotkey);
            Assert.False(row.Enabled); // the flip hotkey was switched off - it stays off
        }

        [Fact]
        public void AConfigWrittenBeforeTheMergeIsSeededEvenThoughItsTableExists()
        {
            // The build that had the table but not yet the merge left "[]" behind while the flip kept
            // its own registry values - that empty table has never been offered the flip row.
            Assert.True(AppConfig.NeedsFlipRow("[]", legacyValuesPresent: true, rowCount: 0));
        }

        [Fact]
        public void ATableEmptiedByTheUserIsNotRefilled()
        {
            // Seeding deletes the legacy values, so their absence is what says "already migrated".
            Assert.False(AppConfig.NeedsFlipRow("[]", legacyValuesPresent: false, rowCount: 0));
        }

        [Fact]
        public void ATableWithRowsIsNeverTouched()
        {
            Assert.False(AppConfig.NeedsFlipRow("[{...}]", legacyValuesPresent: true, rowCount: 1));
            Assert.False(AppConfig.NeedsFlipRow("[{...}]", legacyValuesPresent: false, rowCount: 3));
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>The rolling backup's own behaviour (S0072 section 4, gate G5).</summary>
    public class SettingsBackupTests
    {
        private static (SettingsBackup Backup, string Folder, AppConfig Config) Make(bool enabled = true, bool settled = true)
        {
            string folder = Path.Combine(Path.GetTempPath(), "S0072-backup-" + Guid.NewGuid().ToString("N"));
            var config = new AppConfig { SettingsBackupEnabled = enabled };
            var backup = new SettingsBackup(config, () => Array.Empty<string>())
            {
                FolderOverride = () => folder,
                QuestionSettled = settled,
                IdleMilliseconds = 60_000,
            };
            return (backup, folder, config);
        }

        private static readonly string[] ScenarioXml =
        {
            LauncherScenarioStore.ToXml(new LauncherScenario { Id = Guid.Parse("7f6e5d4c3b2a4948b7a69584756a4332"), Name = "Calculator", Path = "calc.exe" }),
        };

        [Fact]
        public void EachSaveRestartsTheActualQuietTimer()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                var (backup, folder, _) = Make();
                void Pump(int milliseconds)
                {
                    var time = System.Diagnostics.Stopwatch.StartNew();
                    while (time.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(2); }
                }
                try
                {
                    backup.IdleMilliseconds = 300;
                    backup.Start();
                    backup.NotifyChanged(); Pump(180);
                    backup.NotifyChanged(); Pump(180);
                    Assert.False(Directory.Exists(folder));
                    Pump(220);
                    Assert.Single(Directory.GetFiles(folder));
                }
                catch (Exception ex) { failure = ex; }
                finally { backup.Stop(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(10000));
            if (failure != null) throw failure;
        }

        [Fact]
        public void ManySavesInOneQuietWindowWriteExactlyOnce()
        {
            var (backup, folder, _) = Make();
            try
            {
                backup.Start();
                for (int i = 0; i < 5; i++) { backup.SavedForTest(); Thread.Sleep(2); }
                backup.IdleElapsedForTest();
                Assert.Single(Directory.GetFiles(folder));
                // The quiet window restarts: the write happened, and a quiet minute after the last
                // save writes again - not one write per save.
                backup.SavedForTest();
                backup.IdleElapsedForTest();
                Assert.Single(Directory.GetFiles(folder)); // the same minute: the same file, replaced
            }
            finally { backup.Stop(); Directory.Delete(folder, true); }
        }

        [Fact]
        public void TheSwitchOffSilencesTheWriter()
        {
            var (backup, folder, _) = Make(enabled: false);
            try
            {
                backup.Start();
                backup.SavedForTest(); backup.IdleElapsedForTest();
                backup.Flush();
                Assert.False(Directory.Exists(folder));
            }
            finally { backup.Stop(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Fact]
        public void AnOpenRestoreQuestionSilencesTheWriter()
        {
            var (backup, folder, _) = Make(settled: false);
            try
            {
                backup.Start();
                backup.SavedForTest(); backup.IdleElapsedForTest(); backup.Flush();
                Assert.False(Directory.Exists(folder)); // the seeding save of a fresh profile writes no backup
                backup.QuestionSettled = true;
                backup.SavedForTest(); backup.IdleElapsedForTest();
                Assert.Single(Directory.GetFiles(folder));
            }
            finally { backup.Stop(); Directory.Delete(folder, true); }
        }

        [Fact]
        public void TheBackupIsTheV2WritersOutputWithTargetsAndScenarios()
        {
            var (backup, folder, config) = Make();
            config.TrafficLightTargets.Add(new TrafficLightTarget { Id = "t1", Kind = "ping", Address = "nas.local" });
            try
            {
                var scenarios = new Func<IReadOnlyList<string>>(() => ScenarioXml);
                var direct = new SettingsBackup(config, scenarios) { FolderOverride = () => folder, QuestionSettled = true };
                direct.Start();
                direct.SavedForTest(); direct.IdleElapsedForTest();

                string file = Directory.GetFiles(folder).Single();
                string text = File.ReadAllText(file, Encoding.UTF8);
                Assert.Contains("nas.local", text);           // targets always (D3)
                Assert.Contains("Calculator", text);          // scenarios included (D2)
                var doc = SettingsFileReader.Read(Encoding.UTF8.GetBytes(text));
                Assert.Equal(SettingsFileInventory.Exported.Length, doc.Values.Count);
                // Byte-identical to what the transfer writer produces over the same inputs.
                string again = SettingsFileWriter.Write(config, DateTime.Parse(doc.ExportedAtUtc, System.Globalization.CultureInfo.InvariantCulture),
                    doc.App, ScenarioXml);
                Assert.Equal(again, text);
                direct.Stop();
            }
            finally { backup.Stop(); Directory.Delete(folder, true); }
        }

        [Fact]
        public void KeepLastThreeExactlyLikeTheImportSnapshots()
        {
            var (backup, folder, _) = Make();
            try
            {
                Directory.CreateDirectory(folder);
                for (int i = 1; i <= 5; i++)
                {
                    string file = Path.Combine(folder, $"CyrFlip-settings-26-10-1{i}_1200.json");
                    File.WriteAllText(file, "{}");
                    File.SetLastWriteTimeUtc(file, new DateTime(2026, 10, 10 + i, 12, 0, 0, DateTimeKind.Utc));
                }
                backup.Prune();
                string[] kept = Directory.GetFiles(folder);
                Assert.Equal(3, kept.Length);
                Assert.Contains(kept, f => f.Contains("14_1200"));
                Assert.DoesNotContain(kept, f => f.Contains("11_1200"));
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void AnUnreadableNewestBackupIsNotOffered()
        {
            var (backup, folder, _) = Make();
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "CyrFlip-settings-26-10-14_1200.json"), "{ not a settings file");
                string good = Path.Combine(folder, "CyrFlip-settings-26-10-13_1200.json");
                File.WriteAllText(good, SettingsFileWriter.Write(new AppConfig(), DateTime.UtcNow, "1.0", Array.Empty<string>()));
                File.SetLastWriteTimeUtc(Path.Combine(folder, "CyrFlip-settings-26-10-14_1200.json"), DateTime.UtcNow);
                Assert.Equal(Path.GetFullPath(good), backup.NewestReadable());
            }
            finally { Directory.Delete(folder, true); }
        }
    }

    public class BackupOfferDecisionTests
    {
        [Theory]
        [InlineData(true, false, false, true, (int)BackupOfferDecision.Ask)]        // fresh, backup there: ask
        [InlineData(true, false, false, false, (int)BackupOfferDecision.Settle)]    // fresh, nothing to offer: writer starts
        [InlineData(false, true, false, true, (int)BackupOfferDecision.Ask)]        // "Not now" re-asks next start
        [InlineData(false, false, false, true, (int)BackupOfferDecision.NeverAsked)] // an existing profile is never asked
        [InlineData(true, false, true, true, (int)BackupOfferDecision.NeverAsked)]  // "No - start fresh" is final
        [InlineData(false, true, true, true, (int)BackupOfferDecision.NeverAsked)]
        public void TheOfferFiresExactlyWhenTheSpecSays(bool fresh, bool open, bool declined, bool readable, int expected)
            => Assert.Equal((BackupOfferDecision)expected, BackupOffer.Decide(fresh, open, declined, readable));
    }

    [Collection(SharedGdiCollection.Name)]
    public class BackupRestoreDialogTests
    {
        [Fact]
        public void TheSafeAnswerIsNotNowAndEscapeGivesIt()
        {
            using var dialog = new BackupRestoreDialog("English", "14.10.2026 12:00");
            Assert.Equal(3, dialog.Buttons.Length);
            Assert.Same(dialog.Buttons[1], dialog.AcceptButton);
            Assert.Same(dialog.AcceptButton, dialog.CancelButton);
            Assert.Equal("Not now", dialog.Buttons[1].Text);
            Assert.Equal(DialogResult.Yes, dialog.Buttons[0].DialogResult);
            Assert.Equal(DialogResult.No, dialog.Buttons[2].DialogResult);
        }

        [UiSweepFact]
        public void AllThirteenLanguagesFitAndTranslate()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    foreach (string language in Localization.Names)
                    {
                        using var dialog = new BackupRestoreDialog(language, "14.10.2026 12:00");
                        dialog.Show(); dialog.PerformLayout();
                        foreach (Button button in dialog.Buttons)
                            Assert.True(button.Width >= TextRenderer.MeasureText(button.Text, button.Font).Width, language + ": " + button.Text);
                        Assert.True(dialog.Height <= Screen.FromControl(dialog).WorkingArea.Height, language + ": dialog taller than screen");
                        dialog.Close();
                    }
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(30000));
            if (failure != null) throw failure;
        }
    }
}

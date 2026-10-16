using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The traffic light's lifecycle inside the tray shell (ticket S0069 D6): <b>nothing here exists until
    /// the module is switched on</b> - no engine, no scheduler (and so no timer, no network event
    /// subscription), no journal, no window object - and all of it goes away when it is switched off.
    /// </summary>
    internal sealed partial class CyrFlipContext
    {
        private Hotkey _trafficLightHotkey = Hotkey.Parse(AppConfig.DefaultTrafficLightHotkey);
        private bool _trafficLightChordWasLive;
        private ToolStripMenuItem? _trafficLightItem;
        private TrafficLightEngine? _trafficEngine;
        private TrafficLightScheduler? _trafficScheduler;
        private TrafficLightJournal? _trafficJournal;
        private TrafficLightNotifier? _trafficNotifier;
        private TrafficLightWindow? _trafficWindow;
        // The UI-thread heartbeat of the feature: the balloon, the tray text, the settings table. It runs only
        // while the module is on, and does nothing that costs more than reading a few fields.
        private System.Windows.Forms.Timer? _trafficBeat;
        private int _trafficResultsPosted;
        private bool _trafficStartMarkerWritten;
        private string _trafficTrayText = "";
        private DateTime _trafficBalloonAtUtc = DateTime.MinValue;

        private bool TrafficLightChordLive => ChordBinding.TrafficLightLive(_config);

        // ---- construction-time wiring (called from the constructor) ----

        /// <summary>The tray entry, created invisible like every module's: a user who never turns the module on sees the menu they always had.</summary>
        private ToolStripMenuItem CreateTrafficLightItem()
        {
            _trafficLightItem = new ToolStripMenuItem(null, null, (_, _) => ShowTrafficLightFromTray())
            {
                Visible = _config.EnableTrafficLight,
            };
            return _trafficLightItem;
        }

        /// <summary>After the hook is installed and <c>_ui</c> is captured: the chord, then - only if switched on - the module.</summary>
        private void StartTrafficLightAtLaunch()
        {
            _hook.UpdateTrafficLightHotkey(_trafficLightHotkey);
            // Going live is checked like assigning it: a chord the user already gave to something else stays
            // theirs, and this one is switched off rather than bound twice.
            if (TrafficLightChordLive && Chords().CyrFlipOwnerOf(_trafficLightHotkey, ChordKind.TrafficLight) != null)
            {
                _config.EnableTrafficLightHotkey = false;
                _config.Save();
            }
            _hook.UpdateTrafficLightEnabled(_config.EnableHotkeys && TrafficLightChordLive);
            _trafficLightChordWasLive = TrafficLightChordLive;
            _tray.BalloonTipClicked += (_, _) =>
            {
                // Our own balloon, a moment ago: it opens the board (D14).
                if (_trafficScheduler != null && (DateTime.UtcNow - _trafficBalloonAtUtc).TotalSeconds < 30)
                    ShowTrafficLight(takeFocus: true);
            };
            ApplyTrafficLightModule();
            if (_config.EnableTrafficLight && _config.TrafficLightVisible)
                _ui?.Post(_ => ShowTrafficLight(takeFocus: false), null);
        }

        // ---- the module on and off ----

        private void ApplyTrafficLightModule()
        {
            bool on = _config.EnableTrafficLight;
            if (on && _trafficScheduler == null) StartTrafficLight();
            else if (!on && _trafficScheduler != null) StopTrafficLight();
            else if (on) ApplyTrafficLightTargets();
            if (_trafficLightItem != null) _trafficLightItem.Visible = on;
            UpdateTrafficLightTray();
        }

        private void StartTrafficLight()
        {
            _trafficEngine = new TrafficLightEngine();
            _trafficEngine.SetTargets(_config.TrafficLightTargets);
            _trafficNotifier = new TrafficLightNotifier(t => string.IsNullOrWhiteSpace(t.Label) ? T("(no label)") : t.Label.Trim());
            _trafficJournal = new TrafficLightJournal(DataFolder.Current);
            _trafficJournal.WriteFailed += _ => _ui?.Post(_ => ShowWarningBalloon(4000,
                T("The availability traffic light history could not be written - new lines will be saved with the next write.")), null);

            _trafficScheduler = new TrafficLightScheduler(_trafficEngine, TrafficLightProbeSet.CreateDefault());
            _trafficScheduler.StateChanged += OnTrafficLightStateChanged;
            _trafficScheduler.ResultsChanged += OnTrafficLightResults;
            _trafficScheduler.OfflineChanged += OnTrafficLightOffline;

            if (AnyTrafficLightRecords()) WriteTrafficLightStartMarker();
            _trafficBeat = new System.Windows.Forms.Timer { Interval = 2000 };
            _trafficBeat.Tick += (_, _) => OnTrafficLightBeat();
            _trafficBeat.Start();
            _trafficScheduler.Start();
        }

        private void StopTrafficLight()
        {
            _trafficBeat?.Stop();
            _trafficBeat?.Dispose();
            _trafficBeat = null;
            _trafficScheduler?.Dispose();
            _trafficScheduler = null;
            _trafficJournal?.Dispose();
            _trafficJournal = null;
            _trafficNotifier = null;
            _trafficEngine = null;
            _trafficStartMarkerWritten = false;
            if (_trafficWindow != null)
            {
                _trafficWindow.Dispose();
                _trafficWindow = null;
            }
        }

        private bool AnyTrafficLightRecords()
            => _config.TrafficLightTargets.Any(t => t != null && t.RecordHistory && t.Enabled);

        private void WriteTrafficLightStartMarker()
        {
            if (_trafficStartMarkerWritten) return;
            _trafficStartMarkerWritten = true;
            _trafficJournal?.Marker(TrafficJournalMarker.Started);
        }

        /// <summary>The list was edited (here or on the settings page): the engine takes the new one and keeps the results it still can.</summary>
        private void ApplyTrafficLightTargets()
        {
            _trafficEngine?.SetTargets(_config.TrafficLightTargets);
            if (AnyTrafficLightRecords()) WriteTrafficLightStartMarker();
            _trafficWindow?.RefreshBoard();
        }

        // ---- the scheduler's events (not on the UI thread) ----

        private void OnTrafficLightStateChanged(TrafficLightChange change)
        {
            try
            {
                _trafficJournal?.Record(change);
                _trafficNotifier?.Add(change);
            }
            catch (Exception ex) { TrafficLightLog.Fault("state-changed", ex); }
        }

        private void OnTrafficLightResults()
        {
            // Coalesced: sixteen checks finishing together make one repaint, not sixteen.
            if (Interlocked.Exchange(ref _trafficResultsPosted, 1) != 0) return;
            _ui?.Post(_ =>
            {
                Interlocked.Exchange(ref _trafficResultsPosted, 0);
                if (_disposed || _trafficEngine == null) return;
                _trafficWindow?.RefreshBoard();
                UpdateTrafficLightTray();
                _settings?.RefreshTrafficLightStates();
            }, null);
        }

        private void OnTrafficLightOffline(bool offline)
        {
            try
            {
                if (AnyTrafficLightRecords()) _trafficJournal?.Marker(offline ? TrafficJournalMarker.Offline : TrafficJournalMarker.Online);
            }
            catch (Exception ex) { TrafficLightLog.Fault("offline-marker", ex); }
            _ui?.Post(_ => UpdateTrafficLightTray(), null);
        }

        // ---- the UI heartbeat ----

        private void OnTrafficLightBeat()
        {
            if (_disposed || _trafficEngine == null) return;
            UpdateTrafficLightTray();
            TrafficLightBalloon? balloon = _trafficNotifier?.TryTake(DateTime.UtcNow);
            if (balloon != null) ShowTrafficLightBalloon(balloon);
        }

        private void ShowTrafficLightBalloon(TrafficLightBalloon balloon)
        {
            var parts = new List<string>();
            if (balloon.DownLabels.Count > 0)
                parts.Add(balloon.DownMore > 0
                    ? Localization.Format(T, "Unavailable: {0} and {1} more", string.Join(", ", balloon.DownLabels), balloon.DownMore)
                    : Localization.Format(T, "Unavailable: {0}", string.Join(", ", balloon.DownLabels)));
            if (balloon.UpLabels.Count > 0)
                parts.Add(balloon.UpMore > 0
                    ? Localization.Format(T, "Available again: {0} and {1} more", string.Join(", ", balloon.UpLabels), balloon.UpMore)
                    : Localization.Format(T, "Available again: {0}", string.Join(", ", balloon.UpLabels)));
            if (parts.Count == 0) return;
            _trafficBalloonAtUtc = DateTime.UtcNow;
            try
            {
                _tray.ShowBalloonTip(5000, "CyrFlip", string.Join("\r\n", parts),
                    balloon.DownLabels.Count > 0 ? ToolTipIcon.Warning : ToolTipIcon.Info);
            }
            catch (Exception ex) { TrafficLightLog.Fault("balloon", ex); }
        }

        // ---- the tray entry ----

        private void UpdateTrafficLightTray()
        {
            if (_trafficLightItem == null) return;
            _trafficLightItem.Visible = _config.EnableTrafficLight;
            string text = T("Availability traffic light");
            if (_trafficEngine != null)
            {
                string? summary = TrafficLightText.SummaryLine(_trafficEngine.Summary(), T);
                if (summary != null) text += ": " + summary;
            }
            if (text != _trafficTrayText)
            {
                _trafficTrayText = text;
                _trafficLightItem.Text = text;
            }
            _trafficLightItem.ShortcutKeyDisplayString = _config.EnableHotkeys && TrafficLightChordLive ? _trafficLightHotkey.Display : null;
        }

        // ---- the window ----

        private TrafficLightWindow? EnsureTrafficLightWindow()
        {
            if (!_config.EnableTrafficLight || _trafficEngine == null) return null;
            if (_trafficWindow != null && !_trafficWindow.IsDisposed) return _trafficWindow;
            var actions = new TrafficLightBoardActions
            {
                Snapshot = () => _trafficEngine?.Snapshot() ?? new List<TrafficLightTile>(),
                Summary = () => _trafficEngine?.Summary() ?? new TrafficLightSummary(),
                CheckNow = id => _trafficScheduler?.RequestCheck(id),
                CheckAll = () => _trafficScheduler?.RequestAll(),
                // The pencil: the settings window, on this module's own page.
                AddTarget = () => Defer(AddTrafficLightTargetFromBoard),
                EditTargets = () => Defer(() => ShowSettings(SettingsPageKey.TrafficLight)),
                PinnedChanged = _ => { _config.Save(); _settings?.Reload(); },
                Hidden = () =>
                {
                    if (!_config.TrafficLightVisible) return;
                    _config.TrafficLightVisible = false;
                    _config.SaveTrafficLightVisible();
                },
                UtcNow = () => DateTime.UtcNow,
            };
            _trafficWindow = new TrafficLightWindow(_config, actions, _config.UiLanguage);
            return _trafficWindow;
        }

        private void ShowTrafficLight(bool takeFocus)
        {
            TrafficLightWindow? window = EnsureTrafficLightWindow();
            if (window == null) return;
            window.ShowBoard(takeFocus, _indicator.LastActiveWindow);
            if (!_config.TrafficLightVisible)
            {
                _config.TrafficLightVisible = true;
                _config.SaveTrafficLightVisible();
            }
        }

        /// <summary>The tray item takes the focus onto the board, exactly as the chord does (D16).</summary>
        private void ShowTrafficLightFromTray() => ToggleTrafficLightFromChord();

        private void ToggleTrafficLightFromChord()
        {
            if (!_config.EnableTrafficLight) return;
            TrafficLightWindow? window = EnsureTrafficLightWindow();
            if (window == null) return;
            window.ToggleFromChord(_indicator.LastActiveWindow);
            bool visible = window.Visible;
            if (_config.TrafficLightVisible != visible)
            {
                _config.TrafficLightVisible = visible;
                _config.SaveTrafficLightVisible();
            }
        }

        private void OnTrafficLightHotkeyPressed()
        {
            if (!TrafficLightChordLive) return;
            ToggleTrafficLightFromChord();
        }

        // ---- settings ----

        private void OnSetTrafficLightHotkey()
        {
            using var dlg = new HotkeyDialog(_trafficLightHotkey.Display, T("Set the availability traffic light shortcut"), _config.UiLanguage);
            if (dlg.ShowDialog(DialogOwner()) != DialogResult.OK || dlg.CapturedHotkey == null) return;
            if (!Hotkey.TryParse(dlg.CapturedHotkey, out Hotkey next) || !ChordIsFree(next, ChordKind.TrafficLight)) return;

            _trafficLightHotkey = next;
            _config.TrafficLightHotkey = next.Display;
            _config.Save();
            _hook.UpdateTrafficLightHotkey(next);
            UpdateTrayTooltip();
            UpdateTrafficLightTray();
            _settings?.Reload();
        }

        /// <summary>The switch, the chord, the pin or the tile size changed on the settings page.</summary>
        private void OnTrafficLightChanged()
        {
            if (TrafficLightChordLive && !_trafficLightChordWasLive
                && Hotkey.TryParse(_config.TrafficLightHotkey, out Hotkey chord)
                && !ChordIsFree(chord, ChordKind.TrafficLight, askAboutWindows: false))
            {
                _config.EnableTrafficLightHotkey = false;
                _ui?.Post(_ => _settings?.Reload(), null);
            }
            _trafficLightChordWasLive = TrafficLightChordLive;

            _config.Save();
            _trafficLightHotkey = Hotkey.TryParse(_config.TrafficLightHotkey, out Hotkey parsed)
                ? parsed : Hotkey.Parse(AppConfig.DefaultTrafficLightHotkey);
            _hook.UpdateTrafficLightHotkey(_trafficLightHotkey);
            _hook.UpdateTrafficLightEnabled(_config.EnableHotkeys && TrafficLightChordLive);

            ApplyTrafficLightModule();
            // The pin, the tile size and the columns: the board refits its grid.
            if (_trafficWindow != null && !_trafficWindow.IsDisposed) _trafficWindow.ApplyBoardOptions();
            UpdateTrayTooltip();
        }

        private void OnTrafficLightTargetsChanged()
        {
            _config.Save();
            if (_trafficEngine != null) ApplyTrafficLightTargets();
            UpdateTrafficLightTray();
            _settings?.RefreshTrafficLightStates();
        }

        /// <summary>
        /// The board's "add" button: a small window for an address and a label, the recognizer's reading under
        /// them. The new target joins the list exactly as the settings page's one-field add would put it there;
        /// the rest of its options are the settings page's.
        /// </summary>
        private void AddTrafficLightTargetFromBoard()
        {
            if (!_config.EnableTrafficLight || _trafficEngine == null) return;
            using var dialog = new TrafficLightQuickAddDialog(_config.UiLanguage,
                () => _config.TrafficLightTargets, TrafficLightProbeSet.CreateDefault());
            // The question comes from the board's own button, so the board owns it (modal, owned, centred -
            // APP-BEHAVIOUR rule 1); with the board gone, whatever window is open. Owned, it needs no
            // taskbar button of its own and centres on its owner instead of on the screen.
            IWin32Window? owner = _trafficWindow is { IsDisposed: false, Visible: true } ? _trafficWindow : DialogOwner();
            if (owner != null)
            {
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.ShowInTaskbar = false;
            }
            if (dialog.ShowDialog(owner) != DialogResult.OK || dialog.Result.Count == 0) return;
            if (_config.TrafficLightTargets.Count + dialog.Result.Count > TrafficLightTargetList.MaxTargets) return;

            foreach (TrafficLightTarget target in dialog.Result) _config.TrafficLightTargets.Add(target);
            OnTrafficLightTargetsChanged();
            // The settings table, if that window is open, gains the row.
            _settings?.Reload();
        }

        private void WireTrafficLightSettings(SettingsForm settings)
        {
            settings.TrafficLightChanged += (_, _) => OnTrafficLightChanged();
            settings.TrafficLightTargetsChanged += (_, _) => OnTrafficLightTargetsChanged();
            settings.TrafficLightHotkeyChangeRequested += (_, _) => OnSetTrafficLightHotkey();
            settings.OpenTrafficLightRequested += (_, _) => ShowTrafficLight(takeFocus: true);
            settings.TrafficLightCheckAllRequested += (_, _) => _trafficScheduler?.RequestAll();
            settings.TrafficLightHistoryRequested += OnTrafficLightHistoryRequested;
            settings.TrafficLightSnapshot = () => _trafficEngine?.Snapshot() ?? new List<TrafficLightTile>();
        }

        // ---- the journal's three commands ----

        private string TrafficLightJournalPath => Path.Combine(DataFolder.Current, TrafficLightJournal.FileName);

        private void OnTrafficLightHistoryRequested(string action)
        {
            string path = TrafficLightJournalPath;
            switch (action)
            {
                case "open":
                case "folder":
                    // The existence probe is on the pool: the data folder may be on a share, and this is the
                    // hooks' thread (the ScreenshotFolderOpener rule).
                    Task.Run(() => File.Exists(path)).ContinueWith(task =>
                    {
                        bool exists = task.Status == TaskStatus.RanToCompletion && task.Result;
                        _ui?.Post(_ =>
                        {
                            if (!exists)
                            {
                                ShowInfoBalloon(3000, T("The history file does not exist yet: it is written for targets marked \"Include in change history\"."));
                                return;
                            }
                            try
                            {
                                if (action == "open")
                                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
                                else
                                    Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true })?.Dispose();
                            }
                            catch { ShowWarningBalloon(3000, T("The history file could not be opened.")); }
                        }, null);
                    }, TaskScheduler.Default);
                    break;
                case "clear":
                    if (ConfirmDialog.Show(DialogOwner(), _config.UiLanguage,
                            T("Delete the availability traffic light change history? The file contains the addresses of targets. The deletion cannot be undone."),
                            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, danger: true) != DialogResult.Yes)
                        return;
                    TrafficLightJournal? live = _trafficJournal;
                    Task.Run(() =>
                    {
                        using var scratch = live == null ? new TrafficLightJournal(DataFolder.Current) : null;
                        return (live ?? scratch!).Clear();
                    }).ContinueWith(task => _ui?.Post(_ =>
                    {
                        bool ok = task.Status == TaskStatus.RanToCompletion && task.Result;
                        if (ok) ShowInfoBalloon(2500, T("The history was cleared."));
                        else ShowWarningBalloon(3000, T("The history could not be deleted: the file is in use by another program."));
                    }, null), TaskScheduler.Default);
                    break;
            }
        }
    }
}

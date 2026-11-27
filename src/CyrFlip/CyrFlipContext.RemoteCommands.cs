using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The target of REMOTE-CONTROL in the running copy (tickets CYRFLP-0140 and CYRFLP-0142): the listener's lifetime,
    /// follows the exchange module and the account entries (assumption A1); the switch and the grants it reads; the
    /// External control window of rule 18 (one instance) and the person's confirmation on this PC (rule 10, the prompt of
    /// `WINDOW.md` section 5 mirrored in section T2); and the run of a decided verb through the same dispatcher a local
    /// caller meets, with the local setting of APP-COMMANDS rule 13 left out (REMOTE-CONTROL rule 13: a remote request is
    /// decided by rules 8 to 10 alone).
    /// </summary>
    internal sealed partial class CyrFlipContext
    {
        private ExchangeCommandListener? _remoteListener;
        private volatile bool _remoteListening;
        private volatile IReadOnlyList<RemoteControlGrant> _remoteGrants = new RemoteControlGrant[0];
        /// <summary>The trusted other devices of each entry, as its directory last listed them (UI thread).</summary>
        private readonly Dictionary<string, IReadOnlyList<ExchangePeer>> _remoteControllers =
            new Dictionary<string, IReadOnlyList<ExchangePeer>>(StringComparer.Ordinal);
        private bool _remoteLogHooked;
        private ExternalControlWindow? _externalControl;
        /// <summary>The confirmations open now, oldest first (UI thread).</summary>
        private readonly List<RemoteConsentPending> _remotePending = new List<RemoteConsentPending>();
        private int _remoteConsentIds;

        /// <summary>The entries that hold a resident stream: the module on, a signed-in member entry this build enrolled.</summary>
        internal static List<ExchangeAccountEntry> RemoteCandidates(AppConfig config)
            => !config.EnableExchangeClipboard
                ? new List<ExchangeAccountEntry>()
                : config.ExchangeClipboardAccounts
                    .Where(e => !e.IsGuest && !e.NeedsSignIn && e.CommandCapable && e.DeviceId.Length > 0 && e.DeviceKem.Length > 0)
                    .ToList();

        /// <summary>Re-reads the switch and the grants and starts or stops the streams; UI thread.</summary>
        private void RefreshRemoteCommands()
        {
            if (_disposed) return;
            _remoteListening = _config.RemoteCommandsListen;
            _remoteGrants = RemoteControlGrants.Parse(_config.RemoteCommandGrants);
            List<ExchangeAccountEntry> candidates = RemoteCandidates(_config);
            if (candidates.Count == 0)
            {
                _remoteListener?.Dispose();
                _remoteListener = null;
            }
            else
            {
                if (_remoteListener == null)
                {
                    _remoteListener = new ExchangeCommandListener(new ExchangeClient(), new RemoteCommandHost(this),
                        AppCommandManifest.Shipped, RemoteControlRingFile.Default());
                    if (!_remoteLogHooked)
                    {
                        _remoteLogHooked = true;
                        RemoteCommandsLog.Written += OnRemoteLogWritten;
                        Task.Run(RemoteCommandsLog.LoadRecentFromFile);
                    }
                }
                _remoteListener.Update(candidates);
            }
            RefreshRemoteSurfaces();
        }

        /// <summary>The Settings group and the window show one state at every moment (`WINDOW.md` section 2 item 4).</summary>
        private void RefreshRemoteSurfaces()
        {
            _settings?.RefreshRemoteCommandsState();
            _externalControl?.RefreshFromSource();
        }

        private void OnRemoteLogWritten() => PostUi(RefreshRemoteSurfaces);

        private void PostUi(Action action)
        {
            SynchronizationContext? ui = _ui;
            if (ui == null || _disposed) return;
            ui.Post(_ => { if (!_disposed) action(); }, null);
        }

        private void DisposeRemoteCommands()
        {
            if (_remoteLogHooked) RemoteCommandsLog.Written -= OnRemoteLogWritten;
            foreach (RemoteConsentPending pending in _remotePending.ToList()) pending.Answer?.Invoke(false);
            _externalControl?.Dispose();
            _externalControl = null;
            _remoteListener?.Dispose();
            _remoteListener = null;
        }

        /// <summary>
        /// Opens the External control window, or brings the open one to the front (one instance, `WINDOW.md` section 3);
        /// <paramref name="atDevices"/> scrolls it to T3. The directory is read again, so the window starts current.
        /// </summary>
        internal void ShowExternalControl(bool atDevices = false)
        {
            if (_disposed) return;
            if (_externalControl == null || _externalControl.IsDisposed)
            {
                var window = new ExternalControlWindow(_config, _config.UiLanguage, RemoteCommandsViewNow);
                window.ListenChanged += on => SetRemoteListening(on, fromSettings: false);
                window.SetDeviceAllowed = SetRemoteDeviceAllowed;
                window.SetClassConsent = SetRemoteClassConsent;
                window.OpenExchangeSettings = () => ShowSettings(SettingsPageKey.ExchangeServer);
                window.FormClosed += (_, _) => { if (ReferenceEquals(_externalControl, window)) _externalControl = null; };
                _externalControl = window;
                window.Show();
            }
            else
            {
                if (_externalControl.WindowState == FormWindowState.Minimized) _externalControl.WindowState = FormWindowState.Normal;
                _externalControl.RefreshFromSource();
            }
            if (atDevices) _externalControl.ShowDevices();
            ForegroundActivator.Activate(_externalControl);
            _remoteListener?.Relist();
        }

        /// <summary>Rule 9 on a fresh directory: a controller that left it, or whose key changed, loses its grant at once.</summary>
        private void OnRemoteDirectory(string entryId, IReadOnlyList<ExchangePeer> controllers)
        {
            _remoteControllers[entryId] = controllers;
            List<RemoteControlGrant> grants = RemoteControlGrants.Parse(_config.RemoteCommandGrants);
            bool changed = RemoteControlGrants.DropGone(grants, entryId, controllers.Select(c => c.DeviceId).ToList());
            foreach (RemoteControlGrant g in grants.Where(g => g.Entry == entryId))
            {
                ExchangePeer? peer = controllers.FirstOrDefault(c => c.DeviceId == g.DeviceId);
                string name = peer != null ? ExchangeRecipients.NameOf(peer) : g.Name;
                if (name != g.Name) { g.Name = name; changed = true; }
            }
            if (changed) SaveRemoteGrants(grants);
            RefreshRemoteSurfaces();
        }

        private bool SaveRemoteGrants(List<RemoteControlGrant> grants)
        {
            string before = _config.RemoteCommandGrants;
            _config.RemoteCommandGrants = RemoteControlGrants.Serialize(grants);
            bool saved = _config.Save();
            if (!saved) _config.RemoteCommandGrants = before; // the choice shown before it comes back (section 4 T3)
            _remoteGrants = RemoteControlGrants.Parse(_config.RemoteCommandGrants);
            return saved;
        }

        /// <summary>What the window and the Settings group show, built on the UI thread.</summary>
        internal RemoteCommandsView RemoteCommandsViewNow()
        {
            var view = new RemoteCommandsView { Listening = _config.RemoteCommandsListen };
            Dictionary<string, RemoteStreamState> states = _remoteListener?.States() ?? new Dictionary<string, RemoteStreamState>();
            List<RemoteControlGrant> grants = RemoteControlGrants.Parse(_config.RemoteCommandGrants);
            if (_config.EnableExchangeClipboard)
                foreach (ExchangeAccountEntry entry in _config.ExchangeClipboardAccounts.Where(e => !e.IsGuest))
                {
                    var row = new RemoteCommandsEntryView
                    {
                        EntryId = entry.Id, Label = ExchangeClipboardService.LabelOf(entry),
                        ServerCommand = entry.ServerHas(ExchangeWire.FeatureCommand),
                    };
                    if (entry.NeedsSignIn || !entry.CommandCapable) row.NeedsSignIn = true;
                    else if (states.TryGetValue(entry.Id, out RemoteStreamState s))
                    {
                        row.State = s;
                        if (s == RemoteStreamState.Online) row.ServerCommand = true;
                        if (s == RemoteStreamState.ServerWithout) row.ServerCommand = false;
                    }
                    IReadOnlyList<ExchangePeer> peers = _remoteControllers.TryGetValue(entry.Id, out IReadOnlyList<ExchangePeer>? known)
                        ? known : new List<ExchangePeer>();
                    var ids = new HashSet<string>(StringComparer.Ordinal);
                    foreach (ExchangePeer peer in peers)
                    {
                        ids.Add(peer.DeviceId);
                        RemoteControlGrant? g = RemoteControlGrants.Find(grants, entry.Id, peer.DeviceId);
                        row.Devices.Add(DeviceView(peer.DeviceId, ExchangeRecipients.NameOf(peer), g, peer.Product, peer.Online, peer.LastSeenUtcTicks));
                    }
                    // a grant whose device the directory has not been read for yet (after a start) is still shown
                    foreach (RemoteControlGrant g in grants.Where(x => x.Entry == entry.Id && !ids.Contains(x.DeviceId)))
                        row.Devices.Add(DeviceView(g.DeviceId, g.Name.Length > 0 ? g.Name : g.DeviceId.Substring(0, Math.Min(6, g.DeviceId.Length)),
                            g, "", false, 0));
                    view.Entries.Add(row);
                }
            view.Pending.AddRange(_remotePending);
            view.Log = RemoteCommandsLog.Last();
            return view;
        }

        private static RemoteCommandsDeviceView DeviceView(string deviceId, string name, RemoteControlGrant? grant, string product, bool online, long lastSeen)
        {
            var device = new RemoteCommandsDeviceView
            {
                DeviceId = deviceId, Name = name, Allowed = grant != null, Product = product, Online = online, LastSeenUtcTicks = lastSeen,
            };
            if (grant != null) foreach (KeyValuePair<string, string> c in grant.Classes) device.Classes[c.Key] = c.Value;
            return device;
        }

        /// <summary>T3's tick (rules 9 and 10): a ticked device gets <c>safe</c> <c>always</c> and every other class nothing.</summary>
        internal bool SetRemoteDeviceAllowed(string entryId, string deviceId, string name, bool allowed)
        {
            List<RemoteControlGrant> grants = RemoteControlGrants.Parse(_config.RemoteCommandGrants);
            RemoteControlGrant? g = RemoteControlGrants.Find(grants, entryId, deviceId);
            if (!allowed) { if (g != null) grants.Remove(g); }
            else if (g == null) grants.Add(RemoteControlGrant.Ticked(entryId, deviceId, name));
            else return true;
            bool saved = SaveRemoteGrants(grants);
            RefreshRemoteSurfaces();
            return saved;
        }

        /// <summary>
        /// T3's three-way choice of one class: <paramref name="consent"/> <c>ask</c> or <c>always</c>, or null for No access.
        /// A grant left with no class is the device not allowed (the grant record keeps only grants with a class).
        /// </summary>
        internal bool SetRemoteClassConsent(string entryId, string deviceId, string name, string riskClass, string? consent)
        {
            if (!RemoteControl.Classes.Contains(riskClass)) return false;
            List<RemoteControlGrant> grants = RemoteControlGrants.Parse(_config.RemoteCommandGrants);
            RemoteControlGrant? g = RemoteControlGrants.Find(grants, entryId, deviceId);
            if (g == null)
            {
                if (consent == null) return true;
                g = new RemoteControlGrant { Entry = entryId, DeviceId = deviceId, Name = name };
                grants.Add(g);
            }
            if (consent == null) g.Classes.Remove(riskClass); else g.Classes[riskClass] = consent;
            if (g.Classes.Count == 0) grants.Remove(g);
            bool saved = SaveRemoteGrants(grants);
            RefreshRemoteSurfaces();
            return saved;
        }

        /// <summary>
        /// The switch of rule 8, only from the Settings group or the window. Turned on from Settings it opens the window at
        /// T3 (`WINDOW.md` section 2 item 5); turned off it opens nothing.
        /// </summary>
        internal void SetRemoteListening(bool on, bool fromSettings)
        {
            _config.RemoteCommandsListen = on;
            _config.Save();
            _remoteListening = on;
            RefreshRemoteSurfaces();
            if (on && fromSettings) ShowExternalControl(atDevices: true);
        }

        /// <summary>Called on the listener's worker thread; blocks until the verb has answered.</summary>
        private RemoteCommandRun RunRemote(RemoteControlVerdict verdict, RemoteControlRequest request, string controllerName, bool ask)
        {
            AppCommandSpec spec = verdict.Spec!;
            if (ask && !AskOnUi(spec, verdict.Tokens, controllerName)) return new RemoteCommandRun { Declined = true };
            switch (spec.Name)
            {
                case "commands":
                    // Rule 7: answered remotely, the list holds only the entries that carry remote.
                    return new RemoteCommandRun { Response = AppCommandResponse.Done("Done.", AppCommandManifest.Shipped.RemoteView()) };
                case "version":
                    return new RemoteCommandRun
                    {
                        Response = AppCommandResponse.Done("Done.", new Dictionary<string, object>
                        {
                            ["product"] = "CyrFlip", ["version"] = SupportBundle.AppVersion(), ["cliVersion"] = AppCommandManifest.Shipped.CliVersion,
                        }),
                    };
            }
            var local = new AppCommandRequest
            {
                Command = spec.Name, Args = verdict.Tokens, Origin = "remote", RequestId = "remote", WorkingDir = "",
            };
            AppCommandResponse response = AnswerAppCommand(local, remote: true);
            AppCommandsLog.Log(spec.Name, "remote", 0, response);
            return new RemoteCommandRun { Response = response };
        }

        /// <summary>Rule 10's confirmation, answered on the UI thread; false when the person declined or did not answer in 60 s.</summary>
        private bool AskOnUi(AppCommandSpec spec, string[] tokens, string controllerName)
        {
            SynchronizationContext? ui = _ui;
            if (ui == null || _disposed) return false;
            int accepted = 0;
            // Not disposed here: when the wait below gives up, the posted call still sets it later.
            var done = new ManualResetEventSlim(false);
            ui.Post(_ =>
            {
                if (_disposed) { done.Set(); return; }
                try
                {
                    OpenRemoteConsent(spec, tokens, controllerName, ok =>
                    {
                        if (ok) Interlocked.Exchange(ref accepted, 1);
                        done.Set();
                    });
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is System.ComponentModel.Win32Exception)
                {
                    RemoteCommandsLog.Fault("consent-prompt", ex);
                    done.Set();
                }
            }, null);
            done.Wait(TimeSpan.FromSeconds(RemoteControl.ConsentSeconds + 30));
            return Volatile.Read(ref accepted) == 1;
        }

        /// <summary>
        /// Shows the prompt of `WINDOW.md` section 5 and lists the request in T2; the first answer - the prompt, T2, the end
        /// of the 60 seconds - settles it and closes the other. UI thread.
        /// </summary>
        private void OpenRemoteConsent(AppCommandSpec spec, string[] tokens, string controllerName, Action<bool> finish)
        {
            var pending = new RemoteConsentPending
            {
                Id = ++_remoteConsentIds,
                DeviceName = RemoteControlWords.CleanName(controllerName),
                Verb = spec.Name,
                Arguments = ArgumentWords(tokens),
                Risk = spec.Risk,
                DeadlineUtc = DateTime.UtcNow.AddSeconds(RemoteControl.ConsentSeconds),
            };
            string title = Localization.Format(T, "{0} asks {1}", pending.DeviceName, "CyrFlip");
            string summary = RemoteControlWords.HasSummary(spec.Name) ? T(RemoteControlWords.VerbSummary(spec.Name)) : spec.Name;
            string body = summary + (pending.Arguments.Length > 0 ? " (" + pending.Arguments + ")" : "");
            var prompt = new RemoteConsentPrompt(_config.UiLanguage, pending, title, body);
            bool settled = false;
            void Settle(bool ok)
            {
                if (settled) return;
                settled = true;
                _remotePending.Remove(pending);
                prompt.Dismiss();
                finish(ok);
                if (!_disposed) _externalControl?.RefreshFromSource();
            }
            pending.Answer = Settle;
            prompt.Answered += Settle;
            _remotePending.Add(pending);
            prompt.Show();
            _externalControl?.RefreshFromSource();
        }

        /// <summary>A request's arguments in words: <c>--minutes 30</c> as <c>minutes 30</c>, one line, no control character.</summary>
        internal static string ArgumentWords(string[] tokens)
            => RemoteCommandsLog.Clean(string.Join(" ", tokens.Select(t => t.StartsWith("--", StringComparison.Ordinal) ? t.Substring(2) : t)), 120) is string s && s != "?" ? s : "";

        /// <summary><c>hotkeys pause</c> (ticket CYRFLP-0140 A12): the master switch's own apply, without the duplicate warning box.</summary>
        private AppCommandResponse RunHotkeysPause(AppCommandInvocation invocation)
        {
            bool paused = invocation.Flag("paused");
            ApplyHotkeysEnabled(!paused);
            _settings?.RefreshState();
            return AppCommandResponse.Done(T(paused ? "Every CyrFlip key combination is paused." : "The CyrFlip key combinations are on again."),
                new Dictionary<string, object> { ["paused"] = paused });
        }

        private sealed class RemoteCommandHost : IRemoteCommandHost
        {
            private readonly CyrFlipContext _owner;

            public RemoteCommandHost(CyrFlipContext owner) { _owner = owner; }

            public bool Listening => _owner._remoteListening;

            public RemoteControlGrant? GrantFor(string entryId, string deviceId) => RemoteControlGrants.Find(_owner._remoteGrants, entryId, deviceId);

            public RemoteCommandRun Run(RemoteControlVerdict verdict, RemoteControlRequest request, string controllerName, bool ask)
                => _owner.RunRemote(verdict, request, controllerName, ask);

            public void AccountsChanged() => _owner.PostUi(() => _owner._config.SaveExchangeAccounts());

            public void DirectoryListed(string entryId, IReadOnlyList<ExchangePeer> controllers)
                => _owner.PostUi(() => _owner.OnRemoteDirectory(entryId, controllers));

            public void StateChanged() => _owner.PostUi(_owner.RefreshRemoteSurfaces);
        }
    }

    /// <summary>The state line of section T1 (`WINDOW.md` section 4): the first of these that holds.</summary>
    internal enum RemoteThisDeviceState { Off, NoServer, SignIn, Offline, Ready }

    /// <summary>What the External control window and its Settings group draw; built on the UI thread by the context.</summary>
    internal sealed class RemoteCommandsView
    {
        public bool Listening { get; set; }
        public List<RemoteCommandsEntryView> Entries { get; } = new List<RemoteCommandsEntryView>();
        /// <summary>The confirmations open now (section T2).</summary>
        public List<RemoteConsentPending> Pending { get; } = new List<RemoteConsentPending>();
        public IReadOnlyList<string> Log { get; set; } = new string[0];

        /// <summary>T1's state line: off; no entry whose server lists <c>command</c>; an entry to sign in again; unreachable; ready.</summary>
        public static RemoteThisDeviceState StateOf(RemoteCommandsView view)
        {
            if (!view.Listening) return RemoteThisDeviceState.Off;
            List<RemoteCommandsEntryView> carrying = view.Entries.Where(e => e.ServerCommand).ToList();
            if (carrying.Count == 0) return RemoteThisDeviceState.NoServer;
            if (carrying.Any(e => e.NeedsSignIn || e.State == RemoteStreamState.NeedsSignIn)) return RemoteThisDeviceState.SignIn;
            if (!carrying.Any(e => e.State == RemoteStreamState.Online) && carrying.Any(e => e.State == RemoteStreamState.Retrying))
                return RemoteThisDeviceState.Offline;
            return RemoteThisDeviceState.Ready;
        }
    }

    internal sealed class RemoteCommandsEntryView
    {
        public string EntryId { get; set; } = "";
        public string Label { get; set; } = "";
        public bool NeedsSignIn { get; set; }
        /// <summary>The entry's server lists <c>command</c> (its last <c>welcome</c>, or the stream's own state).</summary>
        public bool ServerCommand { get; set; }
        public RemoteStreamState? State { get; set; }
        public List<RemoteCommandsDeviceView> Devices { get; } = new List<RemoteCommandsDeviceView>();
    }

    internal sealed class RemoteCommandsDeviceView
    {
        public string DeviceId { get; set; } = "";
        public string Name { get; set; } = "";
        public bool Allowed { get; set; }
        /// <summary>The record's <c>product</c> (`DEVICE-EXCHANGE` 6.1); empty when the directory was not read yet.</summary>
        public string Product { get; set; } = "";
        public bool Online { get; set; }
        public long LastSeenUtcTicks { get; set; }
        /// <summary>The granted classes and their consent (<c>ask</c> / <c>always</c>); a class absent is No access.</summary>
        public Dictionary<string, string> Classes { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }
}

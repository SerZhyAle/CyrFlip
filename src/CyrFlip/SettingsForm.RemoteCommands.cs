using System;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The External control group on the exchange server page (REMOTE-CONTROL rule 18, `WINDOW.md` section 2, tickets
    /// CYRFLP-0140 and CYRFLP-0142): exactly two rows - the switch of rule 8, off by default, and "Open External control".
    /// Every other part of remote control - the devices, the grants, the confirmations, the log - is in the External
    /// control window and nowhere else. The switch is one setting shown here and in section T1 of the window; turning it
    /// on here opens the window at its device list (the context does it).
    /// </summary>
    internal sealed partial class SettingsForm
    {
        private readonly CheckBox _remoteListen = Check("Listen to commands from my other devices");

        /// <summary>The person turned the switch of rule 8 on or off, here.</summary>
        internal event Action<bool>? RemoteCommandsListenChanged;

        /// <summary>"Open External control": always enabled, also with the switch off or no server connected (section 2 item 6).</summary>
        internal event EventHandler? ExternalControlRequested;

        private Control RemoteCommandsGroup()
        {
            _remoteListen.Name = "setting.remote-commands-listen";
            _remoteListen.CheckedChanged += (_, _) =>
            {
                if (_loading) return;
                RemoteCommandsListenChanged?.Invoke(_remoteListen.Checked);
            };

            Button open = Button("Open External control", () => ExternalControlRequested?.Invoke(this, EventArgs.Empty));
            open.Name = "setting.external-control-open";

            return Group("exchange/commands", "External control",
                Setting(_remoteListen, "Off until you turn it on. Only the devices you allow, and only what you allow."),
                open);
        }

        /// <summary>The stored switch, so the group and the window show one state at every moment.</summary>
        internal void RefreshRemoteCommandsState()
        {
            bool wasLoading = _loading;
            _loading = true;
            try { _remoteListen.Checked = _config.RemoteCommandsListen; }
            finally { _loading = wasLoading; }
        }
    }
}

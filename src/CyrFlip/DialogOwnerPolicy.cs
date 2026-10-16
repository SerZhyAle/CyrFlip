namespace CyrFlip
{
    /// <summary>What a dialog raised by the context should be owned by.</summary>
    internal enum DialogOwnerKind
    {
        /// <summary>No window of ours is there to own it: the tray is the only surface.</summary>
        None,
        /// <summary>The first-run tour is on screen (it is modal and disables every other window).</summary>
        Welcome,
        /// <summary>The settings window is on screen and usable.</summary>
        Settings,
    }

    /// <summary>
    /// Who owns a dialog the tray context raises (APP-BEHAVIOUR rule 1, ticket S0060 P7-b): a secondary
    /// window is modal, owned and centred on its owner. A confirmation, a hotkey capture or a
    /// duplicate-chord warning raised while the settings window is open belongs to it - otherwise it
    /// opens in the middle of the screen with a taskbar button of its own, behind the very window
    /// the user is working in. The decision is pure so the whole matrix is testable without a desktop;
    /// the context reads the three facts and maps the answer to a window.
    ///
    /// <para>The tour wins: it is modal, so a dialog owned by anything else would be disabled behind it.
    /// A minimized settings window owns nothing - an owner that is on the taskbar has no place to
    /// centre on - and the dialog then gets its own taskbar button like the tray's own questions do.</para>
    /// </summary>
    internal static class DialogOwnerPolicy
    {
        internal static DialogOwnerKind Choose(bool welcomeShown, bool settingsShown, bool settingsMinimized)
        {
            if (welcomeShown) return DialogOwnerKind.Welcome;
            if (settingsShown && !settingsMinimized) return DialogOwnerKind.Settings;
            return DialogOwnerKind.None;
        }
    }
}

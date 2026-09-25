namespace CyrFlip
{
    /// <summary>
    /// The "do not record this" markers an application can put beside the text it copies. Password
    /// managers (KeePass, KeePassXC, Bitwarden, 1Password ..) mark a copied secret with one or more of
    /// them, and the history honours every one (spec S0005 CH-1).
    /// </summary>
    internal struct ClipboardPrivacyMarkers
    {
        /// <summary><c>ExcludeClipboardContentFromMonitorProcessing</c> - the documented request to clipboard monitors.</summary>
        public bool ExcludeFromMonitor;
        /// <summary><c>Clipboard Viewer Ignore</c> - the older convention with the same meaning.</summary>
        public bool ViewerIgnore;
        /// <summary><c>CanIncludeInClipboardHistory</c> as a DWORD; null when the format is absent or unreadable.</summary>
        public uint? CanIncludeInHistory;
        /// <summary><c>CanUploadToCloudClipboard</c> as a DWORD; null when the format is absent or unreadable.</summary>
        public uint? CanUploadToCloud;
    }

    /// <summary>The classifier behind the markers, kept apart from the clipboard so it is testable.</summary>
    internal static class ClipboardPrivacy
    {
        public const string ExcludeFromMonitorFormat = "ExcludeClipboardContentFromMonitorProcessing";
        public const string ViewerIgnoreFormat = "Clipboard Viewer Ignore";
        public const string CanIncludeInHistoryFormat = "CanIncludeInClipboardHistory";
        public const string CanUploadToCloudFormat = "CanUploadToCloudClipboard";

        /// <summary>
        /// True when the copying application asked monitors not to keep this content. The two DWORD
        /// formats count only with the value 0: <c>CanIncludeInClipboardHistory = 1</c> is an explicit
        /// "yes, keep it".
        /// </summary>
        public static bool ShouldSkip(ClipboardPrivacyMarkers markers) =>
            markers.ExcludeFromMonitor
            || markers.ViewerIgnore
            || markers.CanIncludeInHistory == 0
            || markers.CanUploadToCloud == 0;
    }
}

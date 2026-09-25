using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// Minimal Unicode-text clipboard access via the raw Win32 API (no OLE/COM).
    ///
    /// WinForms <c>Clipboard</c> goes through OLE, which needs a running message pump on the
    /// calling thread; on the background flip thread that can deadlock when the clipboard owner
    /// is a Chromium/Electron app. The plain Win32 clipboard is synchronous and pump-free.
    /// </summary>
    internal static class Win32Clipboard
    {
        private const uint GMEM_MOVEABLE = 0x0002;

        [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseClipboard();
        [DllImport("user32.dll", SetLastError = true)] private static extern bool EmptyClipboard();
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetClipboardData(uint uFormat);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalFree(IntPtr hMem);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr hMem);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalUnlock(IntPtr hMem);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern UIntPtr GlobalSize(IntPtr hMem);

        /// <summary>Read clipboard text. Returns true on a clean access (text may be empty).</summary>
        public static bool TryGetText(out string text)
        {
            text = string.Empty;
            if (!OpenWithRetry())
                return false;
            try
            {
                IntPtr handle = GetClipboardData(CF_UNICODETEXT);
                if (handle == IntPtr.Zero)
                    return true; // no text on the clipboard

                IntPtr ptr = GlobalLock(handle);
                if (ptr == IntPtr.Zero)
                    return false;
                try
                {
                    // Bound the read by the allocated size rather than trusting null-termination,
                    // then trim at the first null (the rest is allocation padding).
                    int maxChars = (int)(GlobalSize(handle).ToUInt64() / sizeof(char));
                    if (maxChars <= 0)
                        return true; // empty
                    string raw = Marshal.PtrToStringUni(ptr, maxChars) ?? string.Empty;
                    int nul = raw.IndexOf('\0');
                    text = nul >= 0 ? raw.Substring(0, nul) : raw;
                }
                finally { GlobalUnlock(handle); }
                return true;
            }
            finally { CloseClipboard(); }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint RegisterClipboardFormat(string lpszFormat);

        // Registered once per process; RegisterClipboardFormat returns the same id for the same name.
        private static readonly uint ExcludeFromMonitorFormat = RegisterClipboardFormat(ClipboardPrivacy.ExcludeFromMonitorFormat);
        private static readonly uint ViewerIgnoreFormat = RegisterClipboardFormat(ClipboardPrivacy.ViewerIgnoreFormat);
        private static readonly uint CanIncludeInHistoryFormat = RegisterClipboardFormat(ClipboardPrivacy.CanIncludeInHistoryFormat);
        private static readonly uint CanUploadToCloudFormat = RegisterClipboardFormat(ClipboardPrivacy.CanUploadToCloudFormat);

        /// <summary>
        /// The history's read: the privacy markers, the sequence number and - only when no marker asks
        /// otherwise - the text, all inside <b>one</b> open, so the markers and the text describe the
        /// same clipboard content. A marked secret is never even copied into this process.
        /// </summary>
        public static bool TryReadForHistory(out string text, out ClipboardPrivacyMarkers markers, out uint sequence)
        {
            text = string.Empty;
            markers = default;
            sequence = 0;
            if (!OpenWithRetry())
                return false;
            try
            {
                // Nobody can change the clipboard while we hold it open, so this number is the content's.
                sequence = GetClipboardSequenceNumber();
                markers.ExcludeFromMonitor = ExcludeFromMonitorFormat != 0 && IsClipboardFormatAvailable(ExcludeFromMonitorFormat);
                markers.ViewerIgnore = ViewerIgnoreFormat != 0 && IsClipboardFormatAvailable(ViewerIgnoreFormat);
                markers.CanIncludeInHistory = ReadDword(CanIncludeInHistoryFormat);
                markers.CanUploadToCloud = ReadDword(CanUploadToCloudFormat);
                if (ClipboardPrivacy.ShouldSkip(markers))
                    return true;
                text = ReadOpenText() ?? string.Empty;
                return true;
            }
            finally { CloseClipboard(); }
        }

        /// <summary>A DWORD-valued format on the already open clipboard; null when absent or unreadable.</summary>
        private static uint? ReadDword(uint format)
        {
            if (format == 0 || !IsClipboardFormatAvailable(format)) return null;
            IntPtr handle = GetClipboardData(format);
            if (handle == IntPtr.Zero) return null;
            if (GlobalSize(handle).ToUInt64() < sizeof(uint)) return null;
            IntPtr ptr = GlobalLock(handle);
            if (ptr == IntPtr.Zero) return null;
            try { return unchecked((uint)Marshal.ReadInt32(ptr)); }
            finally { GlobalUnlock(handle); }
        }

        /// <summary>CF_UNICODETEXT from the already open clipboard; empty when absent, null when unreadable.</summary>
        private static string? ReadOpenText()
        {
            IntPtr handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == IntPtr.Zero)
                return string.Empty;
            IntPtr ptr = GlobalLock(handle);
            if (ptr == IntPtr.Zero)
                return null;
            try
            {
                int maxChars = (int)(GlobalSize(handle).ToUInt64() / sizeof(char));
                if (maxChars <= 0)
                    return string.Empty;
                string raw = Marshal.PtrToStringUni(ptr, maxChars) ?? string.Empty;
                int nul = raw.IndexOf('\0');
                return nul >= 0 ? raw.Substring(0, nul) : raw;
            }
            finally { GlobalUnlock(handle); }
        }

        /// <summary>Replace clipboard contents with <paramref name="text"/> (empty clears it).</summary>
        public static bool TrySetText(string text)
        {
            if (!OpenWithRetry())
                return false;
            try
            {
                if (!EmptyClipboard())
                    return false;
                if (text.Length == 0)
                    return true;

                byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
                IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)(uint)bytes.Length);
                if (hMem == IntPtr.Zero)
                    return false;

                IntPtr dst = GlobalLock(hMem);
                if (dst == IntPtr.Zero)
                {
                    GlobalFree(hMem);
                    return false;
                }
                try { Marshal.Copy(bytes, 0, dst, bytes.Length); }
                finally { GlobalUnlock(hMem); }

                if (SetClipboardData(CF_UNICODETEXT, hMem) == IntPtr.Zero)
                {
                    GlobalFree(hMem); // ownership not transferred on failure
                    return false;
                }
                return true; // the system owns hMem now
            }
            finally { CloseClipboard(); }
        }

        /// <summary>
        /// A raw clipboard format as bytes, so a flip can hand back what it borrowed even when that
        /// was not text (see <see cref="ClipboardHandler.BackupClipboard"/>). Returns false when the
        /// clipboard could not be opened; <paramref name="data"/> is null when the format is absent.
        /// </summary>
        /// <param name="maxBytes">
        /// Above this the format is skipped (<paramref name="data"/> stays null) rather than copied:
        /// a flip must not carry a half-gigabyte scan through memory twice to preserve it.
        /// </param>
        public static bool TryGetBytes(uint format, out byte[]? data, int maxBytes = int.MaxValue)
        {
            data = null;
            if (!OpenWithRetry())
                return false;
            try
            {
                IntPtr handle = GetClipboardData(format);
                if (handle == IntPtr.Zero)
                    return true; // the format is simply not on the clipboard

                IntPtr ptr = GlobalLock(handle);
                if (ptr == IntPtr.Zero)
                    return true; // not a memory-backed format (a bitmap handle, say) - nothing to copy
                try
                {
                    ulong size = GlobalSize(handle).ToUInt64();
                    if (size == 0 || size > (ulong)maxBytes)
                        return true;
                    var bytes = new byte[size];
                    Marshal.Copy(ptr, bytes, 0, bytes.Length);
                    data = bytes;
                }
                finally { GlobalUnlock(handle); }
                return true;
            }
            finally { CloseClipboard(); }
        }

        /// <summary>
        /// Put raw bytes back on the clipboard under <paramref name="format"/>. Used only to restore
        /// what <see cref="TryGetBytes"/> took, and only from <see cref="Restore"/>, which owns the
        /// single open/empty/refill sequence.
        /// </summary>
        private static bool SetBytes(uint format, byte[] data)
        {
            IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)(uint)data.Length);
            if (hMem == IntPtr.Zero)
                return false;

            IntPtr dst = GlobalLock(hMem);
            if (dst == IntPtr.Zero)
            {
                GlobalFree(hMem);
                return false;
            }
            try { Marshal.Copy(data, 0, dst, data.Length); }
            finally { GlobalUnlock(hMem); }

            if (SetClipboardData(format, hMem) == IntPtr.Zero)
            {
                GlobalFree(hMem); // ownership not transferred on failure
                return false;
            }
            return true; // the system owns hMem now
        }

        /// <summary>
        /// Replace the clipboard with several formats at once - one open, one <c>EmptyClipboard</c>,
        /// then every payload. Restoring format by format would not work: each call empties the
        /// clipboard again and would throw away the format restored just before it.
        /// </summary>
        public static bool Restore(IEnumerable<KeyValuePair<uint, byte[]>> payloads)
        {
            if (!OpenWithRetry())
                return false;
            try
            {
                if (!EmptyClipboard())
                    return false;
                bool all = true;
                foreach (KeyValuePair<uint, byte[]> payload in payloads)
                    if (payload.Value != null && payload.Value.Length > 0)
                        all &= SetBytes(payload.Key, payload.Value);
                return all;
            }
            finally { CloseClipboard(); }
        }

        public static bool TryClear()
        {
            if (!OpenWithRetry())
                return false;
            try { return EmptyClipboard(); }
            finally { CloseClipboard(); }
        }

        // The clipboard can be briefly locked by another process; retry for a short while.
        private static bool OpenWithRetry()
        {
            for (int i = 0; i < 12; i++)
            {
                if (OpenClipboard(IntPtr.Zero))
                    return true;
                Thread.Sleep(15);
            }
            return false;
        }
    }
}

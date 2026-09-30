using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>Outcome of reading one format off the open clipboard.</summary>
    internal enum ClipboardRead
    {
        /// <summary>The bytes were read.</summary>
        Ok,
        /// <summary>The format is there but holds nothing (a zero-size block).</summary>
        Empty,
        /// <summary>Bigger than the caller's cap - deliberately left unread.</summary>
        TooLarge,
        /// <summary>Announced but could not be read: the owner failed to render it, or the lock failed.</summary>
        Failed,
    }

    /// <summary>
    /// How text CyrFlip only borrows the clipboard for is marked (ticket S0009, FP-1B). That text is
    /// scaffolding - the converted word on its way into the target, gone again a moment later - and
    /// keeping it out of Windows' own history (Win+V), the cloud clipboard and every monitor that
    /// honours the convention is the rule CyrFlip's own history already follows. It is also what lets
    /// a delayed-rendered paste be told apart from a monitor fetching the text first
    /// (<see cref="ClipboardOwner"/>): a monitor that honours the markers never asks for it.
    /// </summary>
    internal enum TransientMarks
    {
        /// <summary>Text meant to stay on the clipboard: no markers at all.</summary>
        None,
        /// <summary>
        /// Only <c>CanIncludeInClipboardHistory = 0</c> and <c>CanUploadToCloudClipboard = 0</c> - for
        /// a remote-desktop or VM console. Its clipboard bridge is itself a clipboard monitor, and one
        /// that honoured <c>ExcludeClipboardContentFromMonitorProcessing</c> would never carry the text
        /// to the far side; the two history formats concern Windows' history and cloud sync alone.
        /// </summary>
        HistoryOnly,
        /// <summary>All three markers - a local target.</summary>
        All,
    }

    /// <summary>
    /// The clipboard as the backup sees it, so what a flip keeps is testable against a fake without
    /// ever touching the real clipboard of whoever runs the tests (ticket S0009, FP-2/FP-5/FP-7).
    /// </summary>
    internal interface IClipboardReader
    {
        /// <summary>How many formats are on the clipboard; answerable without opening it.</summary>
        int CountFormats();
        /// <summary>Open with the usual short retry; false when another process holds it.</summary>
        bool Open();
        void Close();
        uint Sequence();
        bool IsAvailable(uint format);
        ClipboardRead Read(uint format, int maxBytes, out byte[]? data);
    }

    /// <summary>
    /// The registered clipboard formats CyrFlip reads or writes, by name. Registered once per process;
    /// <c>RegisterClipboardFormat</c> answers the same id for the same name, and a failed registration
    /// is 0, which every caller treats as "no such format".
    /// </summary>
    internal static class ClipboardFormats
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint RegisterClipboardFormat(string lpszFormat);

        // The "do not record this" markers a password manager puts beside a secret (S0005 CH-1).
        public static readonly uint ExcludeFromMonitor = RegisterClipboardFormat(ClipboardPrivacy.ExcludeFromMonitorFormat);
        public static readonly uint ViewerIgnore = RegisterClipboardFormat(ClipboardPrivacy.ViewerIgnoreFormat);
        public static readonly uint CanIncludeInHistory = RegisterClipboardFormat(ClipboardPrivacy.CanIncludeInHistoryFormat);
        public static readonly uint CanUploadToCloud = RegisterClipboardFormat(ClipboardPrivacy.CanUploadToCloudFormat);

        // The two companions of a file selection Explorer puts beside CF_HDROP. "Preferred DropEffect"
        // is what makes a Cut a move: without it the paste copies and the originals stay (FP-7).
        public static readonly uint PreferredDropEffect = RegisterClipboardFormat("Preferred DropEffect");
        public static readonly uint ShellIdListArray = RegisterClipboardFormat("Shell IDList Array");

        // The format Chromium, Electron, Telegram and Office read first for an image (S0026 5.2).
        public static readonly uint Png = RegisterClipboardFormat("PNG");

        /// <summary>Registers any other name, for the line-copy markers.</summary>
        public static uint Register(string name) => RegisterClipboardFormat(name);
    }

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

        /// <summary>The real clipboard behind <see cref="IClipboardReader"/>.</summary>
        public static readonly IClipboardReader Reader = new Win32Reader();

        /// <summary>Read clipboard text. Returns true on a clean access (text may be empty).</summary>
        public static bool TryGetText(out string text)
        {
            text = string.Empty;
            if (!OpenWithRetry())
                return false;
            try
            {
                string? read = ReadOpenText(int.MaxValue, out _);
                if (read == null)
                    return false;
                text = read;
                return true;
            }
            finally { CloseClipboard(); }
        }

        /// <summary>
        /// The history's read: the privacy markers, the sequence number and - only when no marker asks
        /// otherwise - the text, all inside <b>one</b> open, so the markers and the text describe the
        /// same clipboard content. A marked secret is never even copied into this process.
        /// </summary>
        /// <param name="maxBytes">
        /// Above this the text is not read at all and <paramref name="tooLarge"/> is set: the size is
        /// known from the allocation before a single character is marshalled (FP-6).
        /// </param>
        public static bool TryReadForHistory(int maxBytes, out string text, out ClipboardPrivacyMarkers markers,
            out uint sequence, out bool tooLarge)
        {
            text = string.Empty;
            markers = default;
            sequence = 0;
            tooLarge = false;
            if (!OpenWithRetry())
                return false;
            try
            {
                // Nobody can change the clipboard while we hold it open, so this number is the content's.
                sequence = GetClipboardSequenceNumber();
                markers.ExcludeFromMonitor = ClipboardFormats.ExcludeFromMonitor != 0 && IsClipboardFormatAvailable(ClipboardFormats.ExcludeFromMonitor);
                markers.ViewerIgnore = ClipboardFormats.ViewerIgnore != 0 && IsClipboardFormatAvailable(ClipboardFormats.ViewerIgnore);
                markers.CanIncludeInHistory = ReadMarkerDword(ClipboardFormats.CanIncludeInHistory);
                markers.CanUploadToCloud = ReadMarkerDword(ClipboardFormats.CanUploadToCloud);
                if (ClipboardPrivacy.ShouldSkip(markers))
                    return true;
                text = ReadOpenText(maxBytes / sizeof(char), out tooLarge) ?? string.Empty;
                return true;
            }
            finally { CloseClipboard(); }
        }

        /// <summary>What the copy half found on the clipboard, read in one open.</summary>
        internal struct CaptureRead
        {
            public string Text;
            public bool TooLarge;
            /// <summary>An editor marked the copy as its "no selection - copy the whole line" behaviour (FP-4).</summary>
            public bool LineCopy;
            /// <summary>The CF_LOCALE the copying app's text came with; 0 when absent.</summary>
            public uint Locale;
        }

        /// <summary>
        /// The copy half's read: the text (unread above <paramref name="maxChars"/>), the editors'
        /// line-copy markers and the text's locale, all in the same open - one extra
        /// <c>IsClipboardFormatAvailable</c> per marker, and the markers describe exactly this text.
        /// </summary>
        public static bool TryReadCapture(int maxChars, out CaptureRead read)
        {
            read = default;
            read.Text = string.Empty;
            if (!OpenWithRetry())
                return false;
            try
            {
                string? text = ReadOpenText(maxChars, out read.TooLarge);
                if (text == null)
                    return false;
                read.Text = text;
                read.LineCopy = LineCopyMarkers.IsLineCopy(Reader);
                read.Locale = ReadDword(CF_LOCALE) ?? 0;
                return true;
            }
            finally { CloseClipboard(); }
        }

        /// <summary>
        /// A "do not record" DWORD marker on the already open clipboard: null only when absent. One that is
        /// present but unreadable - delay-rendered, shorter than a DWORD - reads as 0, "do not record", the
        /// rule the flip's backup already applies to the same marker (S0031 CH2-3).
        /// </summary>
        private static uint? ReadMarkerDword(uint format) =>
            ClipboardPrivacy.MarkerValue(format != 0 && IsClipboardFormatAvailable(format), ReadDword(format));
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

        /// <summary>
        /// CF_UNICODETEXT from the already open clipboard; empty when absent, null when unreadable.
        /// Text longer than <paramref name="maxChars"/> is not marshalled at all - the allocation's
        /// size says so up front - and comes back empty with <paramref name="tooLarge"/> set.
        /// </summary>
        private static string? ReadOpenText(int maxChars, out bool tooLarge)
        {
            tooLarge = false;
            IntPtr handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == IntPtr.Zero)
                return string.Empty;
            ulong sizeChars = GlobalSize(handle).ToUInt64() / sizeof(char);
            // The allocation holds the text and its terminating NUL.
            if (sizeChars > (ulong)maxChars + 1)
            {
                tooLarge = true;
                return string.Empty;
            }
            IntPtr ptr = GlobalLock(handle);
            if (ptr == IntPtr.Zero)
                return null;
            try
            {
                // Bound the read by the allocated size rather than trusting null-termination,
                // then trim at the first null (the rest is allocation padding).
                int chars = (int)sizeChars;
                if (chars <= 0)
                    return string.Empty;
                string raw = Marshal.PtrToStringUni(ptr, chars) ?? string.Empty;
                int nul = raw.IndexOf('\0');
                return nul >= 0 ? raw.Substring(0, nul) : raw;
            }
            finally { GlobalUnlock(handle); }
        }

        /// <summary>Replace clipboard contents with <paramref name="text"/> (empty clears it).</summary>
        public static bool TrySetText(string text) => TrySetText(text, 0, TransientMarks.None);

        /// <summary>
        /// Replace clipboard contents with <paramref name="text"/> and, when known, the locale it is
        /// written in (<c>CF_LOCALE</c>, FP-8).
        /// </summary>
        /// <param name="marks">
        /// How text that is only on the clipboard for the length of a paste - and is then replaced by
        /// the user's own content again - is marked "do not record" (<see cref="TransientMarks"/>).
        /// </param>
        public static bool TrySetText(string text, uint locale, TransientMarks marks)
        {
            if (!OpenWithRetry())
                return false;
            try
            {
                if (!EmptyClipboard())
                    return false;
                if (text.Length == 0)
                    return true;

                if (!SetBytes(CF_UNICODETEXT, UnicodeBytes(text)))
                    return false;
                SetCompanions(locale, marks);
                return true;
            }
            finally { CloseClipboard(); }
        }

        /// <summary>CF_UNICODETEXT's block: the text in UTF-16 with its terminating NUL.</summary>
        internal static byte[] UnicodeBytes(string text)
        {
            var bytes = new byte[(text.Length + 1) * sizeof(char)];
            Encoding.Unicode.GetBytes(text, 0, text.Length, bytes, 0);
            return bytes;
        }

        /// <summary>A DWORD as the clipboard stores it (little-endian, four bytes).</summary>
        internal static byte[] DwordBytes(uint value) => BitConverter.GetBytes(value);

        /// <summary>The markers <paramref name="marks"/> stands for, as clipboard payloads.</summary>
        internal static IEnumerable<KeyValuePair<uint, byte[]>> TransientMarkers(TransientMarks marks)
        {
            if (marks == TransientMarks.All && ClipboardFormats.ExcludeFromMonitor != 0)
                yield return new KeyValuePair<uint, byte[]>(ClipboardFormats.ExcludeFromMonitor, DwordBytes(0));
            if (marks == TransientMarks.None) yield break;
            if (ClipboardFormats.CanIncludeInHistory != 0)
                yield return new KeyValuePair<uint, byte[]>(ClipboardFormats.CanIncludeInHistory, DwordBytes(0));
            if (ClipboardFormats.CanUploadToCloud != 0)
                yield return new KeyValuePair<uint, byte[]>(ClipboardFormats.CanUploadToCloud, DwordBytes(0));
        }

        /// <summary>CF_LOCALE and the transient markers, on the already open and emptied clipboard.</summary>
        internal static void SetCompanions(uint locale, TransientMarks marks)
        {
            if (locale != 0)
                SetBytes(CF_LOCALE, DwordBytes(locale));
            foreach (KeyValuePair<uint, byte[]> marker in TransientMarkers(marks))
                SetBytes(marker.Key, marker.Value);
        }

        /// <summary>
        /// A raw clipboard format as bytes. Returns false when the clipboard could not be opened;
        /// <paramref name="data"/> is null when the format is absent.
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
                if (!IsClipboardFormatAvailable(format))
                    return true; // the format is simply not on the clipboard
                ReadOpenBytes(format, maxBytes, out data);
                return true;
            }
            finally { CloseClipboard(); }
        }

        /// <summary>One format off the already open clipboard.</summary>
        private static ClipboardRead ReadOpenBytes(uint format, int maxBytes, out byte[]? data)
        {
            data = null;
            IntPtr handle = GetClipboardData(format);
            if (handle == IntPtr.Zero)
                return ClipboardRead.Failed; // announced, yet the owner could not (or would not) render it

            ulong size = GlobalSize(handle).ToUInt64();
            if (size == 0)
                return ClipboardRead.Empty;
            if (size > (ulong)maxBytes)
                return ClipboardRead.TooLarge;

            IntPtr ptr = GlobalLock(handle);
            if (ptr == IntPtr.Zero)
                return ClipboardRead.Failed;
            try
            {
                var bytes = new byte[size];
                Marshal.Copy(ptr, bytes, 0, bytes.Length);
                data = bytes;
                return ClipboardRead.Ok;
            }
            finally { GlobalUnlock(handle); }
        }

        /// <summary>
        /// Put raw bytes on the already open clipboard under <paramref name="format"/>. Used to
        /// restore what a backup took, inside the single open/empty/refill sequence of
        /// <see cref="RestoreIf"/>, and by the plain text writes.
        /// </summary>
        private static bool SetBytes(uint format, byte[] data)
        {
            IntPtr hMem = AllocBytes(data);
            if (hMem == IntPtr.Zero)
                return false;
            if (SetClipboardData(format, hMem) == IntPtr.Zero)
            {
                GlobalFree(hMem); // ownership not transferred on failure
                return false;
            }
            return true; // the system owns hMem now
        }

        /// <summary>A movable global block holding <paramref name="data"/>, or zero; the caller owns it.</summary>
        internal static IntPtr AllocBytes(byte[] data)
        {
            IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)(uint)Math.Max(data.Length, 1));
            if (hMem == IntPtr.Zero)
                return IntPtr.Zero;

            IntPtr dst = GlobalLock(hMem);
            if (dst == IntPtr.Zero)
            {
                GlobalFree(hMem);
                return IntPtr.Zero;
            }
            try { if (data.Length > 0) Marshal.Copy(data, 0, dst, data.Length); }
            finally { GlobalUnlock(hMem); }
            return hMem;
        }

        /// <summary>Frees a block the clipboard did not take.</summary>
        internal static void FreeBytes(IntPtr hMem)
        {
            if (hMem != IntPtr.Zero) GlobalFree(hMem);
        }

        /// <summary>
        /// Replace the clipboard with several formats at once - one open, one <c>EmptyClipboard</c>,
        /// then every payload. Restoring format by format would not work: each call empties the
        /// clipboard again and would throw away the format restored just before it.
        /// </summary>
        public static bool Restore(IEnumerable<KeyValuePair<uint, byte[]>> payloads)
            => RestoreIf(_ => true, payloads);

        /// <summary>
        /// <see cref="Restore"/>, decided <b>inside</b> the open: <paramref name="shouldRestore"/> is
        /// handed the sequence number while nobody else can change it, so "has anyone written since?"
        /// and the write itself cannot be split by another program's copy (FP-1, FP-3).
        /// Returns false when nothing was written.
        /// </summary>
        public static bool RestoreIf(Func<uint, bool> shouldRestore, IEnumerable<KeyValuePair<uint, byte[]>> payloads)
        {
            if (!OpenWithRetry())
                return false;
            try
            {
                if (!shouldRestore(GetClipboardSequenceNumber()))
                    return false;
                if (!EmptyClipboard())
                    return false;
                bool all = true;
                foreach (KeyValuePair<uint, byte[]> payload in payloads)
                    if (payload.Value != null)
                        all &= SetBytes(payload.Key, payload.Value);
                return all;
            }
            finally { CloseClipboard(); }
        }

        /// <summary>
        /// Replace the clipboard with one image as <c>PNG</c> and <c>CF_DIB</c> of the same pixels -
        /// one open, one empty, both formats (S0026 5.1). Writing them in two passes cannot work: each
        /// pass empties the clipboard again. True only when both formats landed.
        /// </summary>
        public static bool TrySetImage(byte[] png, byte[] dib)
        {
            var payloads = new List<KeyValuePair<uint, byte[]>>(2);
            if (ClipboardFormats.Png != 0) payloads.Add(new KeyValuePair<uint, byte[]>(ClipboardFormats.Png, png));
            payloads.Add(new KeyValuePair<uint, byte[]>(CF_DIB, dib));
            return Restore(payloads) && ClipboardFormats.Png != 0;
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

        private sealed class Win32Reader : IClipboardReader
        {
            public int CountFormats() => CountClipboardFormats();
            public bool Open() => OpenWithRetry();
            public void Close() => CloseClipboard();
            public uint Sequence() => GetClipboardSequenceNumber();
            public bool IsAvailable(uint format) => format != 0 && IsClipboardFormatAvailable(format);
            public ClipboardRead Read(uint format, int maxBytes, out byte[]? data) => ReadOpenBytes(format, maxBytes, out data);
        }
    }
}

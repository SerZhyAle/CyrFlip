using System;
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

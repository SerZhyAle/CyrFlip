using System;
using System.Runtime.InteropServices;

namespace CyrFlip
{
    /// <summary>
    /// Central home for all Win32 P/Invoke declarations and interop structs.
    /// Keep signatures here rather than scattering [DllImport] across modules.
    /// </summary>
    internal static class WindowInterop
    {
        // ---- Low-level keyboard hook (KeyboardHook.cs) ----
        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;

        // KBDLLHOOKSTRUCT.flags bit: event was injected by SendInput/keybd_event.
        public const uint LLKHF_INJECTED = 0x10;

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        // CapsLock state + toggling. GetKeyState's low bit is the toggle flag; the CyrFlip UI
        // thread runs a global LL keyboard hook (pumping system-wide key input) so its key-state
        // table stays current. VK_CAPITAL is also sent (down+up) to toggle CapsLock after a flip.
        public const int VK_CAPITAL = 0x14;

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int nVirtKey);

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string? lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // ---- Low-level mouse hook (MouseHook.cs) ----
        // Installed only while the text context menu is enabled: this hook sees every mouse move
        // (up to 1000/s on a gaming mouse) and a GC pause on its thread stalls the pointer
        // system-wide, so a disabled feature must not pay - or make anyone else pay - for it.
        public const int WH_MOUSE_LL = 14;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_RBUTTONUP = 0x0205;
        public const int WM_RBUTTONDBLCLK = 0x0206;
        public const int WM_MBUTTONDOWN = 0x0207;
        public const int WM_MBUTTONUP = 0x0208;
        public const int WM_MBUTTONDBLCLK = 0x0209;

        // MSLLHOOKSTRUCT.flags bit: event was injected (our own SendInput, or another tool's).
        public const uint LLMHF_INJECTED = 0x00000001;

        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // ---- Selection probe (SelectionProbe.cs) ----
        // EM_GETSEL with both out-pointers null returns the range packed into the result, so it is
        // safe to send across processes. Sent with a timeout: a hung app must not hang the probe.
        public const uint EM_GETSEL = 0x00B0;
        public const uint SMTO_ABORTIFHUNG = 0x0002;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
            uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        // ---- Active window + layout (CursorIndicator.cs / ClipboardHandler.cs) ----
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        // Which window is under a screen point - used to tell "the user clicked our own menu" from
        // "the user clicked away", without trusting anyone's idea of a drop-down's bounds.
        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT Point);

        // ---- Process identity of the foreground window (RemoteDesktop.cs) ----
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, System.Text.StringBuilder lpExeName, ref uint lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        // ---- IAccessible2 caret (Ia2Caret.cs) - the caret API screen readers use; the only
        //      source that locates the caret in Chromium/Electron inputs (VS Code chat, browsers). ----
        public const uint OBJID_CLIENT = 0xFFFFFFFC;

        [DllImport("oleacc.dll")]
        public static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint id, ref Guid riid, out IntPtr ppvObject);

        // ---- Window identity (CaretDiagnostics.cs) ----
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern IntPtr GetKeyboardLayout(uint idThread);

        // ---- Input-language switching (LayoutSwitcher.cs) ----
        public const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;

        [DllImport("user32.dll")]
        public static extern uint GetKeyboardLayoutList(int nBuff, [Out] IntPtr[]? lpList);

        // ---- Installing / removing keyboard layouts (InputLayouts.cs) ----
        // These are the documented APIs for loading and unloading a keyboard layout at runtime; the
        // persisted list lives in the registry (see InputLayouts). KLID is an 8-hex-digit string.
        public const uint KLF_ACTIVATE = 0x00000001;
        public const uint KLF_SUBSTITUTE_OK = 0x00000002;
        public const uint KLF_REORDER = 0x00000008;
        public const uint KLF_SETFORPROCESS = 0x00000100;
        public const uint KLF_NOTELLSHELL = 0x00000080;
        public const uint MAPVK_VK_TO_VSC = 0;
        public const uint MAPVK_VSC_TO_VK = 1;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint Flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnloadKeyboardLayout(IntPtr hkl);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, uint Flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern short VkKeyScanEx(char ch, IntPtr dwhkl);

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
            System.Text.StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);

        // Resolves an "@file.dll,-123" indirect string to the localized display name of a layout.
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        public static extern int SHLoadIndirectString(string pszSource, System.Text.StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);

        // LANGID -> BCP-47 tag ("ru-RU", "uk-UA"), used to name the modern per-language profile subkey.
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern int LCIDToLocaleName(uint Locale, System.Text.StringBuilder? lpName, int cchName, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        // ---- Synthesized input for copy/paste (ClipboardHandler.cs) ----
        public const uint CF_UNICODETEXT = 13;

        // The other two formats a flip has to hand back untouched (ClipboardHandler.BackupClipboard).
        // CF_DIB covers images: Windows synthesizes CF_BITMAP and CF_DIBV5 from it, so restoring the
        // DIB restores a picture every app can paste again. CF_HDROP is a copied file selection - a
        // self-contained DROPFILES block, so a byte copy of it round-trips verbatim.
        public const uint CF_DIB = 8;
        public const uint CF_HDROP = 15;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern uint GetClipboardSequenceNumber();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_KEYUP = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        // The union must include the largest member (MOUSEINPUT) so Marshal.SizeOf(INPUT)
        // matches the real struct size on x64 - otherwise SendInput's cbSize check fails.
        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        // ---- Custom cursor (LayoutCursor.cs / CursorIndicator.cs) ----
        public const uint OCR_NORMAL = 32512; // arrow
        public const uint OCR_IBEAM = 32513;  // text "I-beam" - the cursor shown while writing

        public const uint SPI_SETCURSORS = 0x0057;
        public const uint SPIF_SENDCHANGE = 0x02;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CreateIconIndirect(ref ICONINFO iconInfo);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetSystemCursor(IntPtr hcur, uint id);

        // Restores all system cursors to their defaults (used to undo SetSystemCursor).
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyCursor(IntPtr hCursor);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        public struct ICONINFO
        {
            public bool fIcon;       // false => cursor
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        // ---- Caret tracking (CaretOverlay.cs) ----
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct GUITHREADINFO
        {
            public int cbSize;
            public uint flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        // ---- Overlay window placement (CaretOverlay.cs) ----
        public const int WS_EX_TRANSPARENT = 0x20;   // click-through
        public const int WS_EX_TOOLWINDOW = 0x80;    // no taskbar/alt-tab entry
        public const int WS_EX_LAYERED = 0x80000;
        public const int WS_EX_NOACTIVATE = 0x8000000;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint SWP_HIDEWINDOW = 0x0080;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        // ---- Taskbar anchor window (LauncherTaskbarWindow.cs) ----
        // A drop-down only closes on the first outside click when its owner is the foreground
        // window - the same rule tray menus have always had.
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        // ---- Bringing a window up from a surface that never had the focus (ForegroundActivator.cs) ----
        // Windows refuses SetForegroundWindow to a process that is neither the foreground one nor the
        // receiver of the last input event, and refuses it silently. Sharing the foreground thread's
        // input queue for the duration of the call makes the two count as one for that rule.
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        // ---- Cursor-refresh nudge (LayoutCursor.cs) ----
        public const uint INPUT_MOUSE = 0;
        public const uint MOUSEEVENTF_MOVE = 0x0001;

        // ---- Keep-awake: don't sleep / don't blank the screen (KeepAwake.cs) ----
        // One documented call, no registry, no admin rights. ES_CONTINUOUS makes the request
        // "sticky" (one call per change, no polling loop); the request is bound to the calling
        // thread, so it must be driven from a long-lived thread (the tray UI thread) - which also
        // means a hard TerminateProcess clears it for free.
        [Flags]
        public enum EXECUTION_STATE : uint
        {
            ES_CONTINUOUS = 0x80000000,       // keep the request in effect until the next call
            ES_SYSTEM_REQUIRED = 0x00000001,  // don't let the system sleep
            ES_DISPLAY_REQUIRED = 0x00000002, // don't let the display turn off (video-player mode)
        }

        [DllImport("kernel32.dll")]
        public static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

        // ---- MSIX package identity (PackageInfo.cs) ----
        // Returned by GetCurrentPackageFullName when the process has no package identity
        // (i.e. a plain unpackaged exe). Any other return value => running inside an MSIX package.
        public const int APPMODEL_ERROR_NO_PACKAGE = 15700;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetCurrentPackageFullName(ref int packageFullNameLength, System.Text.StringBuilder? packageFullName);

        // The family name ("SZA.CyrFlip_fdk7e19xt9z9j") is the key Windows files the package's own
        // state under - including the startupTask state Autostart reads.
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetCurrentPackageFamilyName(ref int packageFamilyNameLength, System.Text.StringBuilder? packageFamilyName);

        public const int ERROR_INSUFFICIENT_BUFFER = 122;

        // ---- Simple MAPI: hand the log archive to the user's mail client (MailSender.cs) ----
        // Why MAPI at all: mailto: cannot carry an attachment - RFC 2368 has no such field and the
        // non-standard attach= is deliberately ignored by every modern client (it was a hole). Simple
        // MAPI's MAPISendMail with MAPI_DIALOG opens a compose window in the registered default mail
        // client with the file already attached, which is exactly the requested behaviour.
        //
        // Two traps, both handled in MailSender:
        //   - ANSI only: every string below is LPStr, so a non-ASCII path (a Cyrillic Windows account
        //     name) has to be passed as its 8.3 short form via GetShortPathName;
        //   - bitness: mapi32.dll in System32 is a stub forwarding into the registered client's DLL,
        //     so a 32-bit Outlook answers our 64-bit process with MAPI_E_FAILURE. That is not fixable
        //     - it is precisely why the mailto: fallback exists.
        public const uint MAPI_SUCCESS_SUCCESS = 0;
        public const uint MAPI_USER_ABORT = 1;      // user closed the compose window - the scenario succeeded
        public const uint MAPI_E_FAILURE = 2;
        public const uint MAPI_LOGON_UI = 0x00000001;
        public const uint MAPI_DIALOG = 0x00000008; // show the compose window instead of sending silently
        public const uint MAPI_TO = 1;              // MapiRecipDesc.ulRecipClass

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct MapiMessage
        {
            public uint ulReserved;
            [MarshalAs(UnmanagedType.LPStr)] public string? lpszSubject;
            [MarshalAs(UnmanagedType.LPStr)] public string? lpszNoteText;
            [MarshalAs(UnmanagedType.LPStr)] public string? lpszMessageType;
            [MarshalAs(UnmanagedType.LPStr)] public string? lpszDateReceived;
            [MarshalAs(UnmanagedType.LPStr)] public string? lpszConversationID;
            public uint flFlags;
            public IntPtr lpOriginator;
            public uint nRecipCount;
            public IntPtr lpRecips;
            public uint nFileCount;
            public IntPtr lpFiles;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct MapiRecipDesc
        {
            public uint ulReserved;
            public uint ulRecipClass;
            [MarshalAs(UnmanagedType.LPStr)] public string? lpszName;
            [MarshalAs(UnmanagedType.LPStr)] public string? lpszAddress;
            public uint ulEIDSize;
            public IntPtr lpEntryID;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct MapiFileDesc
        {
            public uint ulReserved;
            public uint flFlags;
            public uint nPosition;   // 0xFFFFFFFF = append at the end of the note text
            [MarshalAs(UnmanagedType.LPStr)] public string? lpszPathName;
            [MarshalAs(UnmanagedType.LPStr)] public string? lpszFileName;
            public IntPtr lpFileType;
        }

        [DllImport("mapi32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        public static extern uint MAPISendMail(IntPtr lhSession, IntPtr ulUIParam,
            ref MapiMessage lpMessage, uint flFlags, uint ulReserved);

        // The 8.3 form of a path, so an ANSI MAPI call survives a non-ASCII account name. Returns 0
        // on failure, and a volume with 8.3 names disabled legitimately fails here.
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint GetShortPathName(string lpszLongPath, System.Text.StringBuilder? lpszShortPath, uint cchBuffer);
    }
}

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
        // KBDLLHOOKSTRUCT.flags bit: the key carries the E0 prefix (right Ctrl/Alt, the Win keys, the
        // navigation cluster). Also how a generic injected VK_CONTROL/VK_MENU names its side.
        public const uint LLKHF_EXTENDED = 0x01;

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        public const uint SPI_GETKEYBOARDDELAY = 0x0016;
        public const uint SPI_GETHIGHCONTRAST = 0x0042;
        public const uint HCF_HIGHCONTRASTON = 0x0001;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct HIGHCONTRAST
        {
            public uint cbSize;
            public uint dwFlags;
            public IntPtr lpszDefaultScheme;
        }

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref HIGHCONTRAST pvParam, uint fWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref int pvParam, uint fWinIni);

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

        // WM_GETTEXT / WM_GETTEXTLENGTH are among the handful of messages USER32 marshals across
        // process boundaries itself, so the buffer below may be ours even though the edit control
        // belongs to another application. That is what lets the probe read the selected text out of
        // a classic control without synthesizing Ctrl+C.
        public const uint WM_GETTEXT = 0x000D;
        public const uint WM_GETTEXTLENGTH = 0x000E;

        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SendMessageTimeoutText(IntPtr hWnd, uint Msg, IntPtr wParam,
            System.Text.StringBuilder lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
            uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        /// <summary>
        /// A message whose lParam is a string we send. Used for <c>EM_SETCUEBANNER</c>, the
        /// placeholder text a Win32 edit control has understood since Vista and which net48's
        /// <c>TextBox</c> does not expose (<c>PlaceholderText</c> is .NET Core and later).
        /// </summary>
        [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessageString(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);

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

        // The accessible element under a screen point - how the selection probe asks about the text
        // the user actually right-clicked on, rather than about whatever happens to hold the focus.
        [DllImport("oleacc.dll")]
        public static extern int AccessibleObjectFromPoint(POINT ptScreen, out IntPtr ppacc,
            [MarshalAs(UnmanagedType.Struct)] out object pvarChild);

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

        // ---- The documented input-profile API (input.dll, InputLayoutApi.cs; ticket S0007 WL-1 phase B) ----
        // Not declared in a public header; the entry points and LAYOUTORTIPPROFILE (584 bytes on x64)
        // were checked live on 2026-09-29 - EnumEnabledLayoutOrTip returned "0409:00000409" with
        // LOT_DEFAULT set on the default entry.
        public const uint LOTP_INPUTPROCESSOR = 1;
        public const uint LOTP_KEYBOARDLAYOUT = 2;
        public const uint LOT_DEFAULT = 0x1;
        public const uint LOT_DISABLED = 0x2;
        public const uint ILOT_UNINSTALL = 0x1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct LAYOUTORTIPPROFILE
        {
            public uint dwProfileType;
            public ushort langid;
            public Guid clsid;
            public Guid guidProfile;
            public Guid catid;
            public uint dwSubstituteLayout;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szId;
        }

        [DllImport("input.dll", CharSet = CharSet.Unicode)]
        public static extern uint EnumEnabledLayoutOrTip(string? pszUserReg, string? pszSystemReg, string? pszSoftwareReg,
            [Out] LAYOUTORTIPPROFILE[]? pLayoutOrTip, uint uBufLength);

        [DllImport("input.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool InstallLayoutOrTip(string psz, uint dwFlags);

        [DllImport("input.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetDefaultLayoutOrTip(string psz, uint dwFlags);

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

        // The LCID Windows uses to synthesize CF_TEXT/CF_OEMTEXT from CF_UNICODETEXT. Without it the
        // system takes the input language of whichever thread wrote the clipboard - the clipboard
        // worker's, i.e. usually English - and converted Cyrillic reaches an ANSI-only app as "?".
        public const uint CF_LOCALE = 16;

        [DllImport("user32.dll")]
        public static extern int CountClipboardFormats();

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
        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint uCode, uint uMapType);

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
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        public const uint SWP_NOSIZE = 0x0001;

        /// <summary>True when the window's thread has not answered for about 5 s - asked without waiting on it.</summary>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsHungAppWindow(IntPtr hwnd);
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

        // ---- "Open the selection" (ticket S0008) ----------------------------------------------

        // The type of the volume behind "X:\". Answers from the local mount table - a mapped network
        // drive is DRIVE_REMOTE without its server being contacted, which is the whole point.
        public const uint DRIVE_REMOTE = 4;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetDriveTypeW")]
        public static extern uint GetDriveType(string lpRootPathName);

        // The list Windows itself consults before its "open file - security warning": true for an
        // extension (".exe", ".chm", ".appref-ms"..) whose association runs code.
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AssocIsDangerous(string pszAssoc);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WIN32_FIND_DATA
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
        }

        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        // The on-disk name of the last path segment - what an 8.3 alias or a name with trailing dots
        // actually opens.
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindFirstFileW", SetLastError = true)]
        public static extern IntPtr FindFirstFile(string lpFileName, out WIN32_FIND_DATA lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool FindClose(IntPtr hFindFile);

        // ---- Launcher pipe peer check (ticket S0008 LS-2) -------------------------------------

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint serverProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ProcessIdToSessionId(uint dwProcessId, out uint pSessionId);

        public const uint TOKEN_QUERY = 0x0008;

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        // ---- Layout indicator (ticket S0011) --------------------------------------------------
        // DPI contexts (LI-2): a DPI-unaware or system-aware window reports its caret in logical
        // coordinates, which have to be converted before a per-monitor-aware process can use them.
        // All Windows 10 1607+, which the manifest already requires.

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

        /// <summary>DPI_AWARENESS: -1 invalid, 0 unaware, 1 system aware, 2 per-monitor aware.</summary>
        [DllImport("user32.dll")]
        public static extern int GetAwarenessFromDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool LogicalToPhysicalPointForPerMonitorDPI(IntPtr hwnd, ref POINT lpPoint);

        // Monitor DPI (LI-3): the marker is drawn in physical pixels, so it is scaled by the DPI of the
        // monitor it is shown on.
        public const uint MONITOR_DEFAULTTONEAREST = 2;
        public const uint MONITOR_DEFAULTTOPRIMARY = 1;
        public const int MDT_EFFECTIVE_DPI = 0;

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("shcore.dll")]
        public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        public const int SM_CYCURSOR = 14;

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        // Foreground and focus changes (LI-1, LI-11), delivered out of context: no DLL is injected
        // anywhere, the callback runs on the thread that installed the hook.
        public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        public const uint EVENT_OBJECT_FOCUS = 0x8005;
        public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint idEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        // UWP frame windows (LI-4): the input goes to a CoreWindow child of another thread.
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

        // ---- Theme (ThemeWin32.cs, ticket S0020) ----
        // Every one of these is guarded by its caller: a refusal leaves the light look, never an exception.

        /// <summary>Windows 11 and Windows 10 20H1+; Windows 10 1809-1909 answered to 19 (undocumented).</summary>
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>
        /// A null sub-app name puts the control back on its default theme; <c>"DarkMode_Explorer"</c>,
        /// <c>"DarkMode_CFD"</c> and <c>"DarkMode_ItemsView"</c> are the undocumented dark variants
        /// Explorer itself uses (dark scroll bars, dark edit and combo borders, a dark list header).
        /// </summary>
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

        /// <summary>
        /// uxtheme ordinal 135 (Windows 10 1903+): how the process's <b>native</b> popup menus are drawn -
        /// a text box's Cut/Copy/Paste menu and a window's system menu, which no ToolStrip renderer sees.
        /// 0 = default, 2 = force dark, 3 = force light. Undocumented; called only on a build that has it.
        /// </summary>
        [DllImport("uxtheme.dll", EntryPoint = "#135")]
        public static extern int SetPreferredAppMode(int mode);

        /// <summary>uxtheme ordinal 136: drop the cached menu theme so the next menu opens in the new mode.</summary>
        [DllImport("uxtheme.dll", EntryPoint = "#136")]
        public static extern void FlushMenuThemes();

        public const uint LVM_GETHEADER = 0x101F;

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct COMBOBOXINFO
        {
            public int cbSize;
            public RECT rcItem;
            public RECT rcButton;
            public int stateButton;
            public IntPtr hwndCombo;
            public IntPtr hwndItem;
            public IntPtr hwndList;
        }

        /// <summary>The combo box's own drop-down list window, which a theme set on the combo never reaches.</summary>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetComboBoxInfo(IntPtr hwndCombo, ref COMBOBOXINFO info);

        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_FRAMECHANGED = 0x0020;

        public const int WM_PAINT = 0x000F;
        public const int WM_NCPAINT = 0x0085;
        public const uint LVM_GETHOTITEM = 0x103D;
        public const uint LVM_GETTOOLTIPS = 0x104E;

        /// <summary>The whole window's DC, frame included - where a dark theme's light edit border is painted over.</summary>
        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        /// <summary>The extended style - read to tell a mirrored (right-to-left layout) window apart.</summary>
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        public static extern int GetWindowLong(IntPtr hWnd, int index);

        // ---- Screen region capture (S0026) ----
        public const uint SRCCOPY = 0x00CC0020;
        /// <summary>Without it layered windows (tooltips, many modern menus) are missing from a BitBlt.</summary>
        public const uint CAPTUREBLT = 0x40000000;

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height,
            IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

        /// <summary>Windows 10 2004+: the window is left out of every screen capture and screen share.</summary>
        public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT point);

        [DllImport("shell32.dll")]
        public static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags,
            IntPtr token, out IntPtr path);

        /// <summary>Moves a file; without MOVEFILE_REPLACE_EXISTING an existing target is never replaced.</summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MoveFileEx(string existing, string target, uint flags);

        public const uint MOVEFILE_WRITE_THROUGH = 0x8;
    }
}

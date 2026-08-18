# CyrFlip.UiTest - helpers for the manual/interop checks that xUnit cannot cover
# (tray mouse behaviour, the live settings window, layout switching in a real window).
#
# Import-Module .\tools\uitest\CyrFlip.UiTest.psm1 -Force
#
# Load-bearing details, learned the hard way - do not "simplify" them away:
#   * PowerShell is DPI-unaware by default, so UI Automation reports *physical* pixels while
#     SetCursorPos takes *virtualized* ones. On a scaled display the click then lands somewhere
#     else entirely. Enable-UiTestDpi (called automatically by every function here) fixes it.
#   * The tray icon is found through **MSAA (oleacc)**, not UI Automation: the notification area
#     is a legacy ToolbarWindow32 whose buttons the UIA client bridges only under Windows
#     PowerShell 5.1 - under pwsh 7 the pane comes back childless. MSAA works in both.
#     Its accName carries the whole tooltip ("CyrFlip\nEN ..."), so matching is a -like.
#   * A tray click focuses the taskbar, which is exactly why CyrFlip acts on LastActiveWindow -
#     so the test needs a separate "window the user was typing in" (Start-TargetWindow).
#   * PrintWindow needs PW_RENDERFULLCONTENT (2) to capture WinForms/DWM-composed windows.

$script:NativeReady = $false

function Initialize-UiTestNative {
    if ($script:NativeReady) { return }
    if (-not ('CyrFlipUi' -as [type])) {
        Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

// Only the slots up to accLocation are declared, and the unused ones are stubbed with the right
// arity - the vtable order is load-bearing exactly as in src/CyrFlip/Ia2Caret.cs.
[ComImport, Guid("618736e0-3c3d-11cf-810c-00aa00389b71"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAccessible {
    [PreserveSig] int _GetTypeInfoCount();                                                     // IDispatch 1
    [PreserveSig] int _GetTypeInfo();                                                          // IDispatch 2
    [PreserveSig] int _GetIDsOfNames();                                                        // IDispatch 3
    [PreserveSig] int _Invoke();                                                               // IDispatch 4
    [PreserveSig] int _get_accParent(out IntPtr parent);                                       // 5
    [PreserveSig] int _get_accChildCount(out int count);                                       // 6
    [PreserveSig] int _get_accChild([MarshalAs(UnmanagedType.Struct)] object child, out IntPtr disp); // 7
    [PreserveSig] int get_accName([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.BStr)] out string name); // 8
    [PreserveSig] int _get_accValue([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.BStr)] out string value); // 9
    [PreserveSig] int _get_accDescription([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.BStr)] out string desc); // 10
    [PreserveSig] int _get_accRole([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.Struct)] out object role); // 11
    [PreserveSig] int _get_accState([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.Struct)] out object state); // 12
    [PreserveSig] int _get_accHelp([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.BStr)] out string help); // 13
    [PreserveSig] int _get_accHelpTopic([MarshalAs(UnmanagedType.BStr)] out string file, [MarshalAs(UnmanagedType.Struct)] object child, out int topic); // 14
    [PreserveSig] int _get_accKeyboardShortcut([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.BStr)] out string shortcut); // 15
    [PreserveSig] int _get_accFocus([MarshalAs(UnmanagedType.Struct)] out object child);       // 16
    [PreserveSig] int _get_accSelection([MarshalAs(UnmanagedType.Struct)] out object child);   // 17
    [PreserveSig] int _get_accDefaultAction([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.BStr)] out string action); // 18
    [PreserveSig] int _accSelect(int flags, [MarshalAs(UnmanagedType.Struct)] object child);   // 19
    [PreserveSig] int accLocation(out int left, out int top, out int width, out int height, [MarshalAs(UnmanagedType.Struct)] object child); // 20
}

public static class CyrFlipUi {
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint tid);
    [DllImport("user32.dll")] public static extern uint GetKeyboardLayoutList(int n, [Out] IntPtr[] list);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr extra);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
    [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("oleacc.dll")] public static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint objId, ref Guid iid, out IntPtr ppv);
    [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr process, uint flags);
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr PostMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);

    // GDI and USER handle counts of another process - the two that actually leak in a WinForms tray
    // app (icons, bitmaps, fonts, windows). Private Bytes is read from the Process object instead;
    // these two have no managed equivalent. Returns "gdi user" or "" when the process is gone.
    public static string GuiResources(uint pid) {
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return "";
        try { return GetGuiResources(h, 0) + " " + GetGuiResources(h, 1); }   // 0 = GDI, 1 = USER
        finally { CloseHandle(h); }
    }

    // CyrFlip decides whether to send a CapsLock keystroke by reading the key's current lock state
    // from its clipboard worker - a thread that has no message queue and never pumps one. GetKeyState
    // is documented as answering per-thread, so this asks the question from exactly such a thread.
    // It matters because a lock state cannot be set directly, only toggled: a reading that came back
    // stale would leave the key inverted rather than merely unchanged.
    public static bool CapsLockOffThread() {
        bool result = false;
        var t = new System.Threading.Thread(delegate() { result = (GetKeyState(0x14) & 1) != 0; });
        t.IsBackground = true;
        t.Start();
        t.Join();
        return result;
    }

    // Ask a window's input thread to switch layout, the same way CyrFlip's own LayoutSwitcher does.
    // Posting to the target window (rather than clicking the tray) keeps a long run from churning
    // the layout of whatever the person at the machine is really typing in.
    public static bool SwitchLayout(IntPtr h, IntPtr hkl) {
        const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
        return PostMessageW(h, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl) != IntPtr.Zero;
    }

    public delegate bool EnumProc(IntPtr h, IntPtr p);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    // PerMonitorV2; falls back to system-DPI-aware on anything older than 1703.
    public static void Dpi() {
        try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch { }
        try { SetProcessDPIAware(); } catch { }
    }

    // SetForegroundWindow alone loses to Windows' foreground lock whenever another process owns
    // the foreground (a playing browser tab is enough). Tapping ALT clears the lock for this
    // thread, and attaching to the foreground thread's input queue makes the call legal.
    public static bool ForceForeground(IntPtr h) {
        if (GetForegroundWindow() == h) return true;
        const byte VK_MENU = 0x12;
        const uint KEYEVENTF_KEYUP = 2;
        keybd_event(VK_MENU, 0, 0, IntPtr.Zero);
        keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, IntPtr.Zero);

        uint pid;
        uint fgTid = GetWindowThreadProcessId(GetForegroundWindow(), out pid);
        uint myTid = GetCurrentThreadId();
        bool attached = fgTid != 0 && fgTid != myTid && AttachThreadInput(myTid, fgTid, true);
        try {
            ShowWindow(h, 9);          // SW_RESTORE
            BringWindowToTop(h);
            SetForegroundWindow(h);
        }
        finally { if (attached) AttachThreadInput(myTid, fgTid, false); }
        return GetForegroundWindow() == h;
    }

    public static string Text(IntPtr h) { var sb = new StringBuilder(512); GetWindowTextW(h, sb, sb.Capacity); return sb.ToString(); }
    public static string Cls(IntPtr h) { var sb = new StringBuilder(256); GetClassNameW(h, sb, sb.Capacity); return sb.ToString(); }

    public static IntPtr Hkl(IntPtr h) {
        uint pid; uint tid = GetWindowThreadProcessId(h, out pid);
        return tid == 0 ? IntPtr.Zero : GetKeyboardLayout(tid);
    }

    public static IntPtr[] InstalledLayouts() {
        uint c = GetKeyboardLayoutList(0, null);
        var a = new IntPtr[c];
        if (c > 0) GetKeyboardLayoutList((int)c, a);
        return a;
    }

    public static string CursorAt() {
        POINT p; GetCursorPos(out p);
        return p.X + "," + p.Y + " over " + Cls(WindowFromPoint(p));
    }

    public static void MouseDown(bool right) { mouse_event(right ? 0x0008u : 0x0002u, 0, 0, 0, IntPtr.Zero); }
    public static void MouseUp(bool right) { mouse_event(right ? 0x0010u : 0x0004u, 0, 0, 0, IntPtr.Zero); }

    public static List<IntPtr> ProcessWindows(uint pid, bool visibleOnly) {
        var res = new List<IntPtr>();
        EnumWindows((h, p) => {
            uint wp; GetWindowThreadProcessId(h, out wp);
            if (wp == pid && (!visibleOnly || IsWindowVisible(h))) res.Add(h);
            return true;
        }, IntPtr.Zero);
        return res;
    }

    // --- notification area, via MSAA -------------------------------------------------------
    // Each tray icon is a *child id* of a ToolbarWindow32's accessible object, not a window of its
    // own; accName is the tooltip and accLocation is already in physical pixels (this process is
    // DPI-aware), which is exactly what SetCursorPos wants.
    private const uint OBJID_CLIENT = 0xFFFFFFFC;

    // The toolbars that hold tray icons, paired with "is this the hidden-icons flyout".
    private static List<KeyValuePair<IntPtr, bool>> TrayToolbars() {
        var hosts = new List<KeyValuePair<IntPtr, bool>>();
        EnumWindows((h, p) => {
            string c = Cls(h);
            if (c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd")
                hosts.Add(new KeyValuePair<IntPtr, bool>(h, false));
            else if (c == "NotifyIconOverflowWindow" || c == "TopLevelWindowForOverflowXamlIsland")
                hosts.Add(new KeyValuePair<IntPtr, bool>(h, true));
            return true;
        }, IntPtr.Zero);

        var bars = new List<KeyValuePair<IntPtr, bool>>();
        foreach (var host in hosts) {
            bool overflow = host.Value;
            EnumChildWindows(host.Key, (h, p) => {
                if (Cls(h) == "ToolbarWindow32") bars.Add(new KeyValuePair<IntPtr, bool>(h, overflow));
                return true;
            }, IntPtr.Zero);
        }
        return bars;
    }

    // "name<TAB>x y w h<TAB>overflow(0/1)" per icon - a flat string list so no COM object ever
    // has to cross into PowerShell.
    public static List<string> TrayIcons() {
        var res = new List<string>();
        var iid = new Guid("618736e0-3c3d-11cf-810c-00aa00389b71");   // IID_IAccessible
        foreach (var bar in TrayToolbars()) {
            IntPtr pAcc;
            if (AccessibleObjectFromWindow(bar.Key, OBJID_CLIENT, ref iid, out pAcc) != 0 || pAcc == IntPtr.Zero) continue;
            object raw = Marshal.GetObjectForIUnknown(pAcc);
            Marshal.Release(pAcc);
            var acc = raw as IAccessible;
            if (acc == null) continue;
            try {
                int count;
                if (acc._get_accChildCount(out count) != 0) continue;
                for (int i = 1; i <= count; i++) {
                    string name;
                    if (acc.get_accName(i, out name) != 0 || string.IsNullOrEmpty(name)) continue;
                    int x, y, cw, ch;
                    if (acc.accLocation(out x, out y, out cw, out ch, i) != 0 || cw <= 0) continue;
                    res.Add(name + "\t" + x + " " + y + " " + cw + " " + ch + "\t" + (bar.Value ? "1" : "0"));
                }
            }
            finally { Marshal.ReleaseComObject(raw); }
        }
        return res;
    }

    public static IntPtr FindByTitle(string like) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, p) => {
            if (!IsWindowVisible(h)) return true;
            string t = Text(h);
            if (t.Length > 0 && t.IndexOf(like, StringComparison.OrdinalIgnoreCase) >= 0) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@
    }
    [CyrFlipUi]::Dpi()
    Add-Type -AssemblyName System.Drawing, System.Windows.Forms, UIAutomationClient, UIAutomationTypes
    $script:NativeReady = $true
}

function Enable-UiTestDpi {
    <#
    .SYNOPSIS
    Makes this PowerShell process PerMonitorV2 DPI-aware. Must run before any coordinate is read
    or any click is synthesized; every other function here calls it for you.
    #>
    Initialize-UiTestNative
}

function Get-CyrFlipExe {
    <#
    .SYNOPSIS
    Path to the built CyrFlip.exe (Release by default). Throws when it has not been built yet.
    #>
    [CmdletBinding()]
    param([ValidateSet('Release', 'Debug')][string]$Configuration = 'Release')
    $repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $exe = Join-Path $repo "src\CyrFlip\bin\$Configuration\net48\CyrFlip.exe"
    if (-not (Test-Path $exe)) { throw "Not built: $exe  (run: dotnet build CyrFlip.sln -c $Configuration)" }
    (Resolve-Path $exe).Path
}

function Start-CyrFlipApp {
    <#
    .SYNOPSIS
    Starts CyrFlip and waits until its tray icon is reachable. Returns the Process.
    .PARAMETER Fresh
    Kill any running instance first (the single-instance mutex would make a second copy exit).
    #>
    [CmdletBinding()]
    param(
        [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
        [switch]$Fresh,
        [int]$TimeoutSeconds = 20
    )
    Initialize-UiTestNative
    if ($Fresh) { Stop-CyrFlipApp }
    $p = Start-Process (Get-CyrFlipExe -Configuration $Configuration) -PassThru
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Get-TrayIcon -ErrorAction SilentlyContinue) { return $p }
        Start-Sleep -Milliseconds 400
    }
    Write-Warning "CyrFlip started (pid $($p.Id)) but its tray icon was not found - it may be in the hidden-icons flyout."
    $p
}

function Stop-CyrFlipApp {
    Get-Process CyrFlip -ErrorAction SilentlyContinue | ForEach-Object {
        $_.CloseMainWindow() | Out-Null
        Start-Sleep -Milliseconds 300
        if (-not $_.HasExited) { Stop-Process -Id $_.Id -Force }
    }
}

function Get-TrayIcons {
    <#
    .SYNOPSIS
    Every notification-area icon (taskbar + hidden-icons flyout) with its tooltip and click point.
    .DESCRIPTION
    Read through MSAA - see the note at the top of this module on why not UI Automation.
    Icons whose Overflow flag is $true sit in the flyout behind the chevron: their coordinates are
    only real while that flyout is open.
    #>
    [CmdletBinding()]
    param()
    Initialize-UiTestNative
    foreach ($line in [CyrFlipUi]::TrayIcons()) {
        $parts = $line -split "`t"
        $r = $parts[1] -split ' '
        [pscustomobject]@{
            Name     = $parts[0]
            X        = [int]$r[0] + [int]$r[2] / 2 -as [int]
            Y        = [int]$r[1] + [int]$r[3] / 2 -as [int]
            Left     = [int]$r[0]; Top = [int]$r[1]; Width = [int]$r[2]; Height = [int]$r[3]
            Overflow = $parts[2] -eq '1'
        }
    }
}

function Get-TrayIcon {
    <#
    .SYNOPSIS
    Locates one notification-area icon by tooltip and returns its rect + click point.
    .DESCRIPTION
    The accName carries the whole tooltip ("CyrFlip" + the layout lines), so matching is a -like.
    A visible taskbar icon wins over one in the hidden-icons flyout, whose coordinates only mean
    anything while the flyout is open.
    #>
    [CmdletBinding()]
    param([string]$NameLike = '*CyrFlip*')
    $all = @(Get-TrayIcons | Where-Object { $_.Name -like $NameLike })
    if ($all.Count -eq 0) {
        Write-Error "No tray icon matching '$NameLike' - is the app running?"
        return
    }
    $pick = ($all | Where-Object { -not $_.Overflow } | Select-Object -First 1)
    if (-not $pick) {
        $pick = $all[0]
        Write-Warning "'$($pick.Name -replace '\r?\n', ' | ')' is in the hidden-icons flyout; open the chevron first, or drag the icon onto the taskbar."
    }
    $pick
}

function Invoke-MouseClick {
    <#
    .SYNOPSIS
    Synthesizes a real mouse click at screen coordinates (not a UIA Invoke - the tray path must
    be exercised the way a user exercises it).
    .PARAMETER Count
    2 = double click; the gap stays under the system double-click time.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][int]$X,
        [Parameter(Mandatory)][int]$Y,
        [int]$Count = 1,
        [ValidateSet('Left', 'Right')][string]$Button = 'Left',
        [int]$SettleMs = 400
    )
    Initialize-UiTestNative
    $right = $Button -eq 'Right'
    [void][CyrFlipUi]::SetCursorPos($X, $Y)
    Start-Sleep -Milliseconds $SettleMs
    Write-Verbose ("cursor: " + [CyrFlipUi]::CursorAt())
    for ($i = 0; $i -lt $Count; $i++) {
        [CyrFlipUi]::MouseDown($right); Start-Sleep -Milliseconds 45
        [CyrFlipUi]::MouseUp($right)
        if ($i -lt $Count - 1) { Start-Sleep -Milliseconds 60 }
    }
}

function Invoke-TrayClick {
    <#
    .SYNOPSIS
    Clicks CyrFlip's tray icon. Single left click = switch layout of the last active window,
    double = open Settings, right = the menu.
    #>
    [CmdletBinding()]
    param(
        [string]$NameLike = '*CyrFlip*',
        [int]$Count = 1,
        [ValidateSet('Left', 'Right')][string]$Button = 'Left'
    )
    $icon = Get-TrayIcon -NameLike $NameLike
    if (-not $icon) { return }
    Write-Verbose "tray icon at $($icon.X),$($icon.Y): $($icon.Name -replace '\r?\n', ' | ')"
    Invoke-MouseClick -X $icon.X -Y $icon.Y -Count $Count -Button $Button
}

function Start-TargetWindow {
    <#
    .SYNOPSIS
    Opens a plain WinForms text box to stand in for "the app the user was typing in", activates
    it and returns @{ Process; Handle }. Needed because a tray click moves focus to the taskbar.
    #>
    [CmdletBinding()]
    param([string]$Title = 'CyrFlip layout target', [int]$TimeoutSeconds = 10)
    Initialize-UiTestNative
    $script = Join-Path $PSScriptRoot 'TargetWindow.ps1'
    $p = Start-Process powershell -PassThru -ArgumentList @(
        '-NoProfile', '-WindowStyle', 'Hidden', '-ExecutionPolicy', 'Bypass',
        '-File', $script, '-Title', $Title)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline -and $p.MainWindowHandle -eq 0) {
        Start-Sleep -Milliseconds 200; $p.Refresh()
    }
    if ($p.MainWindowHandle -eq 0) { throw "Target window did not appear within $TimeoutSeconds s" }
    $h = $p.MainWindowHandle

    # It must really reach the foreground: a test that runs against a window CyrFlip never saw as
    # "last active" checks nothing, and Windows' foreground lock makes a single activation call
    # unreliable when another process owns the foreground (e.g. the settings window is open).
    $shell = New-Object -ComObject WScript.Shell
    for ($i = 0; $i -lt 10; $i++) {
        $shell.AppActivate($p.Id) | Out-Null
        if ([CyrFlipUi]::ForceForeground($h)) { break }
        Start-Sleep -Milliseconds 300
    }
    if ([CyrFlipUi]::GetForegroundWindow() -ne $h) {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        throw "Target window never reached the foreground - close any modal dialog and retry."
    }
    Start-Sleep -Milliseconds 500
    [pscustomobject]@{ Process = $p; Handle = $h }
}

function Set-WindowForeground {
    <#
    .SYNOPSIS
    Brings a window to the foreground (through the foreground lock) and confirms it got there.
    .DESCRIPTION
    Re-assert this before every tray click: the click hands focus to the taskbar, and if anything
    else (a browser playing a video, a chat popup) takes the foreground in between, CyrFlip's
    LastActiveWindow is legitimately that other window - the test would then be watching the wrong
    one and read the miss as a bug in the app.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][IntPtr]$Handle, [int]$Attempts = 10)
    Initialize-UiTestNative
    for ($i = 0; $i -lt $Attempts; $i++) {
        if ([CyrFlipUi]::ForceForeground($Handle)) { return $true }
        Start-Sleep -Milliseconds 250
    }
    $false
}

function Get-WindowLayout {
    <#
    .SYNOPSIS
    The HKL currently active for a window's thread - the thing a layout switch must actually move.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][IntPtr]$Handle)
    Initialize-UiTestNative
    $hkl = [CyrFlipUi]::Hkl($Handle)
    [pscustomobject]@{
        Handle = $Handle
        Hkl    = $hkl
        Hex    = $hkl.ToString('X')
        Klid   = ('{0:X8}' -f ($hkl.ToInt64() -band 0xFFFF))
    }
}

function Get-InstalledLayouts {
    Initialize-UiTestNative
    [CyrFlipUi]::InstalledLayouts() | ForEach-Object {
        [pscustomobject]@{ Hkl = $_; Hex = $_.ToString('X'); Klid = ('{0:X8}' -f ($_.ToInt64() -band 0xFFFF)) }
    }
}

function Get-AppResourceUsage {
    <#
    .SYNOPSIS
    One sample of what a long run has to watch: private bytes, GDI and USER handles, threads.
    .DESCRIPTION
    GDI and USER handles are the ones that matter for a tray app that renders its own icon and
    cursor - a leak there is invisible in Task Manager's memory column and ends in the app (or the
    whole session) running out of handles. Private bytes are reported but deliberately not judged:
    the clipboard history is unbounded by design, so it is *expected* to grow.
    #>
    [CmdletBinding()]
    param([string]$ProcessName = 'CyrFlip')
    Initialize-UiTestNative
    Get-Process $ProcessName -ErrorAction SilentlyContinue | ForEach-Object {
        $counts = [CyrFlipUi]::GuiResources([uint32]$_.Id) -split ' '
        [pscustomobject]@{
            Time         = Get-Date
            ProcessId    = $_.Id
            PrivateBytes = $_.PrivateMemorySize64
            WorkingSet   = $_.WorkingSet64
            GdiObjects   = if ($counts.Count -eq 2) { [int]$counts[0] } else { -1 }
            UserObjects  = if ($counts.Count -eq 2) { [int]$counts[1] } else { -1 }
            Threads      = $_.Threads.Count
            Handles      = $_.HandleCount
        }
    }
}

function Switch-WindowLayout {
    <#
    .SYNOPSIS
    Moves a window's input thread to the next installed layout (wrapping), and returns the new HKL.
    .DESCRIPTION
    Drives the indicator the way real typing does - CursorIndicator polls the foreground window, so
    a switch here makes it re-render the tray icon, the I-beam cursor and the caret overlay, which
    is exactly the GDI path a long run needs to exercise. Posts to the window rather than clicking
    the tray, so it never touches the layout of the user's own windows.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][IntPtr]$Handle)
    Initialize-UiTestNative
    $installed = @([CyrFlipUi]::InstalledLayouts())
    if ($installed.Count -lt 2) { Write-Warning 'Only one keyboard layout is installed - nothing to switch between.'; return }
    $current = [CyrFlipUi]::Hkl($Handle)
    $index = [Array]::IndexOf($installed, $current)
    $next = if ($index -ge 0) { $installed[($index + 1) % $installed.Count] } else { $installed[0] }
    [void][CyrFlipUi]::SwitchLayout($Handle, $next)
    $next
}

function Get-CapsLockState {
    <#
    .SYNOPSIS
    Is CapsLock locked on right now? Read from this thread (-OffThread reads it from a fresh thread
    with no message queue, the way CyrFlip's clipboard worker does).
    #>
    [CmdletBinding()]
    param([switch]$OffThread)
    Initialize-UiTestNative
    if ($OffThread) { return [CyrFlipUi]::CapsLockOffThread() }
    ([CyrFlipUi]::GetKeyState(0x14) -band 1) -ne 0
}

function Set-CapsLockState {
    <#
    .SYNOPSIS
    Put CapsLock into a given state, by toggling it only when it differs - there is no API that sets
    a lock state directly, which is the whole reason CyrFlip has to read it before deciding.
    .DESCRIPTION
    The keystroke is injected, so CyrFlip's hook ignores it (LLKHF_INJECTED) - this moves the lock
    state without ever looking like a user pressing a chord.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][bool]$On, [int]$TimeoutMs = 1500)
    Initialize-UiTestNative
    if ((Get-CapsLockState) -eq $On) { return $true }
    [CyrFlipUi]::keybd_event(0x14, 0, 0, [IntPtr]::Zero)
    [CyrFlipUi]::keybd_event(0x14, 0, 2, [IntPtr]::Zero)
    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    while ((Get-Date) -lt $deadline) {
        if ((Get-CapsLockState) -eq $On) { return $true }
        Start-Sleep -Milliseconds 40
    }
    $false
}

function Get-ClipboardSequence {
    <#
    .SYNOPSIS
    Windows' clipboard sequence number - it rises on every write, so it says "a clipboard operation
    happened" without reading (or disturbing) what is on the clipboard.
    #>
    Initialize-UiTestNative
    [CyrFlipUi]::GetClipboardSequenceNumber()
}

function Get-ForegroundWindowInfo {
    Initialize-UiTestNative
    $h = [CyrFlipUi]::GetForegroundWindow()
    [pscustomobject]@{ Handle = $h; Title = [CyrFlipUi]::Text($h); Class = [CyrFlipUi]::Cls($h) }
}

function Get-AppWindows {
    <#
    .SYNOPSIS
    Visible top-level windows of a process, with size - the cheap way to prove "Settings opened".
    .DESCRIPTION
    Matching by *title* is the wrong tool here: the settings caption is localized ("Настройки
    CyrFlip" / "CyrFlip Settings"), and every editor window that happens to have the repo open
    contains the word "CyrFlip" too. Process + size is language-independent, and the size is also
    what separates the settings window from the launcher's 1x1 taskbar window.
    #>
    [CmdletBinding()]
    param([string]$ProcessName = 'CyrFlip')
    Initialize-UiTestNative
    Get-Process $ProcessName -ErrorAction SilentlyContinue | ForEach-Object {
        $procId = $_.Id
        [CyrFlipUi]::ProcessWindows([uint32]$procId, $true) | ForEach-Object {
            $h = $_
            $r = New-Object CyrFlipUi+RECT
            [void][CyrFlipUi]::GetWindowRect($h, [ref]$r)
            [pscustomobject]@{
                ProcessId = $procId
                Handle    = $h
                Title     = [CyrFlipUi]::Text($h)
                Class     = [CyrFlipUi]::Cls($h)
                Width     = $r.Right - $r.Left
                Height    = $r.Bottom - $r.Top
            }
        }
    }
}

function Wait-AppWindow {
    <#
    .SYNOPSIS
    Waits for a real (non-tiny) visible window of the app and returns it; nothing on timeout.
    #>
    [CmdletBinding()]
    param([string]$ProcessName = 'CyrFlip', [int]$MinWidth = 300, [int]$TimeoutSeconds = 8)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $w = Get-AppWindows -ProcessName $ProcessName | Where-Object { $_.Width -ge $MinWidth } | Select-Object -First 1
        if ($w) { return $w }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
}

function Find-AppWindow {
    <#
    .SYNOPSIS
    First visible top-level window whose title contains the given text.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$TitleLike, [int]$TimeoutSeconds = 0)
    Initialize-UiTestNative
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $h = [CyrFlipUi]::FindByTitle($TitleLike)
        if ($h -ne [IntPtr]::Zero) {
            return [pscustomobject]@{ Handle = $h; Title = [CyrFlipUi]::Text($h); Class = [CyrFlipUi]::Cls($h) }
        }
        if ($TimeoutSeconds -gt 0) { Start-Sleep -Milliseconds 300 }
    } while ((Get-Date) -lt $deadline)
}

function Save-WindowShot {
    <#
    .SYNOPSIS
    PNG of a window via PrintWindow(PW_RENDERFULLCONTENT) - works on a background window and
    captures WinForms/DWM content that plain BitBlt returns blank.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ParameterSetName = 'Handle')][IntPtr]$Handle,
        [Parameter(Mandatory, ParameterSetName = 'Title')][string]$TitleLike,
        [Parameter(Mandatory)][string]$Path,
        [switch]$Restore
    )
    Initialize-UiTestNative
    if ($PSCmdlet.ParameterSetName -eq 'Title') {
        $w = Find-AppWindow -TitleLike $TitleLike
        if (-not $w) { Write-Error "No visible window matching '$TitleLike'"; return }
        $Handle = $w.Handle
    }
    if ($Restore) { [void][CyrFlipUi]::ShowWindow($Handle, 9); Start-Sleep -Milliseconds 400 }  # SW_RESTORE
    $r = New-Object CyrFlipUi+RECT
    [void][CyrFlipUi]::GetWindowRect($Handle, [ref]$r)
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    if ($w -le 0 -or $h -le 0) { Write-Error "Window has no area (minimized?)"; return }
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    $ok = [CyrFlipUi]::PrintWindow($Handle, $hdc, 2)   # PW_RENDERFULLCONTENT
    $g.ReleaseHdc($hdc); $g.Dispose()
    $full = if ([System.IO.Path]::IsPathRooted($Path)) { $Path } else { Join-Path (Get-Location) $Path }
    $bmp.Save($full, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    if (-not $ok) { Write-Warning "PrintWindow returned false - the image may be blank." }
    [pscustomobject]@{ Path = $full; Width = $w; Height = $h }
}

Export-ModuleMember -Function Enable-UiTestDpi, Get-CyrFlipExe, Start-CyrFlipApp, Stop-CyrFlipApp,
    Get-TrayIcons, Get-TrayIcon, Invoke-MouseClick, Invoke-TrayClick, Start-TargetWindow, Get-WindowLayout,
    Set-WindowForeground, Get-InstalledLayouts, Get-ForegroundWindowInfo, Get-AppWindows,
    Wait-AppWindow, Find-AppWindow, Save-WindowShot, Get-AppResourceUsage, Switch-WindowLayout,
    Get-CapsLockState, Set-CapsLockState, Get-ClipboardSequence

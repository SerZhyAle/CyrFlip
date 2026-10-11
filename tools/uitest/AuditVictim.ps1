# The stand-in process of the audit rig's self-test (ticket S0061, acceptance 3) - NOT part of CyrFlip.
#
# It does what CyrFlip's UI thread does and what the rig must be able to catch: it owns a window, a
# WH_KEYBOARD_LL hook and a message loop, and on command it
#   stall <ms>              - sleeps on the UI thread (the thread the hook callback runs on)
#   leak <events> <pens> <windows> <kb>
#                           - allocates and never frees: kernel handles, GDI objects, USER objects, native bytes
#   noop                    - answers and does nothing (the control run: the rig must call it flat)
#   exit                    - ends
# Commands arrive as one line in -CommandFile, which the UI thread polls every 10 ms and deletes once it
# has been handled, so a caller can wait for "file gone" to know the work happened. -ReadyFile is created
# when the hook is installed.
#
# It is a throwaway by design: run only by Test-AuditRig.ps1, a hook for a few seconds, no state.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CommandFile,
    [Parameter(Mandatory)][string]$ReadyFile
)

# Only System.Windows.Forms is referenced and no System.Drawing type is touched: under PowerShell 7 a
# Size or Point would need System.Drawing.Primitives, under 5.1 it would need System.Drawing.
$references = @('System.Windows.Forms')
if ($PSVersionTable.PSVersion.Major -ge 6) { $references += 'System.Drawing.Primitives', 'System.ComponentModel.Primitives', 'System.ComponentModel.EventBasedAsync' }
Add-Type -ReferencedAssemblies $references -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

public static class AuditVictim
{
    private delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc cb, IntPtr mod, uint tid);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll")] private static extern void Sleep(uint ms);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateEvent(IntPtr attr, bool manual, bool initial, string name);
    [DllImport("gdi32.dll")] private static extern IntPtr CreatePen(int style, int width, int color);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

    private static HookProc _proc;
    private static IntPtr _hook;

    public static void Run(string commandFile, string readyFile)
    {
        _proc = delegate (int code, IntPtr w, IntPtr l) { return CallNextHookEx(_hook, code, w, l); };
        _hook = SetWindowsHookEx(13, _proc, GetModuleHandle(null), 0);
        Form form = new Form();
        form.ShowInTaskbar = false;
        form.FormBorderStyle = FormBorderStyle.None;
        form.Opacity = 0;
        form.WindowState = FormWindowState.Minimized;
        System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        timer.Interval = 10;
        timer.Tick += delegate
        {
            string line;
            try
            {
                if (!File.Exists(commandFile)) return;
                line = File.ReadAllText(commandFile).Trim();
                File.Delete(commandFile);
            }
            catch { return; }
            string[] a = line.Split(' ');
            switch (a[0])
            {
                case "stall": Sleep((uint)int.Parse(a[1])); break;
                case "leak":
                    int events = int.Parse(a[1]), pens = int.Parse(a[2]), wins = int.Parse(a[3]), kb = int.Parse(a[4]);
                    for (int i = 0; i < events; i++) CreateEvent(IntPtr.Zero, false, false, null);
                    for (int i = 0; i < pens; i++) CreatePen(0, 1, 0);
                    for (int i = 0; i < wins; i++) CreateWindowEx(0, "STATIC", "leak", 0x40000000 /*WS_CHILD*/ , 0, 0, 1, 1, form.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                    if (kb > 0)
                    {
                        IntPtr mem = Marshal.AllocHGlobal(kb * 1024);
                        byte[] zero = new byte[kb * 1024];
                        for (int i = 0; i < zero.Length; i += 4096) zero[i] = 1;   // touch every page
                        Marshal.Copy(zero, 0, mem, zero.Length);
                    }
                    break;
                case "exit": form.Close(); break;
            }
        };
        form.Load += delegate { timer.Start(); File.WriteAllText(readyFile, "ready"); };
        Application.Run(form);
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
    }
}
'@

[AuditVictim]::Run($CommandFile, $ReadyFile)

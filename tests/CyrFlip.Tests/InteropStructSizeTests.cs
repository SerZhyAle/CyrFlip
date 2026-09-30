using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// The x64 sizes of the hand-declared Win32 structs (ticket S0029 RB-8; values recomputed by hand in
    /// the S0027 audit, PHASE_07). A wrong size fails silently at run time - <c>SendInput</c> returns 0
    /// for an <c>INPUT</c> of the wrong size, <c>GetGUIThreadInfo</c> refuses a wrong <c>cbSize</c> - so
    /// a field reordered or retyped by an edit is only ever caught here.
    /// </summary>
    public class InteropStructSizeTests
    {
        [Fact]
        public void EveryInteropStructHasItsWin32SizeOnX64()
        {
            if (IntPtr.Size != 8) return;   // the product ships x64 only; the layouts below are x64's

            var expected = new Dictionary<Type, int>
            {
                [typeof(WindowInterop.INPUT)] = 40,
                [typeof(WindowInterop.MOUSEINPUT)] = 32,
                [typeof(WindowInterop.KEYBDINPUT)] = 24,
                [typeof(WindowInterop.HARDWAREINPUT)] = 8,
                [typeof(WindowInterop.KBDLLHOOKSTRUCT)] = 24,
                [typeof(WindowInterop.MSLLHOOKSTRUCT)] = 32,
                [typeof(WindowInterop.GUITHREADINFO)] = 72,
                [typeof(WindowInterop.ICONINFO)] = 32,
                [typeof(WindowInterop.COMBOBOXINFO)] = 64,
                [typeof(WindowInterop.WIN32_FIND_DATA)] = 592,
                // 4 + pad 4 + 5x8 + 4 + pad 4 + 8 + 4 + pad 4 + 8 + 4 + pad 4 + 8 = 96, as MAPI.h lays it
                // out (the audit's table printed 80 from this same sum - a slip in the addition).
                [typeof(WindowInterop.MapiMessage)] = 96,
                [typeof(WindowInterop.MapiRecipDesc)] = 40,
                [typeof(WindowInterop.MapiFileDesc)] = 40,
                [typeof(WindowInterop.HIGHCONTRAST)] = 16,   // 4 + 4 + 8 (S0036 UI-2)
            };

            var problems = new List<string>();
            foreach (KeyValuePair<Type, int> pair in expected)
            {
                int actual = Marshal.SizeOf(pair.Key);
                if (actual != pair.Value) problems.Add($"{pair.Key.Name}: {actual}, Win32 expects {pair.Value}");
            }
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }
    }
}

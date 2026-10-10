using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CyrFlip
{
    /// <summary>The user's choice in the settings window: follow Windows, or one fixed look.</summary>
    internal enum ThemeMode
    {
        System,
        Light,
        Dark,
    }

    /// <summary>
    /// The pure half of the theme: the invariant registry token and how a mode meets Windows. Nothing
    /// here reads the registry, so the whole matrix is tested on a machine of any theme - the same shape
    /// as <see cref="Localization.MatchLanguage"/>.
    /// </summary>
    internal static class ThemeModes
    {
        public const string SystemToken = "system";
        public const string LightToken = "light";
        public const string DarkToken = "dark";

        /// <summary>
        /// The stored token back to a mode. Anything else - a hand edit, a value written by a build that
        /// does not exist yet - reads as <see cref="ThemeMode.System"/>: the one answer that cannot be wrong.
        /// </summary>
        public static ThemeMode Parse(string? token)
        {
            switch ((token ?? "").Trim().ToLowerInvariant())
            {
                case LightToken: return ThemeMode.Light;
                case DarkToken: return ThemeMode.Dark;
                default: return ThemeMode.System;
            }
        }

        public static string Token(ThemeMode mode)
            => mode == ThemeMode.Light ? LightToken : mode == ThemeMode.Dark ? DarkToken : SystemToken;

        /// <summary>
        /// What gets drawn. High contrast wins over every mode (S0020 v0.2 item 4); otherwise an explicit
        /// mode is itself, and "system" asks Windows.
        /// </summary>
        public static ThemeKind Resolve(ThemeMode mode, bool systemPrefersDark, bool highContrast)
        {
            if (highContrast) return ThemeKind.HighContrast;
            switch (mode)
            {
                case ThemeMode.Light: return ThemeKind.Light;
                case ThemeMode.Dark: return ThemeKind.Dark;
                default: return systemPrefersDark ? ThemeKind.Dark : ThemeKind.Light;
            }
        }
    }

    /// <summary>
    /// The one place that knows which theme is on (<c>APP-STYLE</c> section 2): the mode the user chose,
    /// Windows' own preference, high contrast, and the windows that have to follow. UI thread only.
    ///
    /// <para><b>Inert until <see cref="Initialize"/> runs.</b> The tray context and the one-shot
    /// launcher path call it; a unit test that builds a window never does, so no test ever paints with
    /// the developer's own Windows theme.</para>
    ///
    /// <para><b>No static event a window subscribes to.</b> Every <see cref="ThemedForm"/> attaches
    /// itself when its handle is created, the manager keeps it in a list and drops it on
    /// <see cref="Component.Disposed"/> - so a closed window can never be held alive by the theme, which
    /// is the leak a static <c>ThemeChanged</c> event invites.</para>
    /// </summary>
    internal static class ThemeManager
    {
        /// <summary>
        /// Windows' light/dark preference is re-read at most this often. It moved here from the history
        /// strip, which used to open the registry key on <b>every repaint</b> - and the strip repaints on
        /// every copy. A theme signal drops the cache at once, so this only bounds the cost of repaints.
        /// </summary>
        private const int SystemThemeTtlMs = 2000;

        private static readonly List<Form> Attached = new List<Form>();
        private static bool _initialized;
        private static int _uiThread;
        private static SynchronizationContext? _ui;
        private static ThemeMode _mode = ThemeMode.System;
        private static ThemeKind _kind = ThemeKind.Light;
        private static bool _systemDarkKnown;
        private static bool _systemDark;
        private static int _systemDarkReadAt;

        public static bool IsInitialized => _initialized;
        public static ThemeMode Mode => _mode;
        public static ThemeKind Kind => _kind;
        public static ThemePalette Palette => ThemePalette.For(_kind);

        /// <summary>Start following the theme: resolve, set up the menus, listen to Windows.</summary>
        public static void Initialize(ThemeMode mode)
        {
            _mode = mode;
            if (!_initialized)
            {
                _initialized = true;
                _uiThread = Thread.CurrentThread.ManagedThreadId;
                // The tray context calls this before it has created a single control, so there is no
                // WinForms context on the thread yet - and SystemEvents, finding none, raises its events
                // on a thread of its own. Every signal is therefore posted back here explicitly.
                _ui = SynchronizationContext.Current as WindowsFormsSynchronizationContext ?? new WindowsFormsSynchronizationContext();
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            }
            _kind = ResolveNow(forceRead: true);
            ApplyProcessWide();
        }

        /// <summary>The settings window's selector: takes effect at once, no restart, no "apply".</summary>
        public static void SetMode(ThemeMode mode)
        {
            _mode = mode;
            Refresh(forceRead: false);
        }

        /// <summary>
        /// Windows said something about colours or accessibility (<c>WM_SETTINGCHANGE</c> with
        /// <c>"ImmersiveColorSet"</c> or <c>SPI_SETHIGHCONTRAST</c>, <c>WM_SYSCOLORCHANGE</c>, or
        /// <see cref="SystemEvents.UserPreferenceChanged"/>). Windows is inconsistent about which of them
        /// it sends - it depends on the version and on whether the switch was manual or scheduled - so
        /// every one of them lands here, and one that changes nothing does nothing.
        /// </summary>
        public static void OnSystemSignal()
        {
            if (!_initialized) return;
            Refresh(forceRead: true);
        }

        /// <summary>Called by <see cref="ThemedForm"/> when its handle exists: paint it now, follow later.</summary>
        public static void Attach(Form form)
        {
            if (!_initialized || form.IsDisposed) return;
            // A window of another thread is painted once and not followed: a later theme change is
            // applied from the UI thread, and reaching into that window from there would be cross-thread.
            if (Thread.CurrentThread.ManagedThreadId != _uiThread) { Apply(form); return; }
            if (!Attached.Contains(form))
            {
                Attached.Add(form);
                form.Disposed += OnFormDisposed;
            }
            Apply(form);
        }

        /// <summary>The tray context is going away: stop listening and leave nothing registered.</summary>
        public static void Shutdown()
        {
            if (!_initialized) return;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            foreach (Form form in Attached) form.Disposed -= OnFormDisposed;
            Attached.Clear();
            _ui = null;
            _initialized = false;
        }

        /// <summary>
        /// Windows' own "apps use dark" answer, cached for <see cref="SystemThemeTtlMs"/>. An unreadable
        /// key means light - the Windows default, and what CyrFlip always looked like.
        /// </summary>
        public static bool SystemPrefersDark(bool forceRead = false)
        {
            if (!forceRead && _systemDarkKnown && unchecked(Environment.TickCount - _systemDarkReadAt) < SystemThemeTtlMs)
                return _systemDark;

            _systemDarkReadAt = Environment.TickCount;
            _systemDarkKnown = true;
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                object? value = key?.GetValue("AppsUseLightTheme");
                _systemDark = value != null && Convert.ToInt32(value) == 0;
            }
            catch { _systemDark = false; }
            return _systemDark;
        }

        private static ThemeKind ResolveNow(bool forceRead)
            => ThemeModes.Resolve(_mode, SystemPrefersDark(forceRead), SystemHighContrast());

        /// <summary>
        /// Windows' high-contrast flag, asked each time (S0036 UI-2). <c>SystemInformation.HighContrast</c>
        /// is cached on net48 until the framework's own <c>UserPreferenceChanged</c> handler marks it
        /// stale - and ours is subscribed first, so a toggle resolved against the old value and nothing
        /// was repainted until some unrelated signal came along.
        /// </summary>
        internal static bool SystemHighContrast()
        {
            try
            {
                var info = new WindowInterop.HIGHCONTRAST { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(WindowInterop.HIGHCONTRAST)) };
                if (WindowInterop.SystemParametersInfo(WindowInterop.SPI_GETHIGHCONTRAST, info.cbSize, ref info, 0))
                    return (info.dwFlags & WindowInterop.HCF_HIGHCONTRASTON) != 0;
            }
            catch { }
            return SystemInformation.HighContrast;
        }

        private static void Refresh(bool forceRead)
        {
            if (!_initialized) return;
            ThemeKind kind = ResolveNow(forceRead);
            if (kind == _kind) return;
            _kind = kind;
            ApplyProcessWide();
            // A copy: applying the theme can create a handle, and a handle's creation attaches.
            foreach (Form form in Attached.ToArray())
                if (!form.IsDisposed) Apply(form);
        }

        private static void Apply(Form form)
        {
            ThemePalette palette = Palette;
            ThemeApply.Apply(form, palette);
            (form as ThemedForm)?.RaiseThemeApplied(palette);
        }

        /// <summary>
        /// The two process-wide parts: the renderer every <see cref="ContextMenuStrip"/> in manager render
        /// mode draws with (so a menu built tomorrow cannot forget the theme), and the uxtheme app mode the
        /// native menus - a text box's Cut/Copy/Paste, a window's system menu - are drawn with.
        /// </summary>
        private static void ApplyProcessWide()
        {
            ThemeToolStripRenderer.Install(Palette);
            ThemeWin32.SetMenuMode(_kind);
        }

        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // General carries the immersive colour switch, Accessibility high contrast, Color the rest.
            if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.Color
                && e.Category != UserPreferenceCategory.Accessibility && e.Category != UserPreferenceCategory.VisualStyle)
                return;
            if (Thread.CurrentThread.ManagedThreadId == _uiThread) OnSystemSignal();
            else _ui?.Post(_ => OnSystemSignal(), null);
        }

        private static void OnFormDisposed(object? sender, EventArgs e)
        {
            if (sender is Form form)
            {
                form.Disposed -= OnFormDisposed;
                Attached.Remove(form);
            }
        }
    }

    /// <summary>
    /// <see cref="Control.DeviceDpi"/> on net48 is a private field the framework updates only inside its
    /// own DPI-changed machinery, which this app does not opt into (<c>U-H1</c>: it stayed 96 on every
    /// monitor, so everything that scaled by it - <see cref="SettingsNumeric"/>, the viewport capture -
    /// silently did nothing). The settings window answers <c>WM_DPICHANGED</c> itself and keeps the tree
    /// truthful through this helper. The field name is frozen with the net48 runtime, the way
    /// <see cref="ThemeApply"/> already relies on <c>ButtonBase</c>'s; a runtime without it degrades to
    /// the static 96, which is exactly what the app did before.
    /// </summary>
    internal static class ControlDpi
    {
        // net48 spells it "deviceDpi" (decompiled); the underscore name guards a future respelling.
        private static readonly FieldInfo? Field =
            typeof(Control).GetField("deviceDpi", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? typeof(Control).GetField("_deviceDpi", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>Logical 96-DPI px to device px, never shrinking below the authored size.</summary>
        internal static int Scale(int dpi, int logical) => Math.Max(logical, (int)Math.Round(logical * Math.Max(96, dpi) / 96.0));

        /// <summary>Device px back to logical 96-DPI px (the unit the config stores).</summary>
        internal static int Unscale(int dpi, int device) => (int)Math.Round((double)device * 96 / Math.Max(96, dpi));

        /// <summary>Set the control's own <c>DeviceDpi</c>; false when the runtime refuses.</summary>
        internal static bool Set(Control control, int dpi)
        {
            if (Field == null) return false;
            try { Field.SetValue(control, dpi); return true; }
            catch { return false; }
        }

        /// <summary>
        /// Read the stored per-window DPI back. Only a per-monitor-v2 thread's <c>DeviceDpi</c>
        /// property returns this field (net48 decides in <c>DpiHelper</c>); everywhere else the
        /// property answers the process DPI, so a test peeks at the field itself.
        /// </summary>
        internal static int? Peek(Control control)
        {
            if (Field == null) return null;
            try { return (int?)Field.GetValue(control); }
            catch { return null; }
        }

        /// <summary>The whole subtree: the framework updates children through its own walk, which never runs here.</summary>
        internal static void UpdateTree(Control root, int dpi)
        {
            Set(root, dpi);
            foreach (Control child in root.Controls) UpdateTree(child, dpi);
        }
    }

    /// <summary>
    /// The base of every CyrFlip window. It attaches to <see cref="ThemeManager"/> when its handle is
    /// created - after the constructor has built every control, before anything is painted - which is
    /// what "no white flash" (S0020 section 9) comes down to, and which matters most for the translation
    /// popup: it never activates, so there is no later moment to repaint it in. A handle recreated by a
    /// right-to-left switch attaches again, which re-applies the parts that live on the handle (the dark
    /// title bar among them).
    ///
    /// <para><c>ThemeCoverageTests</c> fails on any <see cref="Form"/> in the assembly that is neither a
    /// <see cref="ThemedForm"/> nor declared out of theme with a reason.</para>
    ///
    /// <para><c>WM_DPICHANGED</c> (S0060 P2, WINDOWS-UI section 5): the app declares PerMonitorV2 but
    /// opts out of WinForms' own DPI machinery, so the framework would neither move the window to the
    /// rect Windows suggests nor update <c>DeviceDpi</c>. This form does both - move, rescale the
    /// field over the whole tree, then hand the change to <see cref="OnDpiChanged"/>, which a window
    /// with pixel geometry overrides to re-measure itself.</para>
    /// </summary>
    internal class ThemedForm : Form
    {
        private const int WM_DPICHANGED = 0x02E0;

        protected override void OnHandleCreated(EventArgs e)
        {
            ThemeManager.Attach(this);
            base.OnHandleCreated(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DPICHANGED) { HandleDpiChanged(ref m); return; }
            base.WndProc(ref m);
        }

        private void HandleDpiChanged(ref Message m)
        {
            int oldDpi = DeviceDpi;
            int newDpi = (int)(((long)m.WParam >> 16) & 0xFFFF);   // LOWORD = X, HIWORD = Y; they are equal on real monitors
            if (newDpi <= 0) newDpi = oldDpi;
            Rectangle suggested = Rectangle.Empty;
            if (m.LParam != IntPtr.Zero)
            {
                var rect = (WindowInterop.RECT)Marshal.PtrToStructure(m.LParam, typeof(WindowInterop.RECT))!;
                suggested = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            }
            bool wasNormal = WindowState == FormWindowState.Normal;
            // The framework's own pass is skipped on purpose: in a process that is not per-monitor aware
            // it re-reads GetDpiForWindow - the system DPI - into every child, overwriting this update
            // asynchronously (found by the test gate). This form owns the whole story instead.
            ControlDpi.UpdateTree(this, newDpi);
            if (wasNormal && suggested.Width > 0 && suggested.Height > 0)
            {
                // Windows suggests the same physical footprint at the new DPI. A maximized window
                // keeps its state; a restored one takes the rectangle, clamped to the monitor.
                try
                {
                    var work = new List<Rectangle> { Screen.FromRectangle(suggested).WorkingArea };
                    Bounds = ScreenPlacement.Clamp(suggested, work);
                }
                catch { Bounds = suggested; }
            }
            OnDpiChanged(oldDpi, newDpi);
            m.Result = IntPtr.Zero;
        }

        internal void RaiseDpiChanged(int oldDpi, int newDpi) => OnDpiChanged(oldDpi, newDpi);

        /// <summary>The window moved to a monitor of another DPI; re-derive whatever is measured in pixels.</summary>
        protected virtual void OnDpiChanged(int oldDpi, int newDpi) { }

        internal void RaiseThemeApplied(ThemePalette palette) => OnThemeApplied(palette);

        /// <summary>
        /// The tree walk has just recoloured this window; repaint whatever it draws itself (the history
        /// strip's cells, the settings window's page icons).
        /// </summary>
        protected virtual void OnThemeApplied(ThemePalette palette) { }
    }
}

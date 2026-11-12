using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>UI-SIZING section 2 rule 1: the input profile a surface opens with.</summary>
    internal enum UiInput
    {
        Pointer,
        Touch,
    }

    /// <summary>
    /// The pure half of the size profile (UI-SIZING sections 2-3): the invariant registry values and how they
    /// meet <see cref="UiMetrics"/>. Nothing here reads the registry or a window, so the whole matrix is
    /// tested on any machine - the same shape as <see cref="ThemeModes"/>.
    ///
    /// <para><b>The offered range is a product decision, recorded in PLAN/S0076_unified-ui-contracts/unified-ui.md:</b> interface
    /// 75-200 per cent in five steps, text 100-200 per cent in four. <see cref="UiMetrics"/> itself accepts
    /// 0.75-3.0 so a test can exercise the vector sizes beyond what the drop-down offers; a stored value that
    /// is not one of the offered steps reads as the ordinary 100 (UI-SIZING: an unknown scale must not
    /// produce a zero-size or an unboundedly shrinking target).</para>
    /// </summary>
    internal static class UiProfiles
    {
        public const string PointerToken = "pointer";
        public const string TouchToken = "touch";
        public const int OrdinaryPercent = 100;

        /// <summary>Interface scale steps: compact, ordinary, and three larger.</summary>
        public static readonly int[] InterfaceSteps = { 75, 100, 125, 150, 200 };

        /// <summary>Independent text enlargement steps.</summary>
        public static readonly int[] TextSteps = { 100, 125, 150, 200 };

        public static UiInput ParseInput(string? token)
            => string.Equals((token ?? "").Trim(), TouchToken, StringComparison.OrdinalIgnoreCase) ? UiInput.Touch : UiInput.Pointer;

        public static string Token(UiInput input) => input == UiInput.Touch ? TouchToken : PointerToken;

        public static int NormalizeInterface(int percent) => InterfaceSteps.Contains(percent) ? percent : OrdinaryPercent;

        public static int NormalizeText(int percent) => TextSteps.Contains(percent) ? percent : OrdinaryPercent;

        public static UiMetrics Metrics(UiInput input, int interfacePercent, int textPercent)
            => new UiMetrics(input == UiInput.Touch, NormalizeInterface(interfacePercent) / 100.0, NormalizeText(textPercent) / 100.0);

        public static UiMetrics Metrics(AppConfig config)
            => Metrics(ParseInput(config.UiInputProfile), config.UiInterfaceScale, config.UiTextScale);

        /// <summary>The font size a window opens with: the base size composed with both scales, once.</summary>
        public static float FontPoints(float basePoints, UiMetrics metrics)
            => (float)Math.Round(basePoints * metrics.InterfaceScale * metrics.TextScale, 1);
    }

    /// <summary>
    /// The one place that knows which size profile is on (UI-SIZING section 2 rule 1: "a stable pointer/keyboard
    /// or touch profile at opening, or through an explicit user choice"). UI thread only.
    ///
    /// <para>Modelled on <see cref="ThemeManager"/>: <b>inert until <see cref="Initialize"/> runs</b> - the tray
    /// context and the one-shot launcher call it, no unit test does, so no test measures with the developer's
    /// own configuration - and <b>no static event</b>: every <see cref="ThemedForm"/> attaches itself when its
    /// handle is created, the manager keeps it in a list and drops it on disposal.</para>
    ///
    /// <para><b>The profile never follows the input device.</b> Nothing here listens to the mouse, the pen or
    /// the touch screen; the profile changes only through <see cref="Set"/>, which the settings window calls
    /// after an explicit choice, so a target never moves under an active interaction.</para>
    /// </summary>
    internal static class UiProfileManager
    {
        private static readonly List<Form> Attached = new List<Form>();
        private static bool _initialized;
        private static int _uiThread;
        private static UiMetrics _current = UiMetrics.Pointer;

        public static bool IsInitialized => _initialized;

        /// <summary>The profile new windows open with; <see cref="UiMetrics.Pointer"/> before <see cref="Initialize"/>.</summary>
        public static UiMetrics Current => _initialized ? _current : UiMetrics.Pointer;

        public static void Initialize(AppConfig config)
        {
            _uiThread = Thread.CurrentThread.ManagedThreadId;
            _current = UiProfiles.Metrics(config);
            _initialized = true;
        }

        /// <summary>The settings window's explicit choice: takes effect at once on every open window.</summary>
        public static void Set(UiInput input, int interfacePercent, int textPercent)
        {
            if (!_initialized) return;
            UiMetrics next = UiProfiles.Metrics(input, interfacePercent, textPercent);
            if (next.Same(_current)) return;
            _current = next;
            // A copy: applying can create a handle, and a handle's creation attaches.
            foreach (Form form in Attached.ToArray())
                if (!form.IsDisposed) Apply(form);
        }

        /// <summary>Called by <see cref="ThemedForm"/> when its handle exists: size it now, follow later.</summary>
        public static void Attach(Form form)
        {
            if (!_initialized || form.IsDisposed) return;
            // A window of another thread is sized once and not followed (a later change is applied from the UI thread).
            if (Thread.CurrentThread.ManagedThreadId != _uiThread) { Apply(form); return; }
            if (!Attached.Contains(form))
            {
                Attached.Add(form);
                form.Disposed += OnFormDisposed;
            }
            Apply(form);
        }

        public static void Shutdown()
        {
            foreach (Form form in Attached) form.Disposed -= OnFormDisposed;
            Attached.Clear();
            _current = UiMetrics.Pointer;
            _initialized = false;
        }

        private static void Apply(Form form)
        {
            UiMetrics metrics = _current;
            UiMetrics.Set(form, metrics);
            (form as ThemedForm)?.RaiseUiProfileApplied(metrics);
        }

        private static void OnFormDisposed(object? sender, EventArgs e)
        {
            if (sender is Form form)
            {
                form.Disposed -= OnFormDisposed;
                Attached.Remove(form);
            }
        }

        /// <summary>
        /// The covered components of a tree measure again at their final width (UI-SIZING 1.1: "remeasure covered
        /// components at their final width and release obsolete cached text heights"). The shared components
        /// derive everything from <see cref="UiMetrics.For"/>, so this only has to tell each one the profile changed.
        /// </summary>
        internal static void Reflow(Control root)
        {
            foreach (Control child in root.Controls)
            {
                switch (child)
                {
                    case Button button when ButtonGlyphs.IsAttached(button):
                        ButtonGlyphs.Reassert(button);
                        break;
                    case CollapsibleGroup group:
                        group.ApplyProfile();
                        break;
                    case BooleanOptionCheckBox option:
                        option.PerformLayout();
                        option.Invalidate();
                        break;
                }
                Reflow(child);
            }
        }
    }
}

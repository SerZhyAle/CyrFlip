using System;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>UI-SIZING section 2: immutable surface choice; DPI is composed once at resolution.</summary>
    internal sealed class UiMetrics
    {
        private static readonly ConditionalWeakTable<Control, UiMetrics> Profiles = new ConditionalWeakTable<Control, UiMetrics>();
        internal static readonly UiMetrics Pointer = new UiMetrics(false, 1, 1);

        internal UiMetrics(bool touch, double interfaceScale, double textScale)
        {
            Touch = touch;
            InterfaceScale = Valid(interfaceScale, .75, 3);
            TextScale = Valid(textScale, 1, 2);
        }

        private static double Valid(double value, double min, double max)
            => double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max ? 1 : value;

        internal bool Touch { get; }
        internal double InterfaceScale { get; }
        internal double TextScale { get; }
        internal int Target => Logical(Touch ? 48 : 32);
        internal int Glyph => Logical(Touch ? 20 : 16);
        internal int Checkbox => Logical(Touch ? 24 : 20);
        internal int Gap => Logical(8);
        internal int HorizontalPadding => Logical(Touch ? 16 : 12);
        internal int Logical(int ordinary) => Math.Max(1, (int)Math.Round(ordinary * InterfaceScale));
        internal int Device(Control control, int logical) => Math.Max(1, (int)Math.Round(logical * control.DeviceDpi / 96.0));

        internal static UiMetrics For(Control control)
        {
            for (Control? current = control; current != null; current = current.Parent)
                if (Profiles.TryGetValue(current, out UiMetrics? metrics)) return metrics;
            return Pointer;
        }

        // A surface owner changes this deliberately between interactions, never on input-device events.
        // Full product profile selection remains tracked by the S0076 migration record.
        internal static void Set(Control surface, UiMetrics metrics)
        {
            Profiles.Remove(surface);
            Profiles.Add(surface, metrics);
        }
    }
}

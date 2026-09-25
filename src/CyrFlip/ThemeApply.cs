using System;
using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace CyrFlip
{
    /// <summary>A control that paints part of itself and has to hear about a theme change.</summary>
    internal interface IThemeAware
    {
        void ApplyTheme(ThemePalette palette);
    }

    /// <summary>
    /// The tree walk that puts a palette on a window (S0020 section 5). WinForms has no resource scope,
    /// so <c>APP-STYLE</c> rule 3's "dynamic reference" is held this way instead:
    ///
    /// <list type="bullet">
    /// <item>controls are <b>built in <see cref="ThemePalette.Light"/></b> - the design-time default,
    /// which is exactly what WinForms draws unthemed;</item>
    /// <item>the first time the walk meets a control it <b>records the built state</b> - each colour,
    /// whether it was set at all, the flat style, the link colours - and from then on reads a colour's
    /// meaning from that record (<see cref="ThemePalette.RoleOfDesignColor"/>), never from whatever the
    /// control shows right now;</item>
    /// <item><b>dark</b> assigns the role's dark value; <b>light and high contrast put the record back</b>
    /// (a colour that was never set is reset, so it inherits again), which is why light looks exactly
    /// like the app always did and high contrast shows system colours only.</item>
    /// </list>
    ///
    /// <para>A control added later - every table row the settings window rebuilds - is themed through
    /// its container's <see cref="Control.ControlAdded"/>; a handle created later - a right-to-left
    /// switch recreates them - gets its native part through <see cref="Control.HandleCreated"/>. All the
    /// handlers are static and the record lives in a <see cref="ConditionalWeakTable{TKey,TValue}"/>, so
    /// the theme never keeps a control alive.</para>
    /// </summary>
    internal static class ThemeApply
    {
        private sealed class State
        {
            public bool BackExplicit, ForeExplicit;
            public Color Back, Fore;
            public FlatStyle Flat;
            public bool UseVisualStyleBackColor;
            public Color FlatBorder, FlatOver, FlatDown;
            public Color LinkColor, ActiveLinkColor, VisitedLinkColor, DisabledLinkColor;
            public bool OwnerDraw;
            public bool NativeDark;
            public ThemePalette? Palette;
            /// <summary>The button's two raw visual-style fields - see <see cref="ReadVisualStyleFields"/>.</summary>
            public (bool Enabled, bool Set)? VisualStyleRaw;
        }

        // ButtonBase.UseVisualStyleBackColor is not a stored value until something sets it: unset, it is
        // computed - true only while the effective BackColor is the Control colour. A button is recorded
        // when the walk first meets it, which for a row added to an already dark table is after its
        // container went dark, so the property would read false and "restore" every such button to a
        // flat face. The two fields behind it are what the constructor actually left; net48 is frozen,
        // so their names are too, and a runtime without them falls back to the property.
        private static readonly FieldInfo? VisualStyleEnabledField = typeof(ButtonBase).GetField("enableVisualStyleBackground", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? VisualStyleSetField = typeof(ButtonBase).GetField("isEnableVisualStyleBackgroundSet", BindingFlags.NonPublic | BindingFlags.Instance);

        private static (bool, bool)? ReadVisualStyleFields(ButtonBase button)
        {
            if (VisualStyleEnabledField == null || VisualStyleSetField == null) return null;
            try { return ((bool)VisualStyleEnabledField.GetValue(button), (bool)VisualStyleSetField.GetValue(button)); }
            catch (ArgumentException) { return null; }
        }

        private static void RestoreVisualStyle(Button button, State state)
        {
            if (state.VisualStyleRaw is (bool enabled, bool set))
            {
                VisualStyleEnabledField!.SetValue(button, enabled);
                VisualStyleSetField!.SetValue(button, set);
                button.Invalidate();
            }
            else button.UseVisualStyleBackColor = state.UseVisualStyleBackColor;
        }

        private static readonly ConditionalWeakTable<Control, State> States = new ConditionalWeakTable<Control, State>();
        private static readonly ConditionalWeakTable<Control, State>.CreateValueCallback Record = RecordState;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYOUTRTL = 0x00400000;

        /// <summary>The palette last put on <paramref name="control"/>, or null if it was never themed.</summary>
        public static ThemePalette? PaletteOf(Control control)
            => States.TryGetValue(control, out State? state) ? state.Palette : null;

        /// <summary>Paint <paramref name="root"/> and everything under it with <paramref name="palette"/>.</summary>
        public static void Apply(Control root, ThemePalette palette)
        {
            root.SuspendLayout();
            try { Walk(root, palette); }
            finally { root.ResumeLayout(true); }
            root.Invalidate(true);
        }

        private static void Walk(Control control, ThemePalette palette)
        {
            // A menu strip draws with the process-wide renderer (ThemeToolStripRenderer), not with colours.
            if (control is ToolStrip) return;

            State state = States.GetValue(control, Record);
            state.Palette = palette;
            Paint(control, state, palette);
            if (control.IsHandleCreated) Native(control, state, palette);
            (control as IThemeAware)?.ApplyTheme(palette);
            foreach (Control child in control.Controls) Walk(child, palette);
        }

        private static State RecordState(Control control)
        {
            PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(control);
            var state = new State
            {
                BackExplicit = properties["BackColor"]?.ShouldSerializeValue(control) ?? false,
                ForeExplicit = properties["ForeColor"]?.ShouldSerializeValue(control) ?? false,
                Back = control.BackColor,
                Fore = control.ForeColor,
            };
            switch (control)
            {
                case Button button:
                    state.Flat = button.FlatStyle;
                    state.UseVisualStyleBackColor = button.UseVisualStyleBackColor;
                    state.VisualStyleRaw = ReadVisualStyleFields(button);
                    state.FlatBorder = button.FlatAppearance.BorderColor;
                    state.FlatOver = button.FlatAppearance.MouseOverBackColor;
                    state.FlatDown = button.FlatAppearance.MouseDownBackColor;
                    break;
                case ComboBox combo:
                    state.Flat = combo.FlatStyle;
                    break;
                case TabPage page:
                    state.UseVisualStyleBackColor = page.UseVisualStyleBackColor;
                    break;
                case LinkLabel link:
                    state.LinkColor = link.LinkColor;
                    state.ActiveLinkColor = link.ActiveLinkColor;
                    state.VisitedLinkColor = link.VisitedLinkColor;
                    state.DisabledLinkColor = link.DisabledLinkColor;
                    break;
                case ListView list:
                    state.OwnerDraw = list.OwnerDraw;
                    if (!list.OwnerDraw)
                    {
                        list.DrawColumnHeader += OnDrawColumnHeader;
                        list.DrawItem += OnDrawItem;
                        list.DrawSubItem += OnDrawSubItem;
                    }
                    break;
            }

            if (control is Label && !(control is LinkLabel) || control is CheckBox || control is RadioButton || control is Button)
                control.Paint += OnPaintDisabled;
            control.ControlAdded += OnControlAdded;
            control.HandleCreated += OnHandleCreated;
            return state;
        }

        // ---- Colours ----

        private static void Paint(Control control, State state, ThemePalette palette)
        {
            switch (control)
            {
                case Button button: PaintButton(button, state, palette); return;
                case ComboBox combo:
                    // Not flat: WinForms' flat combo draws a white frame and a light arrow whatever the
                    // colours. The native face under DarkMode_CFD (see Native) is dark all over; where
                    // that theme is missing, the box stays in the light style it was built in.
                    combo.FlatStyle = state.Flat;
                    break;
                case LinkLabel link:
                    bool dark = palette.IsDark;
                    link.LinkColor = dark ? palette.Link : state.LinkColor;
                    link.ActiveLinkColor = dark ? palette.Link : state.ActiveLinkColor;
                    link.VisitedLinkColor = dark ? palette.Link : state.VisitedLinkColor;
                    link.DisabledLinkColor = dark ? palette.TextDisabled : state.DisabledLinkColor;
                    break;
                case ListView list:
                    // Owner-drawn only for the header, and only in dark: the native header ignores every
                    // colour it is given. A list with check boxes keeps its native header - owner-draw and
                    // check boxes do not mix in a list view (declared in ThemeCoverageTests).
                    list.OwnerDraw = palette.IsDark && !list.CheckBoxes ? true : state.OwnerDraw;
                    break;
            }

            bool owns = OwnsColors(control);
            PaintColor(control, true, state.BackExplicit, state.Back, owns, DefaultBackRole(control), palette);
            PaintColor(control, false, state.ForeExplicit, state.Fore, owns, ThemeRole.TextPrimary, palette);

            if (control is TabPage page)
            {
                // The visual-style page background ignores BackColor; it has to go for the dark one to
                // show, and come back (after the colour - setting BackColor switches it off) for light.
                page.UseVisualStyleBackColor = !palette.IsDark && state.UseVisualStyleBackColor;
            }
        }

        /// <summary>
        /// One colour of one control. An explicitly built colour keeps its meaning in every palette; a
        /// colour the control never set is only assigned in dark, and only on a control that does not
        /// inherit it from its parent anyway - everything else follows its container.
        /// </summary>
        private static void PaintColor(Control control, bool back, bool isExplicit, Color built, bool owns,
            ThemeRole fallback, ThemePalette palette)
        {
            if (isExplicit)
            {
                ThemeRole? role = ThemePalette.RoleOfDesignColor(built);
                if (role == null) return;   // not a palette colour - reported by the coverage test
                Set(control, back, palette.Kind == ThemeKind.Light ? built : palette[role.Value]);
                return;
            }
            if (!owns) return;
            if (palette.IsDark) Set(control, back, palette[ThemePalette.RoleOfDesignColor(built) ?? fallback]);
            else if (back) control.ResetBackColor();
            else control.ResetForeColor();
        }

        private static void Set(Control control, bool back, Color color)
        {
            if (back) { if (control.BackColor != color) control.BackColor = color; }
            else if (control.ForeColor != color) control.ForeColor = color;
        }

        /// <summary>
        /// A control whose default colours are its own rather than its parent's: the window itself, a
        /// page, and every input and list (their default is the "window" colour, not the container's).
        /// </summary>
        private static bool OwnsColors(Control control)
            => control is Form || control is TabPage || control is TextBoxBase || control is ListBox
               || control is ListView || control is ComboBox || control is UpDownBase || control is TreeView
               || control.Parent == null;

        private static ThemeRole DefaultBackRole(Control control)
            => control is TextBoxBase || control is ListBox || control is ListView || control is ComboBox
               || control is UpDownBase || control is TreeView
                ? ThemeRole.SurfaceRaised
                : ThemeRole.SurfaceWindow;

        /// <summary>
        /// Buttons: flat in dark (the visual-style face ignores every colour and stays light), exactly as
        /// built otherwise. A button built with a role colour (the confirmation dialog's destructive answer
        /// on <c>danger</c>) keeps that role as its face.
        /// </summary>
        private static void PaintButton(Button button, State state, ThemePalette palette)
        {
            ThemeRole? backRole = state.BackExplicit ? ThemePalette.RoleOfDesignColor(state.Back) : null;
            bool neutral = backRole == null || backRole == ThemeRole.SurfaceWindow || backRole == ThemeRole.Control;
            if (palette.IsDark)
            {
                button.FlatStyle = FlatStyle.Flat;
                Color face = neutral ? palette.Control : palette[backRole!.Value];
                Set(button, true, face);
                Set(button, false, state.ForeExplicit
                    ? palette[ThemePalette.RoleOfDesignColor(state.Fore) ?? ThemeRole.TextPrimary]
                    : palette.TextPrimary);
                button.FlatAppearance.BorderColor = neutral ? palette.Border : face;
                button.FlatAppearance.MouseOverBackColor = neutral ? palette.ControlHover : Color.Empty;
                button.FlatAppearance.MouseDownBackColor = neutral ? palette.ControlPressed : Color.Empty;
                button.UseVisualStyleBackColor = false;
                return;
            }

            button.FlatStyle = state.Flat;
            PaintColor(button, true, state.BackExplicit, state.Back, false, ThemeRole.Control, palette);
            PaintColor(button, false, state.ForeExplicit, state.Fore, false, ThemeRole.TextPrimary, palette);
            if (!state.BackExplicit) button.ResetBackColor();
            if (!state.ForeExplicit) button.ResetForeColor();
            button.FlatAppearance.BorderColor = state.BackExplicit && palette.Kind == ThemeKind.HighContrast && backRole != null
                ? palette[backRole.Value] : state.FlatBorder;
            button.FlatAppearance.MouseOverBackColor = state.FlatOver;
            button.FlatAppearance.MouseDownBackColor = state.FlatDown;
            // Last: assigning BackColor switches the visual-style face off.
            RestoreVisualStyle(button, state);
        }

        // ---- The native part ----

        private static void Native(Control control, State state, ThemePalette palette)
        {
            bool dark = palette.IsDark;
            if (control is Form form)
            {
                ThemeWin32.SetTitleBar(form.Handle, dark, form.Visible);
                return;
            }

            string? subApp = control switch
            {
                ListView _ => ThemeWin32.ExplorerDark,
                ListBox _ => ThemeWin32.ExplorerDark,
                TreeView _ => ThemeWin32.ExplorerDark,
                TextBoxBase box => box.Multiline ? ThemeWin32.ExplorerDark : ThemeWin32.EditDark,
                ComboBox _ => ThemeWin32.EditDark,
                ScrollableControl scrollable when scrollable.AutoScroll => ThemeWin32.ExplorerDark,
                _ => null,
            };
            if (subApp == null) return;
            // Nothing to undo on a control that was never dark: its theme is still the one WinForms gave it.
            if (!dark && !state.NativeDark) return;

            ThemeWin32.SetTheme(control.Handle, dark ? subApp : null);
            if (control is ListView list) ThemeWin32.SetListViewHeaderTheme(list, dark ? ThemeWin32.HeaderDark : null);
            if (control is ComboBox combo) ThemeWin32.SetComboListTheme(combo, dark ? ThemeWin32.ExplorerDark : null);
            state.NativeDark = dark;
        }

        // ---- Late arrivals ----

        private static void OnControlAdded(object? sender, ControlEventArgs e)
        {
            if (sender is Control parent && e.Control != null && States.TryGetValue(parent, out State? state)
                && state.Palette != null)
                Apply(e.Control, state.Palette);
        }

        private static void OnHandleCreated(object? sender, EventArgs e)
        {
            if (sender is Control control && States.TryGetValue(control, out State? state) && state.Palette != null)
                Native(control, state, state.Palette);
        }

        // ---- List-view headers ----

        private static void OnDrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
        {
            ThemePalette? palette = sender is ListView list ? PaletteOf(list) : null;
            if (palette == null || !palette.IsDark || e.Header == null)
            {
                e.DrawDefault = true;
                return;
            }
            var owner = (ListView)sender!;
            Rectangle bounds = e.Bounds;
            using (var face = new SolidBrush(palette.Control)) e.Graphics.FillRectangle(face, bounds);
            using (var line = new Pen(palette.Border))
            {
                e.Graphics.DrawLine(line, bounds.Right - 1, bounds.Top + 4, bounds.Right - 1, bounds.Bottom - 5);
                e.Graphics.DrawLine(line, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
            }
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            flags |= e.Header.TextAlign == HorizontalAlignment.Center ? TextFormatFlags.HorizontalCenter
                : e.Header.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left;
            if (owner.RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;
            Rectangle text = new Rectangle(bounds.X + 6, bounds.Y, Math.Max(0, bounds.Width - 12), bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, e.Font ?? owner.Font, text, palette.TextPrimary, flags);
        }

        private static void OnDrawItem(object? sender, DrawListViewItemEventArgs e) => e.DrawDefault = true;

        private static void OnDrawSubItem(object? sender, DrawListViewSubItemEventArgs e) => e.DrawDefault = true;

        // ---- Disabled text ----

        /// <summary>
        /// WinForms derives a disabled caption's colour from the background - <c>ControlPaint.Dark</c> of
        /// it - which on a dark surface is darker still, i.e. invisible. In dark, a disabled label, check
        /// box, radio button or button paints its caption again in <c>text.disabled</c>. The handler runs
        /// after the control's own painting and returns at once in every other case.
        /// </summary>
        private static void OnPaintDisabled(object? sender, PaintEventArgs e)
        {
            if (!(sender is Control control) || control.Enabled || control.Text.Length == 0 && control is Label) return;
            ThemePalette? palette = PaletteOf(control);
            if (palette == null || !palette.IsDark) return;
            try
            {
                switch (control)
                {
                    case Label label: RepaintLabel(label, e.Graphics, palette); break;
                    case CheckBox box: RepaintCheck(box, e.Graphics, palette, true, box.CheckState); break;
                    case RadioButton radio: RepaintCheck(radio, e.Graphics, palette, false, radio.Checked ? CheckState.Checked : CheckState.Unchecked); break;
                    case Button button: RepaintButton(button, e.Graphics, palette); break;
                }
            }
            catch (ArgumentException) { /* a disposed font mid-teardown - the native paint stands */ }
        }

        private static readonly MethodInfo? LabelFlags = typeof(Label).GetMethod("CreateTextFormatFlags",
            BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);

        private static void RepaintLabel(Label label, Graphics g, ThemePalette palette)
        {
            using (var back = new SolidBrush(label.BackColor)) g.FillRectangle(back, label.ClientRectangle);
            Rectangle face = label.ClientRectangle;
            face = new Rectangle(face.X + label.Padding.Left, face.Y + label.Padding.Top,
                Math.Max(0, face.Width - label.Padding.Horizontal), Math.Max(0, face.Height - label.Padding.Vertical));
            // The label's own flags, so a wrapped hint wraps exactly where it did while enabled.
            TextFormatFlags flags;
            try { flags = LabelFlags != null ? (TextFormatFlags)LabelFlags.Invoke(label, null) : FallbackFlags(label); }
            catch (TargetInvocationException) { flags = FallbackFlags(label); }
            TextRenderer.DrawText(g, label.Text, label.Font, face, palette.TextDisabled, flags);
        }

        private static TextFormatFlags FallbackFlags(Label label)
        {
            TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;
            if (label.AutoEllipsis) flags |= TextFormatFlags.EndEllipsis;
            if (!label.UseMnemonic) flags |= TextFormatFlags.NoPrefix;
            if (label.RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft | TextFormatFlags.Right;
            return flags;
        }

        /// <summary>
        /// A check box or radio button, glyph and caption both, so the two always agree on which side the
        /// glyph is: logical left, which a mirrored (right-to-left layout) window shows on the right, or
        /// the right for a right-to-left control in an unmirrored window.
        /// </summary>
        private static void RepaintCheck(ButtonBase control, Graphics g, ThemePalette palette, bool checkBox, CheckState state)
        {
            Rectangle client = control.ClientRectangle;
            using (var back = new SolidBrush(control.BackColor)) g.FillRectangle(back, client);
            Size glyph = checkBox
                ? CheckBoxRenderer.GetGlyphSize(g, CheckBoxState.UncheckedDisabled)
                : RadioButtonRenderer.GetGlyphSize(g, RadioButtonState.UncheckedDisabled);
            bool glyphRight = control.RightToLeft == RightToLeft.Yes && !IsMirrored(control);
            int glyphX = glyphRight ? client.Right - control.Padding.Right - glyph.Width : client.Left + control.Padding.Left;
            var glyphAt = new Point(glyphX, client.Top + (client.Height - glyph.Height) / 2);
            if (checkBox)
                CheckBoxRenderer.DrawCheckBox(g, glyphAt, state == CheckState.Checked ? CheckBoxState.CheckedDisabled
                    : state == CheckState.Indeterminate ? CheckBoxState.MixedDisabled : CheckBoxState.UncheckedDisabled);
            else
                RadioButtonRenderer.DrawRadioButton(g, glyphAt, state == CheckState.Checked ? RadioButtonState.CheckedDisabled : RadioButtonState.UncheckedDisabled);
            if (control.Text.Length == 0) return;

            const int gap = 3;
            int width = Math.Max(0, client.Width - glyph.Width - gap - control.Padding.Horizontal);
            var text = glyphRight
                ? new Rectangle(client.Left + control.Padding.Left, client.Top, width, client.Height)
                : new Rectangle(glyphX + glyph.Width + gap, client.Top, width, client.Height);
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            flags |= glyphRight ? TextFormatFlags.Right : TextFormatFlags.Left;
            if (control.RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;
            TextRenderer.DrawText(g, control.Text, control.Font, text, palette.TextDisabled, flags);
        }

        private static void RepaintButton(Button button, Graphics g, ThemePalette palette)
        {
            Rectangle client = button.ClientRectangle;
            using (var back = new SolidBrush(button.BackColor)) g.FillRectangle(back, client);
            using (var border = new Pen(palette.Border))
                g.DrawRectangle(border, client.X, client.Y, client.Width - 1, client.Height - 1);
            // SingleLine: DrawText centres vertically only on a single line, and a tall glyph in a short
            // button (the arrow buttons) otherwise loses its lower half. No button here wraps.
            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            if (button.RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;
            Rectangle text = new Rectangle(client.X + button.Padding.Left, client.Y + button.Padding.Top,
                Math.Max(0, client.Width - button.Padding.Horizontal), Math.Max(0, client.Height - button.Padding.Vertical));
            TextRenderer.DrawText(g, button.Text, button.Font, text, palette.TextDisabled, flags);
        }

        private static bool IsMirrored(Control control)
        {
            if (!control.IsHandleCreated) return false;
            try { return (WindowInterop.GetWindowLong(control.Handle, GWL_EXSTYLE) & WS_EX_LAYOUTRTL) != 0; }
            catch (EntryPointNotFoundException) { return false; }
        }
    }
}

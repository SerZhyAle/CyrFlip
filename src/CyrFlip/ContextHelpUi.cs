using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace CyrFlip
{
    internal static class ContextHelpUi
    {
        private static readonly ConditionalWeakTable<Form, Button> Buttons = new ConditionalWeakTable<Form, Button>();

        private static readonly ConditionalWeakTable<Button, ToolTip> Tips = new ConditionalWeakTable<Button, ToolTip>();

        internal static void RefreshLanguage(ThemedForm form)
        {
            if (!Buttons.TryGetValue(form, out Button? button)) return;
            button.AccessibleName = Localization.Translate(form.HelpLanguage, "Help (F1)");
            if (Tips.TryGetValue(button, out ToolTip? tips)) tips.SetToolTip(button, button.AccessibleName);
        }

        internal static Button Button(Form form)
        {
            string language = form is ThemedForm themed ? themed.HelpLanguage : ContextHelp.ApplicationLanguage();
            var button = new Button { Name = "contextHelp", Size = new Size(44, 44), AutoSize = false, TabStop = true,
                Margin = Padding.Empty, AccessibleName = Localization.Translate(language, "Help (F1)"), UseMnemonic = false };
            ButtonGlyphs.Attach(button, AppGlyphs.Help);
            var tips = new ToolTip { ShowAlways = true };
            Tips.Add(button, tips);
            tips.SetToolTip(button, button.AccessibleName);
            button.Disposed += (_, _) => tips.Dispose();
            button.Click += (_, _) => { if (!(form is ScreenshotToolbar)) ContextHelp.Open(form); };
            return button;
        }

        internal static void Attach(Form form)
        {
            if (Buttons.TryGetValue(form, out _) || form is SettingsForm || form is ScreenshotToolbar || form is TrafficLightWindow) return;
            ContextHelp.Surface? row = ContextHelp.Inventory.surfaces.FirstOrDefault(s => s.type == form.GetType().FullName);
            if (row == null || row.form == "none") return;
            Button button = Button(form);
            Buttons.Add(form, button);
            int edge = Math.Max(44, (int)Math.Round(44.0 * form.DeviceDpi / 96));
            button.Size = new Size(edge, edge);
            TableLayoutPanel? table = form.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
            if (row.form != "layer" && table != null)
            {
                table.SuspendLayout();
                int column = table.ColumnCount;
                while (table.ColumnStyles.Count < column) table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                table.ColumnCount = column + 1;
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, edge));
                button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                table.Controls.Add(button, column, 0);
                table.ResumeLayout(true);
                return;
            }
            if (row.form != "layer" && !(form is ClipboardHistoryWindow))
            {
                // A reserved trailing column leaves the first content row and all commands intact.
                // Companion windows keep the user's saved outer size; modal dialogs gain one column.
                form.SuspendLayout();
                Control[] original = form.Controls.Cast<Control>().ToArray();
                var bounds = original.ToDictionary(c => c, c => c.Bounds);
                bool companion = form is QuickNotesWindow || form is SymbolsWindow || form is TranslationResultWindow || form is ClipboardHistorySearchWindow;
                bool auto = form.AutoSize;
                form.AutoSize = false;
                if (!companion) form.ClientSize = new Size(form.ClientSize.Width + edge, form.ClientSize.Height);
                form.Padding = new Padding(form.Padding.Left, form.Padding.Top, form.Padding.Right + edge, form.Padding.Bottom);
                foreach (Control control in original.Where(c => c.Dock == DockStyle.None)) control.Bounds = bounds[control];
                form.AutoSize = auto;
                form.ResumeLayout(true);
            }
            button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            form.Controls.Add(button);
            void Position()
            {
                button.Location = new Point(form.RightToLeft == RightToLeft.Yes && !form.RightToLeftLayout ? 0 : Math.Max(0, form.ClientSize.Width - button.Width), 0);
                button.BringToFront();
            }
            form.ClientSizeChanged += (_, _) => Position();
            form.RightToLeftChanged += (_, _) => Position();
            Position();
        }
    }

    /// <summary>The capture remains active: this is a child control, never a second window.</summary>
    internal static class CaptureHelpLayer
    {
        private static readonly ConditionalWeakTable<Form, Panel> Layers = new ConditionalWeakTable<Form, Panel>();
        internal static void Toggle(Form form)
        {
            if (Layers.TryGetValue(form, out Panel? open)) { open.Dispose(); return; }
            Control? previous = form.ActiveControl;
            string language = ContextHelp.ApplicationLanguage();
            string T(string key) => Localization.Translate(language, key);
            var panel = new Panel { Name = "captureHelp", Dock = DockStyle.Fill, TabStop = true, BackColor = ThemePalette.Light.SurfaceWindow, AutoScroll = true };
            Layers.Add(form, panel);
            var rows = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12) };
            var instructions = new[] { T("Drag to select or draw. Help does not change the frozen capture."), T("Use the toolbar to choose a shape and colour. Clear removes drawings; Cancel discards the capture."), T("Enter accepts the capture. Escape cancels. Close help first to return to editing.") };
            foreach (string instruction in instructions)
            {
                var marker = new Button { Text = (rows.Controls.Count + 1) + ". " + instruction, AutoSize = true, MinimumSize = new Size(44, 44), MaximumSize = new Size(Math.Max(200, form.ClientSize.Width - 40), 0), TabStop = true };
                marker.AccessibleName = marker.Text;
                rows.Controls.Add(marker);
            }
            var close = new Button { Text = T("Help (F1)"), AutoSize = true, MinimumSize = new Size(44, 44) };
            close.Click += (_, _) => panel.Dispose();
            rows.Controls.Add(close);
            panel.Controls.Add(rows);
            panel.MouseDown += (_, _) => panel.Dispose();
            panel.Disposed += (_, _) => { Layers.Remove(form); if (previous != null && !previous.IsDisposed) previous.Focus(); };
            form.Controls.Add(panel);
            panel.BringToFront();
            ThemeApply.Apply(panel, ThemeApply.PaletteOf(form) ?? ThemePalette.Light);
            rows.Controls[0].Focus();
        }
        internal static bool HandleKey(Form form, Keys key)
        {
            if (!Layers.TryGetValue(form, out Panel? panel)) return false;
            if (key == Keys.Escape || key == Keys.F1) panel.Dispose();
            // Consume editing commands while the read-only explanation has the focus.
            return key != Keys.Tab && key != (Keys.Shift | Keys.Tab);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// Modal picker for adding a keyboard layout: the full list Windows can load
    /// (<see cref="InputLayouts.ListAvailable"/>), grouped by language, with a type-to-filter box so a
    /// user never has to scroll all 200-plus. Returns the chosen KLID via <see cref="SelectedKlid"/>.
    ///
    /// <para>Laid out by an auto-sizing <see cref="TableLayoutPanel"/>, never by pixel coordinates
    /// (ticket S0007, DL-3): the hint and both buttons are translated into 13 languages and drawn at
    /// whatever the display scaling makes of the UI font. The window itself stays resizable - it is a
    /// list - but its minimum size is measured from what it holds.</para>
    /// </summary>
    internal sealed class LayoutPickerDialog : ThemedForm
    {
        private readonly TextBox _filter = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(3, 3, 3, 6) };
        private readonly ListBox _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly List<InputLayouts.Available> _all;
        private readonly HashSet<string> _installed;
        // The script font for hi/bn/zh; WinForms never disposes a font assigned to a control.
        private readonly Font? _ownFont;

        public string? SelectedKlid { get; private set; }

        public LayoutPickerDialog(IEnumerable<string> alreadyInstalled, string uiLanguage, List<InputLayouts.Available>? available = null)
        {
            _all = available ?? InputLayouts.ListAvailable();
            _installed = new HashSet<string>(alreadyInstalled, StringComparer.OrdinalIgnoreCase);
            string T(string ru) => Localization.Translate(uiLanguage, ru);

            Text = T("Добавить раскладку");
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            if (Localization.IsRightToLeft(uiLanguage)) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }
            string? family = Localization.FontFamily(uiLanguage);
            if (family != null)
            {
                try { Font = _ownFont = new Font(family, Font.SizeInPoints); }
                catch { /* the font is missing on this machine - keep the default */ }
            }

            int width = Math.Max(Localization.Scaled(uiLanguage, 460), TextWidth(new string('W', 30)));
            var hint = new Label
            {
                Text = T("Введите язык или название раскладки для фильтра"),
                ForeColor = ThemePalette.Light.TextMuted, AutoSize = true,
                MaximumSize = new Size(width - 30, 0), // wrap rather than widen without limit
                Margin = new Padding(3, 3, 3, 4),
            };
            _filter.TextChanged += (_, _) => Populate();
            _list.DoubleClick += (_, _) => Accept();
            _list.Format += (_, e) => { if (e.ListItem is InputLayouts.Available a) e.Value = Format(a); };

            var add = Command(T("Добавить"), DialogResult.OK);
            var cancel = Command(T("Отмена"), DialogResult.Cancel);
            add.Click += (_, _) => Accept();

            // RightToLeft flow puts the first control added on the right; WinForms mirrors it for RTL UIs.
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
                Margin = new Padding(0, 8, 0, 0),
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(add);

            var layout = new TableLayoutPanel { ColumnCount = 1, RowCount = 4, Dock = DockStyle.Fill, Padding = new Padding(9) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(hint, 0, 0);
            layout.Controls.Add(_filter, 0, 1);
            layout.Controls.Add(_list, 0, 2);
            layout.Controls.Add(buttons, 0, 3);
            Controls.Add(layout);

            width = Math.Max(width, buttons.GetPreferredSize(Size.Empty).Width + 30);
            int fixedRows = hint.GetPreferredSize(Size.Empty).Height + _filter.PreferredHeight
                + buttons.GetPreferredSize(Size.Empty).Height + 60;
            ClientSize = new Size(width, Math.Max(420, fixedRows + 200));
            MinimumSize = SizeFromClientSize(new Size(width, fixedRows + 80));
            AcceptButton = add;
            CancelButton = cancel;

            Populate();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _ownFont?.Dispose();
        }

        private Button Command(string text, DialogResult result) => new Button
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(TextWidth("Cancel"), 0), DialogResult = result,
        };

        private int TextWidth(string text) => TextRenderer.MeasureText(text, Font).Width + 16;

        private void Populate()
        {
            string q = _filter.Text.Trim();
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (InputLayouts.Available a in _all)
            {
                if (_installed.Contains(a.Klid)) continue; // hide layouts already in the list
                if (q.Length > 0 &&
                    a.LanguageName.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) < 0 &&
                    a.DisplayName.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) < 0)
                    continue;
                _list.Items.Add(a);
            }
            _list.EndUpdate();
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        }

        private static string Format(InputLayouts.Available a)
            => a.LanguageName + " — " + a.DisplayName + "  (" + a.Klid + ")";

        private void Accept()
        {
            if (_list.SelectedItem is InputLayouts.Available a)
            {
                SelectedKlid = a.Klid;
                DialogResult = DialogResult.OK;
                Close();
            }
        }
    }
}

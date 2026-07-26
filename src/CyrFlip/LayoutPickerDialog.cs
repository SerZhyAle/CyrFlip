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
    /// </summary>
    internal sealed class LayoutPickerDialog : Form
    {
        private readonly TextBox _filter = new TextBox { Dock = DockStyle.Top };
        private readonly ListBox _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly List<InputLayouts.Available> _all;
        private readonly HashSet<string> _installed;

        public string? SelectedKlid { get; private set; }

        public LayoutPickerDialog(IEnumerable<string> alreadyInstalled, string title, string filterHint, string addText, string cancelText)
        {
            _all = InputLayouts.ListAvailable();
            _installed = new HashSet<string>(alreadyInstalled, StringComparer.OrdinalIgnoreCase);

            Text = title;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(460, 420);
            MinimumSize = new Size(360, 300);
            ShowInTaskbar = false;

            _filter.TextChanged += (_, _) => Populate();
            var hint = new Label { Dock = DockStyle.Top, Text = filterHint, ForeColor = SystemColors.GrayText, Padding = new Padding(2, 4, 2, 2), AutoSize = false, Height = 22 };
            _list.DoubleClick += (_, _) => Accept();
            _list.Format += (_, e) => { if (e.ListItem is InputLayouts.Available a) e.Value = Format(a); };

            var add = new Button { Text = addText, DialogResult = DialogResult.OK, Width = 120, Height = 28 };
            var cancel = new Button { Text = cancelText, DialogResult = DialogResult.Cancel, Width = 120, Height = 28 };
            add.Click += (_, _) => Accept();

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40, Padding = new Padding(6) };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(add);

            // Docked controls fill in reverse add-order, so add fill first, then the two top bars, then bottom.
            Controls.Add(_list);
            Controls.Add(_filter);
            Controls.Add(hint);
            Controls.Add(buttons);
            AcceptButton = add;
            CancelButton = cancel;

            Populate();
        }

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

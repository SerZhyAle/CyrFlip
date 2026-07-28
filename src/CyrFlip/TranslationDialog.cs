using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// Editor for one row of the translation table: which language to translate into, and on which
    /// chord. Laid out by an auto-sizing <see cref="TableLayoutPanel"/> like every other dialog here -
    /// captions exist in 13 languages and fixed geometry clips them.
    /// </summary>
    internal sealed class TranslationDialog : Form
    {
        private readonly string _uiLanguage;
        private readonly ComboBox _target = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Label _hotkey = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) };
        // One line, never wrapped: DialogLayoutTests measures a Label's unconstrained preferred size,
        // so a MaximumSize that wraps the text would read to it as a clipped caption.
        private readonly LinkLabel _coverage = new LinkLabel
        {
            AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 0, 3, 8),
        };

        public TranslationDialog(TranslationProfile? existing, string uiLanguage, string? model = null)
        {
            _uiLanguage = uiLanguage;
            Text = T(existing == null ? "Новое направление перевода" : "Изменить направление перевода");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ApplyScript(uiLanguage);

            // The two live tokens first - they are modes, not languages - then every language Windows
            // knows, sorted by the label actually drawn so the list reads alphabetically in this UI
            // language rather than by ISO code.
            _target.Items.Add(new LanguageItem(TranslationLanguages.UiToken, uiLanguage));
            _target.Items.Add(new LanguageItem(TranslationLanguages.ActiveToken, uiLanguage));
            var languages = new List<LanguageItem>();
            foreach (string code in TranslationLanguages.AllCodes)
                languages.Add(new LanguageItem(code, uiLanguage));
            languages.Sort((a, b) => string.Compare(a.ToString(), b.ToString(), StringComparison.CurrentCultureIgnoreCase));
            foreach (LanguageItem item in languages) _target.Items.Add(item);
            _target.Width = ComboWidth();
            SelectCode(existing?.TargetLang ?? TranslationLanguages.UiToken);

            // CyrFlip claims nothing about what the model understands - it dispatches, the model
            // translates. The one honest answer is the model's own page, so that is what we offer.
            _coverage.Text = T("Какие языки понимает модель - в её описании");
            _coverage.LinkClicked += (_, _) => OllamaManager.OpenModelPage(model);

            _hotkey.Text = string.IsNullOrEmpty(existing?.Hotkey) ? T("Не назначено") : existing!.Hotkey;
            _hotkey.MinimumSize = new Size(TextWidth("Ctrl+Shift+Backspace"), 0); // the longest chord we can produce

            var set = new Button
            {
                Text = T("Задать..."), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(8, 3, 3, 3),
            };
            set.Click += (_, _) => SetHotkey();

            var ok = new Button
            {
                Text = "OK", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(TextWidth("Cancel"), 0), DialogResult = DialogResult.OK,
            };
            ok.Click += (_, _) => BuildProfile(existing);
            var cancel = new Button
            {
                Text = T("Отмена"), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(TextWidth("Cancel"), 0), DialogResult = DialogResult.Cancel,
            };

            // RightToLeft flow, so the first control added sits rightmost - and WinForms mirrors the
            // whole thing for Arabic and Urdu, which puts OK on the left, as those UIs expect.
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill,
                Margin = new Padding(0, 12, 0, 0),
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);

            var grid = new TableLayoutPanel
            {
                ColumnCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill, Padding = new Padding(12),
            };
            grid.Controls.Add(Caption(T("Переводить на:")), 0, 0);
            grid.Controls.Add(_target, 1, 0);
            grid.SetColumnSpan(_target, 2);
            grid.Controls.Add(_coverage, 1, 1);
            grid.SetColumnSpan(_coverage, 2);
            grid.Controls.Add(Caption(T("Комбинация:")), 0, 2);
            grid.Controls.Add(_hotkey, 1, 2);
            grid.Controls.Add(set, 2, 2);
            grid.Controls.Add(buttons, 0, 3);
            grid.SetColumnSpan(buttons, 3);
            Controls.Add(grid);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        /// <summary>The edited row, or null when the dialog was cancelled or the input was rejected.</summary>
        public TranslationProfile? Profile { get; private set; }

        private void SetHotkey()
        {
            using var dialog = new HotkeyDialog(_hotkey.Text, T("Комбинация для перевода"), _uiLanguage);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.CapturedHotkey == null) return;
            _hotkey.Text = dialog.CapturedHotkey;
        }

        private void BuildProfile(TranslationProfile? existing)
        {
            if (!(_target.SelectedItem is LanguageItem target))
            {
                DialogResult = DialogResult.None;
                return;
            }

            // A row without a chord is a valid row: it sits in the table, inert, until the user gives
            // it one (that is also how the starter row arrives when its default chord is taken).
            string chord = _hotkey.Text;
            bool unassigned = chord.Length == 0 || chord == T("Не назначено");
            if (!unassigned && !Hotkey.TryParse(chord, out _))
            {
                DialogResult = DialogResult.None;
                return;
            }

            Profile = new TranslationProfile
            {
                Id = existing?.Id ?? Guid.NewGuid().ToString("N"),
                TargetLang = target.Code,
                Hotkey = unassigned ? "" : chord,
                Enabled = existing?.Enabled ?? true,
            };
        }

        private void SelectCode(string code)
        {
            for (int i = 0; i < _target.Items.Count; i++)
                if (_target.Items[i] is LanguageItem item
                    && string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase))
                {
                    _target.SelectedIndex = i;
                    return;
                }
            if (_target.Items.Count > 0) _target.SelectedIndex = 0;
        }

        private int ComboWidth()
        {
            int width = TextWidth(new string('W', 18));
            foreach (object item in _target.Items)
                width = Math.Max(width, TextWidth(item.ToString() ?? ""));
            return width;
        }

        /// <summary>Mirrors for Arabic/Urdu and picks a font that can draw the script.</summary>
        private void ApplyScript(string uiLanguage)
        {
            if (Localization.IsRightToLeft(uiLanguage)) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }
            string? family = Localization.FontFamily(uiLanguage);
            if (family == null) return;
            try { Font = new Font(family, Font.SizeInPoints); }
            catch { /* the font is missing on this machine - keep the default */ }
        }

        private static Label Caption(string text)
            => new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 10, 3) };

        private int TextWidth(string text) => TextRenderer.MeasureText(text, Font).Width + 16;

        private string T(string ru) => Localization.Translate(_uiLanguage, ru);

        /// <summary>Combo entry: shows the language label, carries the code the profile stores.</summary>
        private sealed class LanguageItem
        {
            public readonly string Code;
            private readonly string _label;

            public LanguageItem(string code, string uiLanguage)
            {
                Code = code;
                _label = TranslationLanguages.Label(code, uiLanguage);
            }

            public override string ToString() => _label;
        }
    }
}

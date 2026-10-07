using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>A modal, replayable tour. Edits apply immediately; navigation never commits them.</summary>
    internal sealed class WelcomeDialog : ThemedForm
    {
        private readonly WelcomeFacts _facts;
        private readonly Action<string> _setLanguage;
        private readonly Action<bool> _setAutostart;
        private readonly Func<bool> _autostartEnabled;
        private readonly TableLayoutPanel _root = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Padding = new Padding(16) };
        private readonly Panel _host = new Panel();
        private readonly Label _step = new Label { AutoSize = true, UseMnemonic = false };
        private readonly FlowLayoutPanel _buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false };
        private readonly Button _back = new Button { AutoSize = true, TabIndex = 0 };
        private readonly Button _next = new Button { AutoSize = true, TabIndex = 1 };
        private readonly Button _cancel = new Button { AutoSize = true, TabIndex = 2, DialogResult = DialogResult.Cancel };
        private readonly List<FlowLayoutPanel> _pages = new List<FlowLayoutPanel>();
        private Font? _ownFont;
        // WinForms can retain an equal ambient Font in native/control layout caches. Keep prior
        // language fonts alive until the entire control tree has been disposed.
        private readonly List<Font> _fonts = new List<Font>();
        private bool _rebuilding;
        private int _wrap;
        internal int PageIndex { get; private set; }
        internal SettingsPageKey? RequestedPage { get; private set; }
        private string T(string source) => Localization.Translate(_facts.Config.UiLanguage, source);
        private string F(string source, params object[] args) => Localization.Format(_facts.Config.UiLanguage, source, args);

        public WelcomeDialog(WelcomeFacts facts, Action<string> setLanguage, Action<bool> setAutostart, Func<bool> autostartEnabled)
        {
            _facts = facts; _setLanguage = setLanguage; _setAutostart = setAutostart; _autostartEnabled = autostartEnabled;
            Text = "CyrFlip"; ShowInTaskbar = true; ShowIcon = false;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.Manual;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _root.Controls.Add(_step); _root.Controls.Add(_host); _root.Controls.Add(_buttons);
            _buttons.Controls.AddRange(new Control[] { _back, _next, _cancel });
            Controls.Add(_root);
            _back.Click += (_, _) => Navigate(PageIndex - 1);
            _next.Click += (_, _) => { if (PageIndex == 4) OpenSettings(SettingsPageKey.General); else Navigate(PageIndex + 1); };
            _cancel.Click += (_, _) => Close();
            AcceptButton = _next; CancelButton = _cancel;
            Rebuild();
            Shown += (_, _) => { Place(); ForegroundActivator.Activate(this); };
        }

        private Label Paragraph(FlowLayoutPanel page, string text)
        {
            var label = new Label { Text = text, AutoSize = true, MaximumSize = new Size(_wrap, 0),
                Margin = new Padding(3, 4, 3, 7), UseMnemonic = false };
            page.Controls.Add(label); return label;
        }

        private FlowLayoutPanel Page(string title, string glyph)
        {
            var page = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown, WrapContents = false, Width = _wrap + 6,
                MaximumSize = new Size(_wrap + 6, 0), Margin = Padding.Empty };
            // Settings vocabulary glyphs; text remains the accessible caption.
            var heading = Paragraph(page, T(title));
            heading.MinimumSize = new Size(0, 24);
            heading.Padding = new Padding(30, 0, 0, 0);
            heading.Paint += (_, e) => GlyphRenderer.Draw(e.Graphics, glyph, new RectangleF(0, 0, 22, 22),
                heading.ForeColor, Localization.IsRightToLeft(_facts.Config.UiLanguage));
            _pages.Add(page); return page;
        }

        private void SettingsLink(FlowLayoutPanel page, string caption, SettingsPageKey key)
        {
            var link = new LinkLabel { Text = caption, AutoSize = true, MaximumSize = new Size(_wrap, 0), Margin = new Padding(3, 5, 3, 5) };
            link.LinkClicked += (_, _) => OpenSettings(key); page.Controls.Add(link);
        }

        private void BrowserLink(FlowLayoutPanel page, string caption, string url)
        {
            var link = new LinkLabel { Text = T(caption), AutoSize = true, Margin = new Padding(3, 5, 3, 5) };
            link.LinkClicked += (_, _) => { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } };
            page.Controls.Add(link);
        }

        private void Rebuild()
        {
            _rebuilding = true;
            SuspendLayout();
            _host.Controls.Clear();
            foreach (var old in _pages) old.Dispose();
            _pages.Clear();
            Font = _ownFont = new Font(Localization.FontFamily(_facts.Config.UiLanguage) ?? "Segoe UI", 9F);
            _fonts.Add(_ownFont);
            RightToLeft = Localization.IsRightToLeft(_facts.Config.UiLanguage) ? RightToLeft.Yes : RightToLeft.No;
            RightToLeftLayout = RightToLeft == RightToLeft.Yes;
            _wrap = Localization.Scaled(_facts.Config.UiLanguage, 480);
            var welcome = Page("Добро пожаловать в CyrFlip", AppGlyphs.Info);
            Paragraph(welcome, T("CyrFlip исправляет текст в неверной раскладке, показывает активный язык и живёт в трее. Всё можно изменить позже."));
            Paragraph(welcome, T("Язык интерфейса:"));
            var languages = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Math.Min(_wrap, 260), AccessibleName = T("Язык интерфейса:") };
            languages.Items.AddRange(Localization.Names); languages.SelectedIndex = Localization.IndexOf(_facts.Config.UiLanguage);
            welcome.Controls.Add(languages);
            languages.SelectedIndexChanged += (_, _) =>
            {
                if (_rebuilding) return;
                string language = Localization.Names[languages.SelectedIndex];
                _setLanguage(language); _facts.Config.UiLanguage = language;
                // Dispose the triggering control after its event has unwound.
                BeginInvoke(new Action(() => { Rebuild(); Place(); }));
            };

            var indicators = Page("Индикаторы", AppGlyphs.LayoutIndicator);
            Paragraph(indicators, F("Метка у каретки: {0}. Курсор мыши: {1} (меняется во всей системе). Значок в трее показывает раскладку.",
                _facts.State(_facts.Config.EnableCaretOverlay), _facts.State(_facts.Config.EnableCursorChange)));
            var badges = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            foreach (var layout in _facts.Layouts)
            {
                string code = WelcomeFacts.LayoutCode(layout.Klid);
                var badge = new Panel { Size = new Size(58, 32), AccessibleName = layout.LanguageName, TabStop = false };
                // Badge paint uses LayoutStyle's colours, deliberately outside theme (LAYOUT-PALETTE).
                badge.Paint += (_, e) => LayoutStyle.DrawCode(e.Graphics, code, Font, badge.ClientRectangle, layout.Klid);
                badges.Controls.Add(badge);
            }
            indicators.Controls.Add(badges);
            // S0055's live gate: CaretOverlay intentionally excludes our own windows. The
            // operator observed no marker here, so retain the specified static-example fallback.
            Paragraph(indicators, T("Переключите раскладку в своём редакторе и посмотрите на метку."));
            SettingsLink(indicators, T("Индикаторы"), SettingsPageKey.Indicators);

            var conversions = Page("Конвертация раскладок", AppGlyphs.ConvertLayout);
            Paragraph(conversions, _facts.ConversionText());
            Paragraph(conversions, F("Смена регистра: {0} ({1}) — если оставили CapsLock включённым.",
                _facts.Config.CaseHotkey, _facts.State(_facts.Config.EnableHotkeys && _facts.Config.EnableCaseHotkey)));
            Paragraph(conversions, T("CyrFlip копирует, преобразует и вставляет текст, затем возвращает буфер обмена."));
            Paragraph(conversions, F("Пример: выделите {0}, затем нажмите комбинацию выше.", "ghbdtn"));
            SettingsLink(conversions, T("Конвертация раскладок"), SettingsPageKey.Conversions);

            var modules = Page("Дополнительные возможности", AppGlyphs.Apps);
            Paragraph(modules, F("Снимок области экрана: {0} ({1}). Остальные модули включаются в настройках.",
                _facts.Config.ScreenshotHotkey, _facts.State(_facts.Config.EnableHotkeys && _facts.Config.EnableScreenshotHotkey)));
            SettingsLink(modules, T("Буфер обмена") + ": " + _facts.State(_facts.Config.EnableClipboardHistory), SettingsPageKey.Clipboard);
            SettingsLink(modules, T("Быстрые заметки") + ": " + _facts.State(_facts.Config.EnableQuickNotes), SettingsPageKey.QuickNotes);
            SettingsLink(modules, T("Своё контекстное меню над выделенным текстом") + ": " + _facts.State(_facts.Config.EnableContextMenu), SettingsPageKey.Hotkeys);
            SettingsLink(modules, T("Перевод") + ": " + _facts.State(_facts.Config.EnableTranslate), SettingsPageKey.Translate);
            SettingsLink(modules, T("Быстрый запуск") + ": " + _facts.State(_facts.Config.EnableScenarioLauncher), SettingsPageKey.Launcher);
            BrowserLink(modules, "Политика конфиденциальности", "https://serzhyale.github.io/CyrFlip/privacy.html");
            BrowserLink(modules, "Установка и предупреждение SmartScreen", "https://serzhyale.github.io/CyrFlip/trust.html");

            var tray = Page("Где находится CyrFlip", AppGlyphs.Settings);
            Paragraph(tray, T("Значок в трее: щелчок переключает язык, двойной щелчок открывает настройки, правый щелчок открывает меню."));
            Paragraph(tray, T("Windows 11 может скрыть новый значок за стрелкой ^. Перетащите его наружу, чтобы видеть постоянно."));
            if (_facts.ShortcutsExist) Paragraph(tray, T("Ярлыки CyrFlip доступны в меню «Пуск» или на рабочем столе."));
            if (_facts.Transient) Paragraph(tray, T("Сначала распакуйте CyrFlip в постоянную папку: временная папка исчезнет, и автозапуск с ярлыками перестанут работать."));
            if (!_facts.Packaged)
            {
                var startup = new CheckBox { Text = T("Запускать CyrFlip вместе с Windows"), AutoSize = true, Checked = _autostartEnabled() };
                bool updating = false;
                startup.CheckedChanged += (_, _) =>
                {
                    if (updating) return;
                    _setAutostart(startup.Checked);
                    updating = true;
                    startup.Checked = _autostartEnabled();
                    updating = false;
                };
                tray.Controls.Add(startup);
            }
            _back.Text = T("Назад"); _next.Text = T("Далее"); _cancel.Text = T("Пропустить");
            // Measure all pages while visible, then reserve the tallest for the entire language.
            int height = 0;
            foreach (var page in _pages)
            {
                _host.Controls.Add(page); page.PerformLayout();
                height = Math.Max(height, page.GetPreferredSize(new Size(_wrap + 6, 0)).Height);
            }
            _host.Size = new Size(_wrap + 6, height);
            _buttons.MinimumSize = new Size(_wrap + 6, 0);
            // RightToLeft mirrors a LeftToRight flow itself; reversing it again scrambles reading order.
            _buttons.FlowDirection = FlowDirection.LeftToRight;
            _rebuilding = false;
            Navigate(PageIndex);
            ResumeLayout(true); PerformLayout();
            // Mirroring recreates the native HWND. WinForms treats its WM_DESTROY as a cancel
            // while modal; this was a language edit, so keep pumping the same tour instead.
            if (Modal) DialogResult = DialogResult.None;
            if (IsHandleCreated) ThemeManager.Attach(this);
        }

        internal void Navigate(int index)
        {
            _buttons.SuspendLayout();
            PageIndex = Math.Max(0, Math.Min(4, index));
            for (int i = 0; i < _pages.Count; i++) _pages[i].Visible = i == PageIndex;
            _back.Visible = PageIndex > 0;
            _buttons.Controls.SetChildIndex(_back, 0);
            _buttons.Controls.SetChildIndex(_next, 1);
            _buttons.Controls.SetChildIndex(_cancel, 2);
            _next.Text = T(PageIndex == 4 ? "Открыть настройки" : "Далее");
            _cancel.Text = T(PageIndex == 4 ? "Закрыть" : "Пропустить");
            _step.Text = F("Шаг {0} из {1}", PageIndex + 1, 5);
            _buttons.ResumeLayout(true);
            _next.Focus();
        }


        private void OpenSettings(SettingsPageKey key) { RequestedPage = key; DialogResult = DialogResult.OK; Close(); }
        private void Place()
        {
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(work.Left + Math.Max(0, (work.Width - Width) / 2), work.Top + Math.Max(0, (work.Height - Height) / 2));
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) foreach (var font in _fonts) font.Dispose();
        }
    }
}

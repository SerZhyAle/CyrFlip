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
        private readonly StepDots _dots = new StepDots(PageCount);
        private readonly FlowLayoutPanel _progress = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false };
        private readonly FlowLayoutPanel _buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false };
        private readonly GlyphButton _back = new GlyphButton(AppGlyphs.Back, false) { TabIndex = 0 };
        private readonly GlyphButton _next = new GlyphButton(AppGlyphs.Forward, true) { TabIndex = 1 };
        private readonly GlyphButton _cancel = new GlyphButton(AppGlyphs.Close, false) { TabIndex = 2, DialogResult = DialogResult.Cancel };
        private readonly List<FlowLayoutPanel> _pages = new List<FlowLayoutPanel>();
        // Every live switch of the tour, so one change can re-read them all: turning a chord on may turn
        // the master switch on, and a refused change has to put its knob back.
        private readonly List<(ToggleSwitch Control, WelcomeSwitch Kind, string? Id)> _toggles = new List<(ToggleSwitch, WelcomeSwitch, string?)>();
        private readonly Action<WelcomeSwitch, string?, bool> _applySwitch;
        private bool _applying;
        private Font? _ownFont;
        private Font _titleFont = SystemFonts.DefaultFont;
        private Font _boldFont = SystemFonts.DefaultFont;
        // WinForms can retain an equal ambient Font in native/control layout caches. Keep prior
        // language fonts alive until the entire control tree has been disposed.
        private readonly List<Font> _fonts = new List<Font>();
        private bool _rebuilding;
        private int _wrap;
        private const int PageCount = 5;
        internal int PageIndex { get; private set; }
        internal SettingsPageKey? RequestedPage { get; private set; }
        private string T(string source) => Localization.Translate(_facts.Config.UiLanguage, source);
        private string F(string source, params object[] args) => Localization.Format(_facts.Config.UiLanguage, source, args);
        private int Px(int logical) => LogicalToDeviceUnits(logical);

        /// <param name="applySwitch">
        /// Applies one switch the way the settings window would. Null writes the value into the live
        /// configuration only (<see cref="WelcomeFacts.Set"/>) - what a tour without a tray behind it does.
        /// </param>
        public WelcomeDialog(WelcomeFacts facts, Action<string> setLanguage, Action<bool> setAutostart, Func<bool> autostartEnabled,
            Action<WelcomeSwitch, string?, bool>? applySwitch = null)
        {
            _facts = facts; _setLanguage = setLanguage; _setAutostart = setAutostart; _autostartEnabled = autostartEnabled;
            _applySwitch = applySwitch ?? facts.Set;
            Text = "CyrFlip"; ShowInTaskbar = true; ShowIcon = false;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.Manual;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _dots.Anchor = AnchorStyles.Left; _dots.Margin = new Padding(3, 0, Px(12), 0);
            _progress.Margin = new Padding(0, 0, 0, Px(10));
            _progress.Controls.Add(_dots); _progress.Controls.Add(_step);
            _root.Controls.Add(_progress); _root.Controls.Add(_host); _root.Controls.Add(_buttons);
            _buttons.Controls.AddRange(new Control[] { _back, _next, _cancel });
            Controls.Add(_root);
            _back.Click += (_, _) => Navigate(PageIndex - 1);
            _next.Click += (_, _) => { if (PageIndex == PageCount - 1) OpenSettings(SettingsPageKey.General); else Navigate(PageIndex + 1); };
            _cancel.Click += (_, _) => Close();
            AcceptButton = _next; CancelButton = _cancel;
            Rebuild();
            Shown += (_, _) => { Place(); ForegroundActivator.Activate(this); };
        }

        // ---- building blocks -------------------------------------------------------------------

        private Label Paragraph(FlowLayoutPanel page, string text)
        {
            var label = new Label { Text = text, AutoSize = true, MaximumSize = new Size(_wrap, 0),
                Margin = new Padding(3, 4, 3, 7), UseMnemonic = false };
            page.Controls.Add(label); return label;
        }

        /// <summary>A secondary line: same wrapping, drawn in the muted text role.</summary>
        private Label Note(FlowLayoutPanel page, string text)
        {
            Label label = Paragraph(page, text);
            label.ForeColor = ThemePalette.Light.TextMuted;
            return label;
        }

        private Control Separator() => new WelcomeRule(_wrap + 6) { Margin = new Padding(0, 0, 0, Px(10)) };

        private static Font Styled(string family, float size, FontStyle style)
        {
            // A family without a bold face throws; a plain face is a lesser evil than a dead tour.
            try { return new Font(family, size, style); }
            catch (ArgumentException) { return new Font(family, size, FontStyle.Regular); }
        }

        private FlowLayoutPanel Page(string title, string glyph)
        {
            var page = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown, WrapContents = false, Width = _wrap + 6,
                MaximumSize = new Size(_wrap + 6, 0), Margin = Padding.Empty };
            // The header: a solid vocabulary-glyph plate and the title beside it. The plate is decoration;
            // the title stays the page's accessible caption.
            int plate = Px(52);
            var head = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2,
                RowCount = 1, Margin = new Padding(0, 0, 0, Px(8)), Font = Font, RightToLeft = RightToLeft };
            head.Controls.Add(new GlyphBadge(glyph, null, plate, true) { Anchor = AnchorStyles.Left, Margin = new Padding(3, 2, Px(14), 2) }, 0, 0);
            head.Controls.Add(new Label { Text = T(title), Font = _titleFont, AutoSize = true, UseMnemonic = false,
                MaximumSize = new Size(Math.Max(Px(120), _wrap - plate - Px(24)), 0), Anchor = AnchorStyles.Left,
                Margin = Padding.Empty }, 1, 0);
            page.Controls.Add(head);
            page.Controls.Add(Separator());
            _pages.Add(page); return page;
        }

        /// <summary>
        /// One feature on a soft glyph plate: a title (a link when <paramref name="open"/> names a settings
        /// page), an optional line beneath it, optional keys under that, and an optional trailing control
        /// (the state pill). With no title the line is plain body text - a callout.
        /// </summary>
        private TableLayoutPanel Row(FlowLayoutPanel page, string? glyph, string? mark, string? title, string? text,
            Control? detail = null, Control? trailing = null, SettingsPageKey? open = null)
        {
            int plate = Px(34), gap = Px(10);
            if (trailing != null) { trailing.Font = Font; trailing.RightToLeft = RightToLeft; }
            int trailing_w = trailing == null ? 0 : trailing.GetPreferredSize(Size.Empty).Width + gap;
            int textMax = Math.Max(Px(140), _wrap - plate - gap - trailing_w - Px(14));
            var row = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, RowCount = 1,
                MinimumSize = new Size(_wrap + 6, 0), Margin = new Padding(0, Px(5), 0, Px(5)), Font = Font, RightToLeft = RightToLeft };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var badge = new GlyphBadge(glyph, mark, plate, false) { Anchor = AnchorStyles.Left, Margin = new Padding(3, 2, gap, 2) };
            row.Controls.Add(badge, 0, 0);

            var body = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Left };
            if (title != null)
            {
                Label head;
                if (open is SettingsPageKey key)
                {
                    var link = new LinkLabel { Text = title, LinkBehavior = LinkBehavior.HoverUnderline };
                    link.LinkClicked += (_, _) => OpenSettings(key);
                    badge.Cursor = Cursors.Hand; badge.Click += (_, _) => OpenSettings(key);
                    head = link;
                }
                else head = new Label { Text = title };
                head.Font = _boldFont; head.AutoSize = true; head.UseMnemonic = false;
                head.MaximumSize = new Size(textMax, 0); head.Margin = Padding.Empty;
                body.Controls.Add(head);
            }
            if (text != null)
            {
                var line = new Label { Text = text, AutoSize = true, UseMnemonic = false, MaximumSize = new Size(textMax, 0),
                    Margin = new Padding(0, title != null ? 1 : 0, 0, 0) };
                if (title != null) line.ForeColor = ThemePalette.Light.TextMuted;
                body.Controls.Add(line);
            }
            if (detail != null)
            {
                detail.Margin = new Padding(0, Px(4), 0, 0);
                body.Controls.Add(detail);
            }
            row.Controls.Add(body, 1, 0);
            if (trailing != null)
            {
                trailing.Anchor = AnchorStyles.Left; trailing.Margin = new Padding(gap, 0, 3, 0);
                row.Controls.Add(trailing, 2, 0);
            }
            page.Controls.Add(row);
            return row;
        }

        /// <summary>
        /// A live switch for one feature: it shows the real state, and a flip is applied at once - the
        /// same way the settings window applies it - then every switch is re-read from the configuration,
        /// so a change that was refused (a chord another action owns) puts its knob back.
        /// </summary>
        private ToggleSwitch Switch(WelcomeSwitch kind, string? id, string name)
        {
            var toggle = new ToggleSwitch(_facts.IsOn(kind, id)) { AccessibleName = name, Font = Font, RightToLeft = RightToLeft,
                AccessibleDescription = _facts.State(_facts.IsOn(kind, id)) };
            _toggles.Add((toggle, kind, id));
            toggle.Toggled += (_, _) =>
            {
                if (_rebuilding || _applying) return;
                _applying = true;
                try { _applySwitch(kind, id, toggle.Checked); }
                catch (Exception ex) { TrayLog.Log("Welcome switch " + kind + " failed: " + ex.GetType().Name); }
                finally { _applying = false; }
                RefreshSwitches();
            };
            return toggle;
        }

        private void RefreshSwitches()
        {
            foreach (var (control, kind, id) in _toggles)
            {
                bool on = _facts.IsOn(kind, id);
                control.Checked = on;
                control.AccessibleDescription = _facts.State(on);
            }
        }

        /// <summary>The trailing group of a row: the chord as keys, when it has one, and the live switch.</summary>
        private FlowLayoutPanel Trailing(string? chord, WelcomeSwitch kind, string? id, string name)
        {
            var group = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
                Font = Font, RightToLeft = RightToLeft, Margin = Padding.Empty };
            if (chord != null)
                group.Controls.Add(new KeyCaps(chord) { Anchor = AnchorStyles.Left, Margin = new Padding(3, 0, Px(10), 0) });
            var toggle = Switch(kind, id, name);
            toggle.Anchor = AnchorStyles.Left; toggle.Margin = new Padding(3, 0, 3, 0);
            group.Controls.Add(toggle);
            return group;
        }

        private Panel LayoutBadge(string klid, string name, int width, int height)
        {
            string code = WelcomeFacts.LayoutCode(klid);
            var badge = new Panel { Size = new Size(width, height), AccessibleName = name, TabStop = false };
            // Badge paint uses LayoutStyle's colours, deliberately outside theme (LAYOUT-PALETTE).
            badge.Paint += (_, e) => LayoutStyle.DrawCode(e.Graphics, code, Font, badge.ClientRectangle, klid);
            return badge;
        }

        private void BrowserLink(FlowLayoutPanel page, string caption, string url)
        {
            var link = new LinkLabel { Text = T(caption), AutoSize = true, Margin = new Padding(3, 5, 3, 5) };
            link.LinkClicked += (_, _) => { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } };
            page.Controls.Add(link);
        }

        // ---- pages -----------------------------------------------------------------------------

        private void Rebuild()
        {
            _rebuilding = true;
            SuspendLayout();
            _host.Controls.Clear();
            foreach (var old in _pages) old.Dispose();
            _pages.Clear(); _toggles.Clear();
            string family = Localization.FontFamily(_facts.Config.UiLanguage) ?? "Segoe UI";
            Font = _ownFont = new Font(family, 9F);
            _titleFont = Styled(family, 15F, FontStyle.Bold);
            _boldFont = Styled(family, 9F, FontStyle.Bold);
            _fonts.Add(_ownFont); _fonts.Add(_titleFont); _fonts.Add(_boldFont);
            RightToLeft = Localization.IsRightToLeft(_facts.Config.UiLanguage) ? RightToLeft.Yes : RightToLeft.No;
            RightToLeftLayout = RightToLeft == RightToLeft.Yes;
            _wrap = Px(Localization.Scaled(_facts.Config.UiLanguage, 480));

            BuildWelcome();
            BuildIndicators();
            BuildConversions();
            BuildModules();
            BuildTray();

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
            _progress.FlowDirection = FlowDirection.LeftToRight;
            _rebuilding = false;
            Navigate(PageIndex);
            ResumeLayout(true); PerformLayout();
            // Mirroring recreates the native HWND. WinForms treats its WM_DESTROY as a cancel
            // while modal; this was a language edit, so keep pumping the same tour instead.
            if (Modal) DialogResult = DialogResult.None;
            if (IsHandleCreated) ThemeManager.Attach(this);
        }

        private void BuildWelcome()
        {
            var welcome = Page("Добро пожаловать в CyrFlip", AppGlyphs.Info);
            Row(welcome, AppGlyphs.ConvertLayout, null, null, T("Исправляет текст, набранный в неверной раскладке"));
            Row(welcome, AppGlyphs.LayoutIndicator, null, null, T("Показывает активный язык у каретки и в трее"));
            Row(welcome, AppGlyphs.Apps, null, null, T("Живёт в трее и не мешает работе"));

            // The one control on this page: the interface language, with the globe the settings use for it.
            var language = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
                Margin = new Padding(0, Px(10), 0, Px(4)), Font = Font, RightToLeft = RightToLeft };
            language.Controls.Add(new GlyphBadge(AppGlyphs.InputLanguage, null, Px(34), false) { Margin = new Padding(3, 2, Px(10), 2), Anchor = AnchorStyles.Left });
            language.Controls.Add(new Label { Text = T("Язык интерфейса:"), AutoSize = true, UseMnemonic = false, Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, Px(8), 0) });
            var languages = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Math.Min(_wrap / 2, Px(240)),
                AccessibleName = T("Язык интерфейса:"), Anchor = AnchorStyles.Left };
            languages.Items.AddRange(Localization.Names); languages.SelectedIndex = Localization.IndexOf(_facts.Config.UiLanguage);
            language.Controls.Add(languages);
            welcome.Controls.Add(language);
            languages.SelectedIndexChanged += (_, _) =>
            {
                if (_rebuilding) return;
                string chosen = Localization.Names[languages.SelectedIndex];
                _setLanguage(chosen); _facts.Config.UiLanguage = chosen;
                // Dispose the triggering control after its event has unwound.
                BeginInvoke(new Action(() => { Rebuild(); Place(); }));
            };
            Note(welcome, T("Всё можно изменить позже."));
        }

        private void BuildIndicators()
        {
            var config = _facts.Config;
            var indicators = Page("Индикаторы", AppGlyphs.LayoutIndicator);
            var badges = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
                Margin = new Padding(0, 0, 0, Px(4)), Font = Font };
            foreach (var layout in _facts.Layouts) badges.Controls.Add(LayoutBadge(layout.Klid, layout.LanguageName, Px(66), Px(36)));
            indicators.Controls.Add(badges);
            // S0055's live gate: CaretOverlay intentionally excludes our own windows. The
            // operator observed no marker here, so retain the specified static-example fallback.
            Note(indicators, T("Переключите раскладку в своём редакторе и посмотрите на метку."));
            Row(indicators, AppGlyphs.LayoutIndicator, null, T("Метка у каретки"), T("Рядом с мигающим курсором ввода"),
                trailing: Trailing(null, WelcomeSwitch.CaretMarker, null, T("Метка у каретки")), open: SettingsPageKey.Indicators);
            // The mouse pointer being replaced is the I-beam itself, so the plate carries its letter.
            Row(indicators, null, "I", T("Курсор мыши"), T("Меняется во всей системе"),
                trailing: Trailing(null, WelcomeSwitch.MouseCursor, null, T("Курсор мыши")), open: SettingsPageKey.Indicators);
            Row(indicators, AppGlyphs.InputLanguage, null, T("Значок в трее"), T("Всегда показывает текущую раскладку"));
        }

        private void BuildConversions()
        {
            var config = _facts.Config;
            var conversions = Page("Конвертация раскладок", AppGlyphs.ConvertLayout);
            var rows = _facts.ConversionRows();
            if (rows.Count == 0)
            {
                Paragraph(conversions, T("Таблица конвертации пуста. Добавьте пару в настройках."));
                Row(conversions, AppGlyphs.ConvertLayout, null, T("Конвертация раскладок"), null, open: SettingsPageKey.Conversions);
            }
            else
            {
                Note(conversions, T("Выделите текст и нажмите комбинацию."));
                foreach (var row in rows)
                    Row(conversions, AppGlyphs.ConvertLayout, null, row.Source + " ⇄ " + row.Target, null,
                        detail: new KeyCaps(row.Hotkey), trailing: Trailing(null, WelcomeSwitch.Conversion, row.Id, row.Source + " ⇄ " + row.Target),
                        open: SettingsPageKey.Conversions);
            }
            Row(conversions, null, "Aa", T("Смена регистра"), T("Если оставили CapsLock включённым"),
                detail: new KeyCaps(config.CaseHotkey), trailing: Trailing(null, WelcomeSwitch.CaseFlip, null, T("Смена регистра")),
                open: SettingsPageKey.Hotkeys);
            Paragraph(conversions, F("Пример: выделите {0}, затем нажмите комбинацию выше.", "ghbdtn")).Margin = new Padding(3, Px(8), 3, Px(4));
            var example = new ConversionExample("ghbdtn", "привет") { Margin = new Padding(3, 0, 3, Px(6)), Font = Font, RightToLeft = RightToLeft };
            conversions.Controls.Add(example);
            Note(conversions, T("CyrFlip копирует, преобразует и вставляет текст, затем возвращает буфер обмена."));
        }

        private void BuildModules()
        {
            var config = _facts.Config;
            var modules = Page("Дополнительные возможности", AppGlyphs.Apps);
            Row(modules, AppGlyphs.Screenshot, null, T("Снимок области экрана"), null,
                trailing: Trailing(config.ScreenshotHotkey, WelcomeSwitch.Screenshot, null, T("Снимок области экрана")), open: SettingsPageKey.Graphics);
            Row(modules, AppGlyphs.ClipboardHistory, null, T("Буфер обмена"), null,
                trailing: Trailing(config.ClipboardHistoryHotkey, WelcomeSwitch.ClipboardHistory, null, T("Буфер обмена")), open: SettingsPageKey.Clipboard);
            Row(modules, AppGlyphs.Note, null, T("Быстрые заметки"), null,
                trailing: Trailing(config.QuickNotesHotkey, WelcomeSwitch.QuickNotes, null, T("Быстрые заметки")), open: SettingsPageKey.QuickNotes);
            Row(modules, AppGlyphs.Symbols, null, T("Символы"), null,
                trailing: Trailing(config.SymbolsHotkey, WelcomeSwitch.Symbols, null, T("Символы")), open: SettingsPageKey.Symbols);
            Row(modules, AppGlyphs.Translate, null, T("Перевод"), null,
                trailing: Trailing(null, WelcomeSwitch.Translate, null, T("Перевод")), open: SettingsPageKey.Translate);
            Row(modules, AppGlyphs.QuickLaunch, null, T("Быстрый запуск"), null,
                trailing: Trailing(null, WelcomeSwitch.QuickLaunch, null, T("Быстрый запуск")), open: SettingsPageKey.Launcher);
            Row(modules, null, "≡", T("Своё контекстное меню над выделенным текстом"), null,
                trailing: Trailing(null, WelcomeSwitch.ContextMenu, null, T("Своё контекстное меню над выделенным текстом")), open: SettingsPageKey.Hotkeys);
            Note(modules, T("Нажмите на название, чтобы открыть настройки модуля."));
            BrowserLink(modules, "Политика конфиденциальности", "https://serzhyale.github.io/CyrFlip/privacy.html");
            BrowserLink(modules, "Установка и предупреждение SmartScreen", "https://serzhyale.github.io/CyrFlip/trust.html");
        }

        private void BuildTray()
        {
            var tray = Page("Где находится CyrFlip", AppGlyphs.Settings);
            Row(tray, AppGlyphs.InputLanguage, null, T("Щелчок"), T("Переключает язык"));
            Row(tray, AppGlyphs.Settings, null, T("Двойной щелчок"), T("Открывает настройки"));
            // The menu has no picture in the vocabulary yet, so its plate carries the menu's own letter-shaped mark.
            Row(tray, null, "≡", T("Правый щелчок"), T("Открывает меню"));
            Row(tray, AppGlyphs.Info, null, null, T("Windows 11 может скрыть новый значок за стрелкой ^. Перетащите его наружу, чтобы видеть постоянно."));
            if (_facts.ShortcutsExist) Row(tray, AppGlyphs.Apps, null, null, T("Ярлыки CyrFlip доступны в меню «Пуск» или на рабочем столе."));
            if (_facts.Transient)
            {
                Row(tray, AppGlyphs.Info, null, null, T("Сначала распакуйте CyrFlip в постоянную папку: временная папка исчезнет, и автозапуск с ярлыками перестанут работать."));
            }
            if (!_facts.Packaged)
            {
                // The one switch here that is not a feature of the app: it lives in Windows' own startup list,
                // so its state is read back from there after every flip.
                string caption = T("Запускать CyrFlip вместе с Windows");
                var startup = new ToggleSwitch(_autostartEnabled()) { AccessibleName = caption, Font = Font, RightToLeft = RightToLeft };
                bool updating = false;
                startup.Toggled += (_, _) =>
                {
                    if (updating) return;
                    _setAutostart(startup.Checked);
                    updating = true;
                    startup.Checked = _autostartEnabled();
                    updating = false;
                };
                var group = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
                    Font = Font, RightToLeft = RightToLeft, Margin = Padding.Empty };
                group.Controls.Add(startup);
                Row(tray, AppGlyphs.Apps, null, caption, null, trailing: group);
            }
        }

        internal void Navigate(int index)
        {
            _buttons.SuspendLayout();
            PageIndex = Math.Max(0, Math.Min(PageCount - 1, index));
            for (int i = 0; i < _pages.Count; i++) _pages[i].Visible = i == PageIndex;
            _back.Visible = PageIndex > 0;
            _buttons.Controls.SetChildIndex(_back, 0);
            _buttons.Controls.SetChildIndex(_next, 1);
            _buttons.Controls.SetChildIndex(_cancel, 2);
            // The last step's button names where it goes - the settings - and wears their gear, not an arrow.
            bool last = PageIndex == PageCount - 1;
            _next.Text = T(last ? "Открыть настройки" : "Далее"); _next.Glyph = last ? AppGlyphs.Settings : AppGlyphs.Forward;
            _cancel.Text = T(last ? "Закрыть" : "Пропустить");
            RefreshSwitches();
            _step.Text = F("Шаг {0} из {1}", PageIndex + 1, PageCount);
            _dots.Current = PageIndex;
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

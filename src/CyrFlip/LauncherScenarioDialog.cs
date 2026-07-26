using System;
using System.Drawing;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// Add/Edit dialog for one launcher scenario - the WinForms port of OneClickRunner's
    /// <c>AppItemDialog</c>, plus the optional per-scenario hotkey (spec v0.2 §13). Edits are made
    /// on a copy: cancel leaves the original untouched, OK exposes the result via
    /// <see cref="Scenario"/>, and fields the current type does not surface are cleared exactly as
    /// the source did.
    ///
    /// The two type-specific sections are swapped in and out of the layout (not merely hidden) so
    /// the auto-sizing form shrinks to the active type - and so the layout guard test only ever
    /// measures controls that are actually laid out.
    /// </summary>
    internal sealed class LauncherScenarioDialog : Form
    {
        private const int TypeExecutable = 0;
        private const int TypeYtDlp = 1;

        private readonly ComboBox _type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox _name = new TextBox();
        private readonly TextBox _path = new TextBox();
        private readonly TextBox _arguments = new TextBox();
        private readonly TextBox _workingDir = new TextBox();
        private readonly CheckBox _runAsAdmin;
        private readonly TextBox _ytOutput = new TextBox();
        private readonly TextBox _ytFormat = new TextBox();
        private readonly Label _hotkey;
        private readonly TableLayoutPanel _executablePanel;
        private readonly TableLayoutPanel _ytDlpPanel;
        private readonly Panel _typeHost;
        private readonly string _uiLanguage;
        private readonly LauncherScenario _edited;

        /// <summary>The assembled scenario when the user confirmed, else null.</summary>
        public LauncherScenario? Scenario { get; private set; }

        public LauncherScenarioDialog(LauncherScenario? existing, string uiLanguage)
        {
            _uiLanguage = uiLanguage;
            _edited = existing?.Clone() ?? new LauncherScenario();
            Text = T(existing == null ? "Новый сценарий" : "Изменить сценарий");
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            if (Localization.IsRightToLeft(uiLanguage)) { RightToLeft = RightToLeft.Yes; RightToLeftLayout = true; }
            string? family = Localization.FontFamily(uiLanguage);
            if (family != null)
            {
                try { Font = new Font(family, Font.SizeInPoints); }
                catch { /* the font is missing on this machine - keep the default */ }
            }

            _runAsAdmin = new CheckBox { Text = T("Запускать от имени администратора"), AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
            _hotkey = new Label
            {
                AutoSize = true, BorderStyle = BorderStyle.FixedSingle, BackColor = SystemColors.Window,
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 5, 6, 5),
                MinimumSize = new Size(TextWidth("Ctrl+Shift+Backspace"), 0),
            };

            _type.Items.Add(T("Программа или скрипт"));
            _type.Items.Add(T("yt-dlp: скачать по ссылке"));
            _type.Width = FieldWidth();
            _name.Width = _path.Width = _arguments.Width = _workingDir.Width = _ytOutput.Width = _ytFormat.Width = FieldWidth();

            _executablePanel = TypeSection();
            _executablePanel.Controls.Add(Caption(T("Путь:")), 0, 0);
            _executablePanel.Controls.Add(_path, 1, 0);
            _executablePanel.Controls.Add(Browse(T("Обзор..."), BrowsePath), 2, 0);
            _executablePanel.Controls.Add(Caption(T("Аргументы:")), 0, 1);
            _executablePanel.Controls.Add(_arguments, 1, 1);
            _executablePanel.Controls.Add(Caption(T("Рабочая папка:")), 0, 2);
            _executablePanel.Controls.Add(_workingDir, 1, 2);
            _executablePanel.Controls.Add(Browse(T("Обзор..."), BrowseWorkingDir), 2, 2);
            _executablePanel.Controls.Add(_runAsAdmin, 1, 3);
            _executablePanel.SetColumnSpan(_runAsAdmin, 2);

            _ytDlpPanel = TypeSection();
            _ytDlpPanel.Controls.Add(Caption(T("Папка загрузки (пусто = Загрузки):")), 0, 0);
            _ytDlpPanel.Controls.Add(_ytOutput, 1, 0);
            _ytDlpPanel.Controls.Add(Browse(T("Обзор..."), BrowseOutput), 2, 0);
            _ytDlpPanel.Controls.Add(Caption(T("Доп. параметры yt-dlp:")), 0, 1);
            _ytDlpPanel.Controls.Add(_ytFormat, 1, 1);
            var ytNote = new Label
            {
                Text = T("Ссылка запрашивается при каждом запуске. Программа yt-dlp должна быть доступна в PATH."),
                AutoSize = true, MaximumSize = new Size(FieldWidth(), 0),
                ForeColor = SystemColors.GrayText, Margin = new Padding(3, 6, 3, 3),
            };
            _ytDlpPanel.Controls.Add(ytNote, 1, 2);
            _ytDlpPanel.SetColumnSpan(ytNote, 2);

            // The inactive section is detached, not hidden, so AutoSize shrinks the form to fit.
            _typeHost = new Panel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
            _type.SelectedIndexChanged += (_, _) => ApplyTypePanel();

            var setHotkey = new Button { Text = T("Задать..."), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(8, 3, 3, 3) };
            setHotkey.Click += (_, _) => SetHotkey();
            var clearHotkey = new Button { Text = T("Очистить"), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(3, 3, 3, 3) };
            clearHotkey.Click += (_, _) => _hotkey.Text = T("Не назначено");
            var hotkeyRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
            hotkeyRow.Controls.Add(_hotkey); hotkeyRow.Controls.Add(setHotkey); hotkeyRow.Controls.Add(clearHotkey);

            var ok = new Button { Text = "OK", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(TextWidth("Cancel"), 0), DialogResult = DialogResult.OK };
            ok.Click += (_, _) => BuildScenario();
            var cancel = new Button { Text = T("Отмена"), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(TextWidth("Cancel"), 0), DialogResult = DialogResult.Cancel };

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
            buttons.Controls.Add(cancel); buttons.Controls.Add(ok);

            var grid = new TableLayoutPanel
            {
                ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill, Padding = new Padding(12),
            };
            grid.Controls.Add(Caption(T("Тип:")), 0, 0);
            grid.Controls.Add(_type, 1, 0);
            grid.Controls.Add(Caption(T("Имя:")), 0, 1);
            grid.Controls.Add(_name, 1, 1);
            grid.Controls.Add(_typeHost, 0, 2); grid.SetColumnSpan(_typeHost, 2);
            grid.Controls.Add(Caption(T("Комбинация:")), 0, 3);
            grid.Controls.Add(hotkeyRow, 1, 3);
            grid.Controls.Add(buttons, 0, 4); grid.SetColumnSpan(buttons, 2);
            Controls.Add(grid);

            AcceptButton = ok; CancelButton = cancel;

            // Populate from the edited copy.
            _name.Text = _edited.Name;
            _path.Text = _edited.IsYtDlp ? "" : _edited.Path;
            _arguments.Text = _edited.Arguments;
            _workingDir.Text = _edited.WorkingDirectory;
            _runAsAdmin.Checked = _edited.RunAsAdmin;
            _ytOutput.Text = _edited.YtDlpOutputFolder;
            _ytFormat.Text = _edited.YtDlpFormat;
            _hotkey.Text = _edited.Hotkey.Length > 0 ? _edited.Hotkey : T("Не назначено");
            _type.SelectedIndex = _edited.IsYtDlp ? TypeYtDlp : TypeExecutable;
            ApplyTypePanel();
        }

        private string T(string ru) => Localization.Translate(_uiLanguage, ru);

        private static TableLayoutPanel TypeSection() => new TableLayoutPanel
        {
            ColumnCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty,
        };

        private static Label Caption(string text)
            => new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 10, 3) };

        private static Button Browse(string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(8, 3, 3, 3) };
            button.Click += (_, _) => action();
            return button;
        }

        private int TextWidth(string text) => TextRenderer.MeasureText(text, Font).Width + 16;

        private int FieldWidth() => TextWidth(new string('W', 38));

        private void ApplyTypePanel()
        {
            TableLayoutPanel wanted = _type.SelectedIndex == TypeYtDlp ? _ytDlpPanel : _executablePanel;
            if (_typeHost.Controls.Count == 1 && _typeHost.Controls[0] == wanted) return;
            _typeHost.Controls.Clear();
            _typeHost.Controls.Add(wanted);
        }

        private void BrowsePath()
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "*.exe;*.bat;*.cmd;*.ps1;*.py;*.vbs|*.exe;*.bat;*.cmd;*.ps1;*.py;*.vbs|*.*|*.*",
                Title = T("Выберите программу или скрипт"),
            };
            if (dialog.ShowDialog(this) == DialogResult.OK) _path.Text = dialog.FileName;
        }

        private void BrowseWorkingDir()
        {
            using var dialog = new FolderBrowserDialog { Description = T("Выберите рабочую папку") };
            if (dialog.ShowDialog(this) == DialogResult.OK) _workingDir.Text = dialog.SelectedPath;
        }

        private void BrowseOutput()
        {
            using var dialog = new FolderBrowserDialog { Description = T("Выберите папку загрузки") };
            if (dialog.ShowDialog(this) == DialogResult.OK) _ytOutput.Text = dialog.SelectedPath;
        }

        private void SetHotkey()
        {
            using var dialog = new HotkeyDialog(
                _hotkey.Text == T("Не назначено") ? "" : _hotkey.Text, T("Комбинация запуска сценария"), _uiLanguage);
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.CapturedHotkey != null)
                _hotkey.Text = dialog.CapturedHotkey;
        }

        private void BuildScenario()
        {
            if (_name.Text.Trim().Length == 0)
            {
                Warn(T("Укажите имя сценария."));
                DialogResult = DialogResult.None;
                return;
            }

            bool isYtDlp = _type.SelectedIndex == TypeYtDlp;
            _edited.Name = _name.Text.Trim();
            _edited.Hotkey = Hotkey.TryParse(_hotkey.Text, out _) ? _hotkey.Text : "";

            if (isYtDlp)
            {
                _edited.Type = LauncherScenarioType.YtDlp;
                // Path is not used to launch a yt-dlp scenario; the label keeps the list readable.
                _edited.Path = "yt-dlp";
                _edited.Arguments = string.Empty;
                _edited.WorkingDirectory = string.Empty;
                _edited.RunAsAdmin = false;
                _edited.YtDlpOutputFolder = _ytOutput.Text.Trim();
                _edited.YtDlpFormat = _ytFormat.Text.Trim();
            }
            else
            {
                if (_path.Text.Trim().Length == 0)
                {
                    Warn(T("Укажите путь к программе или скрипту."));
                    DialogResult = DialogResult.None;
                    return;
                }
                _edited.Type = LauncherScenarioType.Executable;
                _edited.Path = _path.Text.Trim();
                _edited.Arguments = _arguments.Text.Trim();
                _edited.WorkingDirectory = _workingDir.Text.Trim();
                _edited.RunAsAdmin = _runAsAdmin.Checked;
                _edited.YtDlpOutputFolder = string.Empty;
                _edited.YtDlpFormat = string.Empty;
            }

            Scenario = _edited;
        }

        private void Warn(string message)
            => MessageBox.Show(this, message, "CyrFlip", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}

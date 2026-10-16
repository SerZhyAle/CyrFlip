using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>
    /// The "Снимки экрана" page (tickets S0026, S0057 and S0071), a partial like the hotkeys overview
    /// and the traffic light because the form is already large. Three sections in the shape of every
    /// page: the region capture (its chord re-defaulted by S0071), the full capture (new - one freeze
    /// of every display as one picture), and the draw-on-capture module (new, on by default) with its
    /// two chords and its two try-out buttons. Like every page here it does no I/O to draw itself: the
    /// folder is shown, never created.
    /// </summary>
    internal sealed partial class SettingsForm
    {
        // ---- Region capture (S0026) ----
        private readonly CheckBox _screenshotHotkeyEnabled = Check("Screen region capture");
        private readonly Label _screenshotHotkeyValue = new Label { AutoSize = true };
        private readonly CheckBox _screenshotSave = Check("Also save captures to a folder");
        // The box is the top of the folder row's editor strip, with the two buttons under it (WINDOWS-UI section 4:
        // the shared editor column is 200..340 logical px, so the strip is no wider than its box).
        private readonly TextBox _screenshotFolderBox = new TextBox { ReadOnly = true, Width = 340, Margin = new Padding(3, 0, 3, 3) };
        private readonly List<Control> _screenshotFolderControls = new List<Control>();
        // ---- Folder chord (S0057) ----
        private readonly CheckBox _screenshotFolderHotkeyEnabled = Check("Open the screenshots folder");
        private readonly Label _screenshotFolderHotkeyValue = new Label { AutoSize = true };
        // ---- Full capture (S0071) ----
        private readonly CheckBox _screenshotFullHotkeyEnabled = Check("Full screen capture");
        private readonly Label _screenshotFullHotkeyValue = new Label { AutoSize = true };
        // ---- Draw on captures (S0071): the module switch and its two chords ----
        private readonly CheckBox _screenshotAnnotateEnabled = Check("Draw on screen captures");
        private readonly CheckBox _screenshotAnnotateAreaHotkeyEnabled = Check("Area capture with drawing");
        private readonly Label _screenshotAnnotateAreaHotkeyValue = new Label { AutoSize = true };
        private readonly CheckBox _screenshotFullAnnotateHotkeyEnabled = Check("Full screen capture with drawing");
        private readonly Label _screenshotFullAnnotateHotkeyValue = new Label { AutoSize = true };
        private readonly List<Control> _screenshotAnnotateControls = new List<Control>();

        private TabPage GraphicsPage()
        {
            var page = Page("Screenshots",
                "Captures of a screen region or of the whole screen, with optional drawing on them; everything stays on this computer.");
            var panel = ContentPanel(page);
            // The control names are how the ledger test finds the rows (S0062 L32): the region
            // capture's switch carries its section's id (the page was one group before S0071), the
            // new rows carry theirs.
            _screenshotHotkeyEnabled.Name = "group.graphics-capture";
            _screenshotFullHotkeyEnabled.Name = "group.graphics-full";
            _screenshotAnnotateAreaHotkeyEnabled.Name = "group.graphics-annotate";
            _screenshotFolderHotkeyEnabled.Name = "setting.screenshot-folder-chord";

            panel.Controls.Add(Group("graphics/capture", "Screen region capture",
                HotkeyRow(_screenshotHotkeyEnabled, _screenshotHotkeyValue,
                    () => ScreenshotHotkeyChangeRequested?.Invoke(this, EventArgs.Empty),
                    "The screen freezes, you drag a rectangle with the mouse, and the picture is on the clipboard at once. Esc or the right button cancels. One chord cannot belong to two CyrFlip actions."),
                Setting(Button("Capture a region", AppGlyphs.Screenshot, () => ScreenshotRequested?.Invoke(this, EventArgs.Empty)),
                    "The same as the tray menu item. Only the chord catches a tooltip or an open menu: a click on the tray closes them."),
                Setting(_screenshotSave,
                    "On by default: a capture also becomes a PNG file in the folder below, so it survives the clipboard. Switch this off to keep captures on the clipboard only."),
                ScreenshotFolderRow(),
                ScreenshotFolderHotkeyRow(),
                new Label
                {
                    Text = "A capture always goes to the clipboard and is not recorded in the clipboard history. It is also written as a PNG into the folder shown while saving is on (or, when that folder is unreachable, into Pictures\\Screenshots or Downloads, and CyrFlip tells you which at once). The files are not encrypted; CyrFlip never deletes or sends them anywhere. Content Windows protects (DRM video, the UAC prompt) comes out black.",
                    AutoSize = true, UseMnemonic = false, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted,
                    Margin = new Padding(3, 10, 3, 4),
                }));

            panel.Controls.Add(Group("graphics/full", "Full screen capture",
                HotkeyRow(_screenshotFullHotkeyEnabled, _screenshotFullHotkeyValue,
                    () => ScreenshotFullHotkeyChangeRequested?.Invoke(this, EventArgs.Empty),
                    "One press: every display is captured as one picture, straight to the clipboard. Nothing freezes on the screen."),
                Setting(Button("Capture everything", AppGlyphs.Screenshot, () => ScreenshotFullRequested?.Invoke(this, EventArgs.Empty)),
                    "The same as the tray menu item \"Full screen capture\"."),
                new Label
                {
                    Text = "All displays as one image - the whole desktop, taskbars included. Content Windows protects (DRM video, UAC) comes out black.",
                    AutoSize = true, UseMnemonic = false, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted,
                    Margin = new Padding(3, 10, 3, 4),
                }));

            Control annotateAreaRow = HotkeyRow(_screenshotAnnotateAreaHotkeyEnabled, _screenshotAnnotateAreaHotkeyValue,
                () => ScreenshotAnnotateAreaHotkeyChangeRequested?.Invoke(this, EventArgs.Empty),
                "Capture a selected region and draw on it. One chord cannot belong to two CyrFlip actions.");
            Control annotateFullRow = HotkeyRow(_screenshotFullAnnotateHotkeyEnabled, _screenshotFullAnnotateHotkeyValue,
                () => ScreenshotFullAnnotateHotkeyChangeRequested?.Invoke(this, EventArgs.Empty),
                "Capture every display as one picture and draw on it. One chord cannot belong to two CyrFlip actions.");
            Control annotateAreaButton = Setting(Button("Capture a region and draw", AppGlyphs.Screenshot, () => ScreenshotAnnotateAreaRequested?.Invoke(this, EventArgs.Empty)),
                "The same as the \"area capture with drawing\" chord.");
            Control annotateFullButton = Setting(Button("Capture everything and draw", AppGlyphs.Screenshot, () => ScreenshotFullAnnotateRequested?.Invoke(this, EventArgs.Empty)),
                "The same as the \"full screen capture with drawing\" chord.");
            // Gated by the module switch (the page keeps the switch itself): with the module off no
            // editor can appear from any entry point, so the entry points grey out with it.
            _screenshotAnnotateControls.Add(annotateAreaRow);
            _screenshotAnnotateControls.Add(annotateFullRow);
            _screenshotAnnotateControls.Add(annotateAreaButton);
            _screenshotAnnotateControls.Add(annotateFullButton);

            panel.Controls.Add(Group("graphics/annotate", "Draw on screen captures",
                Setting(_screenshotAnnotateEnabled,
                    "After the capture the frozen picture stays on the screen, ready for your ovals, rectangles, lines and arrows."),
                annotateAreaRow,
                annotateFullRow,
                annotateAreaButton,
                annotateFullButton,
                new Label
                {
                    Text = "Until OK nothing reaches the clipboard or the folder. Cancel or a focus loss discards the capture, drawings included.",
                    AutoSize = true, UseMnemonic = false, MaximumSize = new Size(890, 0), ForeColor = ThemePalette.Light.TextMuted,
                    Margin = new Padding(3, 10, 3, 4),
                }));
            return page;
        }

        /// <summary>
        /// The folder setting, in the shape of every value row (WINDOWS-UI section 4): its caption and hint in
        /// the leading column; in the trailing one the read-only box with the two buttons under it - a strip
        /// no wider than the shared editor column, which is why they stack rather than stand in one line
        /// (a 340 px box and two commands side by side made every other row of the page 600 px of editor).
        /// </summary>
        private Control ScreenshotFolderRow()
        {
            // The setting names itself and says what it does (APP-SETTINGS rule 3: caption, control and hint).
            var folderCaption = new Label { Text = "Folder for screen region captures", AutoSize = true, Tag = "caption" };
            var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
            Button choose = Button("Choose...", AppGlyphs.ChooseFolder, ChooseScreenshotFolder);
            Button open = Button("Open folder", AppGlyphs.Folder, OpenScreenshotFolder);
            buttons.Controls.Add(choose);
            buttons.Controls.Add(open);
            var strip = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(3, 5, 3, 5) };
            strip.Controls.Add(_screenshotFolderBox);
            strip.Controls.Add(buttons);
            Control row = CaptionedSetting(strip, folderCaption,
                "The saved pictures go here; by default it is the Windows Screenshots folder.");
            _rowHints[strip] = ((CaptionedRow)row.Tag!).Hint;
            // Gated by the save switch with the rest of the folder block; the hint stays readable while it is off.
            _screenshotFolderControls.Add(folderCaption);
            _screenshotFolderControls.Add(_screenshotFolderBox);
            _screenshotFolderControls.Add(choose);
            _screenshotFolderControls.Add(open);
            return row;
        }

        /// <summary>
        /// The folder-opening chord's row (S0057), directly under the folder row and gated with it:
        /// greyed while saving is off, so the user sees why the chord does nothing (D2).
        /// </summary>
        private Control ScreenshotFolderHotkeyRow()
        {
            Control row = HotkeyRow(_screenshotFolderHotkeyEnabled, _screenshotFolderHotkeyValue,
                () => ScreenshotFolderHotkeyChangeRequested?.Invoke(this, EventArgs.Empty),
                "Works while saving captures to a folder is on. It opens the folder CyrFlip writes the captures into. One chord cannot be given to two CyrFlip actions.");
            // The whole row follows the folder row's gate; disabling the block disables everything in it.
            _screenshotFolderControls.Add(row);
            return row;
        }

        /// <summary>The folder the settings show: the user's choice, else the Screenshots known folder.</summary>
        private string ScreenshotFolderShown()
        {
            try { return new ScreenshotSaver().DisplayFolder(_config.ScreenshotFolder) ?? ""; }
            catch { return _config.ScreenshotFolder; }
        }

        private void ChooseScreenshotFolder()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = Translate("Folder for screen region captures"),
                SelectedPath = ScreenshotFolderShown(),
                ShowNewFolderButton = true,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath)) return;
            // The default folder chosen explicitly is stored as "the default", so it keeps following a
            // Pictures folder the user later moves to OneDrive (CAPTURE-OUTPUT rule 9).
            string? standard = new ScreenshotSaver().DisplayFolder("");
            string chosen = dialog.SelectedPath;
            GraphicsSettingChanged(() => _config.ScreenshotFolder =
                standard != null && string.Equals(chosen.TrimEnd('\\'), standard.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                    ? "" : chosen, reload: true);
        }

        private void OpenScreenshotFolder()
        {
            // The chord and this button share the one opener (S0057), so they can never disagree
            // about the target - and the probe never runs on this, the keyboard hook's, thread.
            ScreenshotFolderOpenRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Apply one Graphics setting and tell the context; mirrors <see cref="QuickNotesSettingChanged"/>.</summary>
        private void GraphicsSettingChanged(Action apply, bool reload = false)
        {
            if (_loading) return;
            apply();
            GraphicsChanged?.Invoke(this, EventArgs.Empty);
            if (reload) Reload();
        }

        /// <summary>Mirror the Graphics settings into the page and gate the rows behind their switches.</summary>
        private void ReloadGraphicsState()
        {
            _screenshotHotkeyEnabled.Checked = _config.EnableScreenshotHotkey;
            _screenshotHotkeyValue.Text = _config.ScreenshotHotkey;
            _screenshotSave.Checked = _config.ScreenshotSaveEnabled;
            _screenshotFullHotkeyEnabled.Checked = _config.EnableScreenshotFullHotkey;
            _screenshotFullHotkeyValue.Text = _config.ScreenshotFullHotkey;
            _screenshotAnnotateEnabled.Checked = _config.EnableScreenshotAnnotate;
            _screenshotAnnotateAreaHotkeyEnabled.Checked = _config.EnableScreenshotAnnotateAreaHotkey;
            _screenshotAnnotateAreaHotkeyValue.Text = _config.ScreenshotAnnotateAreaHotkey;
            _screenshotFullAnnotateHotkeyEnabled.Checked = _config.EnableScreenshotFullAnnotateHotkey;
            _screenshotFullAnnotateHotkeyValue.Text = _config.ScreenshotFullAnnotateHotkey;
            _screenshotFolderHotkeyEnabled.Checked = _config.EnableScreenshotFolderHotkey;
            _screenshotFolderHotkeyValue.Text = _config.ScreenshotFolderHotkey;
            _screenshotFolderBox.Text = ScreenshotFolderShown();
            foreach (Control control in _screenshotFolderControls)
                control.Enabled = _config.ScreenshotSaveEnabled;
            // The module-preferring rule: with the draw module off its rows grey out, and the switch
            // itself stays enabled - it is the way back.
            foreach (Control control in _screenshotAnnotateControls)
                control.Enabled = _config.EnableScreenshotAnnotate;
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using CyrFlip;
using Xunit;

namespace CyrFlip.Tests
{
    /// <summary>
    /// Regression guard for the localization layer. Three complementary checks:
    ///   1. every string registered in <see cref="Localization"/> has a non-empty translation in
    ///      <b>every</b> supported language - a missing cell would silently fall back to English;
    ///   2. every dynamically built string listed in <see cref="DynamicStrings"/> is actually
    ///      registered (the control-tree walk cannot reach those rows, so nothing else would catch it);
    ///   3. the live settings window is built in each language and walked: any surviving Cyrillic in a
    ///      non-Cyrillic language is an untranslated static label (OS-provided layout/language names
    ///      are excluded, and the "ЙЦУКЕН" layout name is allowed everywhere).
    /// </summary>
    [Collection(SharedGdiCollection.Name)]   // builds the real window - see SharedGdiCollection
    public class SettingsLocalizationTests
    {
        private static readonly Regex Cyrillic = new Regex("[Ѐ-ӿ]");

        /// <summary>Languages whose own script is Cyrillic, so surviving Cyrillic proves nothing.</summary>
        private static readonly HashSet<string> CyrillicUi = new HashSet<string> { "Русский", "Українська" };

        private static readonly string[] DynamicStrings =
        {
            "Set default", "Delete", "Add layout", "Type a language or layout name to filter",
            "Small", "Medium", "Large",
            "Add", "Cancel", "Remove the «{0}» layout from Windows?",
            "Windows must keep at least one layout — this one cannot be removed.",
            "The change has been applied to the Windows layouts. It usually takes effect immediately; if something looks off, sign out of Windows and back in.",
            "Restore the Windows layouts to their original state?", "Not assigned", "Assign...", "Clear",
            "{0} → layout {1} (not installed)", "Shortcut for switching to this language",
            "The {0} combination is already used by the CyrFlip hotkey «{1}» — Windows would never receive it.",
            "The {0} combination is already assigned to «{1}».",
            "Windows has no free slots left for language shortcuts.",
            "Could not write the shortcut to the Windows registry.",
            "The shortcut has been written to the Windows settings — the system will handle it, not CyrFlip. If it does not work right away, sign out of Windows and back in.",
            "Restore the Windows language shortcuts to their original state?", "Fix CapsLock",
            "Clipboard manager", "Delete all saved clipboard history?",
            // The conversion table (rows are rebuilt per refresh, so the walk can't see them).
            "Layout pair", "Shortcut", "The table is empty — no shortcut converts text right now.",
            "Change...", "(not installed)", "Install at least two layouts to create a conversion.",
            "The {0} combination is already used by «{1}».",
            // Tray menu and tooltip (no control tree at all).
            "Show history", "Hide history", "Clipboard history", "Cursor: layout indicator",
            "Caret: overlay label", "Caret: dot style", "Settings...", "Exit",
            "hotkeys are off", "case", "clipboard manager",
            "screen stays on", "keeping awake",
            "Set case hotkey", "Set clipboard history hotkey",
            // Tray hotkeys hover popup (S0059).
            "Text", "Conversion", "Translation", "Scenarios", "Mouse",
            "next layout", "settings", "Left Click", "Double Click", "switch layout",
            "screenshots folder", "open screenshots folder", "Hotkeys (tray menu)", "{0} more in Settings",
            // The screenshots folder chord (S0057): its dialog title and the two balloons - none of
            // which the walk over the settings control tree can reach.
            "Set the screenshots folder hotkey", "The screenshots folder is unavailable: {0}",
            "This folder does not exist yet - it appears with the first saved capture.",
            "That combination is already taken by {0}. They can't share — pick another one.",
            "the case-flip hotkey", "a layout conversion", "another CyrFlip hotkey",
            // Dialogs and windows built outside the settings control tree.
            "New combo (a modifier is required):", "New layout conversion", "Edit layout conversion",
            "From layout:", "To layout:", "Hotkey:", "Conversion hotkey",
            "The source and target layouts must be different.",
            "Set a hotkey first.", "The hotkey \"{0}\" was not recognized.",
            "Some CyrFlip settings could not be read and were left as they are.",
            "Enter at least 3 characters:", "Text", "Date", "Source", "Close", "Restore to clipboard",
            // The exchange file (ticket S0023): the flow's dialogs and its import report.
            "Export to exchange file", "Import from exchange file", "CyrFlip exchange file", "All files",
            "Transfer...", "Export...", "Import...",
            "Exported notes: {0}, history entries: {1}.", "Could not read the file: {0}",
            "The file contains no note or history entry that can be imported.",
            "Notes without an Id in the file: {0} - they look hand-written. Import them as new notes?",
            "Found notes: {0}, history entries: {1}.",
            "Quick notes are off - the notes in the file will be skipped.",
            "Clipboard history is off - the history entries will be skipped.",
            "To be added: {0} notes, {1} history entries.",
            "Notes to be updated (the file has a newer version): {0}.",
            "Already here: {0} notes, {1} history entries.", "Blocks that could not be read: {0}.", "Nothing to import.",
            "The import only adds and updates - nothing already here is deleted. Import?",
            "Import finished: {0} notes added, {1} updated, {2} history entries added.",
            "The file was written in another version of the CyrFlip exchange format. Update CyrFlip to import it.",
            "The file is larger than {0} MB - such a file is not imported.", "The marker “{0}” was not found - this is not a CyrFlip exchange file.",
            "Line {0}: {1}", "and {0} more", "the block has no text inside a backtick fence",
            "the text fence is never closed - the rest of the file was not read", "the “{0}” field is missing or malformed",
            "the text does not match its Id - it was changed or damaged", "the text is larger than allowed",
            "a metadata line is longer than {0} characters", "more than {0} objects - the rest were not imported",
            "Enter at least 3 characters to search by text fragment.", "No matches found.", "Found: {0}",
            "Nothing selected. I flip text, not thin air — highlight something first.",
            "Couldn't read or replace the selection. The clipboard had other plans.",
            "The selection is too large to convert (over a million characters). Select a part of it.",
            "Another clipboard operation has not finished yet. Try again in a second.",
            "Fragment is too large for history (>128 KB).", "Couldn't update Windows startup:",
            "Capturing for ~7s. Click into that uncooperative input box and type or wiggle the caret — show me where it's hiding.",
            "Caret diagnostics saved — the caret's hiding spot is exposed. Opening:",
            "Caret diagnostics failed (the caret won this round):",
            "Easy — one diagnostics capture is already snooping around.",
            // The scenario launcher (list rows, dialogs, tray submenu, Jump List tasks, balloons -
            // all built outside the walked static control tree).
            "Quick launch", "Name", "Path", "Arguments", "Working folder", "Admin",
            "Unreadable files: {0}", "{0} (copy)", "Remove the scenario \"{0}\"?",
            "Delete the conversion row \"{0}\"? This cannot be undone.",
            "Delete the translation row \"{0}\"? This cannot be undone.",
            "Run", "Clone", "Export... (short form)", "Import... (short form)", "Add...",
            "Import from OneClickRunner...", "No OneClickRunner scenarios were found.",
            "Export scenario to XML", "Scenario \"{0}\" exported.", "Export failed: {0}",
            "Select a scenario XML file", "Import failed: {0}", "Imported scenario \"{0}\".",
            "Its shortcut is already taken, so it was not carried over.",
            "Found OneClickRunner scenarios ({0}). Bring them into CyrFlip? The source files stay untouched.",
            "Scenarios imported: {0}.", "Corrupt files skipped: {0}.",
            "New ids assigned because of collisions: {0}.", "Calculator",
            "Manage scenarios...", "Exit CyrFlip", "a quick-launch scenario",
            "Couldn't start \"{0}\": {1}", "The scenario has no path set.", "File not found: {0}",
            "\"{0}\" was not found as a file or on PATH.",
            "A yt-dlp scenario needs the link prompt, which is unavailable in this context.",
            "The link contains characters that are not allowed (quotes or control characters).",
            "The link must be an http:// or https:// address.",
            "These parameters will not be passed to yt-dlp: only Latin letters, digits, spaces and the characters + / [ ] < > = * . _ , - are allowed",
            "yt-dlp was not found on PATH. Install it so the yt-dlp command works in a terminal.",
            "Cannot use the download folder \"{0}\": {1}",
            "The scenario was not found — open CyrFlip to refresh the Jump List.",
            "yt-dlp download", "Link to download:", "Start",
            "New scenario", "Edit scenario", "Type:", "Name:", "Program or script",
            "yt-dlp: download a link", "Path:", "Arguments:", "Working folder:", "Browse...",
            "Run as administrator", "Download folder (empty = Downloads):",
            "Extra yt-dlp options:",
            "The link is asked for on every run. The yt-dlp program must be available on PATH.",
            "Scenario launch shortcut", "Select a program or script", "Select the working folder",
            "Select the download folder", "Enter a scenario name.", "Enter the path to a program or script.",
            // The translator (table rows, the Ollama status line, the result window, the tray entry -
            // none of which the walk over the settings control tree can reach).
            "Translation", "Into language", "No directions yet - add the first one to assign a chord.",
            "Ollama is already installed - press \"Start Ollama\".",
            "Opening the Ollama website - install it from there.",
            "Download and install Ollama? This is several hundred megabytes and may take a while.",
            "Downloading Ollama...", "Downloading Ollama:", "Launching the Ollama installer...",
            "The download failed. Opening the Ollama website...",
            "Ollama is not installed - press \"Install Ollama\".", "Starting Ollama...",
            "Could not start Ollama.", "Ollama is not running - press \"Start Ollama\".",
            "Ollama was not found - press \"Install Ollama\".", "Enter a model name, for example aya-expanse:8b.",
            "Downloading the model:", "Model installed:", "Could not download the model. Is Ollama running?",
            "Ollama is running. Installed models:",
            "Ollama is running but no model is installed - press \"Download the model\".",
            "New translation direction", "Edit the translation direction", "Translate into:",
            "Translation chord", "Copy", "Paste", "Again", "Settings",
            "Interface language", "Active layout language",
            "Translate the clipboard", "There is no text on the clipboard to translate.",
            "Translating... (Esc to cancel)", "the first {0} of {1} characters were translated",
            "The focus has moved - paste the translation by hand.", "There is no text to translate.",
            "Ollama, the local translator, was not found. It has to be installed once.",
            "Ollama is not running.",
            "No model is installed. We recommend aya-expanse:8b (~4.7 GB) - download it in the settings.",
            "Ollama answered with an error:", "Ollama refused the request. Check the server address and the model in the settings.",
            "The model did not answer in time. It may be too large for this computer.",
            "The model could not handle this text.", "text translation",
            // S0010: the partial answer's note, the remote server that does not answer, the refused installer.
            "the model stopped answering - the translation may be incomplete",
            "The server {0} does not answer. Check the address in the settings and that Ollama is running there.",
            "The downloaded installer is not signed by Ollama - it has been deleted. Opening the Ollama website...",
            // "Send logs to the author": the pre-send dialog and the two answers for a machine whose
            // mail client cannot take an attachment from a link.
            "Logs for the author", "The log archive is ready:", "File", "Size", "What it holds",
            "truncated - only the tail was kept", "left out - the total size limit was reached",
            "You send the message yourself - CyrFlip transmits nothing over the network. Clipboard history and quick notes are not part of the archive; scenario paths and arguments, window titles and selected text are never written to the logs. The logs do contain file paths - the usual ones no longer carry your Windows account name, but one in an unusual shape still can.",
            // What each collected file holds (S0010 TD-1) - SupportBundle.Contents.
            "version, Windows, layouts, enabled features and CyrFlip settings",
            "scenario launches: name, kind, outcome - no paths or arguments",
            "context menu: commands, window classes and numbers - no text",
            "translations: languages, model, lengths and outcome - no text",
            "quick notes: counts only", "clipboard history: counts only",
            "selection conversions: lengths and process names - no text",
            "caret diagnostics: window classes and processes - titles only as a length",
            "the current layout's code",
            "Create the message", "Open the archive folder", "Could not build the log archive:",
            "Your mail program does not accept an attachment from a link. The message is open and the archive is selected in Explorer - drag it into the message before sending.",
            "The mail program could not be opened. Please send the archive by hand to:",
            // The quick notes: the window is built on demand and lives outside the settings control
            // tree entirely, so every one of its captions has to be listed here.
            "Quick notes", "Note", "Checklist", "Item", "Created", "Name (optional)",
            "Add item", "Remove item", "Copy for Google Keep",
            "Notes: {0}", "(empty)", "New note", "Created {0}, changed {1}",
            "The fragment is larger than {0} KB and does not fit in a note.",
            "The tick marks cannot survive the switch to text. Continue?",
            "List items hold no line endings: the text will come back with plain newlines. Continue?",
            "The text is on the clipboard. Create a note in Google Keep and paste it there.",
            "The tick marks do not travel to Google Keep.", "Open Google Keep in the browser?",
            "Export the note", "Export every note", "Text file",
            "Note saved: {0}", "Notes saved: {0}",
            "This file is not protected by DPAPI - anyone with access to the folder can read it.",
            "Could not save the file: {0}", "Delete this note? There is no undo.",
            "Delete every quick note, the journal and its backup? There is no undo.",
            "Notes live only on this computer and are encrypted with DPAPI. Do not keep passwords, production tokens or private keys here.",
            "Save the selection to quick notes", "Set the quick-notes hotkey",
            "quick notes", "the quick notes",
            "Unreadable quick-note records: {0}. Every other note is there.",
            "Notes live only on this computer, in a file encrypted with the DPAPI of your Windows account. CyrFlip never sends them anywhere and indexes them with nothing.",
            "This is not a secret store: do not keep passwords, production tokens or private keys here.",
            // The About tab's version line: filled in by ApplyLanguage rather than built as a
            // static caption, so the control-tree walk sees it already translated.
            "Version {0}",
        };

        [Fact]
        public void EveryRegisteredStringIsTranslatedIntoEverySupportedLanguage()
        {
            var problems = new List<string>();
            foreach (KeyValuePair<string, string[]> entry in Localization.All)
            {
                // Values are parallel to Names: one cell per language, the key's own English text included.
                Assert.Equal(Localization.Names.Length, entry.Value.Length);
                for (int i = 0; i < entry.Value.Length; i++)
                    if (string.IsNullOrWhiteSpace(entry.Value[i]))
                        problems.Add($"[{Localization.Names[i]}] missing: \"{Trim(entry.Key)}\"");
            }
            Assert.True(problems.Count == 0, "Untranslated strings:\n" + string.Join("\n", problems));
        }

        [Fact]
        public void EveryDynamicStringIsRegisteredInTheLocalizationTable()
        {
            var known = new HashSet<string>();
            foreach (KeyValuePair<string, string[]> entry in Localization.All) known.Add(entry.Key);

            var missing = new List<string>();
            foreach (string source in DynamicStrings)
                if (!known.Contains(source)) missing.Add(Trim(source));

            Assert.True(missing.Count == 0,
                "These strings are shown to users but have no entry in Localization:\n" + string.Join("\n", missing));
        }

        [Fact]
        public void EveryUserFacingStringTranslatesOutOfRussian()
        {
            var problems = new List<string>();
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try { foreach (string language in Localization.Names) Run(language, problems); }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();

            // A window that could not be built is not an untranslated one (S0029 RB-1): say so under
            // its own heading, or the report sends the reader hunting for a missing translation.
            Assert.True(failure == null, "The settings window could not be built or walked:\n" + failure);
            Assert.True(problems.Count == 0, "Untranslated user-facing strings:\n" + string.Join("\n", problems));
        }

        private static void Run(string language, List<string> problems)
        {
            bool cyrillicScript = CyrillicUi.Contains(language);
            Type type = typeof(SettingsForm);
            // With the exchange rows, so the walk sees their captions (ticket S0023).
            object form = TestForms.NewSettings(new AppConfig { UiLanguage = language }, withExchange: true);
            try
            {
                MethodInfo translate = type.GetMethod("Translate", BindingFlags.NonPublic | BindingFlags.Instance)!;
                foreach (string src in DynamicStrings)
                {
                    string outp = (string)translate.Invoke(form, new object[] { src })!;
                    // A translation that legitimately equals its Russian source (proper nouns, "EN ⇄ RU")
                    // is a hit, not a miss; the real failure is Cyrillic surviving in a Latin-script build.
                    if (!cyrillicScript && Cyrillic.IsMatch(StripAllowed(outp)))
                        problems.Add($"[{language}] dynamic: \"{Trim(src)}\" -> \"{Trim(outp)}\"");
                }

                if (!cyrillicScript)
                {
                    var skip = new HashSet<object>();
                    // The screenshot folder box shows a path - the user's data, whatever script it is in (S0026).
                    foreach (string field in new[] { "_layoutRows", "_languageRows", "_uiLanguage", "_screenshotFolderBox", "_overviewWindowsRows" })
                        if (type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(form) is object c)
                            skip.Add(c);
                    Walk(language, form, skip, problems);
                }
                MnemonicWalk(language, (System.Windows.Forms.Control)form, problems);
            }
            finally { (form as IDisposable)?.Dispose(); }
        }

        /// <summary>
        /// S0036 UI-9: a label that reads mnemonics turns a lone "&amp;" into an underline - English
        /// "Language &amp; region" read "Language region". Every label in every language either shows
        /// its text as written or has no lone ampersand in it.
        /// </summary>
        private static void MnemonicWalk(string language, System.Windows.Forms.Control control, List<string> problems)
        {
            if (control is System.Windows.Forms.Label label && label.UseMnemonic
                && label.Text.Replace("&&", "").IndexOf('&') >= 0)
                problems.Add($"[{language}] a label eats its ampersand: \"{Trim(label.Text)}\"");
            foreach (System.Windows.Forms.Control child in control.Controls) MnemonicWalk(language, child, problems);
        }

        private static void Walk(string language, object control, HashSet<object> skip, List<string> problems)
        {
            if (skip.Contains(control)) return;
            Type ct = control.GetType();
            string? text = Property(ct, "Text")?.GetValue(control) as string;
            if (!string.IsNullOrEmpty(text) && Cyrillic.IsMatch(StripAllowed(text!)))
                problems.Add($"[{language}] static {ct.Name}: \"{Trim(text!)}\"");

            // Property, not GetProperty: TableLayoutPanel shadows Controls with `new`, and a plain
            // GetProperty throws AmbiguousMatchException on exactly such types (found via the
            // settings header the WINDOWS-UI ticket added). The most derived declaration wins.
            if (Property(ct, "Controls")?.GetValue(control) is IEnumerable children)
                foreach (object child in children) Walk(language, child, skip, problems);
        }

        private static PropertyInfo? Property(Type type, string name)
        {
            for (Type? t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                PropertyInfo? p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (p != null) return p;
            }
            return null;
        }

        // ЙЦУКЕН is the Cyrillic layout name and legitimately appears verbatim in any language.
        private static string StripAllowed(string s) => s.Replace("ЙЦУКЕН", "");

        private static string Trim(string s) => s.Length <= 60 ? s : s.Substring(0, 57) + "...";
    }
}

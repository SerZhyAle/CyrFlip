using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// Registry and coordinator for companion surfaces and selection surfaces (S0094).
    /// Holds the single source of truth for topmost companion windows and exclusions,
    /// and coordinates temporary yielding of WS_EX_TOPMOST when dialogs or dropdowns are active.
    /// </summary>
    public static class CompanionSurfaceRegistry
    {
        /// <summary>
        /// Authoritative set of companion window type names that belong to the registry.
        /// </summary>
        public static readonly HashSet<string> CompanionTypeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "ClipboardHistoryWindow",
            "TranslationResultWindow",
            "SymbolsWindow",
            "TrayHotkeysPopup",
            "TrafficLightWindow",
        };

        /// <summary>
        /// Authoritative dictionary of non-companion windows using TopMost and the concrete reason for exclusion.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> ExcludedTopmostWindows = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "AnnotationEditor", "Active only during screenshot annotation session; no pickers exist while active." },
            { "ScreenshotToolbar", "Drawing toolbar for screenshot editor; active during capture session only." },
            { "CaretOverlay", "Click-through caret layout indicator; does not receive input or obscure selections." },
            { "RegionSelectionOverlay", "Fullscreen region selection canvas active only while dragging selection." },
            { "SettingsLoadingNotice", "Transient startup notice living under 1 second before any picker can open." },
            { "ClipboardHistorySearchWindow", "Selection surface / picker dialog; satellites yield topmost to it." },
        };

        private sealed class Entry
        {
            public Form Form { get; }
            public CompanionSurfaceKind Kind { get; }
            public Func<bool>? IsPinned { get; }

            public Entry(Form form, CompanionSurfaceKind kind, Func<bool>? isPinned)
            {
                Form = form;
                Kind = kind;
                IsPinned = isPinned;
            }
        }

        private static readonly List<Entry> _entries = new List<Entry>();
        private static bool _dialogActive;
        private static bool _dropdownDropped;
        private static ComboBox? _droppedCombo;

        /// <summary>True while a window of ours that is not a companion is the active window.</summary>
        internal static bool DialogActive => _dialogActive;

        /// <summary>True while a combo box list of ours is open.</summary>
        internal static bool DropdownDropped => _dropdownDropped;

        /// <summary>
        /// Whether an active window of this type is a dialog the companions step aside for: every window of
        /// ours that is not itself a companion.
        /// </summary>
        internal static bool IsDialogTypeName(string typeName) => !CompanionTypeNames.Contains(typeName);

        /// <summary>
        /// Reads Form.ActiveForm and sets dialog active if the active window is not a companion.
        /// </summary>
        public static void UpdateDialogActiveFromActiveForm()
        {
            Form? active = Form.ActiveForm;
            if (active == null || active.IsDisposed)
            {
                SetDialogActive(false);
                return;
            }
            SetDialogActive(IsDialogTypeName(active.GetType().Name));
        }

        /// <summary>
        /// A window of ours became the active window (S0098). <see cref="ThemedForm"/> calls this from its own
        /// activation, which WinForms raises for the <c>WM_ACTIVATE</c> Windows sends - a message an
        /// <see cref="IMessageFilter"/> never sees, because it only gets the posted ones.
        /// </summary>
        internal static void NotifyFormActivated(Form form)
        {
            SetDialogActive(IsDialogTypeName(form.GetType().Name));
        }

        /// <summary>
        /// A window of ours stopped being the active window, or closed (S0098). Which window is in the foreground
        /// now is settled only after the message that reports the change, so it is read one turn later, through
        /// the thread's synchronization context: a window that has been destroyed by then cannot be the one asked
        /// to run it. Without a context (no window was ever created on the thread) the read is immediate.
        /// </summary>
        internal static void NotifyFormLeft()
        {
            SynchronizationContext? ui = SynchronizationContext.Current;
            if (ui == null) UpdateDialogActiveFromActiveForm();
            else ui.Post(_ => UpdateDialogActiveFromActiveForm(), null);
        }

        /// <summary>
        /// Follows the list of a combo box (S0098): <see cref="ComboBox.DropDown"/> comes before the list is
        /// shown and <see cref="ComboBox.DropDownClosed"/> after it is gone, both from the notification Windows
        /// sends to the combo's parent. The theme walk calls this once for every combo box of every window.
        /// </summary>
        internal static void WatchDropdown(ComboBox combo)
        {
            combo.DropDown -= OnComboDropDown;
            combo.DropDownClosed -= OnComboDropDownClosed;
            combo.HandleDestroyed -= OnComboHandleDestroyed;
            combo.DropDown += OnComboDropDown;
            combo.DropDownClosed += OnComboDropDownClosed;
            combo.HandleDestroyed += OnComboHandleDestroyed;
        }

        private static void OnComboDropDown(object? sender, EventArgs e)
        {
            _droppedCombo = sender as ComboBox;
            SetDropdownDropped(true);
        }

        private static void OnComboDropDownClosed(object? sender, EventArgs e)
        {
            if (ReferenceEquals(_droppedCombo, sender)) _droppedCombo = null;
            SetDropdownDropped(false);
        }

        /// <summary>A window closed with its list open gets no close-up: the flag would stay up for good.</summary>
        private static void OnComboHandleDestroyed(object? sender, EventArgs e)
        {
            if (!ReferenceEquals(_droppedCombo, sender)) return;
            _droppedCombo = null;
            SetDropdownDropped(false);
        }

        /// <summary>
        /// Registers a companion surface form in the registry.
        /// </summary>
        public static void RegisterCompanion(Form form, CompanionSurfaceKind kind, Func<bool>? isPinned = null)
        {
            if (form == null) return;
            lock (_entries)
            {
                _entries.RemoveAll(e => e.Form == form || e.Form.IsDisposed);
                _entries.Add(new Entry(form, kind, isPinned));
            }
            form.HandleCreated -= OnCompanionHandleCreated;
            form.HandleCreated += OnCompanionHandleCreated;
            ApplyToForm(form);
        }

        /// <summary>
        /// WinForms rebuilds a window when its right-to-left layout is switched on (a language change), and the
        /// new window takes WS_EX_TOPMOST from <see cref="Form.TopMost"/> whatever the registry had yielded.
        /// One message-loop turn later, with the window shown again, the registry puts it back (S0096).
        /// </summary>
        private static void OnCompanionHandleCreated(object? sender, EventArgs e)
        {
            if (!(sender is Form form) || form.IsDisposed) return;
            form.BeginInvoke(new Action(() => ApplyToForm(form)));
        }

        /// <summary>
        /// Unregisters a companion surface form.
        /// </summary>
        public static void UnregisterCompanion(Form form)
        {
            if (form == null) return;
            form.HandleCreated -= OnCompanionHandleCreated;
            lock (_entries)
            {
                _entries.RemoveAll(e => e.Form == form || e.Form.IsDisposed);
            }
        }

        /// <summary>
        /// Signals whether a dialog of our process is currently active.
        /// </summary>
        public static void SetDialogActive(bool active)
        {
            if (_dialogActive == active) return;
            _dialogActive = active;
            ApplyToAll();
        }

        /// <summary>
        /// Signals whether a native dropdown list (e.g. combo box) is currently open.
        /// </summary>
        public static void SetDropdownDropped(bool dropped)
        {
            if (_dropdownDropped == dropped) return;
            _dropdownDropped = dropped;
            ApplyToAll();
        }

        /// <summary>
        /// Evaluates and applies the yield policy to all currently registered companion windows.
        /// </summary>
        public static void ApplyToAll()
        {
            bool hasSelection = TopmostYieldPolicy.IsSelectionSurfaceActive(_dialogActive, _dropdownDropped);
            List<Entry> snapshot;
            lock (_entries)
            {
                _entries.RemoveAll(e => e.Form.IsDisposed);
                snapshot = new List<Entry>(_entries);
            }

            foreach (Entry entry in snapshot)
            {
                ApplyEntry(entry, hasSelection);
            }
        }

        /// <summary>
        /// Applies the yield policy to a single registered form.
        /// </summary>
        public static void ApplyToForm(Form form)
        {
            if (form == null || form.IsDisposed) return;
            bool hasSelection = TopmostYieldPolicy.IsSelectionSurfaceActive(_dialogActive, _dropdownDropped);
            Entry? entry = null;
            lock (_entries)
            {
                entry = _entries.Find(e => e.Form == form);
            }
            if (entry != null)
            {
                ApplyEntry(entry, hasSelection);
            }
        }

        private static void ApplyEntry(Entry entry, bool hasSelection)
        {
            Form form = entry.Form;
            if (form.IsDisposed || !form.IsHandleCreated) return;

            bool isVisible = form.Visible;
            bool isPinned = entry.IsPinned?.Invoke() ?? true;
            bool shouldBeTopmost = TopmostYieldPolicy.ShouldBeTopmost(entry.Kind, isVisible, isPinned, hasSelection);

            if (!isVisible) return;

            Reconcile(form, shouldBeTopmost);
        }

        /// <summary>
        /// Puts the window's real WS_EX_TOPMOST bit where the policy says, without hiding it or taking the
        /// focus; returns whether it had to move it. The bit is read from the window, never remembered: the
        /// pin switch (<see cref="Form.TopMost"/>) and a recreated handle both move it behind the registry's
        /// back, and a remembered "yielded" flag then kept a pinned traffic light above the dialog that the
        /// registry believed it had stepped aside for (S0096).
        /// </summary>
        internal static bool Reconcile(Form form, bool shouldBeTopmost)
        {
            if (form.IsDisposed || !form.IsHandleCreated) return false;

            bool isTopmost = (GetWindowLong(form.Handle, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;
            if (isTopmost == shouldBeTopmost) return false;

            const uint flags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE;
            SetWindowPos(form.Handle, shouldBeTopmost ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, flags);
            return true;
        }
    }
}

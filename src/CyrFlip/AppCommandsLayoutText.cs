using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CyrFlip
{
    /// <summary>
    /// The seam between the layout verbs and Windows (ticket S0109): which layouts are installed, which one the window
    /// the person last typed in has, CapsLock, and the request itself. The production host is
    /// <see cref="IndicatorLayoutHost"/>; a test supplies a fake, so the whole answer table is a unit test with no
    /// keyboard, no window and no registry.
    /// </summary>
    internal interface ILayoutCommandHost
    {
        /// <summary>The installed layouts in Windows' own order.</summary>
        IReadOnlyList<LayoutEntry> Entries();
        /// <summary>The layout of the window the person last typed in; null with no usable window (none tracked, or one of ours).</summary>
        LayoutEntry? Active();
        bool CapsLockOn();
        /// <summary>Ask that window for the layout; posts nothing when it already has it.</summary>
        SwitchOutcome Switch(IntPtr hkl);
        /// <summary>Run once after a request was posted, so the marker follows at once, as it does after the tray click.</summary>
        void AfterSwitch();
    }

    /// <summary>
    /// The five verbs of the layout and text group (ticket S0109, APP-COMMANDS 0.2): <c>layout list</c>,
    /// <c>layout current</c>, <c>layout switch</c>, <c>text convert</c> and <c>text flip-case</c>. Each is a pure
    /// function over the parsed invocation and what it is handed, so the running copy and a one-shot process answer
    /// alike. Order inside each: the arguments (exit 10) -> the layout a value names (10 or 11) -> the act -> the answer.
    /// Nothing the caller typed - a text, a path - goes into a message or a log line, except a layout value, which is
    /// cut to <see cref="ValueChars"/> characters.
    /// </summary>
    internal static class AppCommandsLayoutText
    {
        /// <summary>The verb's own bound on <c>--text</c>; the parser's 32 KiB as UTF-8 always stops a text first.</summary>
        public const int MaxTextChars = 32768;
        /// <summary>The longest layout value a message repeats.</summary>
        public const int ValueChars = 16;
        /// <summary>
        /// How long one answer line may be. The caller reads at most <see cref="AppCommandLine.MaxRequestBytes"/> bytes of
        /// it, and a text of 32 KiB can come back longer than it went (Latin letters become two-byte Cyrillic ones), so a
        /// result that would not fit is refused instead of being cut or arriving unreadable.
        /// </summary>
        public const int MaxAnswerBytes = AppCommandLine.MaxRequestBytes - 256;

        /// <summary>The conversion as the product runs it: Windows' own mappings, the built-in table for the seeded pair.</summary>
        public static string ProductConverter(string text, string fromKlid, string toKlid, bool convertSymbols)
            => KeyboardLayoutConverter.Convert(text, fromKlid, toKlid, convertSymbols);

        // ------------------------------------------------------------------ layout list

        /// <summary>
        /// The installed layouts, one object each with <c>klid</c>, <c>code</c> and <c>label</c>. <paramref name="running"/> is
        /// whether a running copy answers: only then does every object carry <c>current</c>, true for exactly the layout of
        /// <paramref name="active"/> (false everywhere with no usable window).
        /// </summary>
        public static AppCommandResponse LayoutList(IReadOnlyList<LayoutEntry> entries, LayoutEntry? active, bool running,
            Func<string, string> T)
        {
            try
            {
                if (entries.Count == 0)
                    return AppCommandResponse.Failed(AppCommandReasons.Error, AppCommandExit.Failed,
                        T("Windows reports no keyboard layouts to list."));
                var layouts = new List<object>();
                foreach (LayoutEntry entry in entries)
                {
                    var item = new Dictionary<string, object>
                    {
                        ["klid"] = entry.Klid, ["code"] = entry.Code, ["label"] = entry.Label,
                    };
                    if (running) item["current"] = active != null && active.Hkl == entry.Hkl;
                    layouts.Add(item);
                }
                return AppCommandResponse.Done(Localization.Format(T, "Layouts listed: {0}.", entries.Count),
                    new Dictionary<string, object> { ["count"] = entries.Count, ["layouts"] = layouts.ToArray() });
            }
            catch (Exception ex)
            {
                return Crashed("layout list", ex, T);
            }
        }

        // ------------------------------------------------------------------ layout current

        public static AppCommandResponse LayoutCurrent(LayoutEntry? active, bool capsLock, Func<string, string> T)
        {
            try
            {
                if (active == null) return NoWindow(T);
                return AppCommandResponse.Done(Localization.Format(T, "The layout is {0} ({1}).", active.Code, active.Klid),
                    new Dictionary<string, object> { ["code"] = active.Code, ["klid"] = active.Klid, ["capsLock"] = capsLock });
            }
            catch (Exception ex)
            {
                return Crashed("layout current", ex, T);
            }
        }

        // ------------------------------------------------------------------ layout switch

        public static AppCommandResponse LayoutSwitch(AppCommandInvocation invocation, ILayoutCommandHost host, Func<string, string> T)
        {
            try
            {
                string raw = invocation.Option("to") ?? "";
                LayoutValue value = LayoutValue.Parse(raw);
                if (value.Kind == LayoutValueKind.Invalid)
                    return AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                        T("The value of --to names no keyboard layout. Give a language code such as en, a KLID such as 00000409, or next."));

                var catalog = new LayoutCatalog(host.Entries());
                LayoutEntry? target = null;
                if (value.Kind != LayoutValueKind.Next)
                {
                    target = catalog.Find(value);
                    if (target == null)
                    {
                        // Named but absent is 11, a value no layout could carry is 10 (Assumed 3). Never added to Windows.
                        if (!catalog.NamesSomething(value))
                            return AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                                T("The value of --to names no keyboard layout. Give a language code such as en, a KLID such as 00000409, or next."));
                        return AppCommandResponse.Failed(AppCommandReasons.TargetMissing, AppCommandExit.NotReady,
                            Localization.Format(T, "The keyboard layout \"{0}\" is not installed in Windows. CyrFlip does not add layouts.", Show(raw)));
                    }
                }
                else if (catalog.Entries.Count < 2)
                {
                    // Nothing failed and no screen to say it on: answered, not switched (Assumed 6).
                    if (catalog.Entries.Count == 0)
                        return AppCommandResponse.Failed(AppCommandReasons.Error, AppCommandExit.Failed, T("Windows reports no keyboard layouts to list."));
                    return Answer(catalog.Entries[0], switched: false,
                        T("Nothing to switch to: Windows has only one keyboard layout installed."));
                }

                LayoutEntry? active = host.Active();
                if (active == null) return NoWindow(T);
                if (target == null)
                {
                    // The tray click's rotation: the same arithmetic over the same list.
                    IntPtr[] installed = catalog.Entries.Select(e => e.Hkl).ToArray();
                    IntPtr next = LayoutSwitcher.NextAfter(installed, active.Hkl);
                    target = catalog.Entries.First(e => e.Hkl == next);
                }

                if (target.Hkl == active.Hkl)
                    return Answer(target, switched: false,
                        Localization.Format(T, "The layout {0} ({1}) is already active.", target.Code, target.Klid));

                switch (host.Switch(target.Hkl))
                {
                    case SwitchOutcome.Requested:
                        host.AfterSwitch();
                        return Answer(target, switched: true,
                            Localization.Format(T, "Asked the window to switch to {0} ({1}).", target.Code, target.Klid));
                    case SwitchOutcome.AlreadyActive:
                        return Answer(target, switched: false,
                            Localization.Format(T, "The layout {0} ({1}) is already active.", target.Code, target.Klid));
                    default:
                        return NoWindow(T);
                }
            }
            catch (Exception ex)
            {
                return Crashed("layout switch", ex, T);
            }
        }

        private static AppCommandResponse Answer(LayoutEntry layout, bool switched, string message)
            => AppCommandResponse.Done(message, new Dictionary<string, object>
            {
                ["klid"] = layout.Klid, ["code"] = layout.Code, ["switched"] = switched,
            });

        private static AppCommandResponse NoWindow(Func<string, string> T)
            => AppCommandResponse.Failed(AppCommandReasons.TargetMissing, AppCommandExit.NotReady,
                T("There is no window to act on. Click in the program you type in, then try again."));

        // ------------------------------------------------------------------ text convert

        /// <summary>
        /// A text typed in one layout, read as the same keys of another. <paramref name="installed"/> is the PC's layouts;
        /// <paramref name="convert"/> is the converter (<see cref="ProductConverter"/>, or a test's with a fixed layout list).
        /// A code no installed layout carries falls back to the curated primary KLID of that language - which is what lets
        /// the seeded US and Russian pair reach the built-in table without the Russian keyboard - and any other layout that
        /// is not installed has no table, so it is refused naming the value. <c>--symbols</c> is true when absent and the
        /// stored setting is never read.
        /// </summary>
        public static AppCommandResponse TextConvert(AppCommandInvocation invocation, IReadOnlyList<LayoutEntry> installed,
            Func<string, string, string, bool, string> convert, Func<string, string> T)
        {
            try
            {
                AppCommandResponse? bad = CheckText(invocation, T, out string text);
                if (bad != null) return bad;

                var catalog = new LayoutCatalog(installed);
                string? fromKlid = Side(invocation, "from", catalog, T, out bool fromInstalled, out bad);
                if (fromKlid == null) return bad!;
                string? toKlid = Side(invocation, "to", catalog, T, out bool toInstalled, out bad);
                if (toKlid == null) return bad!;

                if (string.Equals(fromKlid, toKlid, StringComparison.OrdinalIgnoreCase))
                    return AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                        T("The two layouts are the same. Name two different layouts."));
                if (!(fromInstalled && toInstalled) && !KeyboardLayoutConverter.IsBuiltInPair(fromKlid, toKlid))
                    return NotInstalledHere(invocation.Option(fromInstalled ? "to" : "from") ?? "", T);

                bool symbols = invocation.Option("symbols") == null || invocation.Flag("symbols");
                string converted = convert(text, fromKlid, toKlid, symbols);
                return Fitted(converted, !string.Equals(converted, text, StringComparison.Ordinal),
                    T("Converted the text."), T);
            }
            catch (Exception ex)
            {
                return Crashed("text convert", ex, T);
            }
        }

        /// <summary>
        /// The KLID one side names, whether that layout is installed, or null with the refusal in <paramref name="refusal"/>.
        /// Internal because the selection verbs share the layout-value grammar byte for byte (ticket S0129).
        /// </summary>
        internal static string? Side(AppCommandInvocation invocation, string option, LayoutCatalog catalog, Func<string, string> T,
            out bool installed, out AppCommandResponse? refusal)
        {
            installed = false;
            refusal = null;
            string raw = invocation.Option(option) ?? "";
            LayoutValue value = LayoutValue.Parse(raw);
            AppCommandResponse NamesNothing() => AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                Localization.Format(T, "The value of --{0} names no keyboard layout. Give a language code such as en, or a KLID such as 00000409.", option));

            // "next" is a rotation of a switch; here it names nothing.
            if (value.Kind != LayoutValueKind.Klid && value.Kind != LayoutValueKind.Code)
            {
                refusal = NamesNothing();
                return null;
            }

            LayoutEntry? entry = catalog.Find(value);
            if (entry != null)
            {
                installed = true;
                return entry.Klid;
            }
            // Not installed. A code of a curated language still has a KLID, which is what lets the seeded US and Russian
            // pair reach the built-in table without the Russian keyboard; the pair check in the caller decides.
            if (value.Kind == LayoutValueKind.Code)
            {
                string? curated = LayoutCatalog.CuratedPrimaryKlid(value.Text);
                if (curated != null) return curated;
            }
            if (!catalog.NamesSomething(value))
            {
                refusal = NamesNothing();
                return null;
            }
            // A KLID Windows can name but this PC has not: the caller refuses it unless it is the built-in pair.
            if (value.Kind == LayoutValueKind.Klid) return value.Text;
            refusal = NotInstalledHere(raw, T);
            return null;
        }

        private static AppCommandResponse NotInstalledHere(string raw, Func<string, string> T)
            => AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                Localization.Format(T, "The keyboard layout \"{0}\" is not installed on this PC, so the text cannot be converted from or to it.", Show(raw)));

        // ------------------------------------------------------------------ text flip-case

        public static AppCommandResponse TextFlipCase(AppCommandInvocation invocation, Func<string, string> T)
        {
            try
            {
                AppCommandResponse? bad = CheckText(invocation, T, out string text);
                if (bad != null) return bad;
                string flipped = CaseFlipEngine.Flip(text);
                return Fitted(flipped, !string.Equals(flipped, text, StringComparison.Ordinal), T("Flipped the case of the text."), T);
            }
            catch (Exception ex)
            {
                return Crashed("text flip-case", ex, T);
            }
        }

        // ------------------------------------------------------------------ shared

        /// <summary>The refusal of an empty or over-long <c>--text</c> (10), else null with the text.</summary>
        private static AppCommandResponse? CheckText(AppCommandInvocation invocation, Func<string, string> T, out string text)
        {
            text = invocation.Option("text") ?? "";
            if (text.Length == 0)
                return AppCommandResponse.Refused(AppCommandReasons.BadArgument, AppCommandExit.Usage,
                    T("The command needs some text. Give it with --text."));
            if (text.Length > MaxTextChars)
                return AppCommandResponse.Refused(AppCommandReasons.TooLarge, AppCommandExit.Usage,
                    Localization.Format(T, "The text is too long. The limit is {0} characters.", MaxTextChars));
            return null;
        }

        /// <summary>The answer of a text verb, or - when the text would not fit in one answer line - the refusal that says so.</summary>
        private static AppCommandResponse Fitted(string text, bool changed, string message, Func<string, string> T)
        {
            AppCommandResponse response = AppCommandResponse.Done(message,
                new Dictionary<string, object> { ["text"] = text, ["changed"] = changed });
            if (Encoding.UTF8.GetByteCount(AppCommandProtocol.Serialize(response)) > MaxAnswerBytes)
                return AppCommandResponse.Refused(AppCommandReasons.TooLarge, AppCommandExit.Usage,
                    T("The converted text is too long to answer in one piece. Use a shorter text."));
            return response;
        }

        /// <summary>A layout value as a message repeats it: short, one line.</summary>
        private static string Show(string value)
        {
            var shown = new StringBuilder();
            foreach (char c in value.Length <= ValueChars ? value : value.Substring(0, ValueChars))
                shown.Append(char.IsControl(c) ? ' ' : c);
            if (value.Length > ValueChars) shown.Append("..");
            return shown.ToString();
        }

        /// <summary>A fault would otherwise fault the task: the caller always gets an answer. Only the type is logged.</summary>
        private static AppCommandResponse Crashed(string verb, Exception ex, Func<string, string> T)
        {
            LauncherLog.Log("Command: " + verb + " failed: " + ex.GetType().Name);
            return AppCommandResponse.Failed(AppCommandReasons.Error, AppCommandExit.Failed,
                T("The command failed. The log bundle on the About page has the details."));
        }
    }

    /// <summary>
    /// The production side of <see cref="ILayoutCommandHost"/>: a thin object over the indicator that owns "the window
    /// the person last typed in". It holds no state of its own. The window is asked for at the moment of each call; none
    /// of it is activated (a command leaves windows as it found them, unlike the tray click, which has taken the focus).
    /// </summary>
    internal sealed class IndicatorLayoutHost : ILayoutCommandHost
    {
        private readonly CursorIndicator _indicator;

        public IndicatorLayoutHost(CursorIndicator indicator)
        {
            _indicator = indicator;
        }

        public IReadOnlyList<LayoutEntry> Entries() => InstalledEntries();

        public LayoutEntry? Active()
        {
            IntPtr window = _indicator.CommandTargetWindow();
            if (window == IntPtr.Zero) return null;
            IntPtr hkl = CursorIndicator.HklOf(window);
            string klid = LayoutIdentity.KlidForHkl(hkl).ToUpperInvariant();
            return new LayoutEntry(hkl, klid, LayoutIdentity.CodeFor(unchecked((uint)(long)hkl), klid), "");
        }

        public bool CapsLockOn() => CursorIndicator.IsCapsLockOn();

        public SwitchOutcome Switch(IntPtr hkl) => LayoutSwitcher.SwitchToHkl(_indicator.CommandTargetWindow(), hkl);

        public void AfterSwitch() => _indicator.Poll();

        /// <summary>
        /// The layouts of <c>GetKeyboardLayoutList</c> in Windows' order - the running copy and a one-shot process ask
        /// the same call, so they list the same layouts in the same order. The label (Windows' own name, read from the
        /// registry and a resource file) is looked up only when somebody reads it, so a switch pays nothing for it on the
        /// hooks' thread.
        /// </summary>
        internal static IReadOnlyList<LayoutEntry> InstalledEntries()
        {
            var entries = new List<LayoutEntry>();
            foreach (IntPtr hkl in KeyboardLayoutConverter.InstalledLayouts())
            {
                string klid = LayoutIdentity.KlidForHkl(hkl).ToUpperInvariant();
                if (klid.Length == 0) continue;
                entries.Add(new LayoutEntry(hkl, klid, LayoutIdentity.CodeFor(unchecked((uint)(long)hkl), klid),
                    () => InputLayouts.DisplayNameFor(klid)));
            }
            return entries;
        }
    }
}

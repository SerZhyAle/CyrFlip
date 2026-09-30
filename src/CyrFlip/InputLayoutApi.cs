using System;
using System.Collections.Generic;
using System.Globalization;
using static CyrFlip.WindowInterop;

namespace CyrFlip
{
    /// <summary>
    /// One enabled entry of the user's input list as the documented input-profile API reports it: a
    /// keyboard (<c>0419:00000409</c> - the US keyboard under Russian) or a text input processor
    /// (<c>0804:{CLSID}{PROFILE}</c> - Microsoft Pinyin). <see cref="Id"/> names the host language,
    /// which is what a KLID-only list could never carry (ticket S0007, WL-2).
    /// </summary>
    internal sealed class LayoutOrTip
    {
        public string Id { get; set; } = "";
        public ushort LangId { get; set; }
        public bool IsTip { get; set; }
        /// <summary>The keyboard's KLID, lower case; empty for a TIP.</summary>
        public string Klid { get; set; } = "";
        public bool IsDefault { get; set; }
    }

    /// <summary>The seam over <c>input.dll</c>, so the phase-B edits are tested without touching Windows.</summary>
    internal interface IInputLayoutApi
    {
        /// <summary>The enabled entries in Windows' order; false when the API is missing or answered nothing.</summary>
        bool TryEnumerate(out List<LayoutOrTip> entries);
        bool Install(string ids, bool uninstall);
        bool SetDefault(string id);
    }

    /// <summary>
    /// WL-1 phase B (ticket S0007): add, remove and "make default" go through the documented
    /// <c>InstallLayoutOrTip</c> / <c>SetDefaultLayoutOrTip</c>, which edit the modern profile and the
    /// legacy Preload as Windows' own settings page does - language + keyboard/TIP pairs, never a
    /// rebuild from a KLID list. Every edit is <b>verified</b> by enumerating again: an API that is
    /// missing, refuses, or answers true without doing it returns <c>null</c>, and the caller falls back
    /// to the phase-A registry path, which is safe on its own. There is no API for the order within a
    /// language, so ↑↓ stays on the phase-A path.
    /// </summary>
    internal static class InputLayoutApi
    {
        /// <summary>The live API; tests swap in a fake.</summary>
        internal static IInputLayoutApi Current { get; set; } = new Win32InputLayoutApi();

        /// <summary>The API spelling of a keyboard installed under its own language: <c>0419:00000419</c>.</summary>
        internal static string IdFor(string klid)
            => InputLayouts.LangIdOf(klid).ToString("X4", CultureInfo.InvariantCulture) + ":" + klid.ToUpperInvariant();

        /// <summary>Every enabled keyboard entry of <paramref name="klid"/>, under whichever language it lives.</summary>
        internal static List<LayoutOrTip> KeyboardEntries(List<LayoutOrTip> entries, string klid)
            => entries.FindAll(e => !e.IsTip && string.Equals(e.Klid, klid, StringComparison.OrdinalIgnoreCase));

        /// <summary>Enable <paramref name="klid"/> under its own language. Already enabled anywhere = done.</summary>
        internal static InputLayouts.EditResult? Add(IInputLayoutApi api, string klid)
        {
            if (!api.TryEnumerate(out List<LayoutOrTip> before)) return null;
            if (KeyboardEntries(before, klid).Count > 0) return InputLayouts.EditResult.Ok;
            if (!api.Install(IdFor(klid), uninstall: false)) return null;
            return api.TryEnumerate(out List<LayoutOrTip> after) && KeyboardEntries(after, klid).Count > 0
                ? InputLayouts.EditResult.Ok : (InputLayouts.EditResult?)null;
        }

        /// <summary>
        /// Disable every keyboard entry of <paramref name="klid"/> - the phase-A path removed its value
        /// from every language too. A KLID the API lists no keyboard for (the stand-in row of a TIP, an
        /// entry only the legacy store knows) is left to the caller's path.
        /// </summary>
        internal static InputLayouts.EditResult? Remove(IInputLayoutApi api, string klid)
        {
            if (!api.TryEnumerate(out List<LayoutOrTip> before)) return null;
            List<LayoutOrTip> targets = KeyboardEntries(before, klid);
            if (targets.Count == 0) return null;
            if (targets.Count >= before.Count) return InputLayouts.EditResult.LastLayout;
            var ids = new List<string>();
            foreach (LayoutOrTip t in targets) ids.Add(t.Id);
            if (!api.Install(string.Join(";", ids), uninstall: true)) return null;
            return api.TryEnumerate(out List<LayoutOrTip> after) && KeyboardEntries(after, klid).Count == 0
                ? InputLayouts.EditResult.Ok : (InputLayouts.EditResult?)null;
        }

        /// <summary>
        /// Make <paramref name="klid"/> the default input method: its first keyboard entry, or - for the
        /// primary KLID a TIP shows up as in Preload (Pinyin as <c>00000804</c>) - the first TIP of that
        /// language, the same choice the phase-A <c>InputMethodOverride</c> made.
        /// </summary>
        internal static InputLayouts.EditResult? MakeDefault(IInputLayoutApi api, string klid)
        {
            if (!api.TryEnumerate(out List<LayoutOrTip> entries)) return null;
            LayoutOrTip? target = DefaultTargetFor(entries, klid);
            if (target == null) return null;
            if (target.IsDefault) return InputLayouts.EditResult.Ok;
            if (!api.SetDefault(target.Id)) return null;
            return api.TryEnumerate(out List<LayoutOrTip> after)
                && after.Exists(e => e.IsDefault && string.Equals(e.Id, target.Id, StringComparison.OrdinalIgnoreCase))
                ? InputLayouts.EditResult.Ok : (InputLayouts.EditResult?)null;
        }

        internal static LayoutOrTip? DefaultTargetFor(List<LayoutOrTip> entries, string klid)
        {
            List<LayoutOrTip> keyboards = KeyboardEntries(entries, klid);
            if (keyboards.Count > 0) return keyboards[0];
            if (klid.StartsWith("0000", StringComparison.Ordinal))
            {
                ushort lang = InputLayouts.LangIdOf(klid);
                return entries.Find(e => e.IsTip && e.LangId == lang);
            }
            return null;
        }

        /// <summary>
        /// Reads one <c>szId</c> into an entry; null for anything that is neither shape (a disabled
        /// entry is dropped by the caller). A keyboard's KLID is the eight hex digits after the colon.
        /// </summary>
        internal static LayoutOrTip? Parse(string id, uint profileType, uint flags)
        {
            if (id == null || id.Length < 6 || id[4] != ':'
                || !ushort.TryParse(id.Substring(0, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort lang))
                return null;
            string rest = id.Substring(5);
            bool tip = profileType == LOTP_INPUTPROCESSOR || rest.StartsWith("{", StringComparison.Ordinal);
            if (!tip && !(rest.Length == 8 && uint.TryParse(rest, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)))
                return null;
            return new LayoutOrTip
            {
                Id = id,
                LangId = lang,
                IsTip = tip,
                Klid = tip ? "" : rest.ToLowerInvariant(),
                IsDefault = (flags & LOT_DEFAULT) != 0,
            };
        }
    }

    /// <summary>The real <c>input.dll</c>. Every failure - a missing DLL or entry point included - reads as "no".</summary>
    internal sealed class Win32InputLayoutApi : IInputLayoutApi
    {
        public bool TryEnumerate(out List<LayoutOrTip> entries)
        {
            entries = new List<LayoutOrTip>();
            try
            {
                uint count = EnumEnabledLayoutOrTip(null, null, null, null, 0);
                if (count == 0) return false;
                // Headroom for an entry enabled between the two calls; the buffer is counted in elements.
                var buffer = new LAYOUTORTIPPROFILE[count + 8];
                uint got = EnumEnabledLayoutOrTip(null, null, null, buffer, (uint)buffer.Length);
                if (got == 0 || got > buffer.Length) return false;
                for (int i = 0; i < got; i++)
                {
                    if ((buffer[i].dwFlags & LOT_DISABLED) != 0) continue;
                    LayoutOrTip? entry = InputLayoutApi.Parse(buffer[i].szId, buffer[i].dwProfileType, buffer[i].dwFlags);
                    if (entry == null) return false; // a shape we do not understand: do not edit on a partial picture
                    entries.Add(entry);
                }
                return entries.Count > 0;
            }
            catch { entries.Clear(); return false; }
        }

        public bool Install(string ids, bool uninstall)
        {
            try { return InstallLayoutOrTip(ids, uninstall ? ILOT_UNINSTALL : 0); }
            catch { return false; }
        }

        public bool SetDefault(string id)
        {
            try { return SetDefaultLayoutOrTip(id, 0); }
            catch { return false; }
        }
    }
}

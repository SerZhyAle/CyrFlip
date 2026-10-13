using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CyrFlip
{
    /// <summary>
    /// The accepted HTTP status codes of an <c>http</c> target (ticket S0069 D8): ranges and lists such as
    /// <c>200-399,401</c>. Pure; the default is <see cref="TrafficLightTarget.DefaultAcceptedStatusCodes"/>.
    /// </summary>
    internal sealed class TrafficLightStatusCodes
    {
        private readonly int[] _from;
        private readonly int[] _to;

        private TrafficLightStatusCodes(int[] from, int[] to) { _from = from; _to = to; }

        public static readonly TrafficLightStatusCodes Default = Parse(TrafficLightTarget.DefaultAcceptedStatusCodes);

        public bool Contains(int status)
        {
            for (int i = 0; i < _from.Length; i++)
                if (status >= _from[i] && status <= _to[i]) return true;
            return false;
        }

        /// <summary>The canonical text: sorted, merged, one range per run (<c>200-399,401</c>).</summary>
        public override string ToString()
        {
            var builder = new StringBuilder();
            for (int i = 0; i < _from.Length; i++)
            {
                if (i > 0) builder.Append(',');
                builder.Append(_from[i].ToString(CultureInfo.InvariantCulture));
                if (_to[i] != _from[i]) builder.Append('-').Append(_to[i].ToString(CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        /// <summary>The text parsed, or the default when it does not parse - what a probe uses.</summary>
        public static TrafficLightStatusCodes Parse(string? text)
            => TryParse(text, out TrafficLightStatusCodes? parsed) && parsed != null ? parsed : Default ?? Build(new[] { 200 }, new[] { 399 });

        /// <summary>
        /// Items separated by commas or semicolons, each a code or <c>from-to</c>, codes 100-599, at least one
        /// item. White space around an item is ignored; anything else refuses the whole text.
        /// </summary>
        public static bool TryParse(string? text, out TrafficLightStatusCodes? codes)
        {
            codes = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var ranges = new List<KeyValuePair<int, int>>();
            foreach (string raw in text!.Split(',', ';'))
            {
                string item = raw.Trim();
                if (item.Length == 0) return false;
                int dash = item.IndexOf('-');
                int from, to;
                if (dash < 0)
                {
                    if (!TryCode(item, out from)) return false;
                    to = from;
                }
                else
                {
                    if (!TryCode(item.Substring(0, dash).Trim(), out from)) return false;
                    if (!TryCode(item.Substring(dash + 1).Trim(), out to)) return false;
                    if (to < from) return false;
                }
                ranges.Add(new KeyValuePair<int, int>(from, to));
            }

            ranges.Sort((a, b) => a.Key.CompareTo(b.Key));
            var mergedFrom = new List<int>();
            var mergedTo = new List<int>();
            foreach (KeyValuePair<int, int> range in ranges)
            {
                int last = mergedTo.Count - 1;
                if (last >= 0 && range.Key <= mergedTo[last] + 1)
                {
                    if (range.Value > mergedTo[last]) mergedTo[last] = range.Value;
                }
                else
                {
                    mergedFrom.Add(range.Key);
                    mergedTo.Add(range.Value);
                }
            }
            codes = Build(mergedFrom.ToArray(), mergedTo.ToArray());
            return true;
        }

        private static TrafficLightStatusCodes Build(int[] from, int[] to) => new TrafficLightStatusCodes(from, to);

        private static bool TryCode(string text, out int code)
        {
            code = 0;
            if (text.Length != 3) return false;
            foreach (char c in text) if (c < '0' || c > '9') return false;
            code = int.Parse(text, CultureInfo.InvariantCulture);
            return code >= 100 && code <= 599;
        }
    }
}

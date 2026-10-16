using System;
using System.Collections.Generic;
using System.Linq;

namespace CyrFlip
{
    /// <summary>How a listed device stands for sealing (<c>DEVICE-EXCHANGE</c> item DR).</summary>
    internal enum ExchangePeerState
    {
        /// <summary>Capable, keyed, and its key is the one this device trusts.</summary>
        Trusted,
        /// <summary>No capability or no key: never a recipient (<c>e2e-missing</c>).</summary>
        Missing,
        /// <summary>Its key differs from the trusted one: sealed nothing until removed (<c>key-changed</c>).</summary>
        KeyChanged,
    }

    /// <summary>
    /// The trust and recipient rules of item DG, pure: the entry keeps the trusted keys and persists what
    /// <see cref="Learn"/> added. The same rules the phone and the watch apply to the same directory, so both
    /// ends pick the same recipients (the Android reference: <c>E2eRecipients</c>).
    /// </summary>
    internal static class ExchangeRecipients
    {
        public static ExchangePeerState StateOf(ExchangePeer peer, string? trustedKem)
        {
            if (!peer.Capable || peer.Kem.Length == 0) return ExchangePeerState.Missing;
            if (!string.IsNullOrEmpty(trustedKem) && !string.Equals(trustedKem, peer.Kem, StringComparison.Ordinal))
                return ExchangePeerState.KeyChanged;
            return ExchangePeerState.Trusted;
        }

        /// <summary>
        /// Trust on first use: the key of every other capable, keyed device not yet in <paramref name="trusted"/>
        /// is added. Never a replacement. Returns whether anything was added (the caller then saves the entry).
        /// </summary>
        public static bool Learn(string selfDeviceId, IEnumerable<ExchangePeer> peers, IDictionary<string, string> trusted)
        {
            bool added = false;
            foreach (ExchangePeer peer in peers)
            {
                if (peer.DeviceId.Length == 0 || peer.DeviceId == selfDeviceId) continue;
                if (!peer.Capable || peer.Kem.Length == 0) continue;
                if (trusted.ContainsKey(peer.DeviceId) || trusted.Count >= ExchangeAccountEntry.MaxTrustedKeys) continue;
                trusted[peer.DeviceId] = peer.Kem;
                added = true;
            }
            return added;
        }

        /// <summary>
        /// The recipients of a box: listed devices other than this one whose state is <see cref="ExchangePeerState.Trusted"/>,
        /// the 40 seen most recently when there are more (item DH). <paramref name="trusted"/> must already hold
        /// what <see cref="Learn"/> added.
        /// </summary>
        public static List<ExchangePeer> Select(string selfDeviceId, IEnumerable<ExchangePeer> peers, IDictionary<string, string> trusted, int max)
        {
            return peers
                .Where(p => p.DeviceId.Length > 0 && p.DeviceId != selfDeviceId
                            && StateOf(p, trusted.TryGetValue(p.DeviceId, out string? k) ? k : null) == ExchangePeerState.Trusted)
                .OrderByDescending(p => p.UpdatedAtUtcTicks)
                .Take(max)
                .ToList();
        }

        /// <summary>
        /// The names of the other devices that cannot be sealed to, for the result a person reads. A name is the
        /// directory's label of the device, cut to a line and stripped of control characters; a device with no
        /// name is shown by its short id.
        /// </summary>
        public static void Classify(string selfDeviceId, IEnumerable<ExchangePeer> peers, IDictionary<string, string> trusted,
            ICollection<string> missing, ICollection<string> keyChanged)
        {
            foreach (ExchangePeer p in peers)
            {
                if (p.DeviceId.Length == 0 || p.DeviceId == selfDeviceId) continue;
                switch (StateOf(p, trusted.TryGetValue(p.DeviceId, out string? k) ? k : null))
                {
                    case ExchangePeerState.Missing: missing.Add(NameOf(p)); break;
                    case ExchangePeerState.KeyChanged: keyChanged.Add(NameOf(p)); break;
                }
            }
        }

        internal static string NameOf(ExchangePeer p)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in p.Name)
                if (c >= 0x20 && c != 0x7F && sb.Length < 40) sb.Append(c);
            string name = sb.ToString().Trim();
            return name.Length > 0 ? name : p.DeviceId.Substring(0, Math.Min(6, p.DeviceId.Length));
        }

        /// <summary>The opening check of item DH: <paramref name="fromKey"/> is the key trusted for <paramref name="from"/>.</summary>
        public static byte[]? TrustedKemOf(IDictionary<string, string> trusted, string from, Func<string, byte[]?> decode)
            => trusted.TryGetValue(from, out string? kem) ? decode(kem) : null;
    }
}

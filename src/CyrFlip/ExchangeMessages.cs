using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace CyrFlip
{
    /// <summary>What an operation was doing when it failed; a few rows of the table read differently per context.</summary>
    internal enum ExchangeContext
    {
        Enroll,
        Send,
        Get,
    }

    /// <summary>
    /// A failure as a person reads it (<c>DEVICE-EXCHANGE</c> item DA): what happened, then what to try. The
    /// code is never the headline - it goes to the details line and the log.
    /// </summary>
    internal sealed class ExchangeMessage
    {
        public string Code = "";
        public string What = "";
        /// <summary>The id of the step of item CY the row points at, or empty when the row carries its own advice.</summary>
        public string StepId = "";
        public string Try = "";

        public bool HasStep => Try.Length > 0;

        /// <summary>The two parts, one above the other, for a balloon or a box.</summary>
        public string Text => HasStep ? What + "\n" + Try : What;

        /// <summary>The line a person can open: the code as written in the table, no secret, no address of a stranger.</summary>
        public string Details => Code;
    }

    /// <summary>
    /// The one table of messages of <c>DEVICE-EXCHANGE</c> item CZ and the ladder of checks of item CY, in this
    /// program's keys: the English text of the contract is the key of the translation table, and the
    /// Russian and Ukrainian columns are the contract's words, not reworded. A text is never mixed from two
    /// languages and never joined with another sentence of ours (item DA).
    /// </summary>
    internal static class ExchangeMessages
    {
        // ---- item CY: the fifteen steps, in the contract's order. "Applies": any, network, internet. ----
        private sealed class Step
        {
            public readonly string Id;
            public readonly string Text;
            public readonly string Applies;
            public Step(string id, string text, string applies) { Id = id; Text = text; Applies = applies; }
        }

        private static readonly Step[] Steps =
        {
            new Step("pc-asleep", "Switch on and wake the device that runs the server or the sharing service, and keep it from going to sleep.", "any"),
            new Step("address-lan", "Check the address, and make sure both devices are on the same Wi-Fi or home network.", "network"),
            new Step("guest-network", "Join the main Wi-Fi network, not the router's guest network: in a guest network devices often cannot see each other.", "network"),
            new Step("vpn", "Turn the VPN off on this device for a moment and try again.", "any"),
            new Step("windows-public-network", "On the Windows computer, set this network to private: Windows keeps other devices out of a public network.", "network"),
            new Step("ip-family", "Try another network, for example mobile data: the server may be reachable only by a kind of address (IPv4 or IPv6) that this network does not give you.", "any"),
            new Step("server-internet", "From another network, for example mobile data, check that the server answers from the internet.", "internet"),
            new Step("port-forward", "In the router's settings, forward the server's port to the computer that runs it.", "internet"),
            new Step("device-clock", "Set the right date and time on this device, or turn on automatic time, and try again.", "any"),
            new Step("certificate", "If you did not expect this, do not go on: ask whoever runs the server. If the server was reinstalled, confirm its new fingerprint.", "any"),
            new Step("credentials", "Check the login and type the password again. If you used a one-time code, ask for a new one.", "any"),
            new Step("sign-in-rate", "Wait a few seconds and try again: the server takes about one attempt per second.", "any"),
            new Step("invitation", "Check the invitation or ask for a new one: each works once, and this one may be wrong, used or withdrawn.", "any"),
            new Step("device-removed", "Sign in to the account again to add this device back.", "any"),
            new Step("operator", "Ask whoever runs the server: this can be fixed only on the server's side.", "any"),
        };

        // ---- item CZ: code -> the sentence of what happened, and the step it points at ("" = none) ----
        private static readonly Dictionary<string, KeyValuePair<string, string>> Rows =
            new Dictionary<string, KeyValuePair<string, string>>
        {
            [ExchangeCodes.BadCredentials] = Row("The login or the password is wrong.", "credentials"),
            [ExchangeCodes.DeviceRevoked] = Row("This device was removed from the account.", "device-removed"),
            [ExchangeCodes.SignedOut] = Row("The password of this account was changed, so this device was signed out.", "credentials"),
            [ExchangeCodes.VersionUnsupported] = Row("This program and the server speak different versions of the exchange.", "operator"),
            [ExchangeCodes.TlsRequired] = Row("The server accepts this only over a protected connection, and this one is not protected.", "operator"),
            [ExchangeCodes.IdCollision] = Row("The server already knows another device under this device's number. Remove this server from the list and add it again.", ""),
            [ExchangeCodes.Capacity] = Row("The server cannot take this: it has reached a limit of its own, or the item is too big to be listed.", "operator"),
            [ExchangeCodes.Unavailable] = Row("The device or the server is not available right now.", "pc-asleep"),
            [ExchangeCodes.RateLimited] = Row("Too many attempts in a row.", "sign-in-rate"),
            [ExchangeCodes.LimitReached] = Row("A limit set on the server has been reached.", "operator"),
            [ExchangeCodes.NotPermitted] = Row("A guest account cannot do this. If you need it, ask for an invitation of the kind \"own device or family\".", ""),
            [ExchangeCodes.Unreachable] = Row("Cannot reach the server.", "pc-asleep"),
            [ExchangeCodes.Unanswered] = Row("The server did not answer.", "operator"),
            [ExchangeCodes.TlsFailed] = Row("A protected connection could not be set up.", "device-clock"),
            [ExchangeCodes.CertificateChanged] = Row("This server does not look like the one you connected to before.", "certificate"),
            [ExchangeCodes.CertificateUntrusted] = Row("This server does not look like the one you connected to before.", "certificate"),
            [ExchangeCodes.EmptyCredential] = Row("Enter the login and the password first.", ""),
            [ExchangeCodes.ClipboardEmpty] = Row("There is nothing in the clipboard on the server.", ""),
            [ExchangeCodes.E2eMissing] = Row("This device cannot receive protected content yet, so nothing is sent to it through the server.", ""),
            [ExchangeCodes.KeyChanged] = Row("The protection key of this device has changed, so nothing is sent to it. Remove the device from the account and add it again.", ""),
            [ExchangeCodes.ServerNoE2e] = Row("This server cannot carry protected content, so folders, broadcasts and the clipboard do not go through it.", "operator"),
            [ExchangeCodes.SealedFailed] = Row("This could not be opened on this device. Try again from the other device.", ""),

            // This program's own sentences, for classes the table has no row for (item CZ allows a local class).
            [ExchangeCodes.ServerNoClipboard] = Row("This server does not keep the clipboard.", "operator"),
            [ExchangeCodes.NoRecipient] = Row("No other device of this account can open a protected clipboard yet.", ""),
            [ExchangeCodes.NothingToSend] = Row("This clipboard content cannot be sent. Copy text, a picture or one file.", ""),
            [ExchangeCodes.SeveralObjects] = Row("The clipboard holds several files or a folder. Put them into one archive, copy the archive and send again.", ""),
            [ExchangeCodes.OwnerTimeout] = Row("The program that owns the clipboard did not answer in time. Try again.", ""),
            [ExchangeCodes.ClipboardBusy] = Row("Another program holds the clipboard. Try again.", ""),
            [ExchangeCodes.OperationRunning] = Row("Another clipboard operation is still under way.", ""),
            [ExchangeCodes.TransferFailed] = Row("The connection broke while the clipboard was on its way. Try again.", ""),
            [ExchangeCodes.Damaged] = Row("The clipboard arrived damaged and was not placed. Try again.", ""),
            [ExchangeCodes.PlaceFailed] = Row("The received clipboard could not be placed: another program holds the clipboard or the folder is not writable.", ""),
            [ExchangeCodes.SourceChanged] = Row("The file changed while it was being sent. The clipboard on the server stayed as it was. Try again.", ""),
            [ExchangeCodes.NoAccounts] = Row("First add an account on an exchange server.", ""),
            [ExchangeCodes.KeyLost] = Row("The protection key of this account is gone from this computer. Remove the account and add it again.", ""),
            [ExchangeCodes.TooLarge] = Row("This clipboard value is too large for this program to receive.", ""),
        };

        private static KeyValuePair<string, string> Row(string what, string step)
            => new KeyValuePair<string, string>(what, step);

        /// <summary>The clipboard send's own row for <c>limit-reached</c> (item CZ).</summary>
        private const string LimitReachedOnSend =
            "This server does not take a clipboard of this size, or has the clipboard turned off. Send something smaller, or ask whoever runs the server.";

        /// <summary>
        /// The honest text of item DE, in English, whole: every product that describes the exchange server shows it
        /// in these words, not reworded and not shortened. The Russian and Ukrainian columns are the contract's.
        /// </summary>
        public const string HonestText =
            "The exchange server connects your devices, so it knows the names of your account, devices, folders and broadcasts, sees which devices are online, and how much is sent and when. It cannot open what goes through it: folders and their files, broadcasts and the clipboard are encrypted on your device and opened only on your other devices or by the people you gave access to. To look inside, whoever runs the server would have to add a device of their own to your account, and it would appear in your list of devices.";

        private const string Generic = "The server refused this.";
        private const string SendCancelled = "Sending cancelled. The clipboard on the server stayed as it was.";
        private const string GetCancelled = "Cancelled. Nothing was placed in the clipboard of this computer.";

        /// <summary>The message of <paramref name="code"/> in the language <paramref name="t"/> translates to.</summary>
        public static ExchangeMessage Describe(string code, ExchangeContext context, Func<string, string> t)
        {
            var message = new ExchangeMessage { Code = code };
            if (code == ExchangeCodes.Cancelled)
            {
                message.What = t(context == ExchangeContext.Get ? GetCancelled : SendCancelled);
                return message;
            }
            if (code == ExchangeCodes.LimitReached && context == ExchangeContext.Send)
            {
                message.What = t(LimitReachedOnSend);
                return message;
            }
            if (!Rows.TryGetValue(code, out KeyValuePair<string, string> row))
                row = Row(Generic, "operator");

            message.What = t(row.Key);
            message.StepId = row.Value;
            if (row.Value.Length > 0) message.Try = t(StepText(row.Value));
            return message;
        }

        /// <summary>The English text of a step of item CY (the key of its translation).</summary>
        public static string StepText(string stepId)
        {
            foreach (Step s in Steps)
                if (s.Id == stepId) return s.Text;
            return "";
        }

        /// <summary>
        /// The ladder from a row's step to the end of the steps that apply (item CY): the first is the one a
        /// program names, the rest are what it offers one at a time. An address on the person's own network
        /// skips the internet steps and the other way round; the invitation step is never part of a clipboard
        /// operation.
        /// </summary>
        public static IReadOnlyList<string> Ladder(string fromStepId, bool onOwnNetwork)
        {
            var ids = new List<string>();
            bool started = false;
            foreach (Step s in Steps)
            {
                if (s.Id == fromStepId) started = true;
                if (!started) continue;
                if (s.Id == "invitation") continue;
                if (s.Id == "device-removed" && fromStepId != "device-removed") continue;
                if (s.Applies == "network" && !onOwnNetwork) continue;
                if (s.Applies == "internet" && onOwnNetwork) continue;
                ids.Add(s.Id);
            }
            return ids;
        }

        /// <summary>
        /// Whether a host is on the person's own network as far as the text of the address can say: a private,
        /// loopback or link-local IP literal, a single-label name, or a <c>.local</c> / <c>.lan</c> / <c>.home</c> name.
        /// A public name or address is reached from the internet.
        /// </summary>
        public static bool IsOwnNetwork(string host)
        {
            if (string.IsNullOrEmpty(host)) return false;
            string h = host.Trim('[', ']');
            if (IPAddress.TryParse(h, out IPAddress? ip))
            {
                if (IPAddress.IsLoopback(ip)) return true;
                if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                    return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || (ip.GetAddressBytes()[0] & 0xFE) == 0xFC;
                byte[] b = ip.GetAddressBytes();
                return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
            }
            string lower = host.ToLowerInvariant();
            return lower.IndexOf('.') < 0 || lower.EndsWith(".local", StringComparison.Ordinal)
                   || lower.EndsWith(".lan", StringComparison.Ordinal) || lower.EndsWith(".home", StringComparison.Ordinal)
                   || lower.EndsWith(".internal", StringComparison.Ordinal);
        }

        /// <summary>Every English sentence this class can show - the translation gate reads it.</summary>
        internal static IEnumerable<string> AllSourceTexts()
        {
            foreach (Step s in Steps) yield return s.Text;
            foreach (KeyValuePair<string, KeyValuePair<string, string>> r in Rows) yield return r.Value.Key;
            yield return LimitReachedOnSend;
            yield return Generic;
            yield return SendCancelled;
            yield return GetCancelled;
        }
    }
}

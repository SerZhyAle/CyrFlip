using System.Collections.Generic;

namespace CyrFlip
{
    /// <summary>
    /// The codes of <c>DEVICE-EXCHANGE</c> item CZ: the wire reasons of 7.10 and the classes a program names
    /// itself. A code is for the log and the details line; a person reads the table's text
    /// (<see cref="ExchangeMessages"/>). Nothing here ever carries a secret.
    /// </summary>
    internal static class ExchangeCodes
    {
        // ---- wire reasons (7.10) ----
        public const string BadCredentials = "bad-credentials";
        public const string DeviceRevoked = "device-revoked";
        public const string SignedOut = "signed-out";
        public const string VersionUnsupported = "version-unsupported";
        public const string TlsRequired = "tls-required";
        public const string IdCollision = "id-collision";
        public const string Capacity = "capacity";
        public const string Unavailable = "unavailable";
        public const string RateLimited = "rate-limited";
        public const string LimitReached = "limit-reached";
        public const string NotPermitted = "not-permitted";

        // ---- local classes (item CZ, kind "local") ----
        public const string Unreachable = "unreachable";
        public const string Unanswered = "unanswered";
        public const string TlsFailed = "tls-failed";
        public const string CertificateChanged = "certificate-changed";
        public const string EmptyCredential = "empty-credential";
        public const string SealedFailed = "sealed-failed";

        // ---- states and results ----
        public const string ClipboardEmpty = "clipboard-empty";
        public const string ServerNoE2e = "server-no-e2e";
        public const string E2eMissing = "e2e-missing";
        public const string KeyChanged = "key-changed";

        // ---- this program's own (not in the table; each maps to a row or to a sentence of the clipboard page) ----
        /// <summary>No pin and Windows does not trust the chain: the person has not accepted the fingerprint.</summary>
        public const string CertificateUntrusted = "certificate-untrusted";
        /// <summary>The server's <c>welcome</c> lists no <c>clipboard</c> (item BV): nothing is asked of it.</summary>
        public const string ServerNoClipboard = "server-no-clipboard";
        /// <summary>No other device of the account can open a protected value (item DG).</summary>
        public const string NoRecipient = "no-recipient";
        /// <summary>The clipboard holds nothing that can be sent (CLIPBOARD-GUARD item O).</summary>
        public const string NothingToSend = "nothing-to-send";
        /// <summary>Several files or a folder (item BU).</summary>
        public const string SeveralObjects = "several-objects";
        /// <summary>The program that owns the clipboard did not render in time (CLIPBOARD-GUARD item N).</summary>
        public const string OwnerTimeout = "owner-timeout";
        /// <summary>Another program holds the clipboard open.</summary>
        public const string ClipboardBusy = "clipboard-busy";
        /// <summary>One operation at a time (CLIPBOARD-GUARD item N).</summary>
        public const string OperationRunning = "operation-running";
        public const string Cancelled = "cancelled";
        public const string TransferFailed = "transfer-failed";
        /// <summary>The value arrived, but its size or SHA-256 differs from the server's members (item BY).</summary>
        public const string Damaged = "damaged";
        public const string PlaceFailed = "place-failed";
        /// <summary>The file on disk changed between the hash and the send.</summary>
        public const string SourceChanged = "source-changed";
        /// <summary>No account entry that can take part (none, or only guests and entries asking for the password).</summary>
        public const string NoAccounts = "no-accounts";
        /// <summary>A value too large to receive on this computer (memory for a text or picture).</summary>
        public const string TooLarge = "too-large";
        /// <summary>The private device key of the entry is gone (item DF): the entry cannot seal or open and must be added again.</summary>
        public const string KeyLost = "key-lost";

        /// <summary>The wire reasons of 7.10 this program tells apart; anything else is a generic refusal.</summary>
        private static readonly HashSet<string> WireReasons = new HashSet<string>
        {
            BadCredentials, DeviceRevoked, SignedOut, VersionUnsupported, TlsRequired, IdCollision, Capacity,
            Unavailable, RateLimited, LimitReached, NotPermitted,
        };

        public static bool IsKnownWireReason(string reason) => WireReasons.Contains(reason);

        /// <summary>Whether a refusal of a <c>hello</c> means the entry must be signed in again (items I and AT).</summary>
        public static bool AsksForPassword(string code)
            => code == BadCredentials || code == DeviceRevoked || code == SignedOut;

        public static ExchangeSignInState SignInStateOf(string code)
        {
            switch (code)
            {
                case SignedOut: return ExchangeSignInState.SignedOut;
                case DeviceRevoked: return ExchangeSignInState.DeviceRemoved;
                case BadCredentials: return ExchangeSignInState.BadCredentials;
                default: return ExchangeSignInState.Ok;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CyrFlip
{
    /// <summary>
    /// The framing of the exchange server wire (<c>DEVICE-EXCHANGE</c> 7.1, ticket S0074): a 4-byte unsigned
    /// big-endian length, then that many bytes of one UTF-8 JSON object, <c>schemaVersion</c> 2 first. Pure -
    /// it knows no socket - so the corpus of transcripts the catalog publishes can be replayed against it.
    /// </summary>
    internal static class ExchangeWire
    {
        public const int SchemaVersion = 2;

        /// <summary>Largest frame until a stream is authenticated (<c>welcome</c> or <c>enrolled</c> included).</summary>
        public const int MaxFramePreAuth = 16384;

        /// <summary>Largest frame once it is, in both directions (item T).</summary>
        public const int MaxFramePostAuth = 262144;

        /// <summary>The product value this program enrolls under (<c>DEVICE-EXCHANGE</c> 6.1, item BV).</summary>
        public const string Product = "cyrflip-windows";

        public const string RoleClipboard = "clipboard";
        public const string CapabilityE2e = "e2e-v1";
        public const string FeatureClipboard = "clipboard";
        public const string FeatureSealed = "sealed";

        public const string KindSealed = "sealed";

        /// <summary>One frame, encoded: <paramref name="members"/> follow <c>schemaVersion</c> in the order given.</summary>
        public static byte[] Encode(ExchangeMembers members)
        {
            var body = new StringBuilder(256);
            body.Append("{\"schemaVersion\":").Append(SchemaVersion.ToString(CultureInfo.InvariantCulture));
            foreach (KeyValuePair<string, object?> member in members.Items)
            {
                body.Append(',');
                ExchangeJson.WriteString(body, member.Key);
                body.Append(':');
                ExchangeJson.WriteValue(body, member.Value);
            }
            body.Append('}');

            byte[] text = Encoding.UTF8.GetBytes(body.ToString());
            if (text.Length > MaxFramePostAuth)
                throw new ExchangeProtocolException("frame-too-large");
            byte[] frame = new byte[4 + text.Length];
            frame[0] = (byte)(text.Length >> 24);
            frame[1] = (byte)(text.Length >> 16);
            frame[2] = (byte)(text.Length >> 8);
            frame[3] = (byte)text.Length;
            Buffer.BlockCopy(text, 0, frame, 4, text.Length);
            return frame;
        }

        public static async Task WriteFrameAsync(Stream stream, ExchangeMembers members, CancellationToken ct)
        {
            byte[] frame = Encode(members);
            await stream.WriteAsync(frame, 0, frame.Length, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// The next frame, or null when the stream ended cleanly between two frames. A length of zero or above
        /// the limit in force, a body that is not one JSON object, and a stream that ends inside a frame are
        /// <see cref="ExchangeProtocolException"/> - the contract closes the stream on each of them.
        /// </summary>
        public static async Task<ExchangeFrame?> ReadFrameAsync(Stream stream, bool authenticated, CancellationToken ct)
        {
            byte[] header = new byte[4];
            int read = 0;
            while (read < 4)
            {
                int n = await stream.ReadAsync(header, read, 4 - read, ct).ConfigureAwait(false);
                if (n == 0)
                {
                    if (read == 0) return null;
                    throw new ExchangeProtocolException("truncated-frame");
                }
                read += n;
            }

            uint length = ((uint)header[0] << 24) | ((uint)header[1] << 16) | ((uint)header[2] << 8) | header[3];
            uint limit = authenticated ? (uint)MaxFramePostAuth : (uint)MaxFramePreAuth;
            if (length == 0 || length > limit)
                throw new ExchangeProtocolException("frame-length");

            byte[] body = new byte[length];
            int have = 0;
            while (have < body.Length)
            {
                int n = await stream.ReadAsync(body, have, body.Length - have, ct).ConfigureAwait(false);
                if (n == 0) throw new ExchangeProtocolException("truncated-frame");
                have += n;
            }
            return Decode(body);
        }

        /// <summary>One frame body (the JSON, no length) as a view; anything that is not one object is a protocol error.</summary>
        public static ExchangeFrame Decode(byte[] body)
        {
            object? parsed;
            try
            {
                string text = new UTF8Encoding(false, true).GetString(body);
                parsed = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 32 }.DeserializeObject(text);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is FormatException)
            {
                throw new ExchangeProtocolException("not-json");
            }
            var map = parsed as Dictionary<string, object>;
            if (map == null) throw new ExchangeProtocolException("not-an-object");
            return new ExchangeFrame(map);
        }
    }

    /// <summary>The wire broke a rule of 7.1; the reason word is for the log, never for a person.</summary>
    internal sealed class ExchangeProtocolException : Exception
    {
        public ExchangeProtocolException(string reason) : base(reason) { }
    }

    /// <summary>
    /// The members of a frame about to be written, in order. A value is a string, a number, a bool, null, another
    /// <see cref="ExchangeMembers"/>, a list of those, or <see cref="ExchangeRawJson"/> (a box another layer built).
    /// </summary>
    internal sealed class ExchangeMembers
    {
        private readonly List<KeyValuePair<string, object?>> _items = new List<KeyValuePair<string, object?>>();

        public IReadOnlyList<KeyValuePair<string, object?>> Items => _items;

        public ExchangeMembers(string type)
        {
            _items.Add(new KeyValuePair<string, object?>("type", type));
        }

        public ExchangeMembers Add(string name, object? value)
        {
            _items.Add(new KeyValuePair<string, object?>(name, value));
            return this;
        }

        /// <summary>The same as <see cref="Add"/>, but nothing is added for a null or empty string.</summary>
        public ExchangeMembers AddIfSet(string name, string? value)
        {
            if (!string.IsNullOrEmpty(value)) _items.Add(new KeyValuePair<string, object?>(name, value));
            return this;
        }

        // ---- the envelopes this program sends (DEVICE-EXCHANGE 7.2, 7.4, items BW and BY) ----

        public static ExchangeMembers Hello(string deviceId, string deviceToken, string productVersion)
            => new ExchangeMembers("hello")
                .Add("deviceId", deviceId)
                .Add("deviceToken", deviceToken)
                .Add("productVersion", productVersion);

        public static ExchangeMembers Enroll(string login, string password, string deviceId, string deviceName,
            string productVersion, string e2eKemBase64Url)
            => new ExchangeMembers("enroll")
                .Add("login", login)
                .Add("password", password)
                .Add("deviceId", deviceId)
                .Add("deviceName", deviceName)
                .Add("product", ExchangeWire.Product)
                .Add("productVersion", productVersion)
                .Add("platform", "windows")
                .Add("roles", new object[] { ExchangeWire.RoleClipboard })
                .Add("capabilities", new object[] { ExchangeWire.CapabilityE2e })
                .Add("e2eKey", new ExchangeMembers.Plain().Add("v", 1).Add("kem", e2eKemBase64Url));

        public static ExchangeMembers List() => new ExchangeMembers("list");

        public static ExchangeMembers Revoke(string deviceId) => new ExchangeMembers("revoke").Add("deviceId", deviceId);

        public static ExchangeMembers ClipboardInfo() => new ExchangeMembers("clipboard-info");

        public static ExchangeMembers ClipboardGet(string? clipId)
            => new ExchangeMembers("clipboard-get").AddIfSet("clipId", clipId);

        /// <summary>
        /// A sealed put (item DN): <c>kind</c> "sealed", <c>size</c> the sealed bytes, the box - and no
        /// <c>mimeType</c> and no <c>name</c>, which would name the value in clear.
        /// </summary>
        public static ExchangeMembers ClipboardPutSealed(long sealedSize, string boxJson)
            => new ExchangeMembers("clipboard-put")
                .Add("kind", ExchangeWire.KindSealed)
                .Add("size", sealedSize)
                .Add("sealed", new ExchangeRawJson(boxJson));

        /// <summary>An object member without a <c>type</c> (the nested <c>e2eKey</c>).</summary>
        internal sealed class Plain
        {
            private readonly List<KeyValuePair<string, object?>> _items = new List<KeyValuePair<string, object?>>();
            public IReadOnlyList<KeyValuePair<string, object?>> Items => _items;
            public Plain Add(string name, object? value)
            {
                _items.Add(new KeyValuePair<string, object?>(name, value));
                return this;
            }
        }
    }

    /// <summary>JSON text that is already a value (the sealed box); written as it is.</summary>
    internal sealed class ExchangeRawJson
    {
        public readonly string Json;
        public ExchangeRawJson(string json) { Json = json; }
    }

    /// <summary>A minimal JSON writer: stable member order, no HTML escaping, UTF-8 left as it is.</summary>
    internal static class ExchangeJson
    {
        public static void WriteValue(StringBuilder sb, object? value)
        {
            switch (value)
            {
                case null: sb.Append("null"); break;
                case string s: WriteString(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case ExchangeRawJson raw: sb.Append(raw.Json); break;
                case ExchangeMembers.Plain plain: WriteObject(sb, plain.Items); break;
                case System.Collections.IEnumerable list:
                    sb.Append('[');
                    bool first = true;
                    foreach (object? item in list)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteValue(sb, item);
                    }
                    sb.Append(']');
                    break;
                default: throw new ArgumentException("unsupported JSON value " + value.GetType().Name);
            }
        }

        private static void WriteObject(StringBuilder sb, IReadOnlyList<KeyValuePair<string, object?>> items)
        {
            sb.Append('{');
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                WriteString(sb, items[i].Key);
                sb.Append(':');
                WriteValue(sb, items[i].Value);
            }
            sb.Append('}');
        }

        public static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c == '\u2028' || c == '\u2029')
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    /// <summary>A received frame: typed reads over the parsed members. Unknown members are ignored, never an error (7.1).</summary>
    internal sealed class ExchangeFrame
    {
        private readonly Dictionary<string, object> _members;

        public ExchangeFrame(Dictionary<string, object> members) { _members = members; }

        public string Type => Str("type");

        public bool Has(string name) => _members.ContainsKey(name);

        public string Str(string name)
            => _members.TryGetValue(name, out object? v) && v is string s ? s : "";

        /// <summary>An integer member, or -1 when it is absent, not a number or has a fraction.</summary>
        public long Long(string name)
        {
            if (!_members.TryGetValue(name, out object? v) || v == null) return -1;
            switch (v)
            {
                case int i: return i;
                case long l: return l;
                case decimal d: return d == decimal.Truncate(d) ? (long)d : -1;
                case double dbl: return dbl == Math.Floor(dbl) && Math.Abs(dbl) < 9e15 ? (long)dbl : -1;
                default: return -1;
            }
        }

        public ExchangeFrame? Obj(string name)
            => _members.TryGetValue(name, out object? v) && v is Dictionary<string, object> d ? new ExchangeFrame(d) : null;

        public IReadOnlyList<ExchangeFrame> Objects(string name)
        {
            var list = new List<ExchangeFrame>();
            if (_members.TryGetValue(name, out object? v) && v is object[] arr)
                foreach (object item in arr)
                    if (item is Dictionary<string, object> d) list.Add(new ExchangeFrame(d));
            return list;
        }

        public IReadOnlyList<string> Strings(string name)
        {
            var list = new List<string>();
            if (_members.TryGetValue(name, out object? v) && v is object[] arr)
                foreach (object item in arr)
                    if (item is string s) list.Add(s);
            return list;
        }

        /// <summary>A member re-serialized as JSON text - what the sealed box layer reads. Empty when absent.</summary>
        public string JsonOf(string name)
            => _members.TryGetValue(name, out object? v) && v is Dictionary<string, object>
                ? new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(v)
                : "";
    }
}

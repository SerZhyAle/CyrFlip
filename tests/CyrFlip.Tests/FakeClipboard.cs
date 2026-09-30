using System.Collections.Generic;
using CyrFlip;

namespace CyrFlip.Tests
{
    /// <summary>
    /// An in-memory clipboard behind <see cref="IClipboardReader"/>, so what a flip backs up can be
    /// tested without ever opening the real clipboard - a test that did would wipe whatever the person
    /// running it had copied.
    /// </summary>
    internal sealed class FakeClipboard : IClipboardReader
    {
        private readonly Dictionary<uint, byte[]?> _formats = new Dictionary<uint, byte[]?>();
        private readonly HashSet<uint> _failing = new HashSet<uint>();

        /// <summary>Another process holds the clipboard: every open fails.</summary>
        public bool Locked { get; set; }
        public uint SequenceNumber { get; set; } = 100;
        public int Opens { get; private set; }

        /// <summary>
        /// Every read of a content format moves the sequence - a delay-rendering owner (Excel) rendering
        /// on demand (S0032 FP2-1).
        /// </summary>
        public bool BumpSequenceOnRead { get; set; }
        public bool IsOpen { get; private set; }

        /// <summary>Put a format on the clipboard; null data is an announced format with nothing in it.</summary>
        public FakeClipboard With(uint format, byte[]? data)
        {
            _formats[format] = data;
            return this;
        }

        /// <summary>A format that is announced but whose owner fails to render it.</summary>
        public FakeClipboard Failing(uint format)
        {
            _formats[format] = null;
            _failing.Add(format);
            return this;
        }

        public int CountFormats() => _formats.Count;

        public bool Open()
        {
            if (Locked) return false;
            Opens++;
            IsOpen = true;
            return true;
        }

        public void Close() => IsOpen = false;

        public uint Sequence() => SequenceNumber;

        public bool IsAvailable(uint format) => format != 0 && _formats.ContainsKey(format);

        public ClipboardRead Read(uint format, int maxBytes, out byte[]? data)
        {
            data = null;
            if (BumpSequenceOnRead && (format == WindowInterop.CF_UNICODETEXT || format == WindowInterop.CF_DIB || format == WindowInterop.CF_HDROP))
                SequenceNumber++;
            if (_failing.Contains(format) || !_formats.TryGetValue(format, out byte[]? stored)) return ClipboardRead.Failed;
            if (stored == null || stored.Length == 0) return ClipboardRead.Empty;
            if (stored.Length > maxBytes) return ClipboardRead.TooLarge;
            data = (byte[])stored.Clone();
            return ClipboardRead.Ok;
        }
    }
}

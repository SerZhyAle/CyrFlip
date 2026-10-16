using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CyrFlip
{
    /// <summary>One clipboard value ready to be sent: its kind, its size, its name and type, and where the bytes are.</summary>
    internal sealed class ExchangeClipboardPayload
    {
        public const string Text = "text";
        public const string Image = "image";
        public const string File = "file";

        public string Kind = "";
        /// <summary>The bytes that cross the wire before sealing: UTF-8 text with LF ends, a PNG, or the file's length.</summary>
        public long ClearSize;
        /// <summary>The bytes of a text or a picture; null for a file, which is streamed from disk.</summary>
        public byte[]? Bytes;
        public string FilePath = "";
        /// <summary>A file's base name; empty for a text and a picture (item DN carries it inside the box).</summary>
        public string Name = "";
        /// <summary>Empty for a text; <c>image/png</c> for a picture; the guessed type of a file.</summary>
        public string MimeType = "";
        public long FileLastWriteUtcTicks;

        /// <summary>A fresh stream over the clear bytes; the caller disposes it.</summary>
        public Stream OpenClear()
        {
            if (Bytes != null) return new MemoryStream(Bytes, false);
            return new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.SequentialScan);
        }
    }

    /// <summary>
    /// Reads what the clipboard holds, for the send (<c>CLIPBOARD-GUARD</c> items M to P): one open, one object, in
    /// the order a file, a text, a picture; the clipboard is closed before any disk or network I/O. No marker is
    /// read and none is honoured - the person's press is the decision (item M).
    /// </summary>
    internal static class ExchangeClipboardSource
    {
        /// <summary>A one-file <c>CF_HDROP</c> names the file; the file is looked at only after the clipboard is closed.</summary>
        public static ExchangeOutcome<ExchangeClipboardPayload> Read(IClipboardReader reader, long cap)
        {
            string? failure = null;
            List<string>? paths = null;
            ExchangeClipboardPayload? payload = null;

            if (!reader.Open()) return ExchangeOutcome.Fail<ExchangeClipboardPayload>(ExchangeCodes.ClipboardBusy);
            try
            {
                failure = ReadOpen(reader, cap, out paths, out payload);
            }
            finally { reader.Close(); }

            if (failure != null) return ExchangeOutcome.Fail<ExchangeClipboardPayload>(failure);
            if (paths != null) return FromPath(paths, cap);
            return payload != null
                ? ExchangeOutcome.Success(payload)
                : ExchangeOutcome.Fail<ExchangeClipboardPayload>(ExchangeCodes.NothingToSend);
        }

        private static string? ReadOpen(IClipboardReader reader, long cap, out List<string>? paths, out ExchangeClipboardPayload? payload)
        {
            paths = null;
            payload = null;

            // 1. a file (item O.1): the HDROP itself is a few hundred bytes
            if (reader.IsAvailable(WindowInterop.CF_HDROP)
                && reader.Read(WindowInterop.CF_HDROP, 4 * 1024 * 1024, out byte[]? drop) == ClipboardRead.Ok && drop != null)
            {
                List<string> parsed = ParseDropFiles(drop);
                if (parsed.Count > 0)
                {
                    paths = parsed;
                    return null;
                }
            }

            // 2. a text (item O.2): the block is checked against the cap before one character is marshalled into a string
            if (reader.IsAvailable(WindowInterop.CF_UNICODETEXT))
            {
                long utf16Limit = Math.Min(int.MaxValue - 2L, cap * 2 + 2);
                ClipboardRead status = reader.Read(WindowInterop.CF_UNICODETEXT, (int)utf16Limit, out byte[]? raw);
                if (status == ClipboardRead.TooLarge) return ExchangeCodes.LimitReached;
                if (status == ClipboardRead.Ok && raw != null)
                {
                    string text = Encoding.Unicode.GetString(raw);
                    int nul = text.IndexOf('\0');
                    if (nul >= 0) text = text.Substring(0, nul);
                    if (text.Length > 0)
                    {
                        byte[] utf8 = Encoding.UTF8.GetBytes(NormalizeLineEnds(text));
                        if (utf8.LongLength > cap) return ExchangeCodes.LimitReached;
                        payload = new ExchangeClipboardPayload { Kind = ExchangeClipboardPayload.Text, Bytes = utf8, ClearSize = utf8.LongLength };
                        return null;
                    }
                }
            }

            // 3. a picture (item O.3): the registered PNG keeps transparency, then CF_DIBV5, then CF_DIB
            uint cappedBytes = (uint)Math.Min(int.MaxValue, cap);
            if (ClipboardFormats.Png != 0 && reader.IsAvailable(ClipboardFormats.Png))
            {
                ClipboardRead status = reader.Read(ClipboardFormats.Png, (int)cappedBytes, out byte[]? png);
                if (status == ClipboardRead.TooLarge) return ExchangeCodes.LimitReached;
                if (status == ClipboardRead.Ok && png != null)
                {
                    payload = Picture(png);
                    return null;
                }
            }
            foreach (uint format in new[] { WindowInterop.CF_DIBV5, WindowInterop.CF_DIB })
            {
                if (!reader.IsAvailable(format)) continue;
                ClipboardRead status = reader.Read(format, (int)cappedBytes, out byte[]? dib);
                if (status == ClipboardRead.TooLarge) return ExchangeCodes.LimitReached;
                if (status != ClipboardRead.Ok || dib == null) continue;
                // converted after the clipboard is closed would need the bytes anyway; the conversion is CPU, not I/O
                byte[]? converted = ClipboardImage.DibToPng(dib);
                if (converted == null) continue;
                if (converted.LongLength > cap) return ExchangeCodes.LimitReached;
                payload = Picture(converted);
                return null;
            }

            return null; // nothing that can be sent
        }

        private static ExchangeClipboardPayload Picture(byte[] png)
            => new ExchangeClipboardPayload { Kind = ExchangeClipboardPayload.Image, Bytes = png, ClearSize = png.LongLength, MimeType = "image/png" };

        /// <summary>CRLF and a lone CR become LF (item CB); the receiver puts its own platform's ends back.</summary>
        internal static string NormalizeLineEnds(string text)
            => text.IndexOf('\r') < 0 ? text : text.Replace("\r\n", "\n").Replace('\r', '\n');

        /// <summary>The file named by a one-entry HDROP, looked at after the clipboard is closed (item O.1, N).</summary>
        private static ExchangeOutcome<ExchangeClipboardPayload> FromPath(List<string> paths, long cap)
        {
            if (paths.Count != 1) return ExchangeOutcome.Fail<ExchangeClipboardPayload>(ExchangeCodes.SeveralObjects);
            string path = paths[0];
            try
            {
                if (Directory.Exists(path)) return ExchangeOutcome.Fail<ExchangeClipboardPayload>(ExchangeCodes.SeveralObjects);
                var info = new FileInfo(path);
                if (!info.Exists) return ExchangeOutcome.Fail<ExchangeClipboardPayload>(ExchangeCodes.NothingToSend);
                if (info.Length < 1) return ExchangeOutcome.Fail<ExchangeClipboardPayload>(ExchangeCodes.NothingToSend); // a value has at least one byte
                if (info.Length > cap) return ExchangeOutcome.Fail<ExchangeClipboardPayload>(ExchangeCodes.LimitReached);
                return ExchangeOutcome.Success(new ExchangeClipboardPayload
                {
                    Kind = ExchangeClipboardPayload.File,
                    ClearSize = info.Length,
                    FilePath = path,
                    Name = WireName(info.Name),
                    MimeType = MimeTypes.Guess(info.Extension),
                    FileLastWriteUtcTicks = info.LastWriteTimeUtc.Ticks,
                });
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ArgumentException)
            {
                return ExchangeOutcome.Fail<ExchangeClipboardPayload>(ExchangeCodes.NothingToSend);
            }
        }

        /// <summary>
        /// A name as <c>DEVICE-EXCHANGE</c> item BW bounds it: at most 255 bytes of UTF-8, no <c>/</c>, no <c>\</c>, no control
        /// character, not <c>.</c> or <c>..</c>. A longer name is cut in front of its extension.
        /// </summary>
        internal static string WireName(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(c < 0x20 || c == 0x7F || c == '/' || c == '\\' ? '_' : c);
            string clean = sb.ToString().Trim();
            if (clean.Length == 0 || clean == "." || clean == "..") clean = "file";
            while (Encoding.UTF8.GetByteCount(clean) > 255)
            {
                string ext = Path.GetExtension(clean);
                string stem = Path.GetFileNameWithoutExtension(clean);
                if (stem.Length <= 1 || ext.Length > 40) { clean = clean.Substring(0, clean.Length - 1); continue; }
                clean = stem.Substring(0, stem.Length - 1) + ext;
            }
            return clean;
        }

        /// <summary>The paths of a <c>DROPFILES</c> block (wide or ANSI), in order.</summary>
        internal static List<string> ParseDropFiles(byte[] block)
        {
            var list = new List<string>();
            if (block.Length < 20) return list;
            int offset = BitConverter.ToInt32(block, 0);
            bool wide = BitConverter.ToInt32(block, 16) != 0;
            if (offset < 20 || offset >= block.Length) return list;

            int step = wide ? 2 : 1;
            int start = offset;
            while (start + step <= block.Length)
            {
                int end = start;
                while (end + step <= block.Length && (wide ? BitConverter.ToChar(block, end) != '\0' : block[end] != 0)) end += step;
                if (end == start) break; // the empty string that ends the list
                list.Add(wide ? Encoding.Unicode.GetString(block, start, end - start) : Encoding.Default.GetString(block, start, end - start));
                start = end + step;
            }
            return list;
        }

        /// <summary>A wide <c>DROPFILES</c> block naming <paramref name="paths"/> (one is enough for a received file).</summary>
        internal static byte[] BuildDropFiles(params string[] paths)
        {
            byte[] names = Encoding.Unicode.GetBytes(string.Join("\0", paths) + "\0\0");
            var block = new byte[20 + names.Length];
            block[0] = 20; // pFiles
            block[16] = 1; // fWide
            Buffer.BlockCopy(names, 0, block, 20, names.Length);
            return block;
        }
    }

    /// <summary>The media types of the files a clipboard can name; anything else is <c>application/octet-stream</c>.</summary>
    internal static class MimeTypes
    {
        private static readonly Dictionary<string, string> ByExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = "text/plain", [".csv"] = "text/csv", [".htm"] = "text/html", [".html"] = "text/html",
            [".json"] = "application/json", [".xml"] = "application/xml", [".pdf"] = "application/pdf",
            [".zip"] = "application/zip", [".7z"] = "application/x-7z-compressed", [".gz"] = "application/gzip",
            [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif",
            [".webp"] = "image/webp", [".bmp"] = "image/bmp", [".svg"] = "image/svg+xml",
            [".mp3"] = "audio/mpeg", [".wav"] = "audio/wav", [".m4a"] = "audio/mp4", [".flac"] = "audio/flac",
            [".mp4"] = "video/mp4", [".mkv"] = "video/x-matroska", [".webm"] = "video/webm", [".mov"] = "video/quicktime",
            [".doc"] = "application/msword", [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xls"] = "application/vnd.ms-excel", [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        };

        public static string Guess(string extension)
            => ByExtension.TryGetValue(extension ?? "", out string? mime) ? mime : "application/octet-stream";
    }

    /// <summary>
    /// Places a received value on the clipboard (<c>CLIPBOARD-GUARD</c> item Q): a copy the person asked for, so no
    /// backup and no restore; one open, one <c>EmptyClipboard</c>, every format, one close; no privacy marker.
    /// A file is first written into the received-files folder (never overwriting, never into a log folder), then
    /// put on the clipboard as a file list that says "copy".
    /// </summary>
    internal static class ExchangeClipboardSink
    {
        /// <summary>The folder's name for a part still arriving; removed when it is a day old.</summary>
        public const string PartSuffix = ".cyrflip-part";

        public static string FolderOf(AppConfig config)
            => string.IsNullOrWhiteSpace(config.ExchangeClipboardReceivedFolder)
                ? AppConfig.DefaultExchangeClipboardReceivedFolder
                : config.ExchangeClipboardReceivedFolder;

        /// <summary>A name that does not exist in <paramref name="folder"/>: the received name, then <c>name (1).ext</c>, <c>(2)</c> and so on.</summary>
        public static string UniquePath(string folder, string name, Func<string, bool>? exists = null)
        {
            exists ??= File.Exists;
            string safe = SafeName(name);
            string stem = Path.GetFileNameWithoutExtension(safe);
            string ext = Path.GetExtension(safe);
            string candidate = Path.Combine(folder, safe);
            for (int n = 1; exists(candidate) && n < 100000; n++)
                candidate = Path.Combine(folder, stem + " (" + n.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")" + ext);
            return candidate;
        }

        /// <summary>Only the base name, with every character Windows refuses replaced; never empty, never a reserved dot name.</summary>
        internal static string SafeName(string name)
        {
            string baseName = name ?? "";
            int cut = baseName.LastIndexOfAny(new[] { '/', '\\' });
            if (cut >= 0) baseName = baseName.Substring(cut + 1);
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(baseName.Length);
            foreach (char c in baseName)
                sb.Append(Array.IndexOf(invalid, c) >= 0 || c < 0x20 ? '_' : c);
            string cleaned = sb.ToString().Trim().TrimEnd('.', ' ');
            if (cleaned.Length == 0 || cleaned == "." || cleaned == "..") return "received-file";
            if (IsReservedDeviceName(cleaned)) cleaned = "_" + cleaned;
            return cleaned.Length > 200 ? cleaned.Substring(0, 200) : cleaned;
        }

        private static bool IsReservedDeviceName(string name)
        {
            string stem = Path.GetFileNameWithoutExtension(name).ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL") return true;
            if (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && char.IsDigit(stem[3])) return true;
            return false;
        }

        /// <summary>Text: <c>CF_UNICODETEXT</c> with this platform's line ends (item CB).</summary>
        public static bool PlaceText(byte[] utf8)
        {
            string text = new UTF8Encoding(false, false).GetString(utf8);
            string crlf = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
            return Win32Clipboard.TrySetText(crlf);
        }

        /// <summary>A picture: <c>PNG</c> plus <c>CF_DIB</c> of the same pixels, in one pass.</summary>
        public static bool PlaceImage(byte[] png)
        {
            byte[]? dib = ClipboardImage.PngToDib(png);
            return dib != null && Win32Clipboard.TrySetImage(png, dib);
        }

        /// <summary>A file already written to <paramref name="path"/>: <c>CF_HDROP</c> and <c>Preferred DropEffect</c> = copy.</summary>
        public static bool PlaceFile(string path)
        {
            var payloads = new List<KeyValuePair<uint, byte[]>>
            {
                new KeyValuePair<uint, byte[]>(WindowInterop.CF_HDROP, ExchangeClipboardSource.BuildDropFiles(path)),
            };
            if (ClipboardFormats.PreferredDropEffect != 0)
                payloads.Add(new KeyValuePair<uint, byte[]>(ClipboardFormats.PreferredDropEffect, BitConverter.GetBytes(1))); // DROPEFFECT_COPY
            return Win32Clipboard.Restore(payloads);
        }

        /// <summary>Deletes the parts a crashed receive left in the received-files folder once they are a day old.</summary>
        public static void SweepStaleParts(string folder)
        {
            try
            {
                if (!Directory.Exists(folder)) return;
                foreach (string part in Directory.GetFiles(folder, "*" + PartSuffix))
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(part) > TimeSpan.FromDays(1))
                        File.Delete(part);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }
    }
}

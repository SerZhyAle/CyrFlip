using System;

namespace CyrFlip
{
    /// <summary>
    /// Injects a PNG <c>tIME</c> chunk - the <c>CAPTURE-OUTPUT</c> rule 16 carrier for a screenshot:
    /// the capture time inside the file must equal the time in its name, and the rule's EXIF clause is
    /// for photos, so the saver writes the same local wall-clock second the name was formed from
    /// (a proposal to the catalog names tIME as that carrier). The chunk lands right after IHDR, where
    /// every reader accepts an ancillary chunk. A byte array that is not a PNG with an IHDR - or
    /// anything that goes wrong - comes back unchanged: decoration never fails a capture.
    /// </summary>
    internal static class PngTime
    {
        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>The PNG file head: signature (8) plus one whole IHDR chunk (4 + 4 + 13 + 4).</summary>
        private const int HeadLength = 33;

        internal static byte[] Inject(byte[] png, DateTime captured)
        {
            try
            {
                if (png.Length < HeadLength) return png;
                for (int i = 0; i < Signature.Length; i++)
                    if (png[i] != Signature[i]) return png;
                // The first chunk must be IHDR: length 13, type "IHDR".
                if (png[8] != 0 || png[9] != 0 || png[10] != 0 || png[11] != 13) return png;
                if (png[12] != (byte)'I' || png[13] != (byte)'H' || png[14] != (byte)'D' || png[15] != (byte)'R') return png;

                // 4 length + 4 type + 7 data + 4 CRC. Data: year (BE u16), month, day, hour, minute, second.
                var chunk = new byte[19];
                WriteBE(chunk, 0, 7);
                chunk[4] = (byte)'t';
                chunk[5] = (byte)'I';
                chunk[6] = (byte)'M';
                chunk[7] = (byte)'E';
                chunk[8] = (byte)(captured.Year >> 8);
                chunk[9] = (byte)captured.Year;
                chunk[10] = (byte)captured.Month;
                chunk[11] = (byte)captured.Day;
                chunk[12] = (byte)captured.Hour;
                chunk[13] = (byte)captured.Minute;
                chunk[14] = (byte)captured.Second;
                WriteBE(chunk, 15, unchecked((int)Crc32(chunk, 4, 11)));

                var result = new byte[png.Length + chunk.Length];
                Buffer.BlockCopy(png, 0, result, 0, HeadLength);
                Buffer.BlockCopy(chunk, 0, result, HeadLength, chunk.Length);
                Buffer.BlockCopy(png, HeadLength, result, HeadLength + chunk.Length, png.Length - HeadLength);
                return result;
            }
            catch
            {
                return png;
            }
        }

        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }

        /// <summary>The PNG (IEEE, reflected 0xEDB88320) CRC-32 over a byte range.</summary>
        internal static uint Crc32(byte[] data, int offset, int count)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = offset; i < offset + count; i++)
                crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        private static void WriteBE(byte[] target, int offset, int value)
        {
            target[offset] = (byte)(value >> 24);
            target[offset + 1] = (byte)(value >> 16);
            target[offset + 2] = (byte)(value >> 8);
            target[offset + 3] = (byte)value;
        }
    }
}

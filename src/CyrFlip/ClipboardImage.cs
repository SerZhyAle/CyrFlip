using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace CyrFlip
{
    /// <summary>
    /// One captured region, encoded for the clipboard (ticket S0026, section 5.2): <c>PNG</c> - what
    /// Chromium, Electron, Telegram and Office read first, and lossless - and <c>CF_DIB</c> of the very
    /// same pixels for the targets that read no PNG at all (Paint, WordPad, older RDP hosts; Windows
    /// synthesizes <c>CF_BITMAP</c>/<c>CF_DIBV5</c> from it). Pure: no clipboard, no screen, so
    /// <c>ClipboardImageTests</c> can hold both encodings to the pixels.
    /// </summary>
    internal static class ClipboardImage
    {
        /// <summary>BITMAPINFOHEADER's size - the whole header of a BI_RGB DIB.</summary>
        internal const int HeaderSize = 40;
        private const int BI_RGB = 0;

        /// <summary>The image as a PNG file's bytes - the same bytes the saved file gets (CAPTURE-OUTPUT rule 4).</summary>
        public static byte[] EncodePng(Bitmap image)
        {
            using var stream = new MemoryStream();
            image.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }

        /// <summary>
        /// The image as a packed DIB: a 32bpp <c>BITMAPINFOHEADER</c>, <c>BI_RGB</c>, a positive height
        /// (bottom-up - the first row in memory is the image's bottom row), rows of <c>width * 4</c>
        /// bytes (always a multiple of 4). The alpha byte is set to 255: a screen grab carries none, and
        /// a reader that honours it must not see a transparent picture.
        /// </summary>
        public static byte[] EncodeDib(Bitmap image)
        {
            int width = image.Width, height = image.Height;
            int stride = width * 4;
            var dib = new byte[HeaderSize + stride * height];

            WriteInt32(dib, 0, HeaderSize);   // biSize
            WriteInt32(dib, 4, width);        // biWidth
            WriteInt32(dib, 8, height);       // biHeight > 0: bottom-up
            WriteInt16(dib, 12, 1);           // biPlanes
            WriteInt16(dib, 14, 32);          // biBitCount
            WriteInt32(dib, 16, BI_RGB);      // biCompression
            WriteInt32(dib, 20, stride * height); // biSizeImage
            // biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant stay 0.

            BitmapData data = image.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly,
                PixelFormat.Format32bppRgb);
            try
            {
                for (int y = 0; y < height; y++)
                {
                    // Row y of the image (top-down in GDI+) goes to row (height - 1 - y) of the DIB.
                    int offset = HeaderSize + (height - 1 - y) * stride;
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), dib, offset, stride);
                    for (int x = 3; x < stride; x += 4) dib[offset + x] = 0xFF;
                }
            }
            finally { image.UnlockBits(data); }
            return dib;
        }

        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteInt16(byte[] buffer, int offset, short value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.RegularExpressions;

namespace CyrFlip
{
    /// <summary>
    /// The icon files of the Jump List tasks that stand for a meaning instead of a program (ticket S0022
    /// A6): "Manage scenarios" (settings), "Exit" (exit), a yt-dlp download (download) and the stand-in
    /// for a program whose own icon cannot be read (apps, <c>ICON-EXTERNAL</c> rules 2 and 4). They used to
    /// show CyrFlip's own mark, which is the product and not a meaning (<c>ICON-SET</c> rule 7).
    ///
    /// <para>A shortcut takes the <b>decorated</b> look (<c>ICON-RENDER</c> section 10 B, C, E): the glyph
    /// in the on-plate colour on a flat round plate in the accent, because the shell draws the task on a
    /// light or a dark menu the app cannot see and a plate brings its own background.</para>
    ///
    /// <para><b>Why files.</b> The shell wants an icon <i>location</i> - a file and an index - and an
    /// SDK-style net48 project embeds exactly one Win32 icon. So each one is written once as a PNG-payload
    /// <c>.ico</c> (16/20/24/32/48) into an <c>icons\</c> folder beside <c>layout.txt</c> - the MSIX-aware
    /// <see cref="DataFolder.Current"/>, because a Store build pointing the shell at a virtualized path
    /// would show nothing - and is recreated when missing. The file name carries <see cref="Revision"/>, so a
    /// release that changes the drawing never meets an older file. A file that cannot be written leaves the
    /// task without an icon and never fails the list.</para>
    /// </summary>
    internal static class LauncherShortcutIcons
    {
        /// <summary>Bumped when the drawing changes, so an older file on disk is never reused for it.</summary>
        internal const int Revision = 1;

        internal static readonly int[] Sizes = { 16, 20, 24, 32, 48 };

        private static readonly Regex SafeId = new Regex("^[a-z0-9.-]+$", RegexOptions.CultureInvariant);

        internal static string DefaultFolder => Path.Combine(DataFolder.Current, "icons");

        /// <summary>The plate is the product's accent in its day tone and the glyph the on-plate ink (white where it holds 3 : 1).</summary>
        internal static Color Plate => ThemePalette.Light.Accent;
        internal static Color OnPlate => ThemePalette.Light.AccentInk;

        public static string? PathFor(string glyphId) => PathFor(glyphId, DefaultFolder);

        internal static string? PathFor(string glyphId, string folder)
        {
            try
            {
                if (!SafeId.IsMatch(glyphId)) return null;
                string file = Path.Combine(folder, "decorated" + Revision + "-" + glyphId + ".ico");
                if (File.Exists(file) && new FileInfo(file).Length > 0) return file;

                byte[]? bytes = Encode(glyphId);
                if (bytes == null) return null;
                Directory.CreateDirectory(folder);
                string temp = file + "." + System.Diagnostics.Process.GetCurrentProcess().Id + ".tmp";
                File.WriteAllBytes(temp, bytes);
                try { File.Move(temp, file); }
                catch (IOException)
                {
                    // Another instance won the race: its file is the same file.
                    try { File.Delete(temp); } catch { }
                    if (!File.Exists(file)) return null;
                }
                return file;
            }
            catch (Exception ex)
            {
                LauncherLog.Log("Icons: " + glyphId + " not written: " + ex.GetType().Name);
                return null;
            }
        }

        /// <summary>The .ico bytes of one glyph on its plate, or null when the glyph cannot be drawn.</summary>
        internal static byte[]? Encode(string glyphId)
        {
            var images = new List<byte[]>();
            foreach (int size in Sizes)
            {
                using Bitmap? bitmap = GlyphRenderer.RenderOnPlate(glyphId, size, Plate, OnPlate);
                if (bitmap == null) return null;
                using var png = new MemoryStream();
                bitmap.Save(png, ImageFormat.Png);
                images.Add(png.ToArray());
            }

            using var ico = new MemoryStream();
            using (var writer = new BinaryWriter(ico, System.Text.Encoding.Default, leaveOpen: true))
            {
                writer.Write((short)0);            // reserved
                writer.Write((short)1);            // type: icon
                writer.Write((short)images.Count);
                int offset = 6 + 16 * images.Count;
                for (int i = 0; i < images.Count; i++)
                {
                    writer.Write((byte)Sizes[i]);
                    writer.Write((byte)Sizes[i]);
                    writer.Write((byte)0);         // palette
                    writer.Write((byte)0);         // reserved
                    writer.Write((short)1);        // planes
                    writer.Write((short)32);       // bpp
                    writer.Write(images[i].Length);
                    writer.Write(offset);
                    offset += images[i].Length;
                }
                foreach (byte[] png in images) writer.Write(png);
            }
            return ico.ToArray();
        }
    }
}

using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;

namespace CyrFlip
{
    /// <summary>
    /// The scenario launcher's own visual mark: the icon OneClickRunner carried, brought over with
    /// the feature (<c>assets/oneclickrunner-source/app.ico</c>, an <c>EmbeddedResource</c> so the
    /// single self-contained exe still has nothing to install).
    ///
    /// It marks the <b>feature</b>, never the app: CyrFlip's own icon stays on the tray, the settings
    /// window and every Jump List fallback (tech plan §2.3 - a scenario without an icon must never
    /// borrow foreign branding). Only the two surfaces that exist because of the launcher carry this
    /// one - the header of the "Быстрый запуск" settings page and the taskbar button
    /// (<see cref="LauncherTaskbarWindow"/>) - so the absorbed feature stays recognisable to anyone
    /// who used it as a separate program. The page's tab in the strip is not the mark: a logo on a tab
    /// stands for a meaning (ICON-SET rule 7), so the tab draws the vocabulary's
    /// <c>feature.quick-launch</c> (S0022 A2, A7).
    ///
    /// Everything is best-effort: a missing or unreadable resource yields null and each caller simply
    /// draws nothing extra. Instances are cached for the life of the process (a handful of small
    /// images) and handed out shared - callers must not dispose them. UI thread only.
    /// </summary>
    internal static class LauncherBrand
    {
        /// <summary>Must match the <c>LogicalName</c> of the EmbeddedResource in CyrFlip.csproj.</summary>
        private const string ResourceName = "CyrFlip.launcher.ico";

        private static byte[]? _ico;
        private static bool _read;
        private static readonly Dictionary<int, Icon?> IconCache = new Dictionary<int, Icon?>();
        private static readonly Dictionary<int, Bitmap?> ImageCache = new Dictionary<int, Bitmap?>();

        /// <summary>The mark as an <see cref="Icon"/> at (or nearest to) the requested square size.</summary>
        public static Icon? GetIcon(int size)
        {
            if (IconCache.TryGetValue(size, out Icon? cached))
                return cached;

            Icon? icon = null;
            byte[]? bytes = Bytes();
            if (bytes != null)
            {
                try
                {
                    using var stream = new MemoryStream(bytes);
                    icon = new Icon(stream, new Size(size, size));
                }
                catch { /* corrupt or missing frame - the caller degrades to no icon */ }
            }
            IconCache[size] = icon;
            return icon;
        }

        /// <summary>The mark as a bitmap, for controls that take an <see cref="Image"/>.</summary>
        public static Bitmap? GetImage(int size)
        {
            if (ImageCache.TryGetValue(size, out Bitmap? cached))
                return cached;

            Bitmap? image = null;
            Icon? icon = GetIcon(size);
            if (icon != null)
            {
                try
                {
                    using Bitmap raw = icon.ToBitmap();
                    var scaled = new Bitmap(size, size);
                    using (var g = Graphics.FromImage(scaled))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(raw, new Rectangle(0, 0, size, size));
                    }
                    image = scaled;
                }
                catch { /* display-only */ }
            }
            ImageCache[size] = image;
            return image;
        }

        private static byte[]? Bytes()
        {
            if (_read) return _ico;
            _read = true;
            try
            {
                using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
                if (stream != null)
                {
                    var buffer = new byte[stream.Length];
                    int read = 0;
                    while (read < buffer.Length)
                    {
                        int chunk = stream.Read(buffer, read, buffer.Length - read);
                        if (chunk <= 0) break;
                        read += chunk;
                    }
                    if (read == buffer.Length) _ico = buffer;
                }
            }
            catch { /* the feature simply shows no mark */ }
            return _ico;
        }
    }
}

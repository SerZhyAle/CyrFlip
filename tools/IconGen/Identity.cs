using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace IconGen
{
    /// <summary>
    /// PRODUCT-IDENTITY 0.3 (ticket CYRFLP-0139): the product mark's master and the identity manifest.
    ///
    /// The mark is drawn by code (<see cref="IconVariants.ShapeA"/>), so the master SVG is an output of the
    /// same run that cuts every raster (rule 33): <c>assets/identity/master/mark.svg</c> is the full form on a
    /// 256-unit grid, and the hand-hinted 16, 24 and 32 px forms are written beside it as reduced forms, each
    /// for its one size (rule 34). The MSIX logos are cut here at their native sizes instead of being
    /// resampled from the 256 px PNG by the packaging script. Last, every rendition the run wrote is hashed
    /// into <c>assets/identity/identity-manifest.json</c> (section 5). Nothing here is edited by hand: the
    /// code is what a person changes, and the default run of the generator rewrites all of it.
    /// </summary>
    internal static class Identity
    {
        internal const string Generator = "dotnet run --project tools/IconGen";
        private const string Product = "CyrFlip";
        private const string Owner = "Serhii Zhyhunenko (the product's own drawing)";

        private sealed class Row
        {
            public Row(string surface, string path, string? source = null, string? licence = null)
            {
                Surface = surface;
                Path = path;
                Source = source;
                Licence = licence;
            }

            public string Surface { get; }
            public string Path { get; }
            public string? Source { get; }
            public string? Licence { get; }
        }

        /// <summary>The MSIX logo set the package manifest names, at the size Windows asks for each.</summary>
        internal static readonly (string File, int Size, string Surface)[] MsixLogos =
        {
            ("Square44x44Logo.png", 44, "msix.square44"),
            ("StoreLogo.png", 50, "msix.storelogo"),
            ("Square71x71Logo.png", 71, "msix.square71"),
            ("Square150x150Logo.png", 150, "msix.square150"),
        };

        /// <summary>Writes the master SVG, its reduced forms and the MSIX logos.</summary>
        internal static void WriteMasters(string root)
        {
            string master = Path.Combine(root, "assets", "identity", "master");
            Directory.CreateDirectory(master);
            WriteText(Path.Combine(master, "mark.svg"), IconVariants.SvgA(256));
            foreach (int s in IconVariants.HintedSizes)
                WriteText(Path.Combine(master, $"mark-{s}.svg"), IconVariants.SvgA(s));

            // The help set opens from a local folder and reaches nothing outside docs/help, so it keeps its own
            // byte-identical copies (rule 36): the master as the page-head picture (help.head) and the site favicon.
            // tools/site/apply-identity.py then points the pages at them.
            string help = Path.Combine(root, "docs", "help");
            Directory.CreateDirectory(help);
            WriteText(Path.Combine(help, "product-mark.svg"), IconVariants.SvgA(256));
            File.Copy(Path.Combine(root, "docs", "assets", "favicon.ico"), Path.Combine(help, "favicon.ico"), true);

            string msix = Path.Combine(root, "assets", "identity", "msix");
            Directory.CreateDirectory(msix);
            foreach (var (file, size, _) in MsixLogos)
                Program.SavePng(Program.DrawIcon(size), Path.Combine(msix, file));

            Program.SavePng(Program.DrawIcon(256), Path.Combine(root, "assets", "identity", "hub-card-256.png"));
        }

        /// <summary>Hashes every rendition on disk into the identity manifest. A listed file that is missing fails the run.</summary>
        internal static int WriteManifest(string root)
        {
            var rows = new List<Row> { new Row("app.exe-ico", "assets/cyrflip.ico") };
            foreach (var (file, _, surface) in MsixLogos)
                rows.Add(new Row(surface, "assets/identity/msix/" + file));
            foreach (string file in new[]
            {
                "BoxArt-2160x2160.png", "BoxArt-1080x1080.png", "AppTileIcon-300x300.png",
                "Square-150x150.png", "Square-71x71.png", "PosterArt-1440x2160.png", "PosterArt-720x1080.png",
            })
                rows.Add(new Row("store.ms.logo", "assets/store/" + file));
            rows.Add(new Row("vscode.icon", "vscode-extension/icon.png"));
            rows.Add(new Row("github.social-preview", "assets/social-preview.png"));
            rows.Add(new Row("readme.head", "assets/banner.png"));
            rows.Add(new Row("web.favicon-ico", "docs/assets/favicon.ico"));
            rows.Add(new Row("web.favicon-png", "docs/assets/favicon.png"));
            rows.Add(new Row("web.favicon-ico", "docs/help/favicon.ico"));
            rows.Add(new Row("web.og-image", "docs/assets/social-card.png"));
            rows.Add(new Row("site.identity", "docs/assets/icon-256.png"));
            rows.Add(new Row("help.head", "docs/help/product-mark.svg"));
            rows.Add(new Row("hub.card", "assets/identity/hub-card-256.png"));
            const string preview = "tools/IconGen DrawCursorPreview - an illustration of the layout-branded I-beam, generated with the mark";
            rows.Add(new Row("other.cursor-preview", "assets/cursor-preview.png", preview, Owner));
            rows.Add(new Row("other.cursor-preview", "docs/assets/cursor-preview.png", preview, Owner));
            rows.Add(new Row("other.oneclickrunner-mark", "assets/oneclickrunner-source/app.ico",
                "byte copy of OneClickRunner's app.ico (the absorbed product's own mark, LauncherBrand); not written by this generator",
                "Serhii Zhyhunenko (OneClickRunner, the owner's own product)"));

            string masterPath = "assets/identity/master/mark.svg";
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"schemaVersion\": 1,\n");
            sb.Append("  \"product\": \"").Append(Product).Append("\",\n");
            sb.Append("  \"family\": null,\n");
            sb.Append("  \"master\": {\n");
            sb.Append("    \"svg\": \"").Append(masterPath).Append("\",\n");
            sb.Append("    \"reduced\": [\n");
            for (int i = 0; i < IconVariants.HintedSizes.Length; i++)
            {
                int s = IconVariants.HintedSizes[i];
                sb.Append("      { \"name\": \"hinted-").Append(s).Append("\", \"path\": \"assets/identity/master/mark-").Append(s)
                  .Append(".svg\", \"minPx\": ").Append(s).Append(", \"maxPx\": ").Append(s).Append(" }")
                  .Append(i + 1 < IconVariants.HintedSizes.Length ? ",\n" : "\n");
            }
            sb.Append("    ],\n");
            sb.Append("    \"background\": \"").Append(IconVariants.Hex(Program.Bg1)).Append("\",\n");
            sb.Append("    \"sha256\": \"").Append(Sha256(Path.Combine(root, masterPath))).Append("\"\n");
            sb.Append("  },\n");
            sb.Append("  \"generator\": \"").Append(Generator).Append("\",\n");
            sb.Append("  \"renditions\": [\n");
            for (int i = 0; i < rows.Count; i++)
            {
                Row r = rows[i];
                string full = Path.Combine(root, r.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full))
                {
                    Console.Error.WriteLine("identity: rendition missing: " + r.Path);
                    return 1;
                }
                string format = Path.GetExtension(full).TrimStart('.').ToLowerInvariant();
                sb.Append("    { \"surface\": \"").Append(r.Surface).Append("\", \"path\": \"").Append(r.Path)
                  .Append("\", \"format\": \"").Append(format).Append('"');
                if (format != "svg") // section 5: width and height for the raster formats only
                {
                    (int w, int h) = Dimensions(full, format);
                    sb.Append(", \"width\": ").Append(w.ToString(CultureInfo.InvariantCulture))
                      .Append(", \"height\": ").Append(h.ToString(CultureInfo.InvariantCulture));
                }
                sb.Append(", \"sha256\": \"").Append(Sha256(full)).Append('"');
                if (r.Source != null) sb.Append(", \"source\": \"").Append(r.Source).Append('"');
                if (r.Licence != null) sb.Append(", \"licence\": \"").Append(r.Licence).Append('"');
                sb.Append(" }").Append(i + 1 < rows.Count ? ",\n" : "\n");
            }
            sb.Append("  ]\n");
            sb.Append("}\n");
            WriteText(Path.Combine(root, "assets", "identity", "identity-manifest.json"), sb.ToString());
            return 0;
        }

        /// <summary>The largest frame of an ICO, or a PNG's own size.</summary>
        private static (int, int) Dimensions(string path, string format)
        {
            if (format == "ico")
            {
                byte[] b = File.ReadAllBytes(path);
                int count = b[4] | (b[5] << 8), best = 0;
                for (int i = 0; i < count; i++)
                {
                    int w = b[6 + 16 * i];
                    best = Math.Max(best, w == 0 ? 256 : w);
                }
                return (best, best);
            }
            using var img = Image.FromFile(path);
            return (img.Width, img.Height);
        }

        private static string Sha256(string path)
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        private static void WriteText(string path, string text) =>
            File.WriteAllText(path, text.Replace("\r\n", "\n"), new UTF8Encoding(false));
    }
}

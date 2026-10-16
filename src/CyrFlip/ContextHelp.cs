using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CyrFlip
{
    /// <summary>CONTEXT-HELP rules 3, 12, 17: both entry points use the shipped map and assets.</summary>
    internal static class ContextHelp
    {
        internal sealed class Surface
        {
            public string id { get; set; } = "";
            public string name { get; set; } = "";
            public string form { get; set; } = "";
            public string help { get; set; } = "";
            public string parent { get; set; } = "";
            public string reason { get; set; } = "";
            public string type { get; set; } = "";
        }

        internal sealed class Map
        {
            public int schemaVersion { get; set; }
            public Surface[] surfaces { get; set; } = Array.Empty<Surface>();
        }

        private static readonly Lazy<Map> ShippedMap = new Lazy<Map>(() =>
        {
            using Stream stream = typeof(ContextHelp).Assembly.GetManifestResourceStream("CyrFlip.help-map.json")!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return ReadMap(reader.ReadToEnd());
        });
        internal static Map Inventory => ShippedMap.Value;
        internal static Func<string> ApplicationLanguage { get; set; } = () => "English";
        internal static Action<string> Launch { get; set; } = path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

        internal static Map ReadMap(string json)
        {
            var map = new JavaScriptSerializer().Deserialize<Map>(json);
            if (map == null || map.schemaVersion != 1) throw new InvalidDataException("Unsupported help map version.");
            return map;
        }

        internal static string SurfaceId(Form form)
        {
            if (form is SettingsForm settings) return "settings." + settings.HelpPageKey;
            if (form is WelcomeDialog tour) return "welcome." + tour.PageIndex;
            return Inventory.surfaces.FirstOrDefault(s => s.type == form.GetType().FullName)?.id ?? "index";
        }

        internal static string? Resolve(string id, string language, string? element = null)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            Surface? surface;
            while ((surface = Inventory.surfaces.FirstOrDefault(s => s.id == id)) != null)
            {
                if (!seen.Add(id)) throw new InvalidDataException("Cyclic help inheritance.");
                if (surface.form == "none" || surface.form == "layer") return null;
                if (surface.form != "inherit") break;
                id = surface.parent;
            }
            string? help = surface?.help;
            if (string.IsNullOrEmpty(help)) help = "index";
            string locale = Localization.Codes[Localization.IndexOf(language)];
            string target = locale + "/" + help + ".html";
            if (!HasAsset(target)) target = "en/" + help + ".html";
            if (!HasAsset(target)) target = "en/index.html";
            // Only generated anchors are accepted; never interpret a caller's path or URI.
            return target + (element != null && System.Text.RegularExpressions.Regex.IsMatch(element, @"\Aelement-[0-9]+\z") ? "#" + element : "");
        }

        private static bool HasAsset(string relative) => typeof(ContextHelp).Assembly.GetManifestResourceInfo("CyrFlip.help/" + relative) != null;

        internal static void Open(Form form)
        {
            if (form is TranslationResultWindow result) result.KeepOpenForHelp();
            string id = SurfaceId(form);
            if (Inventory.surfaces.FirstOrDefault(s => s.id == id)?.form == "layer")
            {
                CaptureHelpLayer.Toggle(form);
                return;
            }
            string language = form is ThemedForm themed ? themed.HelpLanguage : ApplicationLanguage();
            string? element = HelpElements.Describe(form).FirstOrDefault(e => e.Control != form && e.Control.ContainsFocus)?.Anchor;
            Open(id, language, element);
        }

        internal static async void Open(string id, string language, string? element = null)
        {
            string? relative = Resolve(id, language, element);
            if (relative == null) return;
            try { string path = await Task.Run(() => Extract(relative)); Launch(path); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                DiagnosticLog.Append(DiagnosticLog.Path("help.log"), "open failed " + ex.GetType().Name);
                ConfirmDialog.Show(language, Localization.Translate(language, "Could not open local help. Try again after checking your default browser."), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static readonly object ExtractionLock = new object();
        internal static string Extract(string target, string? cacheRoot = null)
        {
            lock (ExtractionLock) return ExtractCore(target, cacheRoot);
        }

        private static string ExtractCore(string target, string? cacheRoot)
        {
            Assembly assembly = typeof(ContextHelp).Assembly;
            string root = Path.Combine(cacheRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CyrFlip", "Help"), assembly.ManifestModule.ModuleVersionId.ToString("N"));
            // A versioned folder avoids stale images and permits two installed versions to coexist.
            // Verify each file on every open: interrupted extraction and modified cache files repair themselves.
            foreach (string name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("CyrFlip.help/", StringComparison.Ordinal)))
            {
                string relative = name.Substring("CyrFlip.help/".Length);
                string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid help asset path.");
                using Stream stream = assembly.GetManifestResourceStream(name)!;
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                byte[] bytes = memory.ToArray();
                if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllBytes(temporary, bytes);
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            string themeCss = "";
            if (ThemeManager.Mode == ThemeMode.Light)
            {
                string kit = File.ReadAllText(Path.Combine(root, "sza-kit.css"));
                var light = System.Text.RegularExpressions.Regex.Match(kit, @"html\[data-theme=""light""\]\s*\{([^}]+)\}");
                themeCss = ":root {" + light.Groups[1].Value + "} .shot.light {display:block!important} .shot.dark {display:none!important}";
            }
            else if (ThemeManager.Mode == ThemeMode.Dark)
                themeCss = ":root {--bg:#0a0f0a;--text:#f1f5ee;--text-2:#c3ccbc;--muted:#94a08c;--border:rgba(255,255,255,.08);--acc:#3fb950} .shot.light {display:none!important} .shot.dark {display:block!important}";
            File.WriteAllText(Path.Combine(root, "app-theme.css"), themeCss, new UTF8Encoding(false));
            string[] parts = target.Split('#');
            string local = Path.GetFullPath(Path.Combine(root, parts[0].Replace('/', Path.DirectorySeparatorChar)));
            if (!local.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(local)) throw new InvalidDataException("Invalid help target.");
            // A file URI preserves fragment anchors through the Windows browser hand-off.
            return new Uri(local).AbsoluteUri + (parts.Length == 2 ? "#" + parts[1] : "");
        }
    }
}

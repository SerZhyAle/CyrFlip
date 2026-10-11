// The measuring half of Test-ButtonGlyphs.ps1. It is a separate program, compiled by the script with
// the application's own manifest, for one reason: WinForms on .NET Framework reads its per-monitor DPI
// awareness from the executable's manifest, so a PowerShell host (DeviceDpi stays 96 whatever the
// screen does) can never show a 16 px glyph at 150 or 200 percent. This exe can.
//
// C# 5 on purpose - it is built with the framework's own csc.exe, which every Windows has.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

using System.Windows.Forms;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

internal static class NativeDpi
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern IntPtr GetThreadDpiAwarenessContext();
    [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);
    [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint GetDpiForSystem();
    [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
}

internal static class ButtonGlyphHarness
{
    private const BindingFlags BF = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static Assembly _asm;
    private static string _outDir;
    private static readonly List<string> Failures = new List<string>();
    private static readonly List<string[]> Rows = new List<string[]>();
    private static int _buttons;
    private static bool _dpiNoted;
    private static int _langIndex;

    [STAThread]
    private static int Main(string[] args)
    {
        string exe = null; string[] languages = { "English" }; string[] themesToMeasure = { "light", "dark", "hc" };
        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            if (args[i] == "-exe") exe = args[i + 1];
            else if (args[i] == "-out") _outDir = args[i + 1];
            else if (args[i] == "-languages") languages = args[i + 1].Split('|');
        }
        if (exe == null || _outDir == null) { Console.Error.WriteLine("usage: -exe <CyrFlip.exe> -out <dir> [-languages a|b|c]"); return 2; }
        Directory.CreateDirectory(_outDir);
        Application.EnableVisualStyles();
        Console.WriteLine("process DPI awareness " + NativeDpi.GetAwarenessFromDpiAwarenessContext(NativeDpi.GetThreadDpiAwarenessContext()) + " (0 unaware, 1 system, 2 per-monitor), system DPI " + NativeDpi.GetDpiForSystem());
        _asm = Assembly.LoadFrom(exe);

        object light = Static("ThemePalette", "Light"), dark = Static("ThemePalette", "Dark"), hc = Static("ThemePalette", "HighContrast");
        var palettes = new Dictionary<string, object> { { "light", light }, { "dark", dark }, { "hc", hc } };

        const string message = "Delete the selected item? This cannot be undone.";
        var surfaces = new List<KeyValuePair<string, Func<string, Form>>>();
        surfaces.Add(Surface("ConfirmDialog(OK)", delegate(string l) { return New("ConfirmDialog", l, message, MessageBoxButtons.OK, MessageBoxIcon.Information, false, true); }));
        surfaces.Add(Surface("ConfirmDialog(YesNo)", delegate(string l) { return New("ConfirmDialog", l, message, MessageBoxButtons.YesNo, MessageBoxIcon.Question, false, true); }));
        surfaces.Add(Surface("ConfirmDialog(OKCancel,danger)", delegate(string l) { return New("ConfirmDialog", l, message, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, true, true); }));
        surfaces.Add(Surface("ConfirmDialog(YesNo,danger)", delegate(string l) { return New("ConfirmDialog", l, message, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, true, true); }));
        surfaces.Add(Surface("HotkeyDialog", delegate(string l) { return New("HotkeyDialog", "Ctrl+Shift+F12", "T", l); }));
        surfaces.Add(Surface("YtDlpLinkDialog", delegate(string l) { return New("YtDlpLinkDialog", l); }));
        surfaces.Add(Surface("TranslationDialog", delegate(string l) { return New("TranslationDialog", null, l, null); }));
        surfaces.Add(Surface("LauncherScenarioDialog", delegate(string l) { return New("LauncherScenarioDialog", null, l); }));
        surfaces.Add(Surface("ExchangeExportDialog", delegate(string l) { return New("ExchangeExportDialog", l, true, false); }));

        for (_langIndex = 0; _langIndex < languages.Length; _langIndex++)
        {
            string language = languages[_langIndex];
            foreach (KeyValuePair<string, Func<string, Form>> surface in surfaces)
            {
                foreach (string theme in themesToMeasure)
                {
                    Form form = null;
                    try
                    {
                        form = surface.Value(language);
                        MeasureForm(form, surface.Key, language, theme, palettes);
                    }
                    catch (Exception ex)
                    {
                        Failures.Add("could not build " + surface.Key + " [" + language + "/" + theme + "]: " + ex.GetBaseException().Message);
                    }
                    finally { if (form != null) form.Dispose(); }
                }
            }
        }
        return Report();
    }

    private static KeyValuePair<string, Func<string, Form>> Surface(string name, Func<string, Form> make)
    {
        return new KeyValuePair<string, Func<string, Form>>(name, make);
    }

    private static object Static(string type, string member)
    {
        Type t = _asm.GetType("CyrFlip." + type, true);
        FieldInfo f = t.GetField(member, BF);
        return f != null ? f.GetValue(null) : t.GetProperty(member, BF).GetValue(null, null);
    }

    private static Form New(string type, params object[] ctorArgs)
    {
        return (Form)Activator.CreateInstance(_asm.GetType("CyrFlip." + type, true), BF, null, ctorArgs, null);
    }

    private static object Call(string type, string method, params object[] callArgs)
    {
        return _asm.GetType("CyrFlip." + type, true).GetMethod(method, BF).Invoke(null, callArgs);
    }

    private static void MeasureForm(Form form, string surface, string language, string theme, Dictionary<string, object> palettes)
    {
        form.StartPosition = FormStartPosition.Manual;
        // On a monitor (so its DPI is the one WinForms scales to) but invisible: a window placed off every
        // screen reports 96 DPI.
        form.Location = new Point(40, 40);
        form.Opacity = 0;
        form.ShowInTaskbar = false;
        form.Show();
        Application.DoEvents();
        // A live theme change - walk light, dark, high contrast, then the one measured - not a rebuild.
        foreach (string step in new[] { "light", "dark", "hc", theme })
        {
            Call("ThemeApply", "Apply", form, palettes[step]);
            Application.DoEvents();
        }
        if (!_dpiNoted) { _dpiNoted = true; Console.WriteLine("first form: DeviceDpi " + form.DeviceDpi + ", GetDpiForWindow " + NativeDpi.GetDpiForWindow(form.Handle) + ", font " + form.Font.SizeInPoints + "pt"); }
        object palette = palettes[theme];
        bool highContrast = theme == "hc";

        using (var bmp = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
            string lang = Sanitize(language); if (lang.Length == 0) lang = "lang" + _langIndex;
            string slug = Sanitize(surface) + "_" + lang + "_" + theme;
            bmp.Save(Path.Combine(_outDir, slug + ".png"), ImageFormat.Png);

            foreach (Button b in Buttons(form))
            {
                object glyph = Call("ButtonGlyphs", "GlyphOf", b);
                if (glyph == null) continue;
                Point screen = b.Parent.PointToScreen(b.Location);
                int x = screen.X - form.Left, y = screen.Y - form.Top;
                if (x < 0 || y < 0 || x + b.Width > bmp.Width || y + b.Height > bmp.Height) continue;
                _buttons++;

                Color face = bmp.GetPixel(x + 4, y + 4);
                object look = Call("ButtonGlyphs", "LookOf", b);
                Color ink = (Color)Call("ButtonGlyphs", "InkFor", look, palette, b.Enabled);
                Color text = b.Enabled ? b.ForeColor : (Color)palette.GetType().GetProperty("TextDisabled", BF).GetValue(palette, null);
                double glyphContrast = Contrast(ink, face), textContrast = Contrast(text, face);

                // Geometry from the product's own box (what Draw fills), then pixels: the ink inside the box,
                // and the distance from the box's edge to the first column of the caption's ink.
                Rectangle box;
                using (Graphics g = b.CreateGraphics()) box = (Rectangle)Call("ButtonGlyphs", "GlyphBox", b, g);
                int bx = x + box.X, by = y + box.Y;
                bool glyphOnRight = (bool)Call("ButtonGlyphs", "GlyphOnRight", b);
                int inkMin = int.MaxValue, inkMax = -1;
                for (int cx = bx; cx < bx + box.Width; cx++)
                    if (ColumnHasInk(bmp, cx, by, by + box.Height - 1, face)) { if (cx < inkMin) inkMin = cx; if (cx > inkMax) inkMax = cx; }
                string glyphPx = inkMax >= 0 ? (inkMax - inkMin + 1).ToString(CultureInfo.InvariantCulture) : "0";
                int rowA = y + b.Height / 2 - b.Height / 4, rowB = y + b.Height / 2 + b.Height / 4;
                string gap = "";
                if (glyphOnRight)
                {
                    for (int cx = bx - 1; cx > x + 2; cx--)
                        if (ColumnHasInk(bmp, cx, rowA, rowB, face)) { gap = (bx - 1 - cx).ToString(CultureInfo.InvariantCulture); break; }
                }
                else
                {
                    for (int cx = bx + box.Width; cx < x + b.Width - 2; cx++)
                        if (ColumnHasInk(bmp, cx, rowA, rowB, face)) { gap = (cx - (bx + box.Width)).ToString(CultureInfo.InvariantCulture); break; }
                }                Rows.Add(new[]
                {
                    surface, language, theme, b.Text, (string)glyph, look.ToString(), b.Enabled.ToString(),
                    b.Width.ToString(CultureInfo.InvariantCulture), b.Height.ToString(CultureInfo.InvariantCulture),
                    glyphPx, gap, Call("ButtonGlyphs", "Dpi", b).ToString(),
                    string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", face.R, face.G, face.B),
                    glyphContrast.ToString("0.00", CultureInfo.InvariantCulture), textContrast.ToString("0.00", CultureInfo.InvariantCulture),
                });
                if (b.Enabled && !highContrast)
                {
                    if (glyphContrast < 3.0) Failures.Add("glyph " + glyphContrast.ToString("0.00", CultureInfo.InvariantCulture) + ":1 < 3:1 - " + surface + " [" + language + "/" + theme + "] '" + b.Text + "'");
                    if (textContrast < 4.5) Failures.Add("caption " + textContrast.ToString("0.00", CultureInfo.InvariantCulture) + ":1 < 4.5:1 - " + surface + " [" + language + "/" + theme + "] '" + b.Text + "'");
                }
            }
        }
    }

    private static bool ColumnHasInk(Bitmap bmp, int cx, int y0, int y1, Color face)
    {
        for (int cy = y0; cy <= y1; cy++)
        {
            Color p = bmp.GetPixel(cx, cy);
            if (Math.Abs(p.R - face.R) + Math.Abs(p.G - face.G) + Math.Abs(p.B - face.B) > 60) return true;
        }
        return false;
    }

    private static IEnumerable<Button> Buttons(Control root)
    {
        foreach (Control c in root.Controls)
        {
            Button b = c as Button;
            if (b != null) yield return b;
            foreach (Button inner in Buttons(c)) yield return inner;
        }
    }

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder();
        foreach (char ch in s) sb.Append(char.IsLetterOrDigit(ch) && ch < 128 ? ch : '-');
        return sb.ToString().Trim('-');
    }

    private static double Luminance(Color c)
    {
        Func<int, double> f = delegate(int v) { double x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); };
        return 0.2126 * f(c.R) + 0.7152 * f(c.G) + 0.0722 * f(c.B);
    }

    private static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static int Report()
    {
        var csv = new StringBuilder("Surface,Language,Theme,Caption,Glyph,Look,Enabled,Width,Height,GlyphPx,GapPx,DeviceDpi,Face,GlyphContrast,TextContrast\r\n");
        foreach (string[] row in Rows)
        {
            for (int i = 0; i < row.Length; i++) { if (i > 0) csv.Append(','); csv.Append('"').Append(row[i].Replace("\"", "\"\"")).Append('"'); }
            csv.Append("\r\n");
        }
        File.WriteAllText(Path.Combine(_outDir, "measurements.csv"), csv.ToString(), new UTF8Encoding(true));

        var dpis = new SortedDictionary<string, int>();
        double gMin = 99, tMin = 99; int gapMin = int.MaxValue, gapMax = 0, glyphMin = int.MaxValue, glyphMax = 0;
        foreach (string[] r in Rows)
        {
            if (!dpis.ContainsKey(r[11])) dpis[r[11]] = 0;
            dpis[r[11]]++;
            if (r[6] != "True" || r[2] == "hc") continue;
            gMin = Math.Min(gMin, double.Parse(r[13], CultureInfo.InvariantCulture));
            tMin = Math.Min(tMin, double.Parse(r[14], CultureInfo.InvariantCulture));
            int v;
            if (int.TryParse(r[10], out v)) { gapMin = Math.Min(gapMin, v); gapMax = Math.Max(gapMax, v); }
            if (int.TryParse(r[9], out v)) { glyphMin = Math.Min(glyphMin, v); glyphMax = Math.Max(glyphMax, v); }
        }
        Console.WriteLine("buttons measured: " + _buttons + "  (rows " + Rows.Count + "); shots and measurements.csv in " + _outDir);
        foreach (KeyValuePair<string, int> d in dpis) Console.WriteLine("  window DPI " + d.Key + ": " + d.Value + " buttons");
        Console.WriteLine("glyph contrast min " + gMin.ToString("0.00", CultureInfo.InvariantCulture) + ":1 (3:1 required); caption contrast min " + tMin.ToString("0.00", CultureInfo.InvariantCulture) + ":1 (4.5:1 required); tinted face included");
        if (gapMin != int.MaxValue) Console.WriteLine("measured glyph-to-caption gap " + gapMin + ".." + gapMax + " device px; glyph ink width " + glyphMin + ".." + glyphMax + " device px");
        foreach (string look in new[] { "Affirmative", "Dismissing", "DismissingSafe", "Acting", "Neutral" })
        {
            foreach (string theme in new[] { "light", "dark" })
            {
                double g = 99, t = 99; int n = 0; string face = "";
                foreach (string[] r in Rows)
                {
                    if (r[5] != look || r[2] != theme || r[6] != "True") continue;
                    n++; face = r[12];
                    g = Math.Min(g, double.Parse(r[13], CultureInfo.InvariantCulture));
                    t = Math.Min(t, double.Parse(r[14], CultureInfo.InvariantCulture));
                }
                if (n > 0) Console.WriteLine("  " + look.PadRight(15) + theme.PadRight(6) + "n=" + n.ToString().PadLeft(3) + "  glyph " + g.ToString("0.00", CultureInfo.InvariantCulture) + ":1  caption " + t.ToString("0.00", CultureInfo.InvariantCulture) + ":1  e.g. face " + face);
            }
        }
        if (Failures.Count > 0)
        {
            var unique = new SortedDictionary<string, bool>();
            foreach (string f in Failures) unique[f] = true;
            foreach (string f in unique.Keys) Console.WriteLine("FAIL  " + f);
            return 1;
        }
        Console.WriteLine("PASS  every glyph held 3:1 and every caption 4.5:1 on the face it is drawn on.");
        return 0;
    }
}

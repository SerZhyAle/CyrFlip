using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Web.Script.Serialization;

namespace L10nKeys
{
    internal static class Program
    {
        private const string Usage = @"L10nKeys - the English-source-key migration tool (ticket S0070). Run from the repository root.

  inventory [--out DIR] [--decisions FILE]
        Reads the tables and every literal that reaches them and writes the reports (ru.txt, en.txt, key-map.tsv,
        collisions.txt, sites.csv, unread.txt, identity-uses.txt, non-key-cyrillic.txt, summary.txt) to DIR.
        Touches no source file.
  apply [--decisions FILE] [--out DIR] [--tests]
        Rewrites src/CyrFlip: every table entry to the English-key shape, every literal whose value is a key to
        the key's English text. With --tests, tests/CyrFlip.Tests gets the literals in a translating call too.
        Refuses while an entry is unread or two keys share one English text.
  retest [--dir DIR] [--only A.cs,B.cs] [--dry]
        Replaces every literal that is an old Russian key by its new key, in DIR (default tests/CyrFlip.Tests),
        read from key-map.tsv. For code that passes keys around; an expected on-screen Russian text is the
        caller's to put back.
  snapshot --exe PATH --out FILE
        Writes Translate(language, key) for every registered key in every language of a built CyrFlip.exe.
  compare --old FILE --new FILE --map FILE
        A1: the same cells, through the old-key -> new-key map. Exit code 1 on any difference.
  rename OLD NEW [--root DIR]
        Renames one English key in the tables and at every use (Rename-SourceKey.ps1 wraps this).";

        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            if (args.Length == 0) { Console.WriteLine(Usage); return 2; }
            try
            {
                var opt = Options.Parse(args);
                switch (args[0])
                {
                    case "inventory": return Inventory_(opt, apply: false);
                    case "apply": return Inventory_(opt, apply: true);
                    case "retest": return Retest(opt);
                    case "snapshot": return Snapshot(opt);
                    case "compare": return Compare(opt);
                    case "rename": return Rename(opt);
                    default: Console.WriteLine(Usage); return 2;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAILED: " + ex.Message);
                return 2;
            }
        }

        // ------------------------------------------------------------------ inventory / apply

        private static int Inventory_(Options o, bool apply)
        {
            string root = Directory.GetCurrentDirectory();
            string outDir = o.Get("out", Path.Combine(root, "PLAN", "S0070_english-source-strings", "research"));
            Directory.CreateDirectory(outDir);
            var decisions = ReadDecisions(o.Get("decisions", Path.Combine(outDir, "decisions.tsv")));

            Inventory inv = Migration.Scan(root, Path.Combine("src", "CyrFlip"), decisions);
            var tests = new List<SourceFile>();
            if (o.Flag("tests"))
            {
                foreach (string p in Migration.SourceFiles(root, Path.Combine("tests", "CyrFlip.Tests")))
                {
                    try { tests.Add(SourceFile.Load(root, p, inv.Problems)); }
                    catch (InvalidDataException ex) { Console.WriteLine("SKIPPED (fix by hand): " + ex.Message); }
                }
                Migration.CollectSites(inv, tests);
            }

            WriteReports(inv, outDir);
            Console.WriteLine(File.ReadAllText(Path.Combine(outDir, "summary.txt")));

            var blockers = new List<string>();
            blockers.AddRange(inv.Unread);
            blockers.AddRange(inv.Problems);
            foreach (var g in inv.Collisions) blockers.Add("two keys share the English key " + CsLexer.Quote(g.Key) + ": " + string.Join(" | ", g.Select(e => CsLexer.Quote(e.OldKey))));
            foreach (Entry e in inv.Entries)
            {
                if (CsLexer.HasCyrillic(e.NewKey)) blockers.Add("the English text holds Cyrillic: " + CsLexer.Quote(e.NewKey));
                if (e.NewKey.Trim().Length == 0) blockers.Add("an empty English key for " + CsLexer.Quote(e.OldKey));
            }
            if (!apply) { Console.WriteLine(blockers.Count == 0 ? "inventory: clean" : "inventory: " + blockers.Count + " blocker(s), see unread.txt / collisions.txt"); return blockers.Count == 0 ? 0 : 1; }

            if (blockers.Count > 0)
            {
                foreach (string b in blockers.Take(40)) Console.Error.WriteLine("BLOCKED: " + b);
                return 1;
            }

            int files = 0, edits = 0;
            foreach (SourceFile f in inv.Files.Concat(tests))
            {
                bool isTest = f.Path.StartsWith("tests/", StringComparison.Ordinal);
                List<Edit> list = Migration.EditsFor(inv, f, shapeOnly: isTest);
                if (list.Count == 0) continue;
                string updated = Migration.ApplyEdits(f.Text, list);
                // the rewritten file must lex without a new problem and keep every token
                var again = new List<string>();
                List<Tok> toks = CsLexer.Lex(updated, again, f.Path);
                if (again.Count != 0) throw new InvalidOperationException(f.Path + ": the rewritten file no longer lexes: " + again[0]);
                f.Save(updated);
                files++; edits += list.Count;
            }
            Console.WriteLine("apply: " + edits + " edits in " + files + " files");
            return 0;
        }

        private static Dictionary<string, string> ReadDecisions(string path)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!File.Exists(path)) return map;
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (raw.Trim().Length == 0 || raw.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
                string[] parts = raw.Split('\t');
                if (parts.Length < 2) throw new InvalidOperationException("decisions: a line needs two tab-separated columns: " + raw);
                map[Unquote(parts[0])] = Unquote(parts[1]);
            }
            return map;
        }

        private static string Unquote(string s) => s.Trim();

        private static void WriteReports(Inventory inv, string dir)
        {
            var utf8 = new UTF8Encoding(false);
            Func<string, string> esc = v => CsLexer.Quote(v).Substring(1, CsLexer.Quote(v).Length - 2);

            File.WriteAllLines(Path.Combine(dir, "ru.txt"), inv.Entries.Select(e => esc(e.OldKey)), utf8);
            File.WriteAllLines(Path.Combine(dir, "en.txt"), inv.Entries.Select(e => esc(e.NewKey)), utf8);

            File.WriteAllLines(Path.Combine(dir, "key-map.tsv"),
                inv.Entries.Select(e => esc(e.OldKey) + "\t" + esc(e.NewKey) + "\t" + (e.Variant ? esc(e.En) : "") + "\t" + e.Call.File.Path + ":" + e.Call.Line), utf8);

            var sb = new StringBuilder();
            sb.AppendLine("# English texts shared by more than one Russian key, before the decisions of decisions.tsv are read.");
            foreach (var g in inv.Entries.GroupBy(e => e.En, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                sb.AppendLine("[" + esc(g.Key) + "]");
                foreach (Entry e in g)
                    sb.AppendLine("    " + esc(e.OldKey) + "  ->  " + (e.Variant ? "VARIANT " + esc(e.NewKey) : "(same)") + "   " + e.Call.File.Path + ":" + e.Call.Line);
            }
            if (inv.Collisions.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("# STILL SHARED after the decisions - apply refuses:");
                foreach (var g in inv.Collisions) sb.AppendLine("[" + esc(g.Key) + "]  " + string.Join(" | ", g.Select(e => esc(e.OldKey))));
            }
            File.WriteAllText(Path.Combine(dir, "collisions.txt"), sb.ToString(), utf8);

            File.WriteAllLines(Path.Combine(dir, "sites.csv"),
                new[] { "file,line,column,kind,old_key,new_key" }.Concat(inv.Sites.OrderBy(s => s.File.Path, StringComparer.Ordinal).ThenBy(s => s.Lit.Start).Select(s =>
                    string.Join(",", s.File.Path, s.Lit.Line, Column(s), s.InCall ? "call" : "indirect",
                        "\"" + esc(s.Entry.OldKey).Replace("\"", "\"\"") + "\"", "\"" + esc(s.Entry.NewKey).Replace("\"", "\"\"") + "\""))), utf8);

            var unread = new List<string>(inv.Problems.Select(p => "lexer: " + p));
            unread.AddRange(inv.Unread);
            File.WriteAllLines(Path.Combine(dir, "unread.txt"), unread, utf8);

            var id = new List<string> { "# Literals whose value is a registered key and whose context could be an identity use (ticket S0070 A6)." };
            id.AddRange(inv.Sites.Where(s => s.Context.Length > 0).OrderBy(s => s.File.Path, StringComparer.Ordinal).ThenBy(s => s.Lit.Start)
                .Select(s => s.File.Path + ":" + s.Lit.Line + "  [" + s.Context + "]  " + CsLexer.Quote(s.Entry.OldKey)));
            File.WriteAllLines(Path.Combine(dir, "identity-uses.txt"), id, utf8);

            File.WriteAllLines(Path.Combine(dir, "non-key-cyrillic.txt"), inv.NonKeyCyrillic, utf8);

            var perFile = inv.Sites.GroupBy(s => s.File.Path).OrderByDescending(g => g.Count()).Take(15)
                .Select(g => "    " + g.Count().ToString().PadLeft(4) + "  " + g.Key);
            var sum = new StringBuilder();
            sum.AppendLine("table entries read              : " + inv.Entries.Count);
            sum.AppendLine("  named shape                   : " + inv.Entries.Count(e => !e.PositionalShape));
            sum.AppendLine("  positional shape              : " + inv.Entries.Count(e => e.PositionalShape));
            sum.AppendLine("  keyed by a constant           : " + inv.Entries.Count(e => e.KeyConst != null));
            sum.AppendLine("  variants (decided collisions) : " + inv.Entries.Count(e => e.Variant));
            sum.AppendLine("Add( calls NOT read             : " + inv.Unread.Count);
            sum.AppendLine("lexer problems                  : " + inv.Problems.Count);
            sum.AppendLine("English texts shared (decided)  : " + inv.Collisions.Count);
            sum.AppendLine("key literals outside the tables : " + inv.Sites.Count + "  (in a translating call: " + inv.Sites.Count(s => s.InCall) + ", indirect: " + inv.Sites.Count(s => !s.InCall) + ")");
            sum.AppendLine("  possible identity uses        : " + inv.Sites.Count(s => s.Context.Length > 0));
            sum.AppendLine("Cyrillic literals that are no key: " + inv.NonKeyCyrillic.Count);
            sum.AppendLine("sites per file (top 15):");
            foreach (string l in perFile) sum.AppendLine(l);
            File.WriteAllText(Path.Combine(dir, "summary.txt"), sum.ToString(), utf8);
        }

        private static int Column(Site s)
        {
            int ls = s.File.Text.LastIndexOf('\n', Math.Max(0, s.Lit.Start - 1));
            return s.Lit.Start - ls;
        }

        // ------------------------------------------------------------------ retest

        /// <summary>
        /// Replaces, in the given directory, every literal whose value is an old Russian key by the new key
        /// (read from key-map.tsv). For code that passes keys around - the tests; a literal that is the
        /// expected on-screen Russian text of a Russian-UI assertion is the caller's to put back.
        /// </summary>
        private static int Retest(Options o)
        {
            string root = Directory.GetCurrentDirectory();
            string mapFile = o.Get("map", Path.Combine(root, "PLAN", "S0070_english-source-strings", "research", "key-map.tsv"));
            string dir = o.Get("dir", Path.Combine("tests", "CyrFlip.Tests"));
            var only = o.Get("only", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
            bool dry = o.Flag("dry");

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in File.ReadAllLines(mapFile, Encoding.UTF8))
            {
                string[] p = line.Split('\t');
                if (p.Length >= 2) map[Unescape(p[0])] = Unescape(p[1]);
            }

            int files = 0, edits = 0;
            foreach (string path in Migration.SourceFiles(root, dir))
            {
                if (only.Count > 0 && !only.Any(x => path.EndsWith(x, StringComparison.OrdinalIgnoreCase))) continue;
                SourceFile f;
                try { f = SourceFile.Load(root, path, new List<string>()); }
                catch (InvalidDataException ex) { Console.WriteLine("SKIPPED: " + ex.Message); continue; }
                var list = new List<Edit>();
                foreach (Tok t in f.Toks)
                {
                    if (t.Kind != TokKind.Str || t.Interpolated || t.Raw) continue;
                    if (map.TryGetValue(t.Value, out string? nk))
                        list.Add(new Edit { Start = t.Start, End = t.End, Text = CsLexer.Quote(nk), Why = "key" });
                }
                if (list.Count == 0) continue;
                Console.WriteLine(list.Count.ToString().PadLeft(5) + "  " + f.Path);
                files++; edits += list.Count;
                if (!dry) f.Save(Migration.ApplyEdits(f.Text, list));
            }
            Console.WriteLine("retest: " + edits + " literal(s) in " + files + " file(s)" + (dry ? " (dry run)" : ""));
            return 0;
        }

        // ------------------------------------------------------------------ snapshot / compare

        private static int Snapshot(Options o)
        {
            string exe = Path.GetFullPath(o.Require("exe"));
            string outFile = o.Require("out");
            Assembly asm = Assembly.LoadFrom(exe);
            Type loc = asm.GetType("CyrFlip.Localization", true)!;
            RuntimeHelpers.RunClassConstructor(loc.TypeHandle);
            BindingFlags any = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var names = (string[])loc.GetField("Names", any)!.GetValue(null)!;
            var all = (IEnumerable)loc.GetProperty("All", any)!.GetValue(null)!;
            MethodInfo translate = loc.GetMethod("Translate", any, null, new[] { typeof(string), typeof(string) }, null)!;

            var keys = new List<string>();
            foreach (object kv in all)
                keys.Add((string)kv.GetType().GetProperty("Key")!.GetValue(kv)!);
            keys.Sort(StringComparer.Ordinal);

            var sb = new StringBuilder();
            sb.Append("{\n  \"names\": [").Append(string.Join(", ", names.Select(JsonString))).Append("],\n  \"entries\": {\n");
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                var cells = names.Select(n => (string)translate.Invoke(null, new object[] { n, key })!);
                sb.Append("    ").Append(JsonString(key)).Append(": [").Append(string.Join(", ", cells.Select(JsonString))).Append(']');
                sb.Append(i + 1 < keys.Count ? ",\n" : "\n");
            }
            sb.Append("  }\n}\n");
            File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false));
            Console.WriteLine("snapshot: " + keys.Count + " keys x " + names.Length + " languages -> " + outFile);
            return 0;
        }

        private static string JsonString(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        private static Dictionary<string, string[]> ReadSnapshot(string path, out string[] names)
        {
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
            var root = (Dictionary<string, object>)ser.DeserializeObject(File.ReadAllText(path, Encoding.UTF8));
            names = ((object[])root["names"]).Select(x => (string)x).ToArray();
            var entries = (Dictionary<string, object>)root["entries"];
            return entries.ToDictionary(kv => kv.Key, kv => ((object[])kv.Value).Select(x => (string)x).ToArray(), StringComparer.Ordinal);
        }

        private static int Compare(Options o)
        {
            var oldMap = ReadSnapshot(o.Require("old"), out string[] oldNames);
            var newMap = ReadSnapshot(o.Require("new"), out string[] newNames);
            if (!oldNames.SequenceEqual(newNames)) throw new InvalidOperationException("the two snapshots list different languages");

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in File.ReadAllLines(o.Require("map"), Encoding.UTF8))
            {
                string[] p = line.Split('\t');
                if (p.Length >= 2) map[Unescape(p[0])] = Unescape(p[1]);
            }

            int cells = 0, bad = 0, missing = 0;
            var seenNew = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in oldMap)
            {
                if (!map.TryGetValue(kv.Key, out string? nk) || !newMap.TryGetValue(nk, out string[]? cur))
                {
                    missing++;
                    if (missing <= 20) Console.WriteLine("MISSING: old key " + CsLexer.Quote(kv.Key) + " has no entry in the new build");
                    continue;
                }
                seenNew.Add(nk);
                for (int i = 0; i < oldNames.Length; i++)
                {
                    cells++;
                    if (!string.Equals(kv.Value[i], cur[i], StringComparison.Ordinal))
                    {
                        bad++;
                        if (bad <= 30) Console.WriteLine("DIFF [" + oldNames[i] + "] " + CsLexer.Quote(kv.Key) + "\n   old: " + CsLexer.Quote(kv.Value[i]) + "\n   new: " + CsLexer.Quote(cur[i]));
                    }
                }
            }
            int extra = newMap.Keys.Count(k => !seenNew.Contains(k));
            Console.WriteLine("compare: " + oldMap.Count + " old keys, " + newMap.Count + " new keys, " + cells + " cells, "
                + bad + " different, " + missing + " old keys without a new entry, " + extra + " new keys nobody maps to");
            return bad == 0 && missing == 0 && extra == 0 ? 0 : 1;
        }

        private static string Unescape(string s)
        {
            // key-map.tsv holds C#-escaped text without the surrounding quotes
            var lex = CsLexer.Lex("\"" + s + "\"", new List<string>(), "key-map");
            return lex.Count == 1 && lex[0].Kind == TokKind.Str ? lex[0].Value : s;
        }

        // ------------------------------------------------------------------ rename

        private static int Rename(Options o)
        {
            if (o.Positional.Count < 3) { Console.WriteLine(Usage); return 2; }
            string oldKey = o.Positional[1], newKey = o.Positional[2];
            string root = Path.GetFullPath(o.Get("root", Directory.GetCurrentDirectory()));
            var problems = new List<string>();
            var files = new List<SourceFile>();
            foreach (string sub in new[] { Path.Combine("src", "CyrFlip"), Path.Combine("tests", "CyrFlip.Tests") })
                foreach (string p in Migration.SourceFiles(root, sub)) files.Add(SourceFile.Load(root, p, problems));
            if (CsLexer.HasCyrillic(newKey)) throw new InvalidOperationException("a key holds no Cyrillic");
            if (newKey.Trim().Length == 0 || newKey == oldKey) throw new InvalidOperationException("the new key must be a different non-empty text");

            // the table entry (the first argument of an Add call - a translation column that happens to read
            // the same, "Name" in German, is not the key) and every literal of the same value outside the tables
            int tableHits = 0, useHits = 0;
            var perFile = new Dictionary<SourceFile, List<Edit>>();
            foreach (SourceFile f in files)
            {
                bool isTable = Path.GetFileName(f.Path).StartsWith("Localization", StringComparison.Ordinal);
                List<AddCall> calls = isTable ? TableReader.ReadAddCalls(f) : new List<AddCall>();
                foreach (Tok t in f.Toks)
                {
                    if (t.Kind != TokKind.Str || t.Interpolated || t.Raw || t.Value != oldKey) continue;
                    AddCall? inside = calls.FirstOrDefault(c => t.Start > c.OpenParen && t.End <= c.CloseParen);
                    bool isKey = inside != null && inside.Args.Count > 0 && inside.Args[0].Label == null && inside.Args[0].Lit == t;
                    if (inside != null && !isKey) continue;   // a column of some entry: not ours
                    var list = perFile.TryGetValue(f, out List<Edit>? l) ? l : (perFile[f] = new List<Edit>());
                    list.Add(new Edit { Start = t.Start, End = t.End, Text = CsLexer.Quote(newKey), Why = "rename" });
                    if (isKey) tableHits++; else useHits++;
                }
            }            if (tableHits == 0) throw new InvalidOperationException("no table entry has the key " + CsLexer.Quote(oldKey));
            if (tableHits > 1) throw new InvalidOperationException("the key matches " + tableHits + " table literals; rename by hand");
            foreach (var kv in perFile)
            {
                string updated = Migration.ApplyEdits(kv.Key.Text, kv.Value);
                kv.Key.Save(updated);
            }
            Console.WriteLine("rename: 1 table entry and " + useHits + " use(s) in " + perFile.Count + " file(s). Build and run the localization gates.");
            return 0;
        }
    }

    internal sealed class Options
    {
        public List<string> Positional = new List<string>();
        private readonly Dictionary<string, string> _named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static Options Parse(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a.StartsWith("--", StringComparison.Ordinal))
                {
                    string name = a.Substring(2);
                    bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
                    o._named[name] = hasValue ? args[++i] : "true";
                }
                else o.Positional.Add(a);
            }
            return o;
        }

        public string Get(string name, string fallback) => _named.TryGetValue(name, out string? v) ? v : fallback;
        public string Require(string name) => _named.TryGetValue(name, out string? v) ? v : throw new InvalidOperationException("--" + name + " is required");
        public bool Flag(string name) => _named.ContainsKey(name);
    }
}

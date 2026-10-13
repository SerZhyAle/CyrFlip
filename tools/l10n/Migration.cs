using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace L10nKeys
{
    /// <summary>A literal outside the tables whose value is a registered key - D4 replaces it.</summary>
    internal sealed class Site
    {
        public SourceFile File = null!;
        public Tok Lit = null!;
        public Entry Entry = null!;
        public bool InCall;        // literal is the (first or second) argument of a translating call
        public string Context = ""; // identity-use heuristic, empty when none
    }

    internal sealed class Edit
    {
        public int Start, End;
        public string Text = "";
        public string Why = "";
    }

    internal sealed class Inventory
    {
        public string Root = "";
        public List<SourceFile> Files = new List<SourceFile>();
        public List<string> Problems = new List<string>();
        public List<Entry> Entries = new List<Entry>();
        public Dictionary<string, Entry> ByOldKey = new Dictionary<string, Entry>(StringComparer.Ordinal);
        public List<string> Unread = new List<string>();
        public List<Site> Sites = new List<Site>();
        public List<string> NonKeyCyrillic = new List<string>();
        public List<IGrouping<string, Entry>> Collisions = new List<IGrouping<string, Entry>>();
        public Dictionary<AddCall, bool> TableArgSpans = new Dictionary<AddCall, bool>();
        public Dictionary<string, List<ConstDecl>> Constants = new Dictionary<string, List<ConstDecl>>();
    }

    internal static class Migration
    {
        private static readonly HashSet<string> TranslatingNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "T", "Translate", "translate", "Localize", "TranslateButton", "Format",
        };

        private static readonly HashSet<string> IdentityCalls = new HashSet<string>(StringComparer.Ordinal)
        {
            "Equals", "Contains", "ContainsKey", "StartsWith", "EndsWith", "IndexOf", "SetValue", "TryGetValue",
            "Compare", "CompareOrdinal", "Remove", "Insert", "WriteLine", "Append",
        };

        public static string[] SourceFiles(string root, string sub)
        {
            string dir = Path.Combine(root, sub);
            return Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                         && !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .OrderBy(p => p, StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// Reads the tables and every literal that reaches them. Touches nothing on disk.
        /// <paramref name="decisions"/> maps an old Russian key to the English key chosen for it (D3).
        /// </summary>
        public static Inventory Scan(string root, string srcDir, IDictionary<string, string> decisions)
        {
            var inv = new Inventory { Root = root };
            foreach (string path in SourceFiles(root, srcDir)) inv.Files.Add(SourceFile.Load(root, path, inv.Problems));
            inv.Constants = TableReader.ReadConstants(inv.Files);

            // 1. the tables
            foreach (SourceFile f in inv.Files.Where(x => System.IO.Path.GetFileName(x.Path).StartsWith("Localization", StringComparison.Ordinal)))
            {
                foreach (AddCall call in TableReader.ReadAddCalls(f))
                {
                    inv.TableArgSpans[call] = true;
                    string why;
                    Arg? key = TableReader.Slot(call, "ru", out why);
                    Arg? en = key == null ? null : TableReader.Slot(call, "en", out why);
                    if (key == null || en == null)
                    {
                        inv.Unread.Add(f.Path + ":" + call.Line + ": " + why);
                        continue;
                    }
                    int unlabelled = call.Args.Count(a => a.Label == null);
                    int missing = TableReader.Params.Count(p => TableReader.Slot(call, p, out _) == null);
                    if (call.Args.Count != TableReader.Params.Length || missing > 0)
                    {
                        inv.Unread.Add(f.Path + ":" + call.Line + ": " + call.Args.Count + " arguments, " + missing + " slots unfilled");
                        continue;
                    }
                    var entry = new Entry { Call = call, KeyArg = key, EnArg = en, PositionalShape = unlabelled == call.Args.Count };
                    if (unlabelled != 1 && unlabelled != call.Args.Count)
                    {
                        inv.Unread.Add(f.Path + ":" + call.Line + ": mixed positional/named shape (" + unlabelled + " unlabelled)");
                        continue;
                    }
                    if (en.Kind != "lit" || en.Lit!.Value.Length == 0)
                    {
                        inv.Unread.Add(f.Path + ":" + call.Line + ": the English text is not a non-empty literal");
                        continue;
                    }
                    entry.En = en.Lit.Value;
                    if (key.Kind == "lit") entry.OldKey = key.Lit!.Value;
                    else if (key.Kind == "name")
                    {
                        string last = key.Name!.Substring(key.Name.LastIndexOf('.') + 1);
                        if (!inv.Constants.TryGetValue(last, out List<ConstDecl>? decls) || decls.Count != 1)
                        {
                            inv.Unread.Add(f.Path + ":" + call.Line + ": the key '" + key.Name + "' is not a unique string constant");
                            continue;
                        }
                        entry.KeyConst = decls[0];
                        entry.OldKey = decls[0].Value;
                    }
                    else
                    {
                        inv.Unread.Add(f.Path + ":" + call.Line + ": the key is neither a literal nor a constant");
                        continue;
                    }
                    inv.Entries.Add(entry);
                }
            }

            foreach (Entry e in inv.Entries)
            {
                if (inv.ByOldKey.ContainsKey(e.OldKey))
                    inv.Unread.Add(e.Call.File.Path + ":" + e.Call.Line + ": the key is registered twice: " + CsLexer.Quote(e.OldKey));
                else inv.ByOldKey[e.OldKey] = e;
            }

            // 2. English texts shared by several keys, and the decisions that separate them
            foreach (Entry e in inv.Entries)
            {
                string chosen;
                if (decisions.TryGetValue(e.OldKey, out chosen!)) { e.NewKey = chosen; e.Variant = chosen != e.En; }
                else { e.NewKey = e.En; e.Variant = false; }
            }
            inv.Collisions = inv.Entries.GroupBy(e => e.NewKey, StringComparer.Ordinal).Where(g => g.Count() > 1).ToList();

            // 3. literals outside the tables
            CollectSites(inv, inv.Files);
            return inv;
        }

        /// <summary>Adds the literals of <paramref name="files"/> whose value is a registered key (or other Cyrillic) to the inventory.</summary>
        public static void CollectSites(Inventory inv, IEnumerable<SourceFile> files)
        {
            var keyed = new HashSet<string>(inv.ByOldKey.Keys, StringComparer.Ordinal);
            foreach (SourceFile f in files)
            {
                var spans = inv.TableArgSpans.Keys.Where(c => c.File == f)
                    .Select(c => new int[] { c.OpenParen, c.CloseParen }).OrderBy(s => s[0]).ToList();
                for (int ti = 0; ti < f.Toks.Count; ti++)
                {
                    Tok t = f.Toks[ti];
                    if (t.Kind != TokKind.Str || t.Interpolated || t.Raw) continue;
                    if (!CsLexer.HasCyrillic(t.Value)) continue;
                    if (spans.Any(s => t.Start > s[0] && t.End <= s[1])) continue;   // a table's own arguments
                    if (keyed.Contains(t.Value))
                    {
                        inv.Sites.Add(new Site
                        {
                            File = f, Lit = t, Entry = inv.ByOldKey[t.Value],
                            InCall = InTranslatingCall(f.Toks, ti), Context = IdentityContext(f.Toks, ti),
                        });
                    }
                    else inv.NonKeyCyrillic.Add(f.Path + ":" + t.Line + ": " + CsLexer.Quote(t.Value));
                }
            }
        }

        /// <summary>The same shape the source gate's regex reads: <c>Name( [ident,] "literal" [,)]</c>.</summary>
        private static bool InTranslatingCall(List<Tok> t, int i)
        {
            if (i + 1 >= t.Count || !(t[i + 1].IsPunct(',') || t[i + 1].IsPunct(')'))) return false;
            int p = i - 1;
            if (p >= 1 && t[p].IsPunct(',') && t[p - 1].Kind == TokKind.Ident)
            {
                int q = p - 2;
                while (q >= 1 && t[q].IsPunct('.') && t[q - 1].Kind == TokKind.Ident) q -= 2;
                p = q;
            }
            else if (p >= 0 && t[p].IsPunct('(')) { /* directly after the parenthesis */ }
            else return false;
            if (p < 1 || !t[p].IsPunct('(')) return false;
            return t[p - 1].Kind == TokKind.Ident && TranslatingNames.Contains(t[p - 1].Value);
        }

        private static string IdentityContext(List<Tok> t, int i)
        {
            Tok prev = i > 0 ? t[i - 1] : null!;
            Tok next = i + 1 < t.Count ? t[i + 1] : null!;
            if (prev != null && prev.IsIdent("case")) return "case label";
            if (prev != null && prev.IsPunct('=') && i > 1 && (t[i - 2].IsPunct('=') || t[i - 2].IsPunct('!'))) return "compared (==/!=)";
            if (next != null && (next.IsPunct('=') || next.IsPunct('!')) && i + 2 < t.Count && t[i + 2].IsPunct('=')) return "compared (==/!=)";
            // the call this literal is an argument of
            int depth = 0;
            for (int k = i - 1; k >= 0 && i - k < 80; k--)
            {
                Tok x = t[k];
                if (x.Kind != TokKind.Punct) continue;
                char c = x.Value[0];
                if (c == ')' || c == ']' || c == '}') depth++;
                else if (c == '(' || c == '[' || c == '{')
                {
                    if (depth > 0) { depth--; continue; }
                    if (c == '(' && k > 0 && t[k - 1].Kind == TokKind.Ident && IdentityCalls.Contains(t[k - 1].Value))
                        return "argument of " + t[k - 1].Value + "(...)";
                    if (c == '{' && next != null && next.IsPunct(',')) return "collection initializer";
                    break;
                }
                else if (c == ';') break;
            }
            return "";
        }

        // ------------------------------------------------------------------ edits

        /// <summary>
        /// Every edit of the migration for one file, as non-overlapping source spans. English becomes the key
        /// (D1): the table entry is rewritten, and each literal outside the tables whose value is a key is
        /// replaced by the key's English text (D4).
        /// </summary>
        public static List<Edit> EditsFor(Inventory inv, SourceFile f, bool shapeOnly)
        {
            var edits = new List<Edit>();
            if (!shapeOnly)
            {
                foreach (Entry e in inv.Entries.Where(x => x.Call.File == f)) edits.AddRange(TableEdits(inv, e));
            }
            foreach (Site s in inv.Sites.Where(x => x.File == f))
            {
                if (shapeOnly && !s.InCall) continue;
                edits.Add(new Edit
                {
                    Start = s.Lit.Start, End = s.Lit.End,
                    Text = NewKeyText(s.Entry), Why = "key literal " + CsLexer.Quote(s.Entry.OldKey),
                });
            }
            return edits;
        }

        /// <summary>The source text of an entry's new key: the English literal as written, or a synthetic one for a variant.</summary>
        public static string NewKeyText(Entry e)
            => e.Variant ? CsLexer.Quote(e.NewKey) : e.Call.File.Slice(e.EnArg.ExprStart, e.EnArg.ExprEnd);

        private static IEnumerable<Edit> TableEdits(Inventory inv, Entry e)
        {
            SourceFile f = e.Call.File;
            // the old key as source text: the literal as written, or the constant's own literal
            string oldKeyText = e.KeyConst != null
                ? e.KeyConst.File.Slice(e.KeyConst.Lit.Start, e.KeyConst.Lit.End)
                : f.Slice(e.KeyArg.ExprStart, e.KeyArg.ExprEnd);

            if (e.KeyArg.Kind == "lit")
                yield return new Edit { Start = e.KeyArg.ExprStart, End = e.KeyArg.ExprEnd, Text = NewKeyText(e), Why = "table key" };
            // a key given as a constant keeps its name; the constant's literal is replaced as an ordinary site

            if (e.PositionalShape)
            {
                if (e.Variant) throw new InvalidOperationException(f.Path + ":" + e.Call.Line + ": a variant key on a positional entry is not supported");
                foreach (Arg a in e.Call.Args.Where(x => x.Index >= 1))
                {
                    // the second argument is the Russian column from now on (it was the English one)
                    string label = a.Index == 1 ? "ru: " : TableReader.Params[a.Index] + ": ";
                    if (a.Index == 1)
                        yield return new Edit { Start = a.ExprStart, End = a.ExprEnd, Text = label + oldKeyText, Why = "ru column" };
                    else
                        yield return new Edit { Start = a.ExprStart, End = a.ExprStart, Text = label, Why = "label" };
                }
                yield break;
            }

            if (e.Variant)
            {
                // keep `en:` as the display override and add the Russian column before it
                yield return new Edit { Start = e.EnArg.LabelStart, End = e.EnArg.LabelStart, Text = "ru: " + oldKeyText + ", ", Why = "ru column" };
            }
            else
            {
                yield return new Edit { Start = e.EnArg.LabelStart, End = e.EnArg.ExprEnd, Text = "ru: " + oldKeyText, Why = "ru column" };
            }
        }

        public static string ApplyEdits(string text, List<Edit> edits)
        {
            var sorted = edits.OrderByDescending(x => x.Start).ThenByDescending(x => x.End).ToList();
            for (int i = 1; i < sorted.Count; i++)
            {
                // sorted descending: sorted[i].End must not run into sorted[i-1].Start
                if (sorted[i].End > sorted[i - 1].Start)
                    throw new InvalidOperationException("overlapping edits at offset " + sorted[i].Start + ": " + sorted[i].Why + " / " + sorted[i - 1].Why);
            }
            var sb = new StringBuilder(text);
            foreach (Edit e in sorted) { sb.Remove(e.Start, e.End - e.Start); sb.Insert(e.Start, e.Text); }
            return sb.ToString();
        }
    }
}

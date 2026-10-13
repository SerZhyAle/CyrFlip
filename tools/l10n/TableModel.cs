using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace L10nKeys
{
    /// <summary>One source file: decoded text, how it was encoded, and its tokens.</summary>
    internal sealed class SourceFile
    {
        public string Path = "";       // repository-relative, forward slashes
        public string FullPath = "";
        public string Text = "";
        public bool HasBom;
        public List<Tok> Toks = new List<Tok>();

        public static SourceFile Load(string root, string fullPath, List<string> problems)
        {
            byte[] bytes = File.ReadAllBytes(fullPath);
            bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)); }
            catch (DecoderFallbackException ex) { throw new InvalidDataException(fullPath + " is not valid UTF-8: " + ex.Message); }
            string rel = fullPath.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
            var f = new SourceFile { Path = rel, FullPath = fullPath, Text = text, HasBom = bom };
            f.Toks = CsLexer.Lex(text, problems, rel);
            return f;
        }

        public void Save(string text)
        {
            var enc = new UTF8Encoding(false, true);
            byte[] body = enc.GetBytes(text);
            byte[] all = body;
            if (HasBom)
            {
                all = new byte[body.Length + 3];
                all[0] = 0xEF; all[1] = 0xBB; all[2] = 0xBF;
                Buffer.BlockCopy(body, 0, all, 3, body.Length);
            }
            File.WriteAllBytes(FullPath, all);
        }

        public string Slice(int start, int end) => Text.Substring(start, end - start);
    }

    internal sealed class Arg
    {
        public string? Label;
        public int LabelStart;          // start of "label" (or of the expression when unlabelled)
        public int ExprStart, ExprEnd;  // the expression text
        public List<Tok> Toks = new List<Tok>();   // expression tokens only
        public string Kind = "complex"; // lit | name | complex
        public Tok? Lit;
        public string? Name;            // dotted identifier chain
        public int Index;               // position in the argument list
    }

    internal sealed class AddCall
    {
        public SourceFile File = null!;
        public int Line;
        public int OpenParen, CloseParen;   // text offsets of "(" and ")"
        public List<Arg> Args = new List<Arg>();
    }

    internal sealed class ConstDecl
    {
        public string Name = "";
        public string Value = "";
        public SourceFile File = null!;
        public Tok Lit = null!;
    }

    /// <summary>A registered string: one <c>Add(...)</c> call, read into its key, its English text and the rest.</summary>
    internal sealed class Entry
    {
        public AddCall Call = null!;
        public Arg KeyArg = null!;
        public Arg EnArg = null!;
        public string OldKey = "";      // the Russian key as the running program sees it
        public ConstDecl? KeyConst;     // set when the key is a constant name
        public string En = "";
        public bool PositionalShape;    // all arguments unlabelled
        public string NewKey = "";      // the English key after the migration
        public bool Variant;            // NewKey differs from En: needs the `en:` display override
    }

    internal static class TableReader
    {
        public static readonly string[] Params = { "ru", "en", "uk", "de", "it", "es", "fr", "pt", "ar", "hi", "bn", "ur", "zh" };

        /// <summary>Every <c>const string NAME = "literal";</c> in the given files, by name.</summary>
        public static Dictionary<string, List<ConstDecl>> ReadConstants(IEnumerable<SourceFile> files)
        {
            var map = new Dictionary<string, List<ConstDecl>>(StringComparer.Ordinal);
            foreach (SourceFile f in files)
            {
                List<Tok> t = f.Toks;
                for (int i = 0; i + 4 < t.Count; i++)
                {
                    if (!t[i].IsIdent("const") || !t[i + 1].IsIdent("string")) continue;
                    if (t[i + 2].Kind != TokKind.Ident || !t[i + 3].IsPunct('=') || t[i + 4].Kind != TokKind.Str) continue;
                    if (t[i + 4].Interpolated || t[i + 4].Raw) continue;
                    if (i + 5 < t.Count && !(t[i + 5].IsPunct(';') || t[i + 5].IsPunct(','))) continue;
                    string name = t[i + 2].Value;
                    if (!map.TryGetValue(name, out List<ConstDecl>? list)) map[name] = list = new List<ConstDecl>();
                    list.Add(new ConstDecl { Name = name, Value = t[i + 4].Value, File = f, Lit = t[i + 4] });
                }
            }
            return map;
        }

        /// <summary>Free calls of <c>Add(...)</c> (not <c>x.Add</c>, not a declaration) with their arguments split.</summary>
        public static List<AddCall> ReadAddCalls(SourceFile f)
        {
            var calls = new List<AddCall>();
            List<Tok> t = f.Toks;
            for (int i = 0; i + 1 < t.Count; i++)
            {
                if (!t[i].IsIdent("Add") || !t[i + 1].IsPunct('(')) continue;
                if (i > 0 && (t[i - 1].IsPunct('.') || t[i - 1].Kind == TokKind.Ident)) continue;

                int depth = 0, close = -1;
                for (int k = i + 1; k < t.Count; k++)
                {
                    Tok x = t[k];
                    if (x.Kind != TokKind.Punct) continue;
                    char c = x.Value[0];
                    if (c == '(' || c == '[' || c == '{') depth++;
                    else if (c == ')' || c == ']' || c == '}') { depth--; if (depth == 0) { close = k; break; } }
                }
                if (close < 0) continue;

                var call = new AddCall { File = f, Line = t[i].Line, OpenParen = t[i + 1].Start, CloseParen = t[close].Start };
                int argStart = i + 2;
                int d = 0;
                for (int k = i + 2; k <= close; k++)
                {
                    Tok x = t[k];
                    bool split = false;
                    if (x.Kind == TokKind.Punct)
                    {
                        char c = x.Value[0];
                        if (c == '(' || c == '[' || c == '{') d++;
                        else if (c == ')' || c == ']' || c == '}') { if (k == close) split = true; else d--; }
                        else if (c == ',' && d == 0) split = true;
                    }
                    if (!split) continue;
                    if (k > argStart) call.Args.Add(MakeArg(t, argStart, k - 1, call.Args.Count));
                    argStart = k + 1;
                }
                calls.Add(call);
            }
            return calls;
        }

        private static Arg MakeArg(List<Tok> t, int from, int to, int index)
        {
            var a = new Arg { Index = index, LabelStart = t[from].Start };
            int e = from;
            if (to - from >= 2 && t[from].Kind == TokKind.Ident && t[from + 1].IsPunct(':') && !t[from + 2].IsPunct(':'))
            {
                a.Label = t[from].Value;
                e = from + 2;
            }
            a.ExprStart = t[e].Start;
            a.ExprEnd = t[to].End;
            for (int k = e; k <= to; k++) a.Toks.Add(t[k]);

            if (a.Toks.Count == 1 && a.Toks[0].Kind == TokKind.Str && !a.Toks[0].Interpolated && !a.Toks[0].Raw)
            {
                a.Kind = "lit"; a.Lit = a.Toks[0];
            }
            else if (IsNameChain(a.Toks))
            {
                a.Kind = "name"; a.Name = string.Concat(a.Toks.Select(x => x.Value));
            }
            return a;
        }

        private static bool IsNameChain(List<Tok> toks)
        {
            if (toks.Count == 0 || toks.Count % 2 == 0) return false;
            for (int i = 0; i < toks.Count; i++)
            {
                if (i % 2 == 0) { if (toks[i].Kind != TokKind.Ident) return false; }
                else if (!toks[i].IsPunct('.')) return false;
            }
            return true;
        }

        /// <summary>The argument that fills a parameter slot, or null with the reason in <paramref name="why"/>.</summary>
        public static Arg? Slot(AddCall call, string param, out string why)
        {
            why = "";
            int position = Array.IndexOf(Params, param);
            Arg? found = null;
            int hits = 0;
            foreach (Arg a in call.Args)
            {
                bool match = a.Label != null ? a.Label == param : a.Index == position;
                if (match) { found = a; hits++; }
            }
            if (hits != 1) { why = "slot '" + param + "' filled " + hits + " times"; return null; }
            return found;
        }
    }
}

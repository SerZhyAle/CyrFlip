using System;
using System.Collections.Generic;
using System.Text;

namespace L10nKeys
{
    internal enum TokKind { Ident, Str, Char, Number, Punct }

    /// <summary>One lexical token of a C# source file. Offsets index the file's decoded text.</summary>
    internal sealed class Tok
    {
        public TokKind Kind;
        public int Start;
        public int End;          // exclusive
        public string Value = ""; // Str: the unescaped value; Ident/Punct/Number: the text
        public bool Verbatim;
        public bool Interpolated; // Str only; Value is empty, the token is only a span
        public bool Raw;          // a C# 11 raw literal - never read, reported
        public int Line;          // 1-based

        public bool IsPunct(char c) => Kind == TokKind.Punct && Value.Length == 1 && Value[0] == c;
        public bool IsIdent(string name) => Kind == TokKind.Ident && Value == name;
    }

    /// <summary>
    /// A small, forgiving C# lexer: it finds string literals (with their unescaped values) and the
    /// punctuation around them, and skips comments, preprocessor lines and character literals so that
    /// a quote inside one of those never derails the scan. It is not a parser.
    /// </summary>
    internal static class CsLexer
    {
        public static List<Tok> Lex(string s, List<string> problems, string label)
        {
            var toks = new List<Tok>(s.Length / 6);
            int n = s.Length, i = 0, line = 1;
            bool lineStart = true;
            while (i < n)
            {
                char c = s[i];
                if (c == '\n') { line++; i++; lineStart = true; continue; }
                if (c == ' ' || c == '\t' || c == '\r' || c == '﻿') { i++; continue; }

                if (c == '#' && lineStart)
                {
                    while (i < n && s[i] != '\n') i++;
                    continue;
                }
                lineStart = false;

                if (c == '/' && i + 1 < n && s[i + 1] == '/')
                {
                    while (i < n && s[i] != '\n') i++;
                    continue;
                }
                if (c == '/' && i + 1 < n && s[i + 1] == '*')
                {
                    int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    int stop = end < 0 ? n : end + 2;
                    for (int k = i; k < stop; k++) if (s[k] == '\n') line++;
                    i = stop;
                    continue;
                }

                // strings: "..", @"..", $"..", $@"..", @$".."
                if (c == '"' || ((c == '@' || c == '$') && StringPrefixLength(s, i) > 0))
                {
                    int start = i, startLine = line;
                    int p = i;
                    bool verbatim = false, interp = false;
                    while (s[p] == '@' || s[p] == '$')
                    {
                        if (s[p] == '@') verbatim = true; else interp = true;
                        p++;
                    }
                    // p is at the opening quote
                    if (p + 2 < n && s[p + 1] == '"' && s[p + 2] == '"')
                    {
                        // C# 11 raw literal: find the closing run of quotes, report, do not read
                        int q = p; while (q < n && s[q] == '"') q++;
                        int runLen = q - p;
                        string close = new string('"', runLen);
                        int end = s.IndexOf(close, q, StringComparison.Ordinal);
                        int stop = end < 0 ? n : end + runLen;
                        for (int k = i; k < stop; k++) if (s[k] == '\n') line++;
                        toks.Add(new Tok { Kind = TokKind.Str, Start = start, End = stop, Raw = true, Interpolated = interp, Line = startLine });
                        problems.Add(label + ":" + startLine + ": raw string literal is not read");
                        i = stop;
                        continue;
                    }

                    int j = p + 1;
                    var sb = interp ? null : new StringBuilder();
                    bool closed = false;
                    int depth = 0;
                    while (j < n)
                    {
                        char d = s[j];
                        if (d == '\n') line++;
                        if (interp && depth > 0)
                        {
                            // inside an interpolation hole: code, with nested strings and braces
                            if (d == '{') depth++;
                            else if (d == '}') depth--;
                            else if (d == '"') j = SkipSimpleString(s, j, ref line);
                            else if (d == '\'') j = SkipChar(s, j);
                            j++;
                            continue;
                        }
                        if (d == '"')
                        {
                            if (verbatim && j + 1 < n && s[j + 1] == '"') { if (sb != null) sb.Append('"'); j += 2; continue; }
                            closed = true; j++; break;
                        }
                        if (interp && d == '{')
                        {
                            if (j + 1 < n && s[j + 1] == '{') { j += 2; continue; }
                            depth = 1; j++; continue;
                        }
                        if (interp && d == '}' && j + 1 < n && s[j + 1] == '}') { j += 2; continue; }
                        if (!verbatim && d == '\\')
                        {
                            if (sb != null) j = Unescape(s, j, sb); else j += 2;
                            continue;
                        }
                        if (sb != null) sb.Append(d);
                        j++;
                    }
                    if (!closed) problems.Add(label + ":" + startLine + ": unterminated string literal");
                    toks.Add(new Tok
                    {
                        Kind = TokKind.Str, Start = start, End = j, Value = sb == null ? "" : sb.ToString(),
                        Verbatim = verbatim, Interpolated = interp, Line = startLine,
                    });
                    i = j;
                    continue;
                }

                if (c == '\'')
                {
                    int j = SkipChar(s, i);
                    toks.Add(new Tok { Kind = TokKind.Char, Start = i, End = j + 1, Value = s.Substring(i, Math.Min(j + 1, n) - i), Line = line });
                    i = j + 1;
                    continue;
                }

                if (char.IsLetter(c) || c == '_' || c == '@')
                {
                    int j = i + 1;
                    while (j < n && (char.IsLetterOrDigit(s[j]) || s[j] == '_')) j++;
                    toks.Add(new Tok { Kind = TokKind.Ident, Start = i, End = j, Value = s.Substring(i, j - i), Line = line });
                    i = j;
                    continue;
                }

                if (char.IsDigit(c))
                {
                    int j = i + 1;
                    while (j < n && (char.IsLetterOrDigit(s[j]) || s[j] == '_' || s[j] == '.')) j++;
                    toks.Add(new Tok { Kind = TokKind.Number, Start = i, End = j, Value = s.Substring(i, j - i), Line = line });
                    i = j;
                    continue;
                }

                toks.Add(new Tok { Kind = TokKind.Punct, Start = i, End = i + 1, Value = c.ToString(), Line = line });
                i++;
            }
            return toks;
        }

        /// <summary>Length of a literal prefix ($, @, $@, @$) that is directly followed by a quote, else 0.</summary>
        private static int StringPrefixLength(string s, int i)
        {
            int p = i;
            while (p < s.Length && (s[p] == '@' || s[p] == '$') && p - i < 2) p++;
            return p < s.Length && s[p] == '"' ? p - i : 0;
        }

        private static int SkipSimpleString(string s, int i, ref int line)
        {
            int j = i + 1;
            while (j < s.Length)
            {
                if (s[j] == '\n') line++;
                if (s[j] == '\\') { j += 2; continue; }
                if (s[j] == '"') return j;
                j++;
            }
            return s.Length - 1;
        }

        /// <summary>Index of the closing quote of the character literal that opens at <paramref name="i"/>.</summary>
        private static int SkipChar(string s, int i)
        {
            int j = i + 1;
            while (j < s.Length)
            {
                if (s[j] == '\\') { j += 2; continue; }
                if (s[j] == '\'' || s[j] == '\n') return j;
                j++;
            }
            return s.Length - 1;
        }

        /// <summary>Reads one escape sequence at <paramref name="j"/> (the backslash); returns the index after it.</summary>
        private static int Unescape(string s, int j, StringBuilder sb)
        {
            if (j + 1 >= s.Length) { sb.Append('\\'); return j + 1; }
            char e = s[j + 1];
            switch (e)
            {
                case 'n': sb.Append('\n'); return j + 2;
                case 'r': sb.Append('\r'); return j + 2;
                case 't': sb.Append('\t'); return j + 2;
                case '0': sb.Append('\0'); return j + 2;
                case 'a': sb.Append('\a'); return j + 2;
                case 'b': sb.Append('\b'); return j + 2;
                case 'f': sb.Append('\f'); return j + 2;
                case 'v': sb.Append('\v'); return j + 2;
                case '\\': sb.Append('\\'); return j + 2;
                case '"': sb.Append('"'); return j + 2;
                case '\'': sb.Append('\''); return j + 2;
                case 'u':
                    if (TryHex(s, j + 2, 4, out int u)) { sb.Append((char)u); return j + 6; }
                    break;
                case 'U':
                    if (TryHex(s, j + 2, 8, out int big)) { sb.Append(char.ConvertFromUtf32(big)); return j + 10; }
                    break;
                case 'x':
                {
                    int k = j + 2, v = 0, digits = 0;
                    while (k < s.Length && digits < 4 && Uri.IsHexDigit(s[k])) { v = v * 16 + Convert.ToInt32(s[k].ToString(), 16); k++; digits++; }
                    if (digits > 0) { sb.Append((char)v); return k; }
                    break;
                }
            }
            sb.Append('\\').Append(e);
            return j + 2;
        }

        private static bool TryHex(string s, int at, int count, out int value)
        {
            value = 0;
            if (at + count > s.Length) return false;
            for (int k = 0; k < count; k++)
            {
                if (!Uri.IsHexDigit(s[at + k])) return false;
                value = value * 16 + Convert.ToInt32(s[at + k].ToString(), 16);
            }
            return true;
        }

        public static bool HasCyrillic(string value)
        {
            foreach (char c in value) if (c >= 'Ѐ' && c <= 'ӿ') return true;
            return false;
        }

        /// <summary>A regular C# literal for <paramref name="value"/> (the escape form used in reports).</summary>
        public static string Quote(string value)
        {
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}

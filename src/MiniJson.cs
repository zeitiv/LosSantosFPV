using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LosSantosFpv
{
    /// <summary>
    /// Minimal JSON for the settings file: objects (ordered), arrays, strings, numbers, true/false/null.
    /// Forgiving on input (// and /* */ comments, trailing commas) so hand-edited files still load.
    /// </summary>
    public static class MiniJson
    {
        public class Obj : List<KeyValuePair<string, object>>
        {
            public object this[string key]
            {
                get { foreach (var kv in this) if (kv.Key == key) return kv.Value; return null; }
                set
                {
                    for (int i = 0; i < Count; i++) if (this[i].Key == key) { this[i] = new KeyValuePair<string, object>(key, value); return; }
                    Add(new KeyValuePair<string, object>(key, value));
                }
            }
        }

        // ---------------- parse ----------------

        public static object Parse(string s)
        {
            int i = 0;
            var v = Value(s, ref i);
            Skip(s, ref i);
            if (i < s.Length) throw Error(s, i, "unexpected text after the end");
            return v;
        }

        static Exception Error(string s, int i, string what)
        {
            int line = 1, col = 1;
            for (int k = 0; k < i && k < s.Length; k++) { if (s[k] == '\n') { line++; col = 1; } else col++; }
            return new FormatException($"JSON error at line {line}, column {col}: {what}");
        }

        static void Skip(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '*') { int e = s.IndexOf("*/", i + 2, StringComparison.Ordinal); i = e < 0 ? s.Length : e + 2; continue; }
                break;
            }
        }

        static object Value(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) throw Error(s, i, "unexpected end of file");
            char c = s[i];
            if (c == '{') return Object(s, ref i);
            if (c == '[') return Array(s, ref i);
            if (c == '"') return Str(s, ref i);
            if (Word(s, ref i, "true")) return true;
            if (Word(s, ref i, "false")) return false;
            if (Word(s, ref i, "null")) return null;
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (st == i) throw Error(s, i, $"unexpected '{c}' (strings need \"double quotes\")");
            return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static bool Word(string s, ref int i, string w)
        {
            if (string.CompareOrdinal(s, i, w, 0, w.Length) != 0) return false;
            i += w.Length;
            return true;
        }

        static Obj Object(string s, ref int i)
        {
            var o = new Obj();
            i++;
            while (true)
            {
                Skip(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return o; }
                if (i >= s.Length || s[i] != '"') throw Error(s, i, "expected a \"key\"");
                string k = Str(s, ref i);
                Skip(s, ref i);
                if (i >= s.Length || s[i] != ':') throw Error(s, i, "expected ':'");
                i++;
                o[k] = Value(s, ref i);
                Skip(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return o; }
                throw Error(s, i, "expected ',' or '}' (missing comma?)");
            }
        }

        static List<object> Array(string s, ref int i)
        {
            var a = new List<object>();
            i++;
            while (true)
            {
                Skip(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return a; }
                a.Add(Value(s, ref i));
                Skip(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return a; }
                throw Error(s, i, "expected ',' or ']'");
            }
        }

        static string Str(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber)); i += 4; break;
                    default: sb.Append(e); break;   // \" \\ \/ and lenient: a lone backslash in a Windows path
                }
            }
            throw Error(s, i, "unterminated string");
        }

        // ---------------- write ----------------

        public static string Write(object v)
        {
            var sb = new StringBuilder();
            W(sb, v, 0);
            return sb.Append('\n').ToString();
        }

        static void W(StringBuilder sb, object v, int ind)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case string s: Q(sb, s); break;
                case Obj o:
                    if (o.Count == 0) { sb.Append("{}"); break; }
                    sb.Append("{\n");
                    for (int k = 0; k < o.Count; k++)
                    {
                        sb.Append(' ', ind + 2);
                        Q(sb, o[k].Key);
                        sb.Append(": ");
                        W(sb, o[k].Value, ind + 2);
                        sb.Append(k < o.Count - 1 ? ",\n" : "\n");
                    }
                    sb.Append(' ', ind).Append('}');
                    break;
                case System.Collections.IList a:
                    sb.Append('[');
                    for (int k = 0; k < a.Count; k++) { if (k > 0) sb.Append(", "); W(sb, a[k], ind); }
                    sb.Append(']');
                    break;
                case IFormattable f: sb.Append(f.ToString(null, CultureInfo.InvariantCulture)); break;
                default: Q(sb, v.ToString()); break;
            }
        }

        static void Q(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c); break;
                }
            }
            sb.Append('"');
        }
    }
}

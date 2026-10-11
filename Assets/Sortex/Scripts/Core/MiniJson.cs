// Minimal JSON parser/writer used by both the Unity game and the offline simulator.
// Parses into Dictionary<string, object>, List<object>, double, string, bool and null.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sortex.Core
{
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            if (json == null) return null;
            var p = new Parser(json);
            p.SkipWs();
            object v = p.ReadValue();
            p.SkipWs();
            return v;
        }

        sealed class Parser
        {
            readonly string s;
            int i;
            public Parser(string text) { s = text; i = 0; }

            public void SkipWs()
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            }

            public object ReadValue()
            {
                SkipWs();
                if (i >= s.Length) throw new FormatException("Unexpected end of JSON");
                char c = s[i];
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == '"') return ReadString();
                if (c == 't' && Match("true")) return true;
                if (c == 'f' && Match("false")) return false;
                if (c == 'n' && Match("null")) return null;
                return ReadNumber();
            }

            bool Match(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) == 0)
                {
                    i += word.Length;
                    return true;
                }
                throw new FormatException("Unexpected token at " + i);
            }

            Dictionary<string, object> ReadObject()
            {
                var d = new Dictionary<string, object>();
                i++; // {
                SkipWs();
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    SkipWs();
                    string key = ReadString();
                    SkipWs();
                    if (s[i] != ':') throw new FormatException("Expected ':' at " + i);
                    i++;
                    d[key] = ReadValue();
                    SkipWs();
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("Expected ',' or '}' at " + i);
                }
            }

            List<object> ReadArray()
            {
                var l = new List<object>();
                i++; // [
                SkipWs();
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(ReadValue());
                    SkipWs();
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("Expected ',' or ']' at " + i);
                }
            }

            string ReadString()
            {
                if (s[i] != '"') throw new FormatException("Expected string at " + i);
                i++;
                var sb = new StringBuilder();
                while (i < s.Length)
                {
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\')
                    {
                        char e = s[i++];
                        switch (e)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                                i += 4;
                                break;
                            default: sb.Append(e); break;
                        }
                    }
                    else sb.Append(c);
                }
                throw new FormatException("Unterminated string");
            }

            object ReadNumber()
            {
                int start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                string num = s.Substring(start, i - start);
                return double.Parse(num, NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }

        // ---------- writer ----------
        /// <summary>Serialises Dictionary / List / string / bool / numbers / null to JSON.</summary>
        public static string Serialize(object value, bool pretty = false)
        {
            var sb = new StringBuilder();
            Write(sb, value, pretty, 0);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object v, bool pretty, int depth)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is string) { WriteString(sb, (string)v); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is double || v is float || v is int || v is long || v is decimal)
            {
                sb.Append(Convert.ToDouble(v, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            var dict = v as IDictionary<string, object>;
            if (dict != null)
            {
                // short objects (slots, positions) stay on one line so level files remain readable
                bool inline = !pretty || dict.Count <= 3 && !HasNested(dict.Values);
                sb.Append('{');
                bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    if (!inline) NewLine(sb, depth + 1);
                    WriteString(sb, kv.Key);
                    sb.Append(':');
                    Write(sb, kv.Value, pretty, depth + 1);
                }
                if (!inline && dict.Count > 0) NewLine(sb, depth);
                sb.Append('}');
                return;
            }
            var list = v as System.Collections.IList;
            if (list != null)
            {
                bool inline = !pretty || !HasNested(list);
                sb.Append('[');
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    if (!inline) NewLine(sb, depth + 1);
                    Write(sb, list[i], pretty, depth + 1);
                }
                if (!inline && list.Count > 0) NewLine(sb, depth);
                sb.Append(']');
                return;
            }
            WriteString(sb, v.ToString());
        }

        static bool HasNested(System.Collections.IEnumerable values)
        {
            foreach (var x in values)
            {
                var d = x as IDictionary<string, object>;
                if (d != null && (d.Count > 3 || HasNested(d.Values))) return true;
                if (x is System.Collections.IList && !(x is string))
                {
                    foreach (var y in (System.Collections.IList)x)
                        if (y is IDictionary<string, object> || y is System.Collections.IList) return true;
                }
            }
            return false;
        }

        static void NewLine(StringBuilder sb, int depth)
        {
            sb.Append('\n');
            sb.Append(' ', depth);
        }

        static void WriteString(StringBuilder sb, string s)
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
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---------- typed accessors ----------
        public static Dictionary<string, object> Obj(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            if (d == null) return null;
            object v;
            return d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        public static List<object> Arr(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            if (d == null) return null;
            object v;
            return d.TryGetValue(key, out v) ? v as List<object> : null;
        }

        public static double Num(object o, string key, double fallback = 0)
        {
            var d = o as Dictionary<string, object>;
            if (d == null) return fallback;
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return fallback;
            if (v is double) return (double)v;
            if (v is bool) return (bool)v ? 1 : 0;
            return fallback;
        }

        public static int Int(object o, string key, int fallback = 0)
        {
            return (int)Math.Round(Num(o, key, fallback));
        }

        public static bool Bool(object o, string key, bool fallback = false)
        {
            var d = o as Dictionary<string, object>;
            if (d == null) return fallback;
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return fallback;
            if (v is bool) return (bool)v;
            if (v is double) return (double)v != 0;
            return fallback;
        }

        public static string Str(object o, string key, string fallback = null)
        {
            var d = o as Dictionary<string, object>;
            if (d == null) return fallback;
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return fallback;
            return v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture);
        }
    }
}

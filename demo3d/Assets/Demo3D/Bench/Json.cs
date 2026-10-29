using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Demo3D.Bench
{
    /// <summary>
    /// A small JSON reader for sweep specs: objects become <see cref="Dictionary{TKey,TValue}"/>,
    /// arrays <see cref="List{T}"/>, numbers <see cref="double"/>.
    /// </summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            var p = new Parser(text);
            p.Ws();
            var v = p.Value();
            p.Ws();
            if (p.I != text.Length) throw p.Error("trailing characters");
            return v;
        }

        sealed class Parser
        {
            readonly string _s;
            public int I;

            public Parser(string s) { _s = s; }

            public FormatException Error(string what) => new FormatException($"JSON: {what} at offset {I}");

            public void Ws()
            {
                while (I < _s.Length && char.IsWhiteSpace(_s[I])) I++;
            }

            public object Value()
            {
                if (I >= _s.Length) throw Error("unexpected end");
                char c = _s[I];
                if (c == '{') return Obj();
                if (c == '[') return Arr();
                if (c == '"') return Str();
                if (Lit("true")) return true;
                if (Lit("false")) return false;
                if (Lit("null")) return null;
                return Num();
            }

            bool Lit(string word)
            {
                if (string.CompareOrdinal(_s, I, word, 0, word.Length) != 0) return false;
                I += word.Length;
                return true;
            }

            Dictionary<string, object> Obj()
            {
                var d = new Dictionary<string, object>();
                I++;
                Ws();
                if (I < _s.Length && _s[I] == '}') { I++; return d; }
                while (true)
                {
                    Ws();
                    if (I >= _s.Length || _s[I] != '"') throw Error("expected a key");
                    var k = Str();
                    Ws();
                    if (I >= _s.Length || _s[I] != ':') throw Error("expected ':'");
                    I++;
                    Ws();
                    d[k] = Value();
                    Ws();
                    if (I < _s.Length && _s[I] == ',') { I++; continue; }
                    if (I < _s.Length && _s[I] == '}') { I++; return d; }
                    throw Error("expected ',' or '}'");
                }
            }

            List<object> Arr()
            {
                var l = new List<object>();
                I++;
                Ws();
                if (I < _s.Length && _s[I] == ']') { I++; return l; }
                while (true)
                {
                    Ws();
                    l.Add(Value());
                    Ws();
                    if (I < _s.Length && _s[I] == ',') { I++; continue; }
                    if (I < _s.Length && _s[I] == ']') { I++; return l; }
                    throw Error("expected ',' or ']'");
                }
            }

            string Str()
            {
                var sb = new StringBuilder();
                I++;
                while (I < _s.Length)
                {
                    char c = _s[I++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (I >= _s.Length) break;
                    char e = _s[I++];
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
                            if (I + 4 > _s.Length) throw Error("bad \\u escape");
                            sb.Append((char)int.Parse(_s.Substring(I, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            I += 4;
                            break;
                        default: throw Error("bad escape");
                    }
                }
                throw Error("unterminated string");
            }

            double Num()
            {
                int start = I;
                while (I < _s.Length && "+-0123456789.eE".IndexOf(_s[I]) >= 0) I++;
                if (I == start) throw Error("unexpected character");
                return double.Parse(_s.Substring(start, I - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }

        public static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
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
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }

    /// <summary>Writes one JSON object on one line, fields in insertion order, numbers invariant.</summary>
    public sealed class JsonObject
    {
        readonly StringBuilder _sb = new StringBuilder("{");
        bool _first = true;

        JsonObject Key(string k)
        {
            if (!_first) _sb.Append(", ");
            _first = false;
            _sb.Append(Json.Escape(k)).Append(": ");
            return this;
        }

        public JsonObject Add(string k, string v)
        {
            Key(k)._sb.Append(v == null ? "null" : Json.Escape(v));
            return this;
        }

        public JsonObject Add(string k, double? v, int decimals = 3)
        {
            Key(k);
            if (v == null || double.IsNaN(v.Value) || double.IsInfinity(v.Value)) _sb.Append("null");
            else _sb.Append(Math.Round(v.Value, decimals).ToString("0.######", CultureInfo.InvariantCulture));
            return this;
        }

        public JsonObject Add(string k, long? v)
        {
            Key(k)._sb.Append(v == null ? "null" : v.Value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public JsonObject Add(string k, int? v) => Add(k, v == null ? (long?)null : v.Value);

        public JsonObject Add(string k, bool? v)
        {
            Key(k)._sb.Append(v == null ? "null" : v.Value ? "true" : "false");
            return this;
        }

        public JsonObject Add(string k, IEnumerable<string> v)
        {
            Key(k);
            if (v == null) { _sb.Append("null"); return this; }
            _sb.Append('[');
            bool first = true;
            foreach (var s in v)
            {
                if (!first) _sb.Append(", ");
                first = false;
                _sb.Append(Json.Escape(s));
            }
            _sb.Append(']');
            return this;
        }

        /// <summary>Adds a value that is already JSON (an object or array).</summary>
        public JsonObject AddRaw(string k, string json)
        {
            Key(k)._sb.Append(json ?? "null");
            return this;
        }

        public override string ToString() => _sb.ToString() + "}";
    }
}

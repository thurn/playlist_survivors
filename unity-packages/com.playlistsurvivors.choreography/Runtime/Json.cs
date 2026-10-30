using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PS.Choreography
{
    public enum JsonType { Null, Bool, Number, String, Array, Object }

    /// <summary>
    /// A parsed JSON value. Unity has no System.Text.Json, so the layer carries this small reader
    /// (and <see cref="JsonOut"/> for writing). Objects keep their keys in file order.
    /// </summary>
    public sealed class JsonValue
    {
        public static readonly JsonValue Null = new JsonValue(JsonType.Null);

        public readonly JsonType Type;
        public readonly bool Bool;
        public readonly double Number;
        public readonly string String;
        public readonly List<JsonValue> Items;
        public readonly List<KeyValuePair<string, JsonValue>> Members;

        JsonValue(JsonType type) { Type = type; }
        JsonValue(bool b) { Type = JsonType.Bool; Bool = b; }
        JsonValue(double n) { Type = JsonType.Number; Number = n; }
        JsonValue(string s) { Type = JsonType.String; String = s; }
        JsonValue(List<JsonValue> items) { Type = JsonType.Array; Items = items; }
        JsonValue(List<KeyValuePair<string, JsonValue>> members) { Type = JsonType.Object; Members = members; }

        internal static JsonValue OfNumber(double n) => new JsonValue(n);
        internal static JsonValue OfMembers(List<KeyValuePair<string, JsonValue>> members) => new JsonValue(members);
        internal static JsonValue OfItems(List<JsonValue> items) => new JsonValue(items);

        public bool IsNull => Type == JsonType.Null;
        public int Count => Type == JsonType.Array ? Items.Count : Type == JsonType.Object ? Members.Count : 0;
        public JsonValue this[int i] => Items[i];

        /// <summary>The member's value, or <see cref="Null"/> if absent (or this is not an object).</summary>
        public JsonValue this[string key]
        {
            get
            {
                if (Type != JsonType.Object) return Null;
                for (int i = 0; i < Members.Count; i++)
                    if (Members[i].Key == key) return Members[i].Value;
                return Null;
            }
        }

        public bool Has(string key) => !this[key].IsNull;

        public double AsDouble(double fallback = 0) => Type == JsonType.Number ? Number : fallback;
        public float AsFloat(float fallback = 0) => Type == JsonType.Number ? (float)Number : fallback;
        public int AsInt(int fallback = 0) => Type == JsonType.Number ? (int)Math.Round(Number) : fallback;
        public bool AsBool(bool fallback = false) => Type == JsonType.Bool ? Bool : fallback;
        public string AsString(string fallback = null) => Type == JsonType.String ? String : fallback;

        public static JsonValue Parse(string text)
        {
            var p = new Parser(text);
            p.SkipWs();
            JsonValue v = p.ReadValue();
            p.SkipWs();
            if (p.Pos != text.Length) throw p.Error("trailing characters");
            return v;
        }

        sealed class Parser
        {
            readonly string s;
            public int Pos;
            public Parser(string text) { s = text ?? throw new ArgumentNullException(nameof(text)); }

            public FormatException Error(string what)
            {
                int line = 1;
                for (int i = 0; i < Pos && i < s.Length; i++) if (s[i] == '\n') line++;
                return new FormatException("JSON: " + what + " at line " + line);
            }

            public void SkipWs()
            {
                while (Pos < s.Length)
                {
                    char c = s[Pos];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '﻿') Pos++;
                    else break;
                }
            }

            public JsonValue ReadValue()
            {
                if (Pos >= s.Length) throw Error("unexpected end");
                char c = s[Pos];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return new JsonValue(ReadString());
                    case 't': Expect("true"); return new JsonValue(true);
                    case 'f': Expect("false"); return new JsonValue(false);
                    case 'n': Expect("null"); return Null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return new JsonValue(ReadNumber());
                        throw Error("unexpected '" + c + "'");
                }
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(s, Pos, word, 0, word.Length) != 0) throw Error("expected " + word);
                Pos += word.Length;
            }

            JsonValue ReadObject()
            {
                Pos++;
                var members = new List<KeyValuePair<string, JsonValue>>();
                SkipWs();
                if (Pos < s.Length && s[Pos] == '}') { Pos++; return new JsonValue(members); }
                while (true)
                {
                    SkipWs();
                    if (Pos >= s.Length || s[Pos] != '"') throw Error("expected a key");
                    string key = ReadString();
                    SkipWs();
                    if (Pos >= s.Length || s[Pos] != ':') throw Error("expected ':'");
                    Pos++;
                    SkipWs();
                    members.Add(new KeyValuePair<string, JsonValue>(key, ReadValue()));
                    SkipWs();
                    if (Pos >= s.Length) throw Error("unexpected end");
                    if (s[Pos] == ',') { Pos++; continue; }
                    if (s[Pos] == '}') { Pos++; return new JsonValue(members); }
                    throw Error("expected ',' or '}'");
                }
            }

            JsonValue ReadArray()
            {
                Pos++;
                var items = new List<JsonValue>();
                SkipWs();
                if (Pos < s.Length && s[Pos] == ']') { Pos++; return new JsonValue(items); }
                while (true)
                {
                    SkipWs();
                    items.Add(ReadValue());
                    SkipWs();
                    if (Pos >= s.Length) throw Error("unexpected end");
                    if (s[Pos] == ',') { Pos++; continue; }
                    if (s[Pos] == ']') { Pos++; return new JsonValue(items); }
                    throw Error("expected ',' or ']'");
                }
            }

            string ReadString()
            {
                Pos++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (Pos >= s.Length) throw Error("unterminated string");
                    char c = s[Pos++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (Pos >= s.Length) throw Error("bad escape");
                    char e = s[Pos++];
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
                            if (Pos + 4 > s.Length) throw Error("bad \\u escape");
                            sb.Append((char)int.Parse(s.Substring(Pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            Pos += 4;
                            break;
                        default: throw Error("bad escape");
                    }
                }
            }

            double ReadNumber()
            {
                int start = Pos;
                if (s[Pos] == '-') Pos++;
                while (Pos < s.Length)
                {
                    char c = s[Pos];
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') Pos++;
                    else break;
                }
                if (!double.TryParse(s.Substring(start, Pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                    throw Error("bad number");
                return v;
            }
        }
    }

    /// <summary>
    /// A small streaming JSON writer with fixed, culture-invariant number formats, so the same
    /// values always give the same bytes on one runtime.
    /// </summary>
    public sealed class JsonOut
    {
        readonly TextWriter w;
        bool needComma;

        public JsonOut(TextWriter writer) { w = writer; }

        public TextWriter Writer => w;

        void Sep()
        {
            if (needComma) w.Write(',');
            needComma = false;
        }

        public JsonOut BeginObject() { Sep(); w.Write('{'); return this; }
        public JsonOut EndObject() { w.Write('}'); needComma = true; return this; }
        public JsonOut BeginArray() { Sep(); w.Write('['); return this; }
        public JsonOut EndArray() { w.Write(']'); needComma = true; return this; }
        public JsonOut Newline() { w.Write('\n'); return this; }

        public JsonOut Key(string key)
        {
            Sep();
            WriteString(key);
            w.Write(':');
            return this;
        }

        public JsonOut Value(string s) { Sep(); if (s == null) w.Write("null"); else WriteString(s); needComma = true; return this; }
        public JsonOut Value(bool b) { Sep(); w.Write(b ? "true" : "false"); needComma = true; return this; }
        public JsonOut Value(int i) { Sep(); w.Write(i.ToString(CultureInfo.InvariantCulture)); needComma = true; return this; }
        public JsonOut Value(long i) { Sep(); w.Write(i.ToString(CultureInfo.InvariantCulture)); needComma = true; return this; }
        public JsonOut Value(uint i) { Sep(); w.Write(i.ToString(CultureInfo.InvariantCulture)); needComma = true; return this; }

        /// <summary>A number with at most <paramref name="decimals"/> decimals (trailing zeros dropped).</summary>
        public JsonOut Value(double d, int decimals)
        {
            Sep();
            w.Write(Format(d, decimals));
            needComma = true;
            return this;
        }

        public JsonOut Field(string key, string v) => Key(key).Value(v);
        public JsonOut Field(string key, bool v) => Key(key).Value(v);
        public JsonOut Field(string key, int v) => Key(key).Value(v);
        public JsonOut Field(string key, uint v) => Key(key).Value(v);
        public JsonOut Field(string key, double v, int decimals) => Key(key).Value(v, decimals);

        static readonly string[] Formats = { "0", "0.#", "0.##", "0.###", "0.####", "0.#####", "0.######", "0.#######", "0.########", "0.#########" };

        public static string Format(double d, int decimals)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "null";
            string s = d.ToString(Formats[Math.Max(0, Math.Min(decimals, Formats.Length - 1))], CultureInfo.InvariantCulture);
            return s == "-0" ? "0" : s;
        }

        void WriteString(string s)
        {
            w.Write('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': w.Write("\\\""); break;
                    case '\\': w.Write("\\\\"); break;
                    case '\n': w.Write("\\n"); break;
                    case '\r': w.Write("\\r"); break;
                    case '\t': w.Write("\\t"); break;
                    default:
                        if (c < 0x20) w.Write("\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else w.Write(c);
                        break;
                }
            }
            w.Write('"');
        }
    }
}

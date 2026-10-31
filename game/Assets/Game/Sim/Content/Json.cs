using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PS.Game.Sim.Content
{
    public enum JsonType { Null, Bool, Number, String, Array, Object }

    /// <summary>
    /// A parsed JSON value. Unity has no System.Text.Json, so the sim carries this small reader,
    /// copied from the choreography layer's (unity-packages/.../Json.cs) and owned here. Objects
    /// keep their members in file order, duplicates included, so the content reader can refuse them.
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

        public bool IsNull => Type == JsonType.Null;

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
}

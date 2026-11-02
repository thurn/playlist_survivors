using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Champions.Sim
{
    public enum JsonType { Null, Bool, Number, String, Array, Object }

    /// <summary>
    /// A parsed JSON value. Unity has no System.Text.Json, so the sim carries this small reader, ported
    /// from game/ and owned here. Objects keep their members in file order, duplicates included, so the
    /// content reader can refuse them.
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
                if (!ExactDouble.TryParse(s, start, Pos - start, out double v)) throw Error("bad number");
                return v;
            }
        }
    }

    /// <summary>
    /// Decimal text to the nearest double, ties to even, the same in every runtime: Mono's double.Parse is off by an ulp
    /// for some 17-digit numbers that .NET reads exactly, so a session's entries replayed in the player moved a beat
    /// (ps-6y2.21). Short numbers take Clinger's fast path (an exact mantissa times an exact power of ten, one rounding);
    /// the rest are rounded exactly in big integers.
    /// </summary>
    public static class ExactDouble
    {
        static readonly double[] Pow10 =
        {
            1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22,
        };

        /// <summary>The number in <paramref name="s"/>[start, start + length): a JSON number (sign, digits, fraction, exponent).</summary>
        public static bool TryParse(string s, int start, int length, out double value)
        {
            value = 0;
            int i = start, end = start + length;
            bool negative = i < end && s[i] == '-';
            if (negative) i++;
            var mantissa = System.Numerics.BigInteger.Zero;
            ulong small = 0;
            int digits = 0, exponent = 0;
            bool any = false, dot = false;
            for (; i < end; i++)
            {
                char c = s[i];
                if (c >= '0' && c <= '9')
                {
                    any = true;
                    if (digits < 19) small = small * 10 + (ulong)(c - '0');
                    else
                    {
                        if (digits == 19) mantissa = small;
                        mantissa = mantissa * 10 + (c - '0');
                    }
                    if (small != 0 || digits >= 19) digits++;
                    if (dot) exponent--;
                }
                else if (c == '.' && !dot) dot = true;
                else break;
            }
            if (!any) return false;
            if (i < end)
            {
                if (s[i] != 'e' && s[i] != 'E') return false;
                i++;
                bool negativeExponent = i < end && s[i] == '-';
                if (i < end && (s[i] == '-' || s[i] == '+')) i++;
                if (i == end) return false;
                int e = 0;
                for (; i < end; i++)
                {
                    char c = s[i];
                    if (c < '0' || c > '9') return false;
                    if (e < 100000) e = e * 10 + (c - '0');
                }
                exponent += negativeExponent ? -e : e;
            }
            if (digits <= 19) mantissa = small;
            if (mantissa.IsZero)
            {
                value = negative ? -0.0 : 0.0;
                return true;
            }
            if (digits <= 15 && exponent >= -22 && exponent <= 22)
                value = exponent >= 0 ? small * Pow10[exponent] : small / Pow10[-exponent];
            else value = Exact(mantissa, exponent);
            if (negative) value = -value;
            return true;
        }

        /// <summary>m x 10^e, rounded once to the nearest double, ties to even (normal results; the sim's numbers are).</summary>
        static double Exact(System.Numerics.BigInteger m, int e)
        {
            if (e > 330) return double.PositiveInfinity;
            if (e < -360) return 0;
            System.Numerics.BigInteger num = m, den = System.Numerics.BigInteger.One;
            if (e >= 0) num *= System.Numerics.BigInteger.Pow(10, e);
            else den = System.Numerics.BigInteger.Pow(10, -e);
            // A quotient of 54 bits: the 53 kept and the rounding bit, the remainder the sticky rest.
            int shift = 54 - (BitLength(num) - BitLength(den));
            System.Numerics.BigInteger q, r;
            while (true)
            {
                var n = shift >= 0 ? num << shift : num;
                var d = shift >= 0 ? den : den << -shift;
                q = System.Numerics.BigInteger.DivRem(n, d, out r);
                if (q >= (System.Numerics.BigInteger.One << 54)) shift--;
                else if (q < (System.Numerics.BigInteger.One << 53)) shift++;
                else break;
            }
            long bits = (long)q;
            long kept = bits >> 1;
            if ((bits & 1) == 1 && (!r.IsZero || (kept & 1) == 1)) kept++;
            // kept x 2^(1 - shift), kept within [2^52, 2^53]: exact while the result is normal.
            int power = 1 - shift;
            double v = kept;
            while (power > 0)
            {
                int p = Math.Min(power, 1000);
                v *= BitConverter.Int64BitsToDouble((long)(p + 1023) << 52);
                power -= p;
            }
            while (power < 0)
            {
                int p = Math.Min(-power, 1000);
                v *= BitConverter.Int64BitsToDouble((long)(1023 - p) << 52);
                power += p;
            }
            return v;
        }

        static int BitLength(System.Numerics.BigInteger x)
        {
            int n = 0;
            var bytes = x.ToByteArray();
            int top = bytes.Length - 1;
            while (top > 0 && bytes[top] == 0) top--;
            n = top * 8;
            for (int b = bytes[top]; b != 0; b >>= 1) n++;
            return n;
        }
    }
}

// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.2.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Model Walk - a small JSON reader for the third-person character file (WalkCharacter.gltf, which is JSON). Heron Tools
// ships no JSON library and .NET Framework 4.7.2 / 4.8 (Revit 2020 to 2024) has no System.Text.Json, so this reads the
// file the same way on every Revit year with plain .NET only. Read only - it never writes JSON.
//
// What it gives back: an object is a Dictionary<string, object>, an array a List<object>, a number a double, a string a
// string, true / false a bool, null a null. A broken file throws InvalidDataException with where it broke.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace HeronTools.Services.ModelWalk
{
    internal static class WalkJson
    {
        /// <summary>Reads one JSON value from the whole text. Throws InvalidDataException on a broken file.</summary>
        internal static object Parse(string text)
        {
            if (text == null)
                throw new InvalidDataException("no text");
            var reader = new Reader(text);
            object value = reader.Value();
            reader.SkipSpace();
            if (!reader.AtEnd)
                throw reader.Fail("text after the end");
            return value;
        }

        // ------------------------------------------------------------------ typed helpers for the glTF reader

        /// <summary>The value under a key of an object, or null (not an object, or no such key).</summary>
        internal static object Get(object node, string key) =>
            node is Dictionary<string, object> d && d.TryGetValue(key, out object v) ? v : null;

        internal static Dictionary<string, object> Obj(object node, string key) => Get(node, key) as Dictionary<string, object>;

        internal static List<object> Arr(object node, string key) => Get(node, key) as List<object>;

        internal static bool Has(object node, string key) => Get(node, key) != null;

        internal static string Str(object node, string key) => Get(node, key) as string;

        internal static int Int(object node, string key, int fallback) => Get(node, key) is double n ? (int)n : fallback;

        internal static bool Bool(object node, string key) => Get(node, key) is bool b && b;

        internal static int[] Ints(List<object> list)
        {
            if (list == null)
                return new int[0];
            var result = new int[list.Count];
            for (int i = 0; i < list.Count; i++)
                result[i] = list[i] is double n ? (int)n : 0;
            return result;
        }

        internal static double[] Doubles(List<object> list, double[] fallback)
        {
            if (list == null)
                return fallback;
            var result = new double[list.Count];
            for (int i = 0; i < list.Count; i++)
                result[i] = list[i] is double n ? n : 0.0;
            return result;
        }

        // ------------------------------------------------------------------ the reader

        private sealed class Reader
        {
            // Deeper nesting than this is refused: each level is one call deeper, and running out of stack would end
            // Revit itself (a stack overflow cannot be caught). A glTF file nests about six levels.
            private const int MaxDepth = 64;

            private readonly string _s;
            private int _i;
            private int _depth;

            public Reader(string s) { _s = s; }

            public bool AtEnd => _i >= _s.Length;

            public InvalidDataException Fail(string what) =>
                new InvalidDataException("the file is not valid JSON (" + what + " at character " + _i + ")");

            public void SkipSpace()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c != ' ' && c != '\t' && c != '\r' && c != '\n')
                        return;
                    _i++;
                }
            }

            public object Value()
            {
                SkipSpace();
                if (AtEnd)
                    throw Fail("unexpected end");
                char c = _s[_i];
                switch (c)
                {
                    case '{':
                    case '[':
                        if (++_depth > MaxDepth)
                            throw Fail("nested too deep");
                        object nested = c == '{' ? (object)ObjectValue() : ArrayValue();
                        _depth--;
                        return nested;
                    case '"': return StringValue();
                    case 't': Word("true"); return true;
                    case 'f': Word("false"); return false;
                    case 'n': Word("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                            return NumberValue();
                        throw Fail("unexpected '" + c + "'");
                }
            }

            private Dictionary<string, object> ObjectValue()
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                _i++;   // {
                SkipSpace();
                if (!AtEnd && _s[_i] == '}')
                {
                    _i++;
                    return result;
                }
                while (true)
                {
                    SkipSpace();
                    if (AtEnd || _s[_i] != '"')
                        throw Fail("a name was expected");
                    string key = StringValue();
                    SkipSpace();
                    if (AtEnd || _s[_i] != ':')
                        throw Fail("':' was expected");
                    _i++;
                    result[key] = Value();
                    SkipSpace();
                    if (AtEnd)
                        throw Fail("unexpected end");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == '}') { _i++; return result; }
                    throw Fail("',' or '}' was expected");
                }
            }

            private List<object> ArrayValue()
            {
                var result = new List<object>();
                _i++;   // [
                SkipSpace();
                if (!AtEnd && _s[_i] == ']')
                {
                    _i++;
                    return result;
                }
                while (true)
                {
                    result.Add(Value());
                    SkipSpace();
                    if (AtEnd)
                        throw Fail("unexpected end");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == ']') { _i++; return result; }
                    throw Fail("',' or ']' was expected");
                }
            }

            private string StringValue()
            {
                _i++;   // opening quote
                int start = _i;
                // Fast path: no escapes (the long base64 data string is one of these).
                while (_i < _s.Length && _s[_i] != '"' && _s[_i] != '\\')
                    _i++;
                if (_i < _s.Length && _s[_i] == '"')
                {
                    string plain = _s.Substring(start, _i - start);
                    _i++;
                    return plain;
                }

                var sb = new StringBuilder(_s, start, _i - start, (_i - start) + 16);
                while (true)
                {
                    if (AtEnd)
                        throw Fail("a string has no end");
                    char c = _s[_i++];
                    if (c == '"')
                        return sb.ToString();
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (AtEnd)
                        throw Fail("a string has no end");
                    char e = _s[_i++];
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
                            if (_i + 4 > _s.Length ||
                                !int.TryParse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                throw Fail("a bad \\u escape");
                            sb.Append((char)code);
                            _i += 4;
                            break;
                        default:
                            throw Fail("a bad escape");
                    }
                }
            }

            private double NumberValue()
            {
                int start = _i;
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E')
                        _i++;
                    else
                        break;
                }
                // Invariant culture: a Windows region with a decimal comma must not misread 0.5 as 5.
                if (!double.TryParse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                    throw Fail("a bad number");
                return n;
            }

            private void Word(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0)
                    throw Fail("unexpected word");
                _i += word.Length;
            }
        }
    }
}

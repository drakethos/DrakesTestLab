using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace DrakesTestLab.Cases;

/// <summary>
/// Small self-contained JSON reader (objects, arrays, strings, numbers, true/false/null, plus // and /* */ comments).
/// Unity's JsonUtility silently skipped the lists of objects in test cases, so the cases are parsed here instead and
/// mapped onto the case classes by field name (case-insensitive). Unknown keys are reported, not ignored silently.
/// </summary>
internal static class MiniJson
{
    public static object? Parse(string json)
    {
        var p = new Parser(json);
        var value = p.ReadValue();
        p.SkipSpace();
        if (!p.AtEnd)
            throw p.Error("unexpected text after the JSON value");
        return value;
    }

    private sealed class Parser
    {
        private readonly string _s;
        private int _i;

        public Parser(string s) => _s = s ?? "";

        public bool AtEnd => _i >= _s.Length;

        public Exception Error(string message)
        {
            var line = 1;
            var col = 1;
            for (var k = 0; k < _i && k < _s.Length; k++)
            {
                if (_s[k] == '\n') { line++; col = 1; }
                else col++;
            }

            return new FormatException($"{message} (line {line}, column {col})");
        }

        public void SkipSpace()
        {
            while (_i < _s.Length)
            {
                var c = _s[_i];
                if (char.IsWhiteSpace(c) || c == '﻿')
                {
                    _i++;
                }
                else if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '/')
                {
                    while (_i < _s.Length && _s[_i] != '\n') _i++;
                }
                else if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '*')
                {
                    var end = _s.IndexOf("*/", _i + 2, StringComparison.Ordinal);
                    _i = end < 0 ? _s.Length : end + 2;
                }
                else
                {
                    break;
                }
            }
        }

        public object? ReadValue()
        {
            SkipSpace();
            if (AtEnd)
                throw Error("unexpected end of file");

            var c = _s[_i];
            switch (c)
            {
                case '{': return ReadObject();
                case '[': return ReadArray();
                case '"': return ReadString();
                case 't': Expect("true"); return true;
                case 'f': Expect("false"); return false;
                case 'n': Expect("null"); return null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9'))
                        return ReadNumber();
                    throw Error($"unexpected character '{c}'");
            }
        }

        private void Expect(string word)
        {
            if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0)
                throw Error($"expected '{word}'");
            _i += word.Length;
        }

        private Dictionary<string, object?> ReadObject()
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            _i++; // {
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
                    throw Error("expected a property name in quotes");
                var key = ReadString();
                SkipSpace();
                if (AtEnd || _s[_i] != ':')
                    throw Error("expected ':' after the property name");
                _i++;
                result[key] = ReadValue();
                SkipSpace();
                if (AtEnd)
                    throw Error("unexpected end of file inside an object");
                if (_s[_i] == ',')
                {
                    _i++;
                    SkipSpace();
                    if (!AtEnd && _s[_i] == '}') { _i++; return result; } // tolerate a trailing comma
                    continue;
                }

                if (_s[_i] == '}')
                {
                    _i++;
                    return result;
                }

                throw Error("expected ',' or '}'");
            }
        }

        private List<object?> ReadArray()
        {
            var result = new List<object?>();
            _i++; // [
            SkipSpace();
            if (!AtEnd && _s[_i] == ']')
            {
                _i++;
                return result;
            }

            while (true)
            {
                result.Add(ReadValue());
                SkipSpace();
                if (AtEnd)
                    throw Error("unexpected end of file inside a list");
                if (_s[_i] == ',')
                {
                    _i++;
                    SkipSpace();
                    if (!AtEnd && _s[_i] == ']') { _i++; return result; } // tolerate a trailing comma
                    continue;
                }

                if (_s[_i] == ']')
                {
                    _i++;
                    return result;
                }

                throw Error("expected ',' or ']'");
            }
        }

        private string ReadString()
        {
            var sb = new StringBuilder();
            _i++; // opening quote
            while (_i < _s.Length)
            {
                var c = _s[_i++];
                if (c == '"')
                    return sb.ToString();
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (_i >= _s.Length)
                    break;
                var e = _s[_i++];
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
                        if (_i + 4 > _s.Length || !int.TryParse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                            throw Error("bad \\u escape");
                        sb.Append((char)code);
                        _i += 4;
                        break;
                    default: throw Error($"unknown escape '\\{e}'");
                }
            }

            throw Error("unterminated string");
        }

        private double ReadNumber()
        {
            var start = _i;
            while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0)
                _i++;
            if (!double.TryParse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                throw Error("bad number");
            return d;
        }
    }
}

/// <summary>Copies parsed JSON onto the public fields of a case class. Fields that are missing keep their defaults.</summary>
internal static class CaseMapper
{
    public static void Populate(object target, Dictionary<string, object?> json, List<string> warnings, string path = "")
    {
        var type = target.GetType();
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            known.Add(field.Name);
            if (!json.TryGetValue(field.Name, out var raw) || raw == null)
                continue;

            try
            {
                field.SetValue(target, Convert(raw, field.FieldType, warnings, path + field.Name));
            }
            catch (Exception ex)
            {
                warnings.Add($"'{path}{field.Name}': {ex.Message}");
            }
        }

        foreach (var key in json.Keys)
        {
            if (!known.Contains(key))
                warnings.Add($"unknown field '{path}{key}' (ignored)");
        }
    }

    private static object? Convert(object raw, Type t, List<string> warnings, string path)
    {
        if (t == typeof(string))
            return raw is string s ? s : System.Convert.ToString(raw, CultureInfo.InvariantCulture);

        if (t == typeof(bool))
            return raw is bool b ? b : raw is double d && d != 0d;

        if (t == typeof(int))
        {
            if (raw is bool flag)
                return flag ? 1 : 0;           // tri-state fields accept true / false too
            return (int)Math.Round(ToDouble(raw));
        }

        if (t == typeof(float))
            return (float)ToDouble(raw);

        if (t == typeof(double))
            return ToDouble(raw);

        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
        {
            var item = t.GetGenericArguments()[0];
            var list = (System.Collections.IList)Activator.CreateInstance(t)!;
            if (raw is not List<object?> source)
                throw new FormatException("expected a list [ ... ]");
            for (var i = 0; i < source.Count; i++)
            {
                if (source[i] != null)
                    list.Add(Convert(source[i]!, item, warnings, $"{path}[{i}]."));
            }

            return list;
        }

        if (t.IsClass && raw is Dictionary<string, object?> obj)
        {
            var instance = Activator.CreateInstance(t)!;
            Populate(instance, obj, warnings, path.EndsWith(".") ? path : path + ".");
            return instance;
        }

        throw new FormatException($"cannot read this value as {t.Name}");
    }

    private static double ToDouble(object raw)
    {
        if (raw is double d)
            return d;
        if (raw is bool b)
            return b ? 1d : 0d;
        if (raw is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        throw new FormatException("expected a number");
    }
}

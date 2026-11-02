using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DrakesTestLabServer;

/// <summary>A small JSON reader for the arguments of a server control command: objects, lists, strings, numbers, booleans and null.</summary>
internal static class Json
{
    public static Dictionary<string, object?> ParseObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        var value = ReadValue(json, ref i);
        return value as Dictionary<string, object?> ?? throw new FormatException("the arguments are not a JSON object");
    }

    private static object? ReadValue(string s, ref int i)
    {
        SkipSpace(s, ref i);
        if (i >= s.Length)
            throw new FormatException("unexpected end of JSON");
        switch (s[i])
        {
            case '{':
            {
                i++;
                var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                SkipSpace(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    SkipSpace(s, ref i);
                    var key = ReadString(s, ref i);
                    SkipSpace(s, ref i);
                    Expect(s, ref i, ':');
                    d[key] = ReadValue(s, ref i);
                    SkipSpace(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    Expect(s, ref i, '}');
                    return d;
                }
            }
            case '[':
            {
                i++;
                var list = new List<object?>();
                SkipSpace(s, ref i);
                if (s[i] == ']') { i++; return list; }
                while (true)
                {
                    list.Add(ReadValue(s, ref i));
                    SkipSpace(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    Expect(s, ref i, ']');
                    return list;
                }
            }
            case '"':
                return ReadString(s, ref i);
            default:
            {
                var start = i;
                while (i < s.Length && ",}] \t\r\n".IndexOf(s[i]) < 0)
                    i++;
                var token = s.Substring(start, i - start);
                if (token == "true") return true;
                if (token == "false") return false;
                if (token == "null") return null;
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                    return n;
                throw new FormatException($"unexpected '{token}' in JSON");
            }
        }
    }

    private static string ReadString(string s, ref int i)
    {
        Expect(s, ref i, '"');
        var sb = new StringBuilder();
        while (i < s.Length && s[i] != '"')
        {
            var c = s[i++];
            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }

            var e = s[i++];
            switch (e)
            {
                case 'n': sb.Append('\n'); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case 'u':
                    sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                    i += 4;
                    break;
                default: sb.Append(e); break;
            }
        }

        Expect(s, ref i, '"');
        return sb.ToString();
    }

    private static void SkipSpace(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i]))
            i++;
    }

    private static void Expect(string s, ref int i, char c)
    {
        if (i >= s.Length || s[i] != c)
            throw new FormatException($"expected '{c}' in JSON at {i}");
        i++;
    }
}

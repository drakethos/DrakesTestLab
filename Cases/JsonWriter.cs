using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DrakesTestLab.Cases;

/// <summary>
/// Writes the plain values MiniJson reads (dictionaries, lists, strings, numbers, bools) as indented JSON, keys in insertion order,
/// so saved cases and recorded tests are easy to read and edit by hand.
/// </summary>
internal static class JsonWriter
{
    public static string Write(object? value)
    {
        var sb = new StringBuilder();
        WriteValue(sb, value, 0);
        sb.Append('\n');
        return sb.ToString();
    }

    private static void WriteValue(StringBuilder sb, object? value, int indent)
    {
        switch (value)
        {
            case null:
                sb.Append("null");
                return;
            case string s:
                WriteString(sb, s);
                return;
            case bool b:
                sb.Append(b ? "true" : "false");
                return;
            case float f:
                sb.Append(Number(f));
                return;
            case double d:
                sb.Append(Number(d));
                return;
            case int or long or short or byte:
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            case IDictionary dict:
                WriteObject(sb, dict, indent);
                return;
            case IEnumerable list:
                WriteArray(sb, list, indent);
                return;
            default:
                WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
                return;
        }
    }

    private static string Number(double d)
    {
        // short, stable numbers: 2 decimals is plenty for positions and settings
        var rounded = Math.Round(d, 2);
        return rounded.ToString(rounded == Math.Floor(rounded) ? "0" : "0.##", CultureInfo.InvariantCulture);
    }

    private static void WriteObject(StringBuilder sb, IDictionary dict, int indent)
    {
        if (dict.Count == 0)
        {
            sb.Append("{}");
            return;
        }

        // short objects of plain values stay on one line (a step, an item)
        if (IsFlat(dict.Values) && dict.Count <= 8)
        {
            sb.Append("{ ");
            var first = true;
            foreach (DictionaryEntry e in dict)
            {
                if (!first) sb.Append(", ");
                first = false;
                WriteString(sb, Convert.ToString(e.Key, CultureInfo.InvariantCulture) ?? "");
                sb.Append(": ");
                WriteValue(sb, e.Value, indent);
            }
            sb.Append(" }");
            return;
        }

        sb.Append("{\n");
        var i = 0;
        foreach (DictionaryEntry e in dict)
        {
            sb.Append(' ', (indent + 1) * 2);
            WriteString(sb, Convert.ToString(e.Key, CultureInfo.InvariantCulture) ?? "");
            sb.Append(": ");
            WriteValue(sb, e.Value, indent + 1);
            if (++i < dict.Count) sb.Append(',');
            sb.Append('\n');
        }
        sb.Append(' ', indent * 2).Append('}');
    }

    private static void WriteArray(StringBuilder sb, IEnumerable list, int indent)
    {
        var items = new List<object?>();
        foreach (var item in list)
            items.Add(item);
        if (items.Count == 0)
        {
            sb.Append("[]");
            return;
        }

        if (IsFlat(items) && items.Count <= 12)
        {
            sb.Append("[ ");
            for (var j = 0; j < items.Count; j++)
            {
                if (j > 0) sb.Append(", ");
                WriteValue(sb, items[j], indent);
            }
            sb.Append(" ]");
            return;
        }

        sb.Append("[\n");
        for (var j = 0; j < items.Count; j++)
        {
            sb.Append(' ', (indent + 1) * 2);
            WriteValue(sb, items[j], indent + 1);
            if (j < items.Count - 1) sb.Append(',');
            sb.Append('\n');
        }
        sb.Append(' ', indent * 2).Append(']');
    }

    private static bool IsFlat(IEnumerable values)
    {
        foreach (var v in values)
        {
            if (v is IDictionary || (v is IEnumerable && v is not string))
                return false;
        }

        return true;
    }

    private static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < ' ')
                        sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else
                        sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
    }
}

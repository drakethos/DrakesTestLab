using System;
using System.Collections.Generic;
using BepInEx.Logging;
using DrakesTestLab.Util;
using UnityEngine;

namespace DrakesTestLab.UI.Toolbox;

/// <summary>Live BepInEx + Unity log with per-source filtering, so a mod's own errors are one click away.</summary>
internal sealed class LogTab : ToolboxTab
{
    private bool _errors = true;
    private bool _warnings = true;
    private bool _info;
    private string _search = "";
    private string _source = "";

    public override string Title => "Log";

    public override string Badge
    {
        get
        {
            var c = LogCapture.Instance;
            return c.ErrorCount > 0 ? $" ({c.ErrorCount})" : "";
        }
    }

    public override void Draw()
    {
        var capture = LogCapture.Instance;

        GUILayout.BeginHorizontal();
        _errors = GUILayout.Toggle(_errors, $"Errors ({capture.ErrorCount})", GUILayout.Width(110));
        _warnings = GUILayout.Toggle(_warnings, $"Warnings ({capture.WarningCount})", GUILayout.Width(130));
        _info = GUILayout.Toggle(_info, "Info", GUILayout.Width(60));
        if (GUILayout.Button("Clear"))
            capture.Clear();
        GUILayout.EndHorizontal();

        _search = Ui.Field("Search", _search, 60);

        var sources = new List<string> { "All" };
        sources.AddRange(capture.Sources);
        var selected = _source.Length == 0 ? "All" : _source;
        GUILayout.Label("Source (click to filter):");
        Ui.Grid(sources, 4, n => _source = n == "All" ? "" : n, selected);

        GUILayout.Space(6);
        var shown = 0;
        var entries = capture.Entries;
        var start = Math.Max(0, entries.Count - 600);
        for (var i = start; i < entries.Count; i++)
        {
            var e = entries[i];
            if (!Matches(e))
                continue;

            shown++;
            var color = (e.Level & (LogLevel.Error | LogLevel.Fatal)) != 0 ? "#ff7070"
                : (e.Level & LogLevel.Warning) != 0 ? "#ffd070" : "#cccccc";
            var text = e.Message.Length > 400 ? e.Message.Substring(0, 400) + "..." : e.Message;
            if (GUILayout.Button($"<color={color}>[{e.Time:HH:mm:ss}] {e.Source}: {text}</color>", RowStyle))
                Ui.Copy($"[{e.Level}:{e.Source}] {e.Message}");
        }

        GUILayout.Label(shown == 0 ? "No matching log lines." : "Click a line to copy it.");
    }

    private bool Matches(LogEntry e)
    {
        var isError = (e.Level & (LogLevel.Error | LogLevel.Fatal)) != 0;
        var isWarn = (e.Level & LogLevel.Warning) != 0;
        if (isError ? !_errors : isWarn ? !_warnings : !_info)
            return false;

        if (_source.Length > 0 && !string.Equals(e.Source, _source, StringComparison.OrdinalIgnoreCase))
            return false;

        return _search.Length == 0 || e.Message.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static GUIStyle? _rowStyle;

    private static GUIStyle RowStyle => _rowStyle ??= new GUIStyle(GUI.skin.label)
    {
        richText = true,
        wordWrap = true,
        alignment = TextAnchor.UpperLeft
    };
}

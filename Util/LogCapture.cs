using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using BepInEx.Logging;

namespace DrakesTestLab.Util;

internal sealed class LogEntry
{
    public DateTime Time;
    public LogLevel Level;
    public string Source = "";
    public string Message = "";
}

/// <summary>Collects every BepInEx log line (including the forwarded Unity log) so mod errors show up in game.</summary>
internal sealed class LogCapture : ILogListener
{
    private const int MaxEntries = 1500;

    public static readonly LogCapture Instance = new();

    private readonly ConcurrentQueue<LogEntry> _incoming = new();
    private readonly HashSet<string> _sources = new(StringComparer.OrdinalIgnoreCase);

    public List<LogEntry> Entries { get; } = new();
    public List<string> Sources { get; } = new();
    public int ErrorCount { get; private set; }
    public int WarningCount { get; private set; }

    public static void Start()
    {
        if (!BepInEx.Logging.Logger.Listeners.Contains(Instance))
            BepInEx.Logging.Logger.Listeners.Add(Instance);
    }

    public void LogEvent(object sender, LogEventArgs eventArgs)
    {
        _incoming.Enqueue(new LogEntry
        {
            Time = DateTime.Now,
            Level = eventArgs.Level,
            Source = eventArgs.Source?.SourceName ?? "?",
            Message = eventArgs.Data?.ToString() ?? ""
        });
    }

    /// <summary>Moves queued lines onto the main-thread list. Call once per frame while the UI is visible.</summary>
    public void Drain()
    {
        while (_incoming.TryDequeue(out var entry))
        {
            Entries.Add(entry);
            if (_sources.Add(entry.Source))
            {
                Sources.Add(entry.Source);
                Sources.Sort(StringComparer.OrdinalIgnoreCase);
            }

            if ((entry.Level & (LogLevel.Error | LogLevel.Fatal)) != 0) ErrorCount++;
            else if ((entry.Level & LogLevel.Warning) != 0) WarningCount++;

            if (Entries.Count > MaxEntries)
                Entries.RemoveRange(0, Entries.Count - MaxEntries);
        }
    }

    public void Clear()
    {
        Drain();
        Entries.Clear();
        ErrorCount = 0;
        WarningCount = 0;
    }

    public void Dispose()
    {
    }
}

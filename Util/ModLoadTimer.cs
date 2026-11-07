using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using BepInEx;
using HarmonyLib;

namespace DrakesTestLab.Util;

/// <summary>
/// How long each mod took to load. BepInEx creates plugins one after another, so the time between one plugin's
/// constructor and the next one's is that plugin's load (assembly load + constructor + Awake, which is where mods
/// patch with Harmony).
/// With the optional DrakesTestLab.Preload patcher installed (BepInEx/patchers/DrakesTestLab) every plugin is timed.
/// Without it only the plugins that load after DrakesTestLab are seen individually; the rest are one lump,
/// <see cref="BeforeSeconds"/>.
/// </summary>
internal static class ModLoadTimer
{
    internal sealed class Entry
    {
        public string Name = "";
        public string Guid = "";
        public float StartedAt;
        public float Seconds;
        public bool Closed;
    }

    // keys the preloader patcher writes into AppDomain data
    private const string PatcherKey = "DrakesTestLab.ModLoad";
    private const string PatcherBeforeKey = "DrakesTestLab.BeforeSeconds";
    private const string PatcherEndKey = "DrakesTestLab.ChainloaderDoneTicks";

    private static readonly List<Entry> Entries = new();
    private static float _firstSeen = -1f;
    private static float _closedAt = -1f;
    private static float _before;
    private static bool _patcherRead;

    /// <summary>True when the preloader patcher supplied the timings, so every mod is covered.</summary>
    public static bool FromPatcher { get; private set; }

    /// <summary>Seconds from process start to the first plugin that was timed (patcher: the very first mod; otherwise this mod's successor).</summary>
    public static float BeforeSeconds
    {
        get
        {
            ReadPatcher();
            return _before;
        }
    }

    public static IReadOnlyList<Entry> All
    {
        get
        {
            ReadPatcher();
            return Entries;
        }
    }

    /// <summary>Monotonic seconds (Unity's clock is not reliable this early in startup).</summary>
    private static float Now() => (float)(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);

    /// <summary>True when the patcher is installed and recording: DrakesTestLab's own hook then stays out of the way.</summary>
    internal static bool PatcherActive => AppDomain.CurrentDomain.GetData(PatcherKey) != null;

    /// <summary>Builds the list from the patcher's records, once, when the mods have all loaded.</summary>
    private static void ReadPatcher()
    {
        if (_patcherRead || _closedAt < 0f || AppDomain.CurrentDomain.GetData(PatcherKey) is not List<string> raw || raw.Count == 0)
            return;
        _patcherRead = true;
        FromPatcher = true;
        Entries.Clear();
        foreach (var line in raw.ToArray())
        {
            var p = line.Split('|');
            if (p.Length < 3 || !long.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
                continue;
            Entries.Add(new Entry { Name = p[1], Guid = p[2], StartedAt = (float)(ticks / (double)Stopwatch.Frequency) });
        }

        // the last plugin ends when BepInEx finishes loading plugins (patcher), not at TestLab's Start a frame later
        var lastEnd = _closedAt;
        if (AppDomain.CurrentDomain.GetData(PatcherEndKey) is long doneTicks && doneTicks > 0)
            lastEnd = (float)(doneTicks / (double)Stopwatch.Frequency);
        for (var i = 0; i < Entries.Count; i++)
        {
            var end = i + 1 < Entries.Count ? Entries[i + 1].StartedAt : lastEnd;
            Entries[i].Seconds = Math.Max(0f, end - Entries[i].StartedAt);
            Entries[i].Closed = true;
        }

        if (AppDomain.CurrentDomain.GetData(PatcherBeforeKey) is double before)
            _before = (float)before;
    }

    internal static void OnPluginCreated(BaseUnityPlugin plugin)
    {
        if (PatcherActive)
            return;

        var now = Now();
        if (_firstSeen < 0f)
        {
            _firstSeen = now;
            try
            {
                _before = (float)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds;
            }
            catch
            {
                _before = 0f;
            }
        }

        CloseLast(now);

        var attr = (BepInPlugin?)Attribute.GetCustomAttribute(plugin.GetType(), typeof(BepInPlugin));
        Entries.Add(new Entry
        {
            Name = attr?.Name ?? plugin.GetType().Name,
            Guid = attr?.GUID ?? "",
            StartedAt = now
        });
    }

    /// <summary>The last plugin has no successor: it ends when every plugin's Awake is done (DrakesTestLab's Start).</summary>
    public static void Close()
    {
        if (_closedAt >= 0f)
            return;
        _closedAt = Now();
        CloseLast(_closedAt);
    }

    private static void CloseLast(float now)
    {
        if (Entries.Count == 0)
            return;
        var last = Entries[Entries.Count - 1];
        if (last.Closed)
            return;
        last.Closed = true;
        last.Seconds = Math.Max(0f, now - last.StartedAt);
    }

    /// <summary>JSON array of {name, guid, seconds}, slowest first.</summary>
    public static string ToJson(Func<string, string> esc)
    {
        Close();
        var sorted = new List<Entry>(All);
        sorted.Sort((a, b) => b.Seconds.CompareTo(a.Seconds));
        var sb = new System.Text.StringBuilder("[");
        for (var i = 0; i < sorted.Count; i++)
        {
            var e = sorted[i];
            sb.Append(i == 0 ? "\n" : ",\n");
            sb.Append($"    {{ \"name\": \"{esc(e.Name)}\", \"guid\": \"{esc(e.Guid)}\", \"seconds\": {e.Seconds.ToString("0.000", CultureInfo.InvariantCulture)} }}");
        }

        sb.Append(sorted.Count > 0 ? "\n  ]" : "]");
        return sb.ToString();
    }
}

/// <summary>Fallback when the preloader patcher is not installed: BaseUnityPlugin's constructor is the first managed code of a plugin.</summary>
[HarmonyPatch(typeof(BaseUnityPlugin), MethodType.Constructor)]
internal static class PluginCtorTimingPatch
{
    private static void Prefix(BaseUnityPlugin __instance)
    {
        try
        {
            ModLoadTimer.OnPluginCreated(__instance);
        }
        catch
        {
            // timing must never break a mod's load
        }
    }
}

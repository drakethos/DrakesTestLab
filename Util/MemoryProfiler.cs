using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

namespace DrakesTestLab.Util;

/// <summary>Memory numbers for a stretch of frames (a whole test or one step). All sizes in MB.</summary>
internal readonly struct MemStats
{
    /// <summary>Mono (managed) heap in use at the start, the end and the highest point.</summary>
    public readonly float MonoStartMb, MonoEndMb, MonoPeakMb;

    /// <summary>Everything Unity has allocated (managed + native), same three points.</summary>
    public readonly float TotalStartMb, TotalEndMb, TotalPeakMb;

    /// <summary>Roughly how much managed memory was allocated: the sum of every rise of the heap between frames. A GC hides some, so it is a floor.</summary>
    public readonly float ChurnMb;

    public readonly int Gen0, Gen1, Gen2;
    public readonly bool Valid;

    public MemStats(float monoStart, float monoEnd, float monoPeak, float totalStart, float totalEnd, float totalPeak, float churn, int g0, int g1, int g2)
    {
        MonoStartMb = monoStart; MonoEndMb = monoEnd; MonoPeakMb = monoPeak;
        TotalStartMb = totalStart; TotalEndMb = totalEnd; TotalPeakMb = totalPeak;
        ChurnMb = churn; Gen0 = g0; Gen1 = g1; Gen2 = g2;
        Valid = true;
    }

    public float MonoGrowthMb => MonoEndMb - MonoStartMb;
    public float TotalGrowthMb => TotalEndMb - TotalStartMb;

    public string ToJson()
    {
        if (!Valid)
            return "null";
        var i = CultureInfo.InvariantCulture;
        string F(float v) => v.ToString("0.0", i);
        return $"{{ \"monoStartMb\": {F(MonoStartMb)}, \"monoEndMb\": {F(MonoEndMb)}, \"monoPeakMb\": {F(MonoPeakMb)}, \"totalStartMb\": {F(TotalStartMb)}, \"totalEndMb\": {F(TotalEndMb)}, \"totalPeakMb\": {F(TotalPeakMb)}, \"churnMb\": {F(ChurnMb)}, \"gc0\": {Gen0}, \"gc1\": {Gen1}, \"gc2\": {Gen2} }}";
    }

    public string Summary() => !Valid
        ? "no memory data"
        : $"heap {MonoEndMb:0}MB ({MonoGrowthMb:+0.0;-0.0;0.0}), peak {MonoPeakMb:0}MB, total {TotalEndMb:0}MB ({TotalGrowthMb:+0.0;-0.0;0.0}), churn ~{ChurnMb:0}MB, GC {Gen0}/{Gen1}/{Gen2}";
}

/// <summary>How many Unity objects of each type exist; two censuses show what a test left behind.</summary>
internal sealed class ObjectCensus
{
    public int Total;
    public readonly Dictionary<string, int> ByType = new();

    public static ObjectCensus Take()
    {
        var c = new ObjectCensus();
        var all = Resources.FindObjectsOfTypeAll<UnityEngine.Object>();
        c.Total = all.Length;
        foreach (var o in all)
        {
            if (o == null)
                continue;
            var n = o.GetType().Name;
            c.ByType.TryGetValue(n, out var k);
            c.ByType[n] = k + 1;
        }

        return c;
    }

    /// <summary>The types that grew the most from <paramref name="before"/> to this census, e.g. "GameObject +12, Material +4".</summary>
    public List<(string Type, int Delta)> GrowthSince(ObjectCensus before, int top)
    {
        var list = new List<(string Type, int Delta)>();
        foreach (var kv in ByType)
        {
            before.ByType.TryGetValue(kv.Key, out var was);
            if (kv.Value - was != 0)
                list.Add((kv.Key, kv.Value - was));
        }

        foreach (var kv in before.ByType)
            if (!ByType.ContainsKey(kv.Key))
                list.Add((kv.Key, -kv.Value));
        list.Sort((a, b) => Math.Abs(b.Delta).CompareTo(Math.Abs(a.Delta)));
        return list.Count > top ? list.GetRange(0, top) : list;
    }
}

/// <summary>
/// Records the managed and total memory every frame (next to <see cref="FrameProfiler"/>, same sample index), so a test or step
/// gets its start, end, peak and GC count from the same marks as its frame times.
/// </summary>
internal static class MemoryProfiler
{
    private const float Mb = 1f / (1024f * 1024f);
    private const int MaxSamples = 2_000_000;
    private static readonly List<float> Mono = new(65536);
    private static readonly List<float> Total = new(65536);
    private static readonly List<int> Gc0 = new(65536);
    private static readonly List<int> Gc1 = new(65536);
    private static readonly List<int> Gc2 = new(65536);

    public static int Count => Mono.Count;

    public static float CurrentMonoMb => Profiler.GetMonoUsedSizeLong() * Mb;
    public static float CurrentTotalMb => Profiler.GetTotalAllocatedMemoryLong() * Mb;
    public static float CurrentReservedMb => Profiler.GetTotalReservedMemoryLong() * Mb;

    internal static void Tick()
    {
        if (Mono.Count >= MaxSamples)
            return;
        Mono.Add(CurrentMonoMb);
        Total.Add(CurrentTotalMb);
        Gc0.Add(GC.CollectionCount(0));
        Gc1.Add(GC.CollectionCount(1));
        Gc2.Add(GC.CollectionCount(2));
    }

    /// <summary>Stats for the frames between two marks (from inclusive, to exclusive); marks come from <see cref="FrameProfiler.Mark"/>.</summary>
    public static MemStats Compute(int from, int to)
    {
        to = Math.Min(to, Mono.Count);
        from = Math.Max(0, from);
        if (to - from <= 0)
            return default;

        float monoPeak = 0f, totalPeak = 0f, churn = 0f;
        for (var i = from; i < to; i++)
        {
            if (Mono[i] > monoPeak) monoPeak = Mono[i];
            if (Total[i] > totalPeak) totalPeak = Total[i];
            if (i > from && Mono[i] > Mono[i - 1])
                churn += Mono[i] - Mono[i - 1];
        }

        var last = to - 1;
        return new MemStats(Mono[from], Mono[last], monoPeak, Total[from], Total[last], totalPeak, churn,
            Gc0[last] - Gc0[from], Gc1[last] - Gc1[from], Gc2[last] - Gc2[from]);
    }

    public static string CensusJson(ObjectCensus? before, ObjectCensus? after, Func<string, string> esc)
    {
        if (before == null || after == null)
            return "null";
        var sb = new StringBuilder();
        sb.Append($"{{ \"before\": {before.Total}, \"after\": {after.Total}, \"grew\": [");
        var grew = after.GrowthSince(before, 5);
        for (var i = 0; i < grew.Count; i++)
            sb.Append(i == 0 ? "" : ", ").Append($"{{ \"type\": \"{esc(grew[i].Type)}\", \"delta\": {grew[i].Delta} }}");
        sb.Append("] }");
        return sb.ToString();
    }
}

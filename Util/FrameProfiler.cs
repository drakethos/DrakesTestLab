using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace DrakesTestLab.Util;

/// <summary>Frame-time numbers for a stretch of frames (a whole test or one step).</summary>
internal readonly struct FrameStats
{
    public readonly int Frames;
    public readonly float AvgMs;
    public readonly float P95Ms;
    public readonly float P99Ms;
    public readonly float MaxMs;

    /// <summary>Frames slower than <see cref="FrameProfiler.HitchMs"/>: the stutters a player would feel.</summary>
    public readonly int Hitches;

    public FrameStats(int frames, float avg, float p95, float p99, float max, int hitches)
    {
        Frames = frames;
        AvgMs = avg;
        P95Ms = p95;
        P99Ms = p99;
        MaxMs = max;
        Hitches = hitches;
    }

    public float AvgFps => AvgMs > 0f ? 1000f / AvgMs : 0f;

    /// <summary>The slowest 1% of frames expressed as fps (the usual "1% low").</summary>
    public float LowFps => P99Ms > 0f ? 1000f / P99Ms : 0f;

    public string ToJson()
    {
        var i = CultureInfo.InvariantCulture;
        return $"{{ \"frames\": {Frames}, \"avgMs\": {AvgMs.ToString("0.00", i)}, \"p95Ms\": {P95Ms.ToString("0.00", i)}, \"p99Ms\": {P99Ms.ToString("0.00", i)}, \"maxMs\": {MaxMs.ToString("0.0", i)}, \"hitches\": {Hitches}, \"avgFps\": {AvgFps.ToString("0.0", i)}, \"lowFps\": {LowFps.ToString("0.0", i)} }}";
    }

    public string Summary() => Frames == 0 ? "no frames" : $"{AvgFps:0} fps avg, {LowFps:0} fps 1% low, worst {MaxMs:0} ms, {Hitches} hitch{(Hitches == 1 ? "" : "es")}";
}

/// <summary>
/// Records every frame's unscaled delta time while the game runs, so the runner can say how smooth each test and
/// step was. A step's slice is computed from marks (indexes into the sample list), not from separate timers.
/// </summary>
internal static class FrameProfiler
{
    /// <summary>A frame slower than this counts as a hitch (about 20 fps).</summary>
    public const float HitchMs = 50f;

    private const int MaxSamples = 2_000_000;
    private static readonly List<float> Samples = new(65536);

    public static int Count => Samples.Count;

    /// <summary>Position in the sample list; pass two marks to <see cref="Compute"/>.</summary>
    public static int Mark() => Samples.Count;

    /// <summary>Called once per frame by <see cref="FrameSampler"/>.</summary>
    internal static void Tick(float unscaledDeltaSeconds)
    {
        if (Samples.Count < MaxSamples)
            Samples.Add(unscaledDeltaSeconds * 1000f);
    }

    /// <summary>Stats for the frames recorded between two marks (from inclusive, to exclusive).</summary>
    public static FrameStats Compute(int from, int to)
    {
        to = Math.Min(to, Samples.Count);
        from = Math.Max(0, from);
        var n = to - from;
        if (n <= 0)
            return default;

        var slice = new float[n];
        Samples.CopyTo(from, slice, 0, n);
        double sum = 0;
        var max = 0f;
        var hitches = 0;
        foreach (var ms in slice)
        {
            sum += ms;
            if (ms > max) max = ms;
            if (ms > HitchMs) hitches++;
        }

        Array.Sort(slice);
        return new FrameStats(n, (float)(sum / n), Percentile(slice, 0.95f), Percentile(slice, 0.99f), max, hitches);
    }

    private static float Percentile(float[] sorted, float p)
    {
        var idx = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Max(0, Math.Min(sorted.Length - 1, idx))];
    }
}

/// <summary>Feeds <see cref="FrameProfiler"/> once per frame. Lives on the TestLab input object.</summary>
internal sealed class FrameSampler : MonoBehaviour
{
    private void Update()
    {
        FrameProfiler.Tick(Time.unscaledDeltaTime);
        MemoryProfiler.Tick();
    }
}

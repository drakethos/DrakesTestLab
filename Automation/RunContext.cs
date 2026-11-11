using System;
using System.Collections.Generic;
using DrakesTestLab.Bootstrap;
using UnityEngine;

namespace DrakesTestLab.Automation;

internal sealed class AssertResult
{
    public string Description = "";
    public bool Passed;
    public string Detail = "";
}

internal enum TestStatus
{
    NotRun,
    Running,
    Passed,
    Failed,
    Skipped
}

internal sealed class TestResult
{
    public string Suite = "";
    public string Test = "";
    public TestStatus Status = TestStatus.NotRun;
    public string Reason = "";
    public float Seconds;

    /// <summary>Wall-clock start and end of the test, and the realtime clock at its start (step log lines are stamped relative to it).</summary>
    public DateTime StartedAt;
    public DateTime EndedAt;
    public float StartRealtime;

    /// <summary>Seconds spent in the test's setup (reset + case), before its first step.</summary>
    public float SetupSeconds;

    /// <summary>How long each step took, in order, for the "slowest steps" lists.</summary>
    public List<(string Label, float Seconds)> StepTimes = new();

    /// <summary>Frame-time stats for the whole test, and for each step (parallel to <see cref="StepTimes"/>).</summary>
    public DrakesTestLab.Util.FrameStats Frames;
    public List<DrakesTestLab.Util.FrameStats> StepFrames = new();
    public int FrameMark;

    /// <summary>Memory for the whole test and each step, and the Unity object counts before and after the test (null when counting is off).</summary>
    public DrakesTestLab.Util.MemStats Mem;
    public List<DrakesTestLab.Util.MemStats> StepMem = new();
    public DrakesTestLab.Util.ObjectCensus? CensusBefore;
    public DrakesTestLab.Util.ObjectCensus? CensusAfter;

    public List<string> StepLog = new();
    public List<AssertResult> Asserts = new();
    public string Screenshot = "";

    /// <summary>Every screenshot taken during the test (proof, screenshot steps, failure), paths relative to the results folder.</summary>
    public List<string> Screenshots = new();
}

/// <summary>State shared by the steps and asserts of one running test.</summary>
internal sealed class RunContext
{
    public SuiteDef Suite = null!;
    public TestDef Test = null!;
    public TestResult Result = null!;
    public DateTime StartedAt;
    public float Deadline;
    public bool Unattended;

    /// <summary>Objects a test spawned, by the name it gave them ("as": "chest1"), for later steps and asserts.</summary>
    public readonly Dictionary<string, GameObject> Tags = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Everything the test spawned, removed again when the test ends.</summary>
    public readonly List<GameObject> Spawned = new();

    public object? LastResult;

    /// <summary>A step sent to Player 2 names its object by prefab plus the spot Player 1 has it at ("around"): the nearest
    /// object of that prefab to this spot is the one meant, not the nearest to Player 2.</summary>
    public Vector3? AroundHint;

    public bool TimedOut => Time.realtimeSinceStartup > Deadline;

    /// <summary>Adds a log line stamped with the seconds since the test started, e.g. "[+3.2s] step 2/5: ...".</summary>
    public void Log(string line)
    {
        var t = Time.realtimeSinceStartup - Result.StartRealtime;
        Result.StepLog.Add($"[+{t:0.0}s] {line}");
    }
}

/// <summary>Finds world objects for steps and asserts: a tag a test gave an object, or the nearest object of a prefab.</summary>
internal static class Targets
{
    public static List<ZNetView> AllViews()
    {
        var list = new List<ZNetView>();
        var scene = ZNetScene.instance;
        var field = HarmonyLib.AccessTools.Field(typeof(ZNetScene), "m_instances");
        if (scene == null || field?.GetValue(scene) is not System.Collections.IDictionary dict)
            return list;

        foreach (System.Collections.DictionaryEntry e in dict)
        {
            if (e.Value is ZNetView v && v != null)
                list.Add(v);
        }

        return list;
    }

    public static GameObject? Find(RunContext ctx, string target, float range = 80f, Vector3? around = null)
    {
        if (string.IsNullOrWhiteSpace(target))
            return null;
        if (ctx.Tags.TryGetValue(target, out var tagged) && tagged != null)
            return tagged;

        var player = Player.m_localPlayer;
        var origin = around ?? ctx.AroundHint ?? (player != null ? player.transform.position : Vector3.zero);
        GameObject? best = null;
        var bestD = range;
        foreach (var v in AllViews())
        {
            var go = v.gameObject;
            if (!Utils.GetPrefabName(go).Equals(target, StringComparison.OrdinalIgnoreCase))
                continue;
            var d = Vector3.Distance(go.transform.position, origin);
            if (d < bestD)
            {
                best = go;
                bestD = d;
            }
        }

        return best;
    }

    public static int Count(string prefab, Vector3 around, float radius)
    {
        var n = 0;
        foreach (var v in AllViews())
        {
            if (Utils.GetPrefabName(v.gameObject).Equals(prefab, StringComparison.OrdinalIgnoreCase)
                && Vector3.Distance(v.transform.position, around) <= radius)
                n++;
        }

        return n;
    }

    /// <summary>Position from a step/assert: x/z (world), or dx/dz relative to the player (dz = metres in front).</summary>
    public static Vector3 PointFrom(Dictionary<string, object?> d, Vector3 fallback)
    {
        var p = Player.m_localPlayer;
        Vector3 pos;
        if (d.Has("x") && d.Has("z"))
            pos = new Vector3(d.Num("x"), 0f, d.Num("z"));
        else if (p != null && (d.Has("dx") || d.Has("dz")))
            pos = p.transform.position + p.transform.right * d.Num("dx") + p.transform.forward * d.Num("dz");
        else
            return fallback;

        if (ZoneSystem.instance != null)
            pos.y = ZoneSystem.instance.GetGroundHeight(pos);
        return pos;
    }
}

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

    public bool TimedOut => Time.realtimeSinceStartup > Deadline;

    public void Log(string line) => Result.StepLog.Add(line);
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
        var origin = around ?? (player != null ? player.transform.position : Vector3.zero);
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

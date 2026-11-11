using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Bootstrap;

/// <summary>The kinds of natural world object that get in a test's way (trees, stumps, logs, bushes, plants, rocks, dropped items).</summary>
[Flags]
internal enum NaturalKind
{
    None = 0,
    Tree = 1,
    Stump = 2,
    Log = 4,
    Bush = 8,
    Pickable = 16,
    Rock = 32,
    Drop = 64,

    /// <summary>Everything that grows or lies in the landscape: what an arena should be without.</summary>
    Landscape = Tree | Stump | Log | Bush | Pickable | Rock,
    All = Landscape | Drop
}

/// <summary>
/// Finds and removes the natural objects around a point. A test that walks to "the nearest tree" or counts "what is left
/// to farm" is only repeatable if it knows what is out there, so the lab can clear an area, assert it is clear, and a test
/// adds the trees it needs (the spawn step, or the treeAt function in the shared library) instead of hoping for them.
/// Pieces (including planted saplings), creatures and players are never touched.
/// </summary>
internal static class Naturals
{
    private static readonly FieldInfo? InstancesField = AccessTools.Field(typeof(ZNetScene), "m_instances");

    /// <summary>What kind of natural object this is, or <see cref="NaturalKind.None"/> for anything else.</summary>
    public static NaturalKind Classify(GameObject go)
    {
        if (go == null || go.GetComponent<Piece>() != null || go.GetComponent<Character>() != null || go.GetComponent<Player>() != null)
            return NaturalKind.None;
        if (go.GetComponent<ItemDrop>() != null)
            return NaturalKind.Drop;
        if (go.GetComponent<TreeLog>() != null)
            return NaturalKind.Log;

        var name = go.name;
        if (go.GetComponent<TreeBase>() != null)
            return IsStumpName(name) ? NaturalKind.Stump : NaturalKind.Tree;
        if (go.GetComponent<Pickable>() != null)
            return NaturalKind.Pickable;
        if (go.GetComponent<MineRock>() != null || go.GetComponent<MineRock5>() != null)
            return NaturalKind.Rock;
        if (go.GetComponent<Destructible>() != null)
        {
            if (IsStumpName(name))
                return NaturalKind.Stump;
            if (name.IndexOf("bush", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("shrub", StringComparison.OrdinalIgnoreCase) >= 0)
                return NaturalKind.Bush;
            return NaturalKind.Rock;
        }

        return NaturalKind.None;
    }

    private static bool IsStumpName(string name) =>
        name.IndexOf("stub", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("stump", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>Kinds from step/test words: trees, stumps, logs, bushes, pickables (plants), rocks, drops, landscape, all. Singular forms work too.</summary>
    public static NaturalKind Parse(IEnumerable<object?>? words, NaturalKind fallback)
    {
        if (words == null)
            return fallback;
        var kinds = NaturalKind.None;
        foreach (var w in words)
            kinds |= Parse(Convert.ToString(w, CultureInfo.InvariantCulture), NaturalKind.None);
        return kinds == NaturalKind.None ? fallback : kinds;
    }

    public static NaturalKind Parse(string? word, NaturalKind fallback)
    {
        switch ((word ?? "").Trim().ToLowerInvariant())
        {
            case "tree": case "trees": return NaturalKind.Tree;
            case "stump": case "stumps": return NaturalKind.Stump;
            case "log": case "logs": return NaturalKind.Log;
            case "bush": case "bushes": return NaturalKind.Bush;
            case "pickable": case "pickables": case "plant": case "plants": return NaturalKind.Pickable;
            case "rock": case "rocks": return NaturalKind.Rock;
            case "drop": case "drops": case "item": case "items": return NaturalKind.Drop;
            case "landscape": return NaturalKind.Landscape;
            case "all": return NaturalKind.All;
            default: return fallback;
        }
    }

    private static List<GameObject> Find(Vector3 center, float radius, NaturalKind kinds, float keepRocksWithin = 0f)
    {
        var found = new List<GameObject>();
        var scene = ZNetScene.instance;
        if (scene == null || InstancesField?.GetValue(scene) is not IDictionary dict)
            return found;

        foreach (DictionaryEntry entry in dict)
        {
            if (entry.Value is not ZNetView view || view == null)
                continue;
            var go = view.gameObject;
            var d = go.transform.position - center;
            d.y = 0f;
            if (d.magnitude > radius)
                continue;
            var kind = Classify(go);
            if (kind == NaturalKind.None || (kinds & kind) == 0)
                continue;
            // the standing stones at the start are rocks near the centre: keep those
            if (kind == NaturalKind.Rock && keepRocksWithin > 0f && d.magnitude < keepRocksWithin)
                continue;
            found.Add(go);
        }

        return found;
    }

    /// <summary>How many natural objects of these kinds are within <paramref name="radius"/> metres (ground distance) of the point.</summary>
    public static int Count(Vector3 center, float radius, NaturalKind kinds) => Find(center, radius, kinds).Count;

    /// <summary>Removes them, the game's own way (the object and its save record). Returns how many.</summary>
    public static int Clear(Vector3 center, float radius, NaturalKind kinds, float keepRocksWithin = 0f)
    {
        var scene = ZNetScene.instance;
        if (scene == null)
            return 0;
        var doomed = Find(center, radius, kinds, keepRocksWithin);
        foreach (var go in doomed)
            scene.Destroy(go);
        return doomed.Count;
    }
}

/// <summary>
/// "clearArea" as a test or suite field, or as a step: <c>true</c> (everything natural within the clear radius of the player),
/// or <c>{ "radius": 30, "what": ["trees","stumps"], "around": "arena" }</c>. Kinds: trees, stumps, logs, bushes, pickables,
/// rocks, drops, landscape (default), all.
/// </summary>
internal sealed class ClearSpec
{
    public bool Enabled = true;
    public float Radius = -1f;
    public NaturalKind Kinds = NaturalKind.Landscape;
    public bool AroundArena;

    public static ClearSpec? Read(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case bool b:
                return new ClearSpec { Enabled = b };
            case Dictionary<string, object?> d:
            {
                var spec = new ClearSpec();
                if (d.TryGetValue("radius", out var r) && r != null)
                    spec.Radius = Convert.ToSingle(r, CultureInfo.InvariantCulture);
                if (d.TryGetValue("what", out var w) && w is List<object?> list)
                    spec.Kinds = Naturals.Parse(list, NaturalKind.Landscape);
                else if (d.TryGetValue("what", out var one) && one is string word)
                    spec.Kinds = Naturals.Parse(word, NaturalKind.Landscape);
                spec.AroundArena = d.TryGetValue("around", out var a) && string.Equals(Convert.ToString(a, CultureInfo.InvariantCulture), "arena", StringComparison.OrdinalIgnoreCase);
                return spec;
            }
            default:
                return new ClearSpec();
        }
    }

    /// <summary>Clears now. Returns how many objects were removed.</summary>
    public int Apply()
    {
        var cfg = TestLabConfig.Instance;
        var player = Player.m_localPlayer;
        var center = AroundArena && TestRules.HasArena ? TestRules.ArenaCenter : (player != null ? player.transform.position : TestRules.ArenaCenter);
        var radius = Radius > 0f ? Radius : cfg.ClearRadius.Value;
        // around the arena the standing stones at the start are kept; around the player nothing is
        return Naturals.Clear(center, radius, Kinds, AroundArena && TestRules.HasArena ? cfg.KeepRadius.Value : 0f);
    }
}

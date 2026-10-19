using System;
using System.Collections.Generic;
using UnityEngine;

namespace DrakesTestLab.Util;

internal enum PrefabKind
{
    Creature,
    Item,
    Piece,
    Other
}

/// <summary>Sorted prefab names grouped by what they are, built once per loaded world.</summary>
internal static class PrefabIndex
{
    private static readonly Dictionary<PrefabKind, List<string>> ByKind = new();
    private static ZNetScene? _builtFor;

    public static List<string> All { get; private set; } = new();

    public static List<string> Of(PrefabKind kind)
    {
        Ensure();
        return ByKind.TryGetValue(kind, out var list) ? list : new List<string>();
    }

    public static void Ensure()
    {
        var scene = ZNetScene.instance;
        if (scene == null || ReferenceEquals(_builtFor, scene))
            return;

        _builtFor = scene;
        ByKind.Clear();
        foreach (PrefabKind kind in Enum.GetValues(typeof(PrefabKind)))
            ByKind[kind] = new List<string>();

        var all = new List<string>();
        foreach (var prefab in scene.m_prefabs)
        {
            if (prefab == null)
                continue;

            var kind = prefab.GetComponent<Character>() != null ? PrefabKind.Creature
                : prefab.GetComponent<ItemDrop>() != null ? PrefabKind.Item
                : prefab.GetComponent<Piece>() != null ? PrefabKind.Piece
                : PrefabKind.Other;
            ByKind[kind].Add(prefab.name);
            all.Add(prefab.name);
        }

        foreach (var list in ByKind.Values)
            list.Sort(StringComparer.OrdinalIgnoreCase);

        all.Sort(StringComparer.OrdinalIgnoreCase);
        All = all;
    }

    public static List<string> Search(IEnumerable<string> source, string filter, int max)
    {
        var result = new List<string>();
        var f = filter.Trim();
        foreach (var name in source)
        {
            if (f.Length > 0 && name.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            result.Add(name);
            if (result.Count >= max)
                break;
        }

        return result;
    }
}

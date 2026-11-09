using System;
using System.Collections.Generic;
using System.Globalization;
using DrakesTestLab.Automation;
using UnityEngine;

namespace DrakesTestLab.Bootstrap;

internal enum QuietZoneMode
{
    /// <summary>Never.</summary>
    Off,

    /// <summary>While the lab runs tests (the default): repeatable runs, nothing wild wanders in.</summary>
    Tests,

    /// <summary>Always, also when you play by hand in the lab.</summary>
    Always
}

/// <summary>
/// A "no-fly zone" around the test arena (and around the player): inside it nothing wild spawns, wild creatures that are
/// already there or wander in are removed, and the game's random events do not start. It exists because wild creatures,
/// raids and rain change what the mod under test does, and a test that passes or fails on those is not repeatable.
///
/// It only ever touches wild creatures: ones made by the game's own spawners (SpawnSystem, SpawnArea, CreatureSpawner,
/// which includes events) and ones that were already alive when the zone switched on. Creatures a test or a mod makes
/// itself (the spawn step, the toolbox, a mod's own code) are never removed, so a suite that spawns its own monsters works
/// the same with the zone on.
/// </summary>
internal static class QuietZone
{
    /// <summary>ZDO flag on a creature made by a natural spawner (or alive when the zone was switched on).</summary>
    internal const string WildKey = "testlab_wild";

    /// <summary>&gt; 0 while a natural spawner is running: creatures born in that window are wild.</summary>
    internal static int NaturalDepth;

    /// <summary>True while the game's own random-event timer is running (a console-started event is not blocked).</summary>
    internal static bool InEventTimer;

    public static int Removed { get; private set; }
    public static int BlockedSpawns { get; private set; }
    public static int BlockedEvents { get; private set; }

    private static bool _wasActive;

    public static QuietZoneMode Mode => TestLabConfig.Instance.QuietZone.Value;
    public static float Radius => Mathf.Max(5f, TestLabConfig.Instance.QuietZoneRadius.Value);

    /// <summary>Should the zone act right now?</summary>
    public static bool Active
    {
        get
        {
            switch (Mode)
            {
                case QuietZoneMode.Always: return true;
                case QuietZoneMode.Tests: return TestRunner.Running;
                default: return false;
            }
        }
    }

    /// <summary>Is this point inside the zone (the arena, or the local player's surroundings)?</summary>
    public static bool Contains(Vector3 pos)
    {
        var r2 = Radius * Radius;
        if (TestRules.HasArena && Flat(pos, TestRules.ArenaCenter) <= r2)
            return true;
        var p = Player.m_localPlayer;
        return p != null && Flat(pos, p.transform.position) <= r2;
    }

    private static float Flat(Vector3 a, Vector3 b)
    {
        var dx = a.x - b.x;
        var dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    // ---- called by the Harmony patches ----

    /// <summary>A natural spawner wants to spawn at <paramref name="pos"/>: say no inside the zone.</summary>
    internal static bool AllowSpawn(Vector3 pos)
    {
        if (Active && Contains(pos))
        {
            BlockedSpawns++;
            return false;
        }

        return true;
    }

    /// <summary>The game's random-event timer wants to start an event: say no when the zone is on.</summary>
    internal static bool AllowAutoEvent()
    {
        if (!Active)
            return true;
        BlockedEvents++;
        return false;
    }

    internal static void MarkWild(Character c)
    {
        var nview = c.GetComponent<ZNetView>();
        if (nview == null || !nview.IsValid())
            return;
        var zdo = nview.GetZDO();
        if (zdo != null && !zdo.GetBool(WildKey))
            zdo.Set(WildKey, true);
    }

    internal static bool IsWild(Character c)
    {
        var nview = c != null ? c.GetComponent<ZNetView>() : null;
        return nview != null && nview.IsValid() && nview.GetZDO() != null && nview.GetZDO().GetBool(WildKey);
    }

    private static bool Exempt(Character c) => c == null || c is Player || c.IsTamed() || c.IsBoss();

    // ---- the sweep (every quarter second, from QuietZoneSweeper) ----

    internal static void Sweep()
    {
        var active = Active;
        if (!active)
        {
            _wasActive = false;
            return;
        }

        var all = Character.GetAllCharacters();
        if (all == null)
            return;

        // switched on in a running game: whatever is alive now was not made by the lab, so it counts as wild
        if (!_wasActive)
        {
            _wasActive = true;
            foreach (var c in all)
                if (!Exempt(c))
                    MarkWild(c);
        }

        List<Character>? doomed = null;
        foreach (var c in all)
        {
            if (Exempt(c) || !Contains(c.transform.position) || !IsWild(c))
                continue;
            (doomed ??= new List<Character>()).Add(c);
        }

        if (doomed == null)
            return;

        foreach (var c in doomed)
        {
            var nview = c.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
                continue;
            if (!nview.IsOwner())
            {
                nview.ClaimOwnership(); // removed on the next sweep, once the claim arrived
                continue;
            }

            if (ZNetScene.instance != null)
                ZNetScene.instance.Destroy(c.gameObject);
            Removed++;
        }
    }

    // ---- test hooks (for the Meta suite) ----

    internal static void RegisterHooks()
    {
        TestHooks.Register("quiet.active", _ => Active, "DrakesTestLab");
        TestHooks.Register("quiet.removed", _ => Removed, "DrakesTestLab");
        TestHooks.Register("quiet.blockedSpawns", _ => BlockedSpawns, "DrakesTestLab");

        // a creature made the way a natural spawner makes one (so it counts as wild)
        TestHooks.Register("quiet.spawnWild", args =>
        {
            var p = Player.m_localPlayer;
            var name = args.TryGetValue("prefab", out var n) ? Convert.ToString(n, CultureInfo.InvariantCulture) ?? "Boar" : "Boar";
            var dist = args.TryGetValue("dist", out var d) ? Convert.ToSingle(d, CultureInfo.InvariantCulture) : 6f;
            var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
            if (p == null || prefab == null)
                return "no player or prefab";
            var pos = p.transform.position + p.transform.forward * dist;
            if (ZoneSystem.instance != null)
                pos.y = ZoneSystem.instance.GetGroundHeight(pos);
            NaturalDepth++;
            try
            {
                UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
            }
            finally
            {
                NaturalDepth--;
            }

            return "ok";
        }, "DrakesTestLab");

        // how many creatures of a kind are within radius of the player
        TestHooks.Register("quiet.creaturesNear", args =>
        {
            var p = Player.m_localPlayer;
            var name = args.TryGetValue("prefab", out var n) ? Convert.ToString(n, CultureInfo.InvariantCulture) ?? "Boar" : "Boar";
            var radius = args.TryGetValue("radius", out var r) ? Convert.ToSingle(r, CultureInfo.InvariantCulture) : 25f;
            if (p == null)
                return -1;
            var count = 0;
            foreach (var c in Character.GetAllCharacters())
            {
                if (c == null || c is Player)
                    continue;
                if (Vector3.Distance(c.transform.position, p.transform.position) <= radius && c.name.StartsWith(name, StringComparison.Ordinal))
                    count++;
            }

            return count;
        }, "DrakesTestLab");

        // remove every creature (not players or bosses) within radius: cleanup for tests that let wild ones live
        TestHooks.Register("quiet.clearCreatures", args =>
        {
            var p = Player.m_localPlayer;
            var radius = args.TryGetValue("radius", out var r) ? Convert.ToSingle(r, CultureInfo.InvariantCulture) : 40f;
            if (p == null || ZNetScene.instance == null)
                return -1;
            var doomed = new List<Character>();
            foreach (var c in Character.GetAllCharacters())
                if (c != null && !(c is Player) && !c.IsBoss() && Vector3.Distance(c.transform.position, p.transform.position) <= radius)
                    doomed.Add(c);
            foreach (var c in doomed)
                ZNetScene.instance.Destroy(c.gameObject);
            return doomed.Count;
        }, "DrakesTestLab");

        // is a point `dist` metres east of the player inside the zone?
        TestHooks.Register("quiet.contains", args =>
        {
            var p = Player.m_localPlayer;
            var dist = args.TryGetValue("dist", out var d) ? Convert.ToSingle(d, CultureInfo.InvariantCulture) : 10f;
            return p != null && Contains(p.transform.position + Vector3.right * dist);
        }, "DrakesTestLab");

        // would the game's own event timer be allowed to start an event right now? (does not count as blocked)
        TestHooks.Register("quiet.eventsAllowed", _ => !Active, "DrakesTestLab");
    }
}

/// <summary>Runs <see cref="QuietZone.Sweep"/> four times a second. Lives on the TestLab input object.</summary>
internal sealed class QuietZoneSweeper : MonoBehaviour
{
    private float _next;

    private void Update()
    {
        if (Time.unscaledTime < _next)
            return;
        _next = Time.unscaledTime + 0.25f;
        try
        {
            QuietZone.Sweep();
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Quiet zone sweep failed: {ex.Message}");
        }
    }
}

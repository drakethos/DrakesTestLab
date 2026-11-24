using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DrakesTestLab.Bootstrap;
using UnityEngine;

namespace DrakesTestLab.Combat;

/// <summary>
/// The fighting ground: flat, cleared of rocks, trees and stumps, and ringed by a closed circle of wooden stakes that
/// cannot be broken (huge health, so neither the player nor any monster can destroy them). Nothing is meant to cross the
/// ring unless the simulation puts it there: while a fight runs, a fighter that ends up outside is put back inside, and any
/// creature that wanders in (a wild spawn) is removed.
/// </summary>
internal static class CombatArena
{
    private static readonly List<GameObject> Ring = new();
    private static readonly HashSet<Character> Seen = new();
    private static float _nextSweep;

    public static Vector3 Center { get; private set; }
    public static float Radius { get; private set; }
    public static bool Built => Ring.Count > 0;

    public static float ConfiguredRadius => Mathf.Clamp(TestLabConfig.Instance.CombatArenaRadius.Value, 20f, 30f);

    /// <summary>Solid things standing inside the fighting ground: rocks, ruins, statues, stumps that survived the clearing. Terrain, creatures and loot do not count.</summary>
    public static List<string> Obstructions(Vector3 center, float radius)
    {
        var found = new Dictionary<int, string>();
        foreach (var c in Physics.OverlapSphere(center + Vector3.up * 2f, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c == null || c.isTrigger) continue;
            if (c.GetComponentInParent<Heightmap>() != null || c.GetComponentInParent<Character>() != null || c.GetComponentInParent<ItemDrop>() != null) continue;
            if (c.GetComponentInParent<Ragdoll>() != null) continue;
            var root = c.attachedRigidbody != null ? c.attachedRigidbody.gameObject : c.transform.root.gameObject;
            if (root.GetComponent<Piece>() != null && Ring.Contains(root)) continue;
            if (root.name.StartsWith("_Zone", StringComparison.Ordinal)) continue;      // the zone container, not a solid object
            found[root.GetInstanceID()] = root.name.Replace("(Clone)", "");
        }
        return found.Values.ToList();
    }

    /// <summary>True when something solid is at this spot (a creature spawned here would be inside it).</summary>
    public static bool IsBlocked(Vector3 pos, float radius = 0.8f)
    {
        foreach (var c in Physics.OverlapCapsule(pos + Vector3.up * 0.6f, pos + Vector3.up * 1.6f, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c == null || c.isTrigger) continue;
            if (c.GetComponentInParent<Heightmap>() != null || c.GetComponentInParent<Character>() != null || c.GetComponentInParent<ItemDrop>() != null) continue;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Picks the fighting ground. Tries sites in different directions: teleport there, let the zone load, flatten and clear, then
    /// check that nothing solid is left inside the ring (the earlier attempts built in standing stones and ruins that the clearing
    /// cannot remove). The first clean site wins; if none is clean, the one with the fewest obstructions. Builds the ring there.
    /// </summary>
    public static IEnumerator ChooseAndBuild(Player player, Vector3 start, Quaternion rot, Action<Vector3> done)
    {
        var radius = ConfiguredRadius;
        var best = start;
        var bestCount = int.MaxValue;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var dir = Quaternion.Euler(0f, attempt * 60f, 0f) * Vector3.back;
            var site = ArenaBuilder.FindFlatSite(start + dir * (160f + attempt * 30f));      // far from the start temple, its stones and the lab arena
            if ((site - start).magnitude < 80f)
            {
                // the flattest ground nearby is the lab arena itself (with its house): never that
                TestLabPlugin.Log?.LogInfo($"Combat arena: candidate {attempt + 1} is the start area ({(site - start).magnitude:0} m); skipped.");
                continue;
            }
            // TeleportTo is refused while the game is busy; ask again until it takes, then confirm the player really moved
            var ok = player.TeleportTo(site + Vector3.up * 0.5f, rot, false);
            for (var t = 0; t < 10 && !ok; t++)
            {
                yield return new WaitForSeconds(1f);
                ok = player.TeleportTo(site + Vector3.up * 0.5f, rot, false);
            }
            yield return new WaitForSeconds(1f);
            var waited = 0f;
            while ((player.IsTeleporting() || Player.m_localPlayer == null) && waited < 60f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            player = Player.m_localPlayer ?? player;
            if ((player.transform.position - site).magnitude > 40f)
            {
                TestLabPlugin.Log?.LogWarning($"Combat arena: site {attempt + 1}: the teleport to {site} did not happen (player at {player.transform.position}); trying the next direction.");
                continue;
            }
            yield return new WaitForSeconds(4f);                 // the zone around the new spot loads
            player = Player.m_localPlayer ?? player;
            var center = player.transform.position;
            if (ZoneSystem.instance != null) center.y = ZoneSystem.instance.GetGroundHeight(center);

            ArenaBuilder.Flatten(center, radius + 6f);
            ArenaBuilder.ClearArena(center, radius + 16f, 0f, true);
            yield return new WaitForSeconds(2f);
            ArenaBuilder.ClearArena(center, radius + 16f, 0f, true);   // zones that finished loading meanwhile
            yield return new WaitForSeconds(1f);
            Physics.SyncTransforms();

            var blockers = Obstructions(center, radius + 10f);      // the ring AND a margin outside it: nothing solid near where you stand to watch
            TestLabPlugin.Log?.LogInfo($"Combat arena: site {attempt + 1} at {center} ({(center - start).magnitude:0} m from the start): {blockers.Count} obstruction(s){(blockers.Count > 0 ? " (" + string.Join(", ", blockers.Distinct().Take(6)) + ")" : "")}.");
            if (blockers.Count < bestCount) { bestCount = blockers.Count; best = center; }
            if (blockers.Count == 0) break;
        }

        if ((player.transform.position - best).sqrMagnitude > 4f)
        {
            for (var t = 0; t < 10 && !player.TeleportTo(best + Vector3.up * 0.5f, rot, false); t++)
                yield return new WaitForSeconds(1f);
            yield return new WaitForSeconds(1f);
            var w = 0f;
            while (player.IsTeleporting() && w < 60f) { w += Time.unscaledDeltaTime; yield return null; }
            yield return new WaitForSeconds(3f);
        }
        if (bestCount > 0)
            TestLabPlugin.Log?.LogWarning($"Combat arena: no clean site found; using the best one ({bestCount} obstruction(s) left).");

        TestLabPlugin.Log?.LogInfo($"Combat arena: using {best}, {(best - start).magnitude:0} m from the start.");
        var build = Build(best, radius);
        while (build.MoveNext())
            yield return build.Current;
        done(best);
    }

    public static IEnumerator Build(Vector3 center, float radius)
    {
        Teardown();
        Center = center;
        Radius = radius;
        // flat ground out past the ring, rocks and stumps included
        ArenaBuilder.Flatten(center, radius + 6f);
        ArenaBuilder.ClearArena(center, radius + 16f, 0f, true);
        yield return new WaitForSeconds(1f);

        var prefab = FindStakePrefab();
        if (prefab == null)
        {
            TestLabPlugin.Log?.LogWarning("Combat arena: no stake wall prefab found; fighting without a ring.");
            yield break;
        }

        // piece length along its own x axis, measured from the first one
        var probe = UnityEngine.Object.Instantiate(prefab, center + Vector3.up * 200f, Quaternion.identity);
        Physics.SyncTransforms();
        var width = 2f;
        var b = new Bounds(probe.transform.position, Vector3.zero);
        foreach (var c in probe.GetComponentsInChildren<Collider>())
            b.Encapsulate(c.bounds);
        if (b.size.x > 0.3f) width = Mathf.Max(b.size.x, b.size.z * 0f);
        UnityEngine.Object.Destroy(probe);

        var count = Mathf.CeilToInt(2f * Mathf.PI * radius / (width * 0.92f));      // slight overlap: no gaps
        for (var i = 0; i < count; i++)
        {
            var a = i * 2f * Mathf.PI / count;
            var pos = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
            if (ZoneSystem.instance != null) pos.y = ZoneSystem.instance.GetGroundHeight(pos);
            // the wall's long side (its local x) runs along the circle: the tangent at angle a is (-sin a, 0, cos a),
            // which a yaw of -(a + 90 degrees) turns local x onto (a yaw of -a pointed it at the centre: walls end-on)
            var yaw = -a * Mathf.Rad2Deg - 90f;
            var go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f));
            MakeUnbreakable(go);
            Ring.Add(go);
            if (i % 12 == 11) yield return null;
        }
        TestLabPlugin.Log?.LogInfo($"Combat arena: {Ring.Count} x {prefab.name} in a ring of {radius:0} m radius.");
    }

    /// <summary>
    /// The unsticker: puts the fighters at a new, clear spot inside the ring (the player first, the foes spread around 8 to 12 m
    /// away) and stops their momentum. Returns the spot, or null if no clear one was found.
    /// </summary>
    public static Vector3? Relocate(Character player, IList<Character> foes)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var a = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
            var r = UnityEngine.Random.Range(6f, Mathf.Max(7f, Radius - 12f));
            var spot = Center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
            if (ZoneSystem.instance != null) spot.y = ZoneSystem.instance.GetGroundHeight(spot);
            if (IsBlocked(spot, 1.0f)) continue;

            var placed = new List<(Character c, Vector3 p)> { (player, spot) };
            var ok = true;
            for (var i = 0; i < foes.Count && ok; i++)
            {
                if (foes[i] == null) continue;
                var found = false;
                for (var t = 0; t < 12 && !found; t++)
                {
                    var ang = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
                    var p = spot + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * UnityEngine.Random.Range(8f, 12f);
                    var off = p - Center; off.y = 0f;
                    if (off.magnitude > Radius - 3f) continue;
                    if (ZoneSystem.instance != null) p.y = ZoneSystem.instance.GetGroundHeight(p);
                    if (IsBlocked(p, 0.9f)) continue;
                    placed.Add((foes[i], p));
                    found = true;
                }
                ok = found;
            }
            if (!ok) continue;

            foreach (var (c, p) in placed)
            {
                c.transform.position = p + Vector3.up * 0.2f;
                var body = c.GetComponent<Rigidbody>();
                if (body != null) body.velocity = Vector3.zero;
            }
            Physics.SyncTransforms();
            return spot;
        }
        return null;
    }

    public static void Teardown()
    {
        foreach (var go in Ring)
        {
            if (go == null) continue;
            if (ZNetScene.instance != null) ZNetScene.instance.Destroy(go);
            else UnityEngine.Object.Destroy(go);
        }
        Ring.Clear();
        Seen.Clear();
    }

    /// <summary>
    /// Called every physics step during a fight. Puts a fighter that got out (a launch, a glitch) back inside the ring, and every
    /// half second removes creatures that are not part of the fight (wild spawns that wandered in).
    /// </summary>
    public static void Contain(IEnumerable<Character> fighters)
    {
        var list = fighters.Where(f => f != null).ToList();
        Keep(list, Center, Radius - 1.5f);
        Sweep(list);
    }

    /// <summary>Puts any of <paramref name="fighters"/> that left the circle (<paramref name="center"/>, <paramref name="limit"/>) back inside it.</summary>
    public static void Keep(IEnumerable<Character> fighters, Vector3 center, float limit)
    {
        foreach (var f in fighters)
        {
            if (f == null) continue;
            var off = f.transform.position - center;
            off.y = 0f;
            if (off.magnitude > limit)
            {
                var back = center + off.normalized * Mathf.Max(0.5f, limit - 1f);
                back.y = ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(back) + 0.2f : f.transform.position.y;
                f.transform.position = back;
                var body = f.GetComponent<Rigidbody>();
                if (body != null) body.velocity = Vector3.zero;
                TestLabPlugin.Log?.LogDebug($"Combat arena: put {f.name} back inside its zone.");
            }
        }
    }

    /// <summary>Removes creatures inside the ring that are not in the fight. Pass every fighter of every lane at once.</summary>
    public static void Sweep(ICollection<Character> fighters)
    {
        if (Time.time < _nextSweep) return;
        _nextSweep = Time.time + 0.5f;
        var set = new HashSet<Character>(fighters.Where(f => f != null));
        foreach (var c in Character.GetAllCharacters().ToList())
        {
            if (c == null || c is Player || set.Contains(c)) continue;
            var d = c.transform.position - Center;
            d.y = 0f;
            if (d.magnitude < Radius + 1f)
            {
                TestLabPlugin.Log?.LogDebug($"Combat arena: removed stray {c.name}.");
                if (ZNetScene.instance != null) ZNetScene.instance.Destroy(c.gameObject);
                else UnityEngine.Object.Destroy(c.gameObject);
            }
        }
    }

    private static void MakeUnbreakable(GameObject go)
    {
        var wear = go.GetComponent<WearNTear>();
        if (wear != null)
        {
            wear.m_health = 1e9f;
            wear.m_noRoofWear = true;
            wear.m_noSupportWear = true;
            wear.m_burnable = false;
            try { wear.Repair(); } catch { /* health is already huge by field */ }
        }
        var piece = go.GetComponent<Piece>();
        if (piece != null) piece.m_canBeRemoved = false;
    }

    /// <summary>The "stake_wall" piece; if a game version renames it, any stake-like piece with a WearNTear is used and the names are logged.</summary>
    private static GameObject? FindStakePrefab()
    {
        var scene = ZNetScene.instance;
        if (scene == null) return null;
        foreach (var name in new[] { "stake_wall" })
        {
            var p = scene.GetPrefab(name);
            if (p != null) return p;
        }
        var found = scene.m_prefabs
            .Where(p => p != null && p.name.IndexOf("stake", System.StringComparison.OrdinalIgnoreCase) >= 0 && p.GetComponent<WearNTear>() != null)
            .OrderBy(p => p.name.IndexOf("wall", System.StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1).ThenBy(p => p.name.Length)
            .ToList();
        TestLabPlugin.Log?.LogInfo("Combat arena: stake-like prefabs: " + string.Join(", ", found.Select(p => p.name)));
        return found.FirstOrDefault();
    }
}

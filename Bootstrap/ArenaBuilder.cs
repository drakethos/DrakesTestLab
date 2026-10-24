using System;
using System.Collections;
using System.Collections.Generic;
using DrakesTestLab.Util;
using UnityEngine;

namespace DrakesTestLab.Bootstrap;

/// <summary>Finds flat meadow ground, levels a big circle, and drops a small house with one of every super station.</summary>
internal static class ArenaBuilder
{
    // Floor plan (top-down, door in the south wall): six crafting stations in a row along the north wall, a chest, a bed
    // on the west wall, a hearth on the east wall. prefab, local x, local z, yaw
    private static readonly (string prefab, float x, float z, float yaw)[] Furniture =
    {
        ("piece_workbench", -6.0f, 3.0f, 180f),
        ("forge", -4.0f, 3.0f, 180f),
        ("piece_stonecutter", -2.0f, 3.0f, 180f),
        ("piece_artisanstation", 0.0f, 3.0f, 180f),
        ("blackforge", 2.0f, 3.0f, 180f),
        ("piece_magetable", 4.0f, 3.0f, 180f),
        ("piece_chest_wood", 6.0f, 3.0f, 180f),
        ("bed", -6.0f, -0.5f, 90f),
        ("hearth", 6.0f, -0.5f, -90f)
    };

    // Stations that only work standing in a burning fire: each gets its own fire pit and is then aligned to the flames.
    // The cauldron and mead ketill need their origin inside the fire's Burning effect area; the cooking station (spit)
    // needs its fire-check points inside it. prefab, local x, local z, yaw
    private static readonly (string prefab, float x, float z, float yaw)[] FireStations =
    {
        ("piece_cauldron", -3.5f, 0.0f, 0f),
        ("piece_MeadCauldron", 0.0f, 0.0f, 0f),
        ("piece_cookingstation", 3.5f, 0.0f, 0f)
    };

    private static readonly string[] WallPrefabs = { "woodwall", "wood_wall_log", "wood_wall", "wood_wall_half" };

    public static bool Busy { get; private set; }

    private static void LogPrefabsLike(string fragment)
    {
        var found = PrefabIndex.Search(PrefabIndex.Of(PrefabKind.Piece), fragment, 40);
        TestLabPlugin.Log?.LogInfo($"Piece prefabs containing '{fragment}': {string.Join(", ", found)}");
    }

    /// <summary>Samples rings around a point for flat, dry, meadow ground.</summary>
    public static Vector3 FindFlatSite(Vector3 around)
    {
        var zs = ZoneSystem.instance;
        var best = around;
        var bestScore = float.MaxValue;
        if (zs == null)
            return best;

        for (var radius = 40f; radius <= 400f; radius += 40f)
        {
            for (var a = 0; a < 12; a++)
            {
                var angle = a * Mathf.PI * 2f / 12f;
                var p = new Vector3(around.x + Mathf.Cos(angle) * radius, 0f, around.z + Mathf.Sin(angle) * radius);
                var h = zs.GetGroundHeight(p);
                if (h < 32f || h > 80f)
                    continue;

                var worst = 0f;
                for (var k = 0; k < 8; k++)
                {
                    var ka = k * Mathf.PI * 2f / 8f;
                    var q = new Vector3(p.x + Mathf.Cos(ka) * 22f, 0f, p.z + Mathf.Sin(ka) * 22f);
                    var hq = zs.GetGroundHeight(q);
                    if (hq < 31f)
                    {
                        worst = 99f;
                        break;
                    }

                    worst = Mathf.Max(worst, Mathf.Abs(hq - h));
                }

                var score = worst + radius * 0.01f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = new Vector3(p.x, h, p.z);
                }
            }
        }

        return best;
    }

    /// <summary>Teleports there, waits for the ground to load, levels it and builds the house.</summary>
    public static IEnumerator BuildAt(Vector3 center, bool teleport, bool house, float radius, Vector3? houseAt = null, float keepRadius = 0f)
    {
        if (Player.m_localPlayer == null || Busy)
            yield break;

        // applying another case (or pressing the button again) must not stack a second house on the first
        if (house && TestRules.HasArena && HouseExists(houseAt ?? center))
        {
            TestLabPlugin.Log?.LogInfo("The test house is already built here; skipping the arena build.");
            yield break;
        }

        Busy = true;
        try
        {
            TestRules.ArenaCenter = center;
            TestRules.ArenaRadius = radius;
            TestRules.HasArena = true;

            if (teleport && Player.m_localPlayer != null)
            {
                var p = Player.m_localPlayer;
                // Land outside the future house (south of its doorway) so nothing is ever built around the player.
                p.TeleportTo(center + new Vector3(0f, 3f, -10f), Quaternion.LookRotation(Vector3.forward), false);
                var wait = 0f;
                yield return null;
                while (wait < 25f && (Player.m_localPlayer == null || Player.m_localPlayer.IsTeleporting() || !GroundLoaded(center)))
                {
                    wait += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            for (var i = 0; i < 20 && !GroundLoaded(center); i++)
                yield return null;

            // Clear first, then level to the height at the arena centre (the spawn height in spawn mode).
            ClearArena(center, radius, keepRadius, TestLabConfig.Instance.ClearRocks.Value);
            center.y = ZoneSystem.instance.GetGroundHeight(center);
            Flatten(center, radius);
            yield return new WaitForSeconds(0.5f);
            TestRules.ArenaCenter = center;

            if (house)
            {
                var houseCenter = houseAt ?? center;
                houseCenter.y = center.y; // the whole arena was levelled to this height
                if (TestLabConfig.Instance.KeepWorld.Value && HouseExists(houseCenter))
                {
                    TestLabPlugin.Log?.LogInfo("Kept world already has the test house; not building it again.");
                    TestRules.ArenaCenter = center;
                    yield break;
                }

                var floorTop = BuildHouse(houseCenter);
                if (floorTop > 0f)
                    yield return BuildFireStations(houseCenter, floorTop);
                LogStationCover(houseCenter);
            }


            TestLabPlugin.Log?.LogInfo($"Test arena ready at {center} (radius {radius}).");
        }
        finally
        {
            Busy = false;
        }
    }

    private static bool GroundLoaded(Vector3 pos)
    {
        var list = new List<Heightmap>();
        Heightmap.FindHeightmap(pos, 8f, list);
        return list.Count > 0;
    }

    /// <summary>Levels and smooths a big circle by driving each heightmap's terrain compiler directly. The game's
    /// own TerrainOp path serialises only a prefab hash, so a custom radius has to bypass it.</summary>
    public static void Flatten(Vector3 center, float radius)
    {
        var settings = new TerrainOp.Settings
        {
            m_level = true,
            m_levelRadius = radius,
            m_levelOffset = 0f,
            m_square = false,
            m_smooth = true,
            m_smoothRadius = radius + 10f,
            m_smoothPower = 3f,
            m_raise = false,
            m_paintCleared = true,
            m_paintRadius = radius
        };

        var doOp = HarmonyLib.AccessTools.Method(typeof(TerrainComp), "DoOperation");
        if (doOp == null)
        {
            TestLabPlugin.Log?.LogWarning("TerrainComp.DoOperation not found; arena not flattened.");
            return;
        }

        var maps = new List<Heightmap>();
        Heightmap.FindHeightmap(center, radius + 12f, maps);
        var done = 0;
        foreach (var map in maps)
        {
            try
            {
                doOp.Invoke(map.GetAndCreateTerrainCompiler(), new object[] { center, Vector3.zero, settings });
                done++;
            }
            catch (Exception ex)
            {
                TestLabPlugin.Log?.LogWarning($"Flatten on one heightmap failed: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        TestLabPlugin.Log?.LogInfo($"Flattened {done}/{maps.Count} heightmaps around {center}.");
    }

    public static float BuildHouse(Vector3 center)
    {
        var scene = ZNetScene.instance;
        if (scene == null)
            return 0f;

        var floor = scene.GetPrefab("wood_floor");
        GameObject? wall = null;
        foreach (var candidate in WallPrefabs)
        {
            wall = scene.GetPrefab(candidate);
            if (wall != null)
                break;
        }

        var missing = new List<string>();
        if (floor == null) missing.Add("wood_floor");
        if (wall == null) missing.Add("wall (tried " + string.Join("/", WallPrefabs) + ")");
        if (floor == null || wall == null)
        {
            LogPrefabsLike("wall");
            LogPrefabsLike("floor");
            TestLabPlugin.Log?.LogWarning("Test house skipped, missing: " + string.Join(", ", missing));
            return 0f;
        }

        var upright = Measure(wall, Quaternion.identity);
        var flat = Measure(wall, Quaternion.Euler(90f, 0f, 0f));
        var floorBox = Measure(floor, Quaternion.identity);
        TestLabPlugin.Log?.LogInfo(
            $"House pieces: wall '{wall.name}' size {upright.size}, floor size {floorBox.size}, flat-wall size {flat.size}.");

        // sink the floor so its top is flush with the levelled ground instead of a step above it
        var floorBase = center.y - (floorBox.size.y - 0.04f);
        var floorTop = floorBase + floorBox.size.y;
        var layers = Mathf.Max(1, Mathf.CeilToInt(2.0f / Mathf.Max(0.1f, upright.size.y) - 0.05f));
        var wallTop = floorTop + layers * upright.size.y;

        // floor
        for (var x = -6; x <= 6; x += 2)
        {
            for (var z = -3; z <= 3; z += 2)
                PlaceByBounds(floor, Quaternion.identity, center + new Vector3(x, 0f, z), floorBase);
        }

        // walls (stacked if the wall piece is shorter than 2 m); the gap at x=0 in the south wall is the doorway
        for (var layer = 0; layer < layers; layer++)
        {
            var baseY = floorTop + layer * upright.size.y;
            for (var x = -6; x <= 6; x += 2)
            {
                PlaceByBounds(wall, Quaternion.identity, center + new Vector3(x, 0f, 4f), baseY);
                if (x != 0)
                    PlaceByBounds(wall, Quaternion.identity, center + new Vector3(x, 0f, -4f), baseY);
            }

            for (var z = -3; z <= 3; z += 2)
            {
                PlaceByBounds(wall, Quaternion.Euler(0f, 90f, 0f), center + new Vector3(-7f, 0f, z), baseY);
                PlaceByBounds(wall, Quaternion.Euler(0f, 90f, 0f), center + new Vector3(7f, 0f, z), baseY);
            }
        }

        // door in the doorway (south wall, x = 0)
        var door = FindDoorPrefab(scene);
        if (door != null)
        {
            var doorBox = Measure(door, Quaternion.identity);
            TestLabPlugin.Log?.LogInfo($"Door piece '{door.name}' size {doorBox.size}.");
            PlaceByBounds(door, Quaternion.identity, center + new Vector3(0f, 0f, -4f), floorTop);
        }
        else
        {
            LogPrefabsLike("door");
        }

        // roof: real gabled roof pieces (ridge along x). Floors are tagged "leaky" and do not count as shelter.
        var roofPiece = FindRoofPrefab(scene);
        if (roofPiece == null || !BuildGableRoof(roofPiece, center, wallTop))
        {
            TestLabPlugin.Log?.LogWarning("No usable roof piece found; falling back to flat wall panels for the roof.");
            var stepX = Mathf.Max(0.5f, flat.size.x);
            var stepZ = Mathf.Max(0.5f, flat.size.z);
            for (var x = -7f + stepX / 2f; x < 7f; x += stepX)
            {
                for (var z = -4f + stepZ / 2f; z < 4f; z += stepZ)
                    PlaceByBounds(wall, Quaternion.Euler(90f, 0f, 0f), center + new Vector3(x, 0f, z), wallTop);
            }
        }

        foreach (var (name, x, z, yaw) in Furniture)
        {
            var prefab = scene.GetPrefab(name);
            if (prefab == null)
            {
                missing.Add(name);
                continue;
            }

            Spawn(prefab, center + new Vector3(x, floorTop - center.y, z), yaw);
        }

        if (missing.Count > 0)
            TestLabPlugin.Log?.LogWarning("Test house: prefabs not found in this game version: " + string.Join(", ", missing));

        return floorTop;
    }

    private static void LogStationCover(Vector3 center)
    {
        Physics.SyncTransforms();
        // the exact check CraftingStation.CheckUsable performs, per station inside the house
        foreach (var station in UnityEngine.Object.FindObjectsOfType<CraftingStation>())
        {
            var d = station.transform.position - center;
            if (station.m_roofCheckPoint == null || Mathf.Abs(d.x) > 8f || Mathf.Abs(d.z) > 5f)
                continue;

            Cover.GetCoverForPoint(station.m_roofCheckPoint.position, out var sc, out var su);
            var fire = EffectArea.IsPointPlus025InsideBurningArea(station.transform.position);
            TestLabPlugin.Log?.LogInfo(
                $"Station '{station.m_name}': underRoof={su}, cover={sc:0.00} (needs underRoof and >= 0.70); fire under it={fire} (needed={station.m_craftRequireFire}).");
        }
    }

    /// <summary>Fire pit + station per entry, aligned so the station is genuinely over burning flames.</summary>
    private static IEnumerator BuildFireStations(Vector3 center, float floorTop)
    {
        var scene = ZNetScene.instance;
        var firePrefab = scene != null ? scene.GetPrefab("fire_pit") : null;
        if (scene == null || firePrefab == null)
        {
            TestLabPlugin.Log?.LogWarning("fire_pit prefab not found; fire-based stations skipped.");
            yield break;
        }

        foreach (var (name, x, z, yaw) in FireStations)
        {
            var stationPrefab = scene.GetPrefab(name);
            if (stationPrefab == null)
            {
                TestLabPlugin.Log?.LogWarning($"Station prefab '{name}' not found.");
                continue;
            }

            var basePos = center + new Vector3(x, floorTop - center.y, z);
            var fire = Spawn(firePrefab, basePos, 0f);
            if (fire == null)
                continue;

            // the Burning area only registers once the fire has been lit and its child object activates
            Bounds? area = null;
            for (var i = 0; i < 240 && area == null; i++)
            {
                yield return null;
                area = FindBurningArea(fire);
            }

            if (area == null)
            {
                TestLabPlugin.Log?.LogWarning($"No burning area appeared for the fire under '{name}'.");
                continue;
            }

            Physics.SyncTransforms();
            var station = Spawn(stationPrefab, basePos, yaw);
            if (station == null)
                continue;

            var shift = Vector3.zero;
            var cooking = station.GetComponent<CookingStation>();
            if (cooking != null && cooking.m_fireCheckPoints != null && cooking.m_fireCheckPoints.Length > 0)
            {
                var avg = Vector3.zero;
                foreach (var t in cooking.m_fireCheckPoints)
                    avg += t.position;
                avg /= cooking.m_fireCheckPoints.Length;
                shift = area.Value.center - avg;
            }
            else
            {
                shift = area.Value.center - station.transform.position;
            }

            station.transform.position += shift;
            station.GetComponent<ZNetView>()?.GetZDO()?.SetPosition(station.transform.position);
            Physics.SyncTransforms();

            TestLabPlugin.Log?.LogInfo(
                $"{name}: placed over fire at {station.transform.position} (shifted {shift}); burning area {area.Value.center} size {area.Value.size}.");
        }

        FireTools.Refuel();
    }

    private static Bounds? FindBurningArea(GameObject fire)
    {
        foreach (var ea in fire.GetComponentsInChildren<EffectArea>(false))
        {
            if (!ea.m_type.ToString().Contains("Burning"))
                continue;

            var col = ea.GetComponent<Collider>();
            if (col != null)
                return col.bounds;
        }

        return null;
    }

    /// <summary>Roof pieces are found by their display token, not by prefab name: "Thatch Roof 26/45/67 deg" = $piece_woodroof26/45/67.</summary>
    private static readonly string[] RoofTokens = { "$piece_woodroof26", "$piece_woodroof45", "$piece_woodroof67" };

    private static GameObject? FindDoorPrefab(ZNetScene scene)
    {
        foreach (var n in new[] { "wood_door", "woodwall_door", "wood_doorway" })
        {
            var g = scene.GetPrefab(n);
            if (g != null && g.GetComponent<Door>() != null)
                return g;
        }

        string? best = null;
        foreach (var n in PrefabIndex.Of(PrefabKind.Piece))
        {
            var l = n.ToLowerInvariant();
            if (l.Contains("door") && l.Contains("wood") && !l.Contains("iron") && !l.Contains("gate") && (best == null || n.Length < best.Length))
                best = n;
        }

        return best != null ? scene.GetPrefab(best) : null;
    }

    private static bool HouseExists(Vector3 houseCenter)
    {
        foreach (var station in UnityEngine.Object.FindObjectsOfType<CraftingStation>())
        {
            var d = station.transform.position - houseCenter;
            if (Mathf.Abs(d.x) < 9f && Mathf.Abs(d.z) < 6f)
                return true;
        }

        return false;
    }

    private static GameObject? FindRoofPrefab(ZNetScene scene)
    {
        foreach (var token in RoofTokens)
        {
            foreach (var prefab in scene.m_prefabs)
            {
                var piece = prefab != null ? prefab.GetComponent<Piece>() : null;
                if (piece != null && piece.m_name == token)
                {
                    TestLabPlugin.Log?.LogInfo($"Roof piece: prefab '{prefab!.name}' ({token}).");
                    return prefab;
                }
            }
        }

        LogPrefabsLike("roof");
        return null;
    }

    /// <summary>
    /// Slope geometry read from the piece's snap points (the same corner points the build tool snaps with):
    /// the low edge sits at the bottom, the high edge at the top, so rows can be chained edge to edge exactly.
    /// All vectors are relative to the prefab pivot at identity rotation.
    /// </summary>
    private readonly struct RoofGeometry
    {
        public RoofGeometry(Vector3 lowCenter, Vector3 run, float width)
        {
            LowCenter = lowCenter;
            Run = run;
            Width = width;
        }

        public Vector3 LowCenter { get; }
        public Vector3 Run { get; }
        public float Width { get; }
    }

    private static RoofGeometry? ReadRoofGeometry(GameObject prefab)
    {
        var holder = new GameObject("DrakesTestLab_Measure");
        holder.SetActive(false);
        try
        {
            var go = UnityEngine.Object.Instantiate(prefab, Vector3.zero, Quaternion.identity, holder.transform);
            var piece = go.GetComponent<Piece>();
            var points = new List<Transform>();
            piece?.GetSnapPoints(points);
            if (points.Count < 3)
            {
                TestLabPlugin.Log?.LogWarning($"Roof '{prefab.name}' has {points.Count} snap points; cannot derive slope.");
                return null;
            }

            var minY = float.MaxValue;
            var maxY = float.MinValue;
            foreach (var t in points)
            {
                minY = Mathf.Min(minY, t.position.y);
                maxY = Mathf.Max(maxY, t.position.y);
            }

            if (maxY - minY < 0.1f)
                return null;

            Vector3 low = Vector3.zero, high = Vector3.zero;
            int nLow = 0, nHigh = 0;
            foreach (var t in points)
            {
                var p = t.position;
                if (p.y < minY + 0.05f) { low += p; nLow++; }
                else if (p.y > maxY - 0.05f) { high += p; nHigh++; }
            }

            if (nLow == 0 || nHigh == 0)
                return null;

            low /= nLow;
            high /= nHigh;
            var run = high - low;
            var flatRun = new Vector3(run.x, 0f, run.z);
            if (flatRun.sqrMagnitude < 0.01f)
                return null;

            var across = Vector3.Cross(Vector3.up, flatRun.normalized);
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var t in points)
            {
                var d = Vector3.Dot(t.position, across);
                lo = Mathf.Min(lo, d);
                hi = Mathf.Max(hi, d);
            }

            TestLabPlugin.Log?.LogInfo(
                $"Roof geometry '{prefab.name}': {points.Count} snap points, rise {run.y:0.00}, run {flatRun.magnitude:0.00}, width {hi - lo:0.00}.");
            return new RoofGeometry(low, run, hi - lo);
        }
        finally
        {
            UnityEngine.Object.Destroy(holder);
        }
    }

    /// <summary>Builds a gable roof: two slopes whose high edges meet over the ridge line z = 0, ridge running along x.</summary>
    private static bool BuildGableRoof(GameObject roof, Vector3 center, float wallTop)
    {
        var g = ReadRoofGeometry(roof);
        if (g == null)
            return false;

        const float overhang = 0.3f;
        foreach (var side in new[] { 1f, -1f })
        {
            var flatRun = new Vector3(g.Value.Run.x, 0f, g.Value.Run.z);
            var desired = new Vector3(0f, 0f, -side);          // climb toward the ridge
            var yaw = Vector3.SignedAngle(flatRun, desired, Vector3.up);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var lowCenter = rot * g.Value.LowCenter;
            var run = rot * g.Value.Run;

            var width = Mathf.Max(0.5f, g.Value.Width);
            var columns = Mathf.CeilToInt((14f + overhang * 2f) / width - 0.01f);
            var rows = Mathf.Max(1, Mathf.CeilToInt((4f + overhang) / Mathf.Abs(run.z) - 0.01f));
            var x0 = -7f - overhang + width / 2f;

            for (var k = 0; k < rows; k++)
            {
                for (var i = 0; i < columns; i++)
                {
                    var edgeTarget = center + new Vector3(x0 + i * width, wallTop - center.y, side * (4f + overhang)) + run * k;
                    Spawn(roof, edgeTarget - lowCenter, rot);
                }
            }

            TestLabPlugin.Log?.LogInfo($"Roof {(side > 0 ? "north" : "south")} slope: {columns}x{rows} pieces, yaw {yaw:0}.");
        }

        return true;
    }

    /// <summary>World-space mesh bounds of a prefab if it were placed at the origin with the given rotation.</summary>
    private static Bounds Measure(GameObject prefab, Quaternion rot)
    {
        var holder = new GameObject("DrakesTestLab_Measure");
        holder.SetActive(false);
        try
        {
            var go = UnityEngine.Object.Instantiate(prefab, Vector3.zero, rot, holder.transform);
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            var first = true;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null)
                    continue;

                var mb = mf.sharedMesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3(
                        (i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    var world = mf.transform.TransformPoint(corner);
                    if (first)
                    {
                        bounds = new Bounds(world, Vector3.zero);
                        first = false;
                    }
                    else
                    {
                        bounds.Encapsulate(world);
                    }
                }
            }

            return bounds;
        }
        finally
        {
            UnityEngine.Object.Destroy(holder);
        }
    }

    /// <summary>Spawns a piece so its mesh bounds are centred on target.x/z and sit on baseY, whatever its pivot is.</summary>
    private static void PlaceByBounds(GameObject prefab, Quaternion rot, Vector3 target, float baseY)
    {
        var b = Measure(prefab, rot);
        var pos = new Vector3(target.x - b.center.x, baseY - b.min.y, target.z - b.center.z);
        Spawn(prefab, pos, rot);
    }

    /// <summary>
    /// Removes trees, bushes, pickables (and optionally rocks) inside the arena so the flat ground is actually clear.
    /// Anything within keepRadius of the arena centre is left alone (the spawn standing stones).
    /// </summary>
    private static void ClearArena(Vector3 center, float radius, float keepRadius, bool clearRocks)
    {
        var scene = ZNetScene.instance;
        var field = HarmonyLib.AccessTools.Field(typeof(ZNetScene), "m_instances");
        if (scene == null || field?.GetValue(scene) is not System.Collections.IDictionary dict)
            return;

        var doomed = new List<GameObject>();
        foreach (System.Collections.DictionaryEntry entry in dict)
        {
            if (entry.Value is not ZNetView view || view == null)
                continue;

            var go = view.gameObject;
            var d = go.transform.position - center;
            d.y = 0f;
            if (d.magnitude > radius + 6f || d.magnitude < keepRadius || go.GetComponent<Piece>() != null
                || go.GetComponent<Character>() != null || go.GetComponent<Player>() != null || go.GetComponent<ItemDrop>() != null)
                continue;

            var plant = go.GetComponent<TreeBase>() != null || go.GetComponent<TreeLog>() != null || go.GetComponent<Pickable>() != null;
            var rock = go.GetComponent<MineRock>() != null || go.GetComponent<MineRock5>() != null || go.GetComponent<Destructible>() != null;
            if (plant || (clearRocks && rock))
                doomed.Add(go);
        }

        foreach (var go in doomed)
            scene.Destroy(go);

        TestLabPlugin.Log?.LogInfo($"Cleared {doomed.Count} trees/bushes/rocks from the arena (kept everything within {keepRadius:0} m of the centre).");
    }

    private static GameObject? Spawn(GameObject prefab, Vector3 pos, Quaternion rot)
    {
        try
        {
            var go = UnityEngine.Object.Instantiate(prefab, pos, rot);

            // Lab pieces must never collapse or rot: no structural-integrity check, no weather wear.
            foreach (var wear in go.GetComponentsInChildren<WearNTear>())
            {
                // Valheim names these backwards: "noSupportWear = true" means the piece IS checked for support
                // (and collapses), "noRoofWear = true" means it takes rain damage without a roof.
                wear.m_noSupportWear = false;
                wear.m_noRoofWear = false;
                wear.m_health = 100000f;
            }

            SetCreator(go.GetComponent<Piece>());
            // so "Save as case" leaves the lab's own arena and house out of a saved build
            if (go.GetComponent<ZNetView>() is { } view && view.IsValid())
                view.GetZDO().Set(Cases.CaseCapture.ArenaPieceKey, 1);
            return go;
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Spawn {prefab.name} failed: {ex.Message}");
            return null;
        }
    }

    private static GameObject? Spawn(GameObject prefab, Vector3 pos, float yaw) => Spawn(prefab, pos, Quaternion.Euler(0f, yaw, 0f));

    /// <summary>Piece.SetCreator gained a PlatformUserID parameter in 1.0, so bind by name and pad.</summary>
    private static void SetCreator(Piece? piece)
    {
        var player = Player.m_localPlayer;
        if (piece == null || player == null)
            return;

        var method = HarmonyLib.AccessTools.Method(typeof(Piece), "SetCreator");
        if (method == null)
            return;

        var ps = method.GetParameters();
        var args = new object?[ps.Length];
        args[0] = player.GetPlayerID();
        for (var i = 1; i < ps.Length; i++)
            args[i] = ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null;
        method.Invoke(piece, args);
    }

}

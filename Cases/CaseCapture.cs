using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using DrakesTestLab.Bootstrap;
using DrakesTestLab.UI.Toolbox;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Cases;

/// <summary>What "Save as case" writes.</summary>
internal sealed class CaptureOptions
{
    public bool World = true;          // time, weather, world keys, bosses
    public bool Rules = true;          // stations, cost, carry weight, no-build, fall damage, noise switches
    public bool Modes = true;          // god, ghost, fly, no cost, hostile, infinite stamina
    public bool Inventory = true;      // items (with who made them and their custom data)
    public bool Skills = true;
    public bool Identity = true;       // who you are playing as
    public bool Wards = true;          // wards around you
    public bool Buildings = true;      // pieces around you (a saved build), chests with contents, signs with text
    public bool ModSettings = true;    // mod settings changed from their defaults
    public bool Position;              // where you stand (world coordinates)
    public float Radius = 20f;         // how far around you wards and buildings are saved
}

/// <summary>
/// Turns the current game into a test case: the dials you set, what you carry, the wards and pieces around you and the mod settings
/// you changed. Only what differs from a plain game is written, so the file stays short and readable.
/// Positions are relative to where you stand and face, like the case's own spawns and wards.
/// </summary>
internal static class CaseCapture
{
    /// <summary>A marker the arena builder puts on its own pieces (house, floor), so a saved build leaves them out.</summary>
    public static readonly int ArenaPieceKey = "drakes_testlab_arena".GetStableHashCode();

    public static Dictionary<string, object?> Capture(CaptureOptions o, string name, string description)
    {
        var p = Player.m_localPlayer ?? throw new InvalidOperationException("no player yet");
        var c = new Dictionary<string, object?> { ["name"] = name };
        if (description.Length > 0)
            c["description"] = description;
        c["reset"] = 1;

        if (o.World)
            World(c);
        if (o.Position)
        {
            c["teleportX"] = p.transform.position.x;
            c["teleportZ"] = p.transform.position.z;
        }
        if (o.Rules)
            Rules(c);
        if (o.Modes)
            Modes(c, p);
        if (o.Inventory)
            Inventory(c, p);
        if (o.Skills)
            Skills(c, p);
        if (o.Identity && WardTools.IsSwitched)
            c["playAs"] = NameOf(WardTools.Me().Id, WardTools.Me().Name);
        if (o.Wards)
            Wards(c, p, o.Radius);
        if (o.Buildings)
            Buildings(c, p, o.Radius);
        if (o.ModSettings)
            ModSettings(c);
        return c;
    }

    public static string Save(Dictionary<string, object?> c, string fileName)
    {
        Directory.CreateDirectory(CaseRunner.Folder);
        var path = Path.Combine(CaseRunner.Folder, Sanitize(fileName) + ".json");
        File.WriteAllText(path, JsonWriter.Write(c));
        return path;
    }

    public static string Sanitize(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var clean = new string(name.Trim().Select(ch => Array.IndexOf(bad, ch) >= 0 ? '-' : ch).ToArray());
        return clean.Length == 0 ? "my-case" : clean;
    }

    // ---------------------------------------------------------------- parts

    private static void World(Dictionary<string, object?> c)
    {
        var env = EnvMan.instance;
        if (env != null)
        {
            c["timeOfDay"] = env.m_debugTimeOfDay ? env.m_debugTime : DayFraction(env);
            c["freezeTime"] = env.m_debugTimeOfDay ? 1 : 0;
            if (!string.IsNullOrEmpty(env.m_debugEnv))
                c["weather"] = env.m_debugEnv;
        }

        var keys = ZoneSystem.instance != null ? ZoneSystem.instance.GetGlobalKeys() : new List<string>();
        var bosses = BossTools.All.Where(b => keys.Contains(b.Key)).Select(b => (object?)b.Name).ToList();
        var other = keys.Where(k => BossTools.All.All(b => b.Key != k)).Select(k => (object?)k).ToList();
        if (bosses.Count > 0)
            c["bosses"] = bosses;
        if (other.Count > 0)
            c["globalKeys"] = other;
    }

    private static float DayFraction(EnvMan env)
    {
        var m = AccessTools.Method(typeof(EnvMan), "GetDayFraction");
        return m != null ? (float)m.Invoke(env, null) : 0.5f;
    }

    private static void Rules(Dictionary<string, object?> c)
    {
        if (TestRules.StationLevel > 0) c["stationLevel"] = TestRules.StationLevel;
        if (Math.Abs(TestRules.CostMultiplier - 1f) > 0.001f) c["costMultiplier"] = TestRules.CostMultiplier;
        if (TestRules.MaxCarryWeight > 0f) c["carryWeight"] = TestRules.MaxCarryWeight;
        if (TestRules.MaxHealth > 0f) c["maxHealth"] = TestRules.MaxHealth;
        if (TestRules.MaxStamina > 0f) c["maxStamina"] = TestRules.MaxStamina;
        if (TestRules.MaxEitr > 0f) c["maxEitr"] = TestRules.MaxEitr;
        if (Math.Abs(TestRules.RunSpeedMultiplier - 1f) > 0.001f) c["runSpeedMultiplier"] = TestRules.RunSpeedMultiplier;
        if (Math.Abs(TestRules.JumpMultiplier - 1f) > 0.001f) c["jumpMultiplier"] = TestRules.JumpMultiplier;
        c["noBuild"] = TestRules.NoBuild switch { NoBuildMode.Off => "off", NoBuildMode.Scaled => "scaled", _ => "vanilla" };
        if (TestRules.NoBuild == NoBuildMode.Scaled)
        {
            c["noBuildScale"] = TestRules.NoBuildScale;
            if (TestRules.NoBuildRadius > 0f) c["noBuildRadius"] = TestRules.NoBuildRadius;
        }
        c["fallDamageMultiplier"] = TestRules.FallDamageMultiplier;
        c["hideUnlockPopups"] = TestRules.HideUnlockPopups ? 1 : 0;
        c["hideTutorials"] = TestRules.HideTutorials ? 1 : 0;
        c["autoDiscovery"] = TestRules.AutoDiscovery ? 1 : 0;
        if (FireTools.InfiniteFuel) c["infiniteFuel"] = 1;
    }

    private static void Modes(Dictionary<string, object?> c, Player p)
    {
        c["god"] = p.InGodMode() ? 1 : 0;
        c["ghost"] = p.InGhostMode() ? 1 : 0;
        c["fly"] = p.InDebugFlyMode() ? 1 : 0;
        c["noCost"] = p.NoCostCheat() ? 1 : 0;
        c["hostile"] = TestLabConfig.Instance.CreaturesHostile.Value ? 1 : 0;
        c["infiniteStamina"] = PlayerTab.InfiniteStamina ? 1 : 0;
    }

    private static void Inventory(Dictionary<string, object?> c, Player p)
    {
        c["clearInventory"] = true;
        var items = p.GetInventory().GetAllItems()
            .OrderBy(i => i.m_gridPos.y).ThenBy(i => i.m_gridPos.x)
            .Select(Item).Where(i => i != null).Select(i => (object?)i).ToList();
        if (items.Count > 0)
            c["items"] = items;
    }

    /// <summary>One stack as an item spec: prefab, count, quality, worn, who made it and its custom data.</summary>
    public static Dictionary<string, object?>? Item(ItemDrop.ItemData item)
    {
        if (item.m_dropPrefab == null)
            return null;
        var d = new Dictionary<string, object?> { ["prefab"] = item.m_dropPrefab.name };
        if (item.m_stack != 1) d["count"] = item.m_stack;
        if (item.m_quality > 1) d["quality"] = item.m_quality;
        if (item.m_equipped) d["equip"] = true;
        if (item.m_crafterID != 0) d["crafter"] = NameOf(item.m_crafterID, item.m_crafterName);
        if (item.m_customData != null && item.m_customData.Count > 0)
            d["data"] = item.m_customData.Select(kv => (object?)new Dictionary<string, object?> { ["key"] = kv.Key, ["value"] = kv.Value }).ToList();
        return d;
    }

    private static void Skills(Dictionary<string, object?> c, Player p)
    {
        var list = new List<object?>();
        foreach (var skill in p.GetSkills().GetSkillList())
        {
            var level = Mathf.FloorToInt(skill.m_level);
            if (level > 0)
                list.Add(new Dictionary<string, object?> { ["name"] = skill.m_info.m_skill.ToString(), ["level"] = level });
        }

        if (list.Count > 0)
            c["skills"] = list;
    }

    private static readonly System.Reflection.MethodInfo? WardEnabled = AccessTools.Method(typeof(PrivateArea), "IsEnabled");

    private static void Wards(Dictionary<string, object?> c, Player p, float radius)
    {
        var list = new List<object?>();
        foreach (var ward in UnityEngine.Object.FindObjectsOfType<PrivateArea>())
        {
            if (ward == null || Vector3.Distance(ward.transform.position, p.transform.position) > radius)
                continue;
            var (dx, dy, dz, _) = Local(p, ward.transform);
            var piece = ward.GetComponent<Piece>();
            var d = new Dictionary<string, object?>
            {
                ["dx"] = dx, ["dz"] = dz,
                ["owner"] = piece != null ? NameOf(piece.GetCreator(), "") : "me",
                ["enabled"] = WardEnabled != null && (bool)WardEnabled.Invoke(ward, null),
                ["radius"] = ward.m_radius
            };
            var permitted = WardTools.Permitted(ward).Select(i => (object?)NameOf(i.Id, i.Name)).ToList();
            if (permitted.Count > 0)
                d["permitted"] = permitted;
            list.Add(d);
        }

        if (list.Count > 0)
            c["wards"] = list;
    }

    private static void Buildings(Dictionary<string, object?> c, Player p, float radius)
    {
        var list = new List<object?>();
        var seen = new HashSet<ZDOID>();
        foreach (var piece in UnityEngine.Object.FindObjectsOfType<Piece>())
        {
            if (piece == null || piece.GetComponent<PrivateArea>() != null || piece.GetComponent<Player>() != null)
                continue;
            if (Vector3.Distance(piece.transform.position, p.transform.position) > radius)
                continue;
            var view = piece.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !seen.Add(view.GetZDO().m_uid))
                continue;
            // the lab's own arena and house, and things nobody built (trees, rocks), stay out
            if (view.GetZDO().GetInt(ArenaPieceKey) != 0 || piece.GetCreator() == 0)
                continue;

            var (dx, dy, dz, yaw) = Local(p, piece.transform);
            var d = new Dictionary<string, object?>
            {
                ["prefab"] = Utils.GetPrefabName(piece.gameObject),
                ["dx"] = dx, ["dy"] = dy, ["dz"] = dz, ["yaw"] = yaw,
                ["owner"] = NameOf(piece.GetCreator(), "")
            };
            var container = piece.GetComponentInChildren<Container>();
            if (container != null && container.GetInventory() != null)
            {
                var items = container.GetInventory().GetAllItems().Select(Item).Where(i => i != null).Select(i => (object?)i).ToList();
                if (items.Count > 0)
                    d["items"] = items;
            }
            var sign = piece.GetComponentInChildren<Sign>();
            if (sign != null && sign.GetText() is { Length: > 0 } text)
                d["text"] = text;
            list.Add(d);
        }

        if (list.Count > 0)
            c["pieces"] = list;
    }

    private static void ModSettings(Dictionary<string, object?> c)
    {
        var list = new List<object?>();
        foreach (var info in Chainloader.PluginInfos.Values)
        {
            // TestLab's own dials are saved as the case's own fields
            if (info.Instance == null || info.Metadata.GUID == TestLabPlugin.GUID)
                continue;
            var config = info.Instance.Config;
            foreach (var definition in config.Keys.ToList())
            {
                var entry = config[definition];
                if (entry.BoxedValue == null || Equals(entry.BoxedValue, entry.DefaultValue))
                    continue;
                list.Add(new Dictionary<string, object?>
                {
                    ["guid"] = info.Metadata.GUID,
                    ["section"] = entry.Definition.Section,
                    ["key"] = entry.Definition.Key,
                    ["value"] = entry.GetSerializedValue()
                });
            }
        }

        if (list.Count > 0)
            c["configs"] = list;
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A position and turn relative to where the player stands and faces (dx right, dz ahead, dy up).</summary>
    public static (float dx, float dy, float dz, float yaw) Local(Player p, Transform t)
    {
        var facing = Quaternion.Euler(0f, p.transform.eulerAngles.y, 0f);
        var local = Quaternion.Inverse(facing) * (t.position - p.transform.position);
        var yaw = Mathf.DeltaAngle(p.transform.eulerAngles.y, t.eulerAngles.y);
        return (local.x, local.y, local.z, yaw);
    }

    /// <summary>A player id as a case name: "me", Alice / Bob / Carol / Amy, or "Name:12345".</summary>
    public static string NameOf(long id, string name)
    {
        if (WardTools.Original != null && WardTools.Original.Id == id)
            return "me";
        if (!WardTools.IsSwitched && Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == id)
            return "me";
        foreach (var known in new[] { WardTools.Alice, WardTools.Amy, WardTools.Bob, WardTools.Carol })
        {
            if (known.Id == id)
                return known.Name;
        }

        return $"{(string.IsNullOrEmpty(name) ? "Player" : name)}:{id.ToString(CultureInfo.InvariantCulture)}";
    }
}

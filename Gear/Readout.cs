using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Gear;

/// <summary>What the crosshair is on: the networked object (or terrain) under it and the facts worth reading off it.</summary>
internal sealed class Target
{
    public GameObject Root = null!;
    public ZNetView? View;
    public Heightmap? Terrain;
    public Vector3 Point;
    public float Distance;
    public string Prefab = "";
    public string Title = "";
    public string Kind = "";

    public bool Alive => Root != null;
}

/// <summary>
/// Reads an object for the glasses and the clipboard: a title, a kind (Piece, Creature, Item, Player, Terrain, Object) and a list
/// of label/value lines. Every section is read in its own method under try/catch, so one game API that moved never blanks the rest.
/// </summary>
internal static class Readout
{
    private static readonly RaycastHit[] Hits = new RaycastHit[48];
    private static int _mask;

    private static int Mask => _mask != 0 ? _mask : _mask = LayerMask.GetMask("item", "piece", "piece_nonsolid", "Default", "static_solid", "Default_small", "character", "character_net", "terrain", "vehicle", "character_ghost");

    /// <summary>The first thing in front of the camera within <paramref name="range"/> metres that is not the player.</summary>
    public static Target? Find(float range)
    {
        var player = Player.m_localPlayer;
        var cam = Camera.main;
        if (player == null || cam == null)
            return null;
        var count = Physics.RaycastNonAlloc(cam.transform.position, cam.transform.forward, Hits, range, Mask, QueryTriggerInteraction.Ignore);
        var best = -1;
        for (var i = 0; i < count; i++)
        {
            var collider = Hits[i].collider;
            if (collider == null || collider.GetComponentInParent<Player>() == player)
                continue;
            if (best < 0 || Hits[i].distance < Hits[best].distance)
                best = i;
        }
        if (best < 0)
            return null;
        return FromHit(Hits[best], player);
    }

    private static Target FromHit(RaycastHit hit, Player player)
    {
        var collider = hit.collider;
        var t = new Target { Point = hit.point, Distance = Vector3.Distance(player.transform.position, hit.point) };
        t.Terrain = collider.GetComponent<Heightmap>() ?? collider.GetComponentInParent<Heightmap>();
        t.View = collider.GetComponentInParent<ZNetView>();
        if (t.View != null)
            t.Root = t.View.gameObject;
        else if (collider.GetComponentInParent<Hoverable>() is MonoBehaviour hover)
            t.Root = hover.gameObject;
        else
            t.Root = collider.attachedRigidbody != null ? collider.attachedRigidbody.gameObject : collider.gameObject;
        t.Prefab = PrefabName(t.Root);
        t.Kind = KindOf(t);
        t.Title = TitleOf(t);
        return t;
    }

    public static string PrefabName(GameObject go)
    {
        var name = go.name;
        var cut = name.IndexOf("(Clone)", StringComparison.Ordinal);
        return cut > 0 ? name.Substring(0, cut) : name;
    }

    private static string KindOf(Target t)
    {
        if (t.Terrain != null && t.View == null)
            return "Terrain";
        if (t.Root.GetComponent<Player>() != null) return "Player";
        if (t.Root.GetComponent<Character>() != null) return "Creature";
        if (t.Root.GetComponent<ItemDrop>() != null) return "Item";
        if (t.Root.GetComponent<Piece>() != null) return "Piece";
        return "Object";
    }

    private static string TitleOf(Target t)
    {
        try
        {
            if (t.Kind == "Terrain")
                return TerrainName(t);
            var hover = t.Root.GetComponentInChildren<Hoverable>();
            var name = hover?.GetHoverName() ?? "";
            if (name.Length == 0 && t.Root.GetComponent<Piece>() is { } piece) name = piece.m_name;
            if (name.Length == 0 && t.Root.GetComponent<Character>() is { } ch) name = ch.m_name;
            if (name.Length == 0 && t.Root.GetComponent<ItemDrop>() is { } drop) name = drop.m_itemData.m_shared.m_name;
            name = Localize(name);
            return name.Length > 0 ? name : t.Prefab;
        }
        catch
        {
            return t.Prefab;
        }
    }

    private static string TerrainName(Target t)
    {
        try { return Heightmap.FindBiome(t.Point).ToString(); }
        catch { return "Terrain"; }
    }

    public static string Localize(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('$') < 0)
            return text ?? "";
        try { return Localization.instance != null ? Localization.instance.Localize(text) : text; }
        catch { return text; }
    }

    /// <summary>The readout lines. <paramref name="full"/> (the glasses) adds owner, build data, contents and the component list.</summary>
    public static List<KeyValuePair<string, string>> Lines(Target t, bool full)
    {
        var lines = new List<KeyValuePair<string, string>>();
        void Add(string k, string v) { if (!string.IsNullOrEmpty(v)) lines.Add(new KeyValuePair<string, string>(k, v)); }
        void Section(Func<string?> read, string key)
        {
            string? v;
            try { v = read(); }
            catch (Exception ex) { v = full ? $"<color=#e0675c>{ex.GetType().Name}</color>" : null; }
            Add(key, v ?? "");
        }

        if (!t.Alive)
            return lines;
        Add("Prefab", t.Prefab);
        Add("Where", $"{t.Distance:0.0} m  ·  {t.Point.x:0.0}, {t.Point.y:0.0}, {t.Point.z:0.0}");
        if (t.Kind == "Terrain")
        {
            Section(() => TerrainLine(t), "Ground");
            return lines;
        }
        if (full)
            Section(() => ZdoLine(t), "ZDO");
        Section(() => PlayerLine(t), "Player");
        Section(() => CreatureLine(t), "Creature");
        Section(() => TameLine(t), "Tamed");
        Section(() => PieceLine(t), "Piece");
        Section(() => HealthLine(t), "Health");
        if (full)
            Section(() => BuildLine(t), "Build");
        Section(() => ItemLine(t), "Item");
        Section(() => ItemDetailLine(t), "Worth");
        if (full)
            Section(() => ItemDataLine(t), "Data");
        Section(() => ContainerLine(t), "Chest");
        if (full)
            Section(() => ContentsLine(t), "Holds");
        Section(() => StationLine(t), "Station");
        Section(() => FireLine(t), "Fire");
        Section(() => DoorLine(t), "Door");
        Section(() => SignLine(t), "Sign");
        Section(() => WardLine(t), "Ward");
        Section(() => StandLine(t), "Stand");
        Section(() => PlantLine(t), "Plant");
        Section(() => PickableLine(t), "Pickable");
        Section(() => NaturalHealthLine(t), "Health");
        Section(() => ShipLine(t), "Ship");
        if (full)
            Section(() => PartsLine(t), "Parts");
        return lines;
    }

    /// <summary>One short line for a note: the most telling state the object has.</summary>
    public static string Summary(Target t)
    {
        foreach (var line in Lines(t, false))
            if (line.Key is "Health" or "Creature" or "Player" or "Item" or "Chest" or "Station" or "Ward" or "Plant" or "Ground")
                return StripTags(line.Value);
        return "";
    }

    public static string StripTags(string s)
    {
        if (s.IndexOf('<') < 0)
            return s;
        var sb = new StringBuilder(s.Length);
        var inside = false;
        foreach (var c in s)
        {
            if (c == '<') inside = true;
            else if (c == '>') inside = false;
            else if (!inside) sb.Append(c);
        }
        return sb.ToString();
    }

    // ---- sections ----

    private static string? TerrainLine(Target t)
    {
        var biome = Heightmap.FindBiome(t.Point).ToString();
        return $"{biome}  ·  height {t.Point.y:0.0}";
    }

    private static string? ZdoLine(Target t)
    {
        var zdo = t.View != null && t.View.IsValid() ? t.View.GetZDO() : null;
        if (zdo == null)
            return t.View != null ? "not valid" : null;
        var owner = t.View!.IsOwner() ? "you" : zdo.GetOwner() == 0L ? "nobody" : zdo.GetOwner().ToString();
        return $"{zdo.m_uid}  ·  owner {owner}";
    }

    private static string? PlayerLine(Target t)
    {
        var p = t.Root.GetComponent<Player>();
        if (p == null)
            return null;
        return $"{p.GetPlayerName()}  ·  id {p.GetPlayerID()}  ·  HP {p.GetHealth():0}/{p.GetMaxHealth():0}";
    }

    private static string? CreatureLine(Target t)
    {
        var c = t.Root.GetComponent<Character>();
        if (c == null || c is Player)
            return null;
        var s = $"HP {c.GetHealth():0}/{c.GetMaxHealth():0}  ·  level {c.GetLevel()}  ·  {c.GetFaction()}";
        if (c.IsBoss()) s += "  ·  boss";
        return s;
    }

    private static string? TameLine(Target t)
    {
        var c = t.Root.GetComponent<Character>();
        if (c == null || c is Player || !c.IsTamed())
            return null;
        var name = t.View != null && t.View.IsValid() ? t.View.GetZDO().GetString("TamedName".GetStableHashCode(), "") : "";
        return name.Length > 0 ? $"yes  ·  named {name}" : "yes";
    }

    private static string? PieceLine(Target t)
    {
        var piece = t.Root.GetComponent<Piece>();
        if (piece == null)
            return null;
        var creator = piece.GetCreator();
        var zdo = t.View != null && t.View.IsValid() ? t.View.GetZDO() : null;
        var creatorName = zdo != null ? zdo.GetString("creatorName".GetStableHashCode(), "") : "";
        var who = creator == 0L ? "world" : creatorName.Length > 0 ? $"{creatorName} ({creator})" : creator.ToString();
        var s = $"{piece.m_category}  ·  by {who}";
        if (piece.m_comfort > 0) s += $"  ·  comfort {piece.m_comfort}";
        return s;
    }

    private static string? HealthLine(Target t)
    {
        var wear = t.Root.GetComponent<WearNTear>();
        if (wear == null)
            return null;
        var max = wear.m_health;
        var zdo = t.View != null && t.View.IsValid() ? t.View.GetZDO() : null;
        var cur = zdo != null ? zdo.GetFloat("health".GetStableHashCode(), max) : max;
        var pct = Mathf.Clamp01(wear.GetHealthPercentage()) * 100f;
        var colour = pct > 66f ? "#7fd18b" : pct > 33f ? "#f0b45a" : "#e0675c";
        return $"<color={colour}>{cur:0}/{max:0}  ({pct:0}%)</color>";
    }

    private static string? BuildLine(Target t)
    {
        var wear = t.Root.GetComponent<WearNTear>();
        if (wear == null)
            return null;
        var s = wear.m_materialType.ToString();
        if (Call(wear, "GetSupport") is float support)
            s += $"  ·  support {support:0.#}";
        if (AccessTools.Field(typeof(WearNTear), "m_haveRoof")?.GetValue(wear) is bool roof)
            s += roof ? "  ·  under roof" : "  ·  open sky";
        if (wear.IsWet()) s += "  ·  wet";
        return s;
    }

    private static string? ItemLine(Target t)
    {
        var drop = t.Root.GetComponent<ItemDrop>();
        if (drop == null)
            return null;
        var d = drop.m_itemData;
        var s = $"{d.m_shared.m_itemType}  ·  x{d.m_stack}";
        if (d.m_quality > 1 || d.m_shared.m_maxQuality > 1) s += $"  ·  quality {d.m_quality}";
        if (d.m_variant > 0 || d.m_shared.m_variants > 1) s += $"  ·  variant {d.m_variant}";
        return s;
    }

    private static string? ItemDetailLine(Target t)
    {
        var drop = t.Root.GetComponent<ItemDrop>();
        if (drop == null)
            return null;
        var d = drop.m_itemData;
        var parts = new List<string>();
        if (d.m_shared.m_useDurability) parts.Add($"durability {d.m_durability:0}/{d.GetMaxDurability():0}");
        parts.Add($"weight {d.GetWeight():0.#}");
        if (!string.IsNullOrEmpty(d.m_crafterName) || d.m_crafterID != 0L) parts.Add($"crafted by {Localize(d.m_crafterName)} ({d.m_crafterID})");
        return string.Join("  ·  ", parts);
    }

    private static string? ItemDataLine(Target t)
    {
        var drop = t.Root.GetComponent<ItemDrop>();
        if (drop == null)
            return null;
        var data = drop.m_itemData.m_customData;
        if (data == null || data.Count == 0)
            return null;
        var keys = new List<string>();
        foreach (var kv in data)
        {
            keys.Add(kv.Value.Length > 24 ? $"{kv.Key}={kv.Value.Substring(0, 24)}…" : $"{kv.Key}={kv.Value}");
            if (keys.Count >= 4) break;
        }
        return $"{data.Count} key(s): {string.Join(", ", keys)}";
    }

    private static string? ContainerLine(Target t)
    {
        var c = t.Root.GetComponent<Container>();
        if (c == null)
            return null;
        var inv = c.GetInventory();
        var s = inv != null ? $"{inv.NrOfItems()} item(s) in {inv.GetWidth()}x{inv.GetHeight()}  ·  {inv.GetTotalWeight():0.#} weight" : "no inventory";
        if (c.IsInUse()) s += "  ·  in use";
        return s;
    }

    private static string? ContentsLine(Target t)
    {
        var c = t.Root.GetComponent<Container>();
        var inv = c != null ? c.GetInventory() : null;
        if (inv == null || inv.NrOfItems() == 0)
            return null;
        var names = new List<string>();
        foreach (var item in inv.GetAllItems())
        {
            names.Add($"{Localize(item.m_shared.m_name)} x{item.m_stack}");
            if (names.Count >= 5) break;
        }
        return string.Join(", ", names) + (inv.NrOfItems() > 5 ? ", …" : "");
    }

    private static string? StationLine(Target t)
    {
        var station = t.Root.GetComponent<CraftingStation>();
        if (station == null)
            return null;
        var s = Localize(station.m_name);
        if (Call(station, "GetLevel") is int level) s += $"  ·  level {level}";
        return s;
    }

    private static string? FireLine(Target t)
    {
        var fire = t.Root.GetComponent<Fireplace>();
        var zdo = t.View != null && t.View.IsValid() ? t.View.GetZDO() : null;
        if (fire == null || zdo == null)
            return null;
        return $"fuel {zdo.GetFloat("fuel".GetStableHashCode(), 0f):0.#}/{fire.m_maxFuel:0}";
    }

    private static string? DoorLine(Target t)
    {
        var door = t.Root.GetComponent<Door>();
        var zdo = t.View != null && t.View.IsValid() ? t.View.GetZDO() : null;
        if (door == null || zdo == null)
            return null;
        var state = zdo.GetInt("state".GetStableHashCode(), 0);
        return state == 0 ? "closed" : state > 0 ? "open (out)" : "open (in)";
    }

    private static string? SignLine(Target t)
    {
        var sign = t.Root.GetComponent<Sign>();
        if (sign == null)
            return null;
        var text = sign.GetText();
        return text.Length > 0 ? $"\"{text}\"" : "(blank)";
    }

    private static string? WardLine(Target t)
    {
        var ward = t.Root.GetComponent<PrivateArea>();
        var zdo = t.View != null && t.View.IsValid() ? t.View.GetZDO() : null;
        if (ward == null || zdo == null)
            return null;
        var on = zdo.GetBool("enabled".GetStableHashCode(), false);
        var owner = zdo.GetString("creatorName".GetStableHashCode(), "");
        var permitted = zdo.GetInt("permitted".GetStableHashCode(), 0);
        return $"{(on ? "active" : "inactive")}  ·  {ward.m_radius:0} m  ·  owner {(owner.Length > 0 ? owner : "nobody")}  ·  {permitted} permitted";
    }

    private static string? StandLine(Target t)
    {
        var stand = t.Root.GetComponent<ItemStand>();
        var zdo = t.View != null && t.View.IsValid() ? t.View.GetZDO() : null;
        if (stand == null || zdo == null)
            return null;
        var item = zdo.GetString("item".GetStableHashCode(), "");
        return item.Length > 0 ? item : "empty";
    }

    private static string? PlantLine(Target t)
    {
        var plant = t.Root.GetComponent<Plant>();
        if (plant == null)
            return null;
        return $"{plant.GetStatus()}  ·  grows in {plant.m_growTime:0}-{plant.m_growTimeMax:0} s";
    }

    private static string? PickableLine(Target t)
    {
        var pick = t.Root.GetComponent<Pickable>();
        var zdo = t.View != null && t.View.IsValid() ? t.View.GetZDO() : null;
        if (pick == null || zdo == null)
            return null;
        var picked = zdo.GetBool("picked".GetStableHashCode(), false);
        return $"{(picked ? "picked" : "ready")}  ·  gives {pick.m_amount}";
    }

    private static string? NaturalHealthLine(Target t)
    {
        var zdo = t.View != null && t.View.IsValid() ? t.View.GetZDO() : null;
        if (zdo == null || t.Root.GetComponent<WearNTear>() != null)
            return null;
        float max;
        var tier = -1;
        if (t.Root.GetComponent<TreeBase>() is { } tree) { max = tree.m_health; tier = tree.m_minToolTier; }
        else if (t.Root.GetComponent<Destructible>() is { } d) { max = d.m_health; tier = d.m_minToolTier; }
        else if (t.Root.GetComponent<TreeLog>() is { } log) { max = log.m_health; tier = log.m_minToolTier; }
        else return null;
        var cur = zdo.GetFloat("health".GetStableHashCode(), max);
        var s = $"{cur:0}/{max:0}";
        if (tier > 0) s += $"  ·  tool tier {tier}";
        return s;
    }

    private static string? ShipLine(Target t)
    {
        var ship = t.Root.GetComponent<Ship>();
        if (ship == null)
            return null;
        return $"speed {ship.GetSpeed():0.0}";
    }

    private static string? PartsLine(Target t)
    {
        var names = new List<string>();
        foreach (var c in t.Root.GetComponents<Component>())
        {
            if (c == null || c is Transform)
                continue;
            var n = c.GetType().Name;
            if (n is "ZNetView" or "ZSyncTransform" or "Rigidbody" or "BoxCollider" or "CapsuleCollider" or "MeshCollider" or "SphereCollider" or "Animator" or "AudioSource")
                continue;
            names.Add(n);
            if (names.Count >= 10) { names.Add("…"); break; }
        }
        return names.Count > 0 ? string.Join(", ", names) : null;
    }

    /// <summary>Calls a game method by name with its default arguments, so a signature that changed never stops the readout.</summary>
    internal static object? Call(object target, string method, params object?[] args)
    {
        var m = AccessTools.Method(target.GetType(), method);
        if (m == null)
            return null;
        var ps = m.GetParameters();
        var full = new object?[ps.Length];
        for (var i = 0; i < ps.Length; i++)
            full[i] = i < args.Length ? args[i] : ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
        return m.Invoke(target, full);
    }
}

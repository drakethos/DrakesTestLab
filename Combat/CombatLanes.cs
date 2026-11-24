using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DrakesTestLab.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Combat;

/// <summary>
/// Spike: several fights at once. Besides the local player, extra bot players ("lanes") stand in the arena, each with the
/// same loadout and its own foes, so one fight yields several runs. Each lane player is a real Player object owned by
/// this game, run through the game's own Player.FixedUpdate (stats, attacks, dodges) with Player.m_localPlayer pointed at
/// it for that call; the game would otherwise delete an owned player that is not the local one. Each lane's foes target
/// only their own lane player. Dormant: every patch below returns at once unless lanes exist.
/// </summary>
internal sealed class CombatLane
{
    public int Index;
    public Player Player = null!;
    public List<Character> Foes = new();
    public RunRecord Run = null!;
    public CombatBot BotDriver = null!;
    public Loadout? Loadout;            // mixed lanes: this lane's own loadout (else the scenario's)
    public List<FoeSpec>? FoeSpecs;     // ... its own foes
    public string Bot = "";             // ... its own bot
    public int Mix = -1;                // mixed lanes: which setup this lane fights with
    public bool Opponent;
    public bool Teammate;               // team fight: this player fights the local player's foes alongside them               // PvP: this lane player is the local player's foe, not a lane of its own
    public Vector3 ZoneCenter;          // each lane fights inside its own circle of the arena
    public float ZoneRadius;
    public bool Done;
    public bool Died;
    public float DiedAt;
}

internal static class CombatLanes
{
    public static readonly List<CombatLane> All = new();
    private static readonly Dictionary<Character, Character> FoeOwner = new();
    private static readonly HashSet<Player> LanePlayers = new();

    public static bool Any => LanePlayers.Count > 0;

    // team fights: these foes pick the nearest living team member (never a fallen one)
    public static readonly HashSet<Character> TeamFoes = new();
    public static Func<IEnumerable<Character>>? TeamAlive;

    public static Character? NearestAlive(Character foe)
    {
        Character? best = null;
        var bestD = float.MaxValue;
        foreach (var c in TeamAlive?.Invoke() ?? Enumerable.Empty<Character>())
        {
            if (c == null) continue;
            var d = (c.transform.position - foe.transform.position).sqrMagnitude;
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }
    public static bool IsLanePlayer(Character? c) => c is Player p && LanePlayers.Contains(p);
    public static Character? OwnerOf(Character foe) => FoeOwner.TryGetValue(foe, out var o) ? o : null;

    /// <summary>A lane player: the Player prefab, owned here, with a name and an empty inventory.</summary>
    public static Player Spawn(int index, Vector3 pos, Quaternion rot, WardTools.Identity? who = null)
    {
        var prefab = ZNetScene.instance.GetPrefab("Player") ?? throw new InvalidOperationException("no Player prefab");
        if (ZoneSystem.instance != null) pos.y = ZoneSystem.instance.GetGroundHeight(pos);
        var go = UnityEngine.Object.Instantiate(prefab, pos, rot);
        var p = go.GetComponent<Player>();
        LanePlayers.Add(p);
        p.SetPlayerID(who?.Id ?? 900000L + index, who?.Name ?? "Lane " + index);
        if (who != null)
        {
            // her own look (the roster's model, hair and colours)
            var vis = p.GetComponent<VisEquipment>();
            if (vis != null)
            {
                if (who.Model >= 0) vis.SetModel(who.Model);
                if (who.Hair.Length > 0)
                {
                    // the live game takes the hair's hash, older ones (and the stub refs) the name: call whichever exists
                    var m = AccessTools.Method(typeof(VisEquipment), "SetHairItem");
                    var arg = m?.GetParameters()[0].ParameterType == typeof(int) ? (object)who.Hair.GetStableHashCode() : who.Hair;
                    try { m?.Invoke(vis, new[] { arg }); } catch (Exception ex) { TestLabPlugin.Log?.LogDebug("Combat lanes: hair: " + ex.Message); }
                }
                if (who.HairColor is { } hc) vis.SetHairColor(hc);
                if (who.SkinColor is { } sc) vis.SetSkinColor(sc);
            }
        }
        p.GetInventory().RemoveAll();
        return p;
    }

    /// <summary>
    /// Lane zones: <paramref name="n"/> circles around the arena centre, far enough apart that no fight reaches the next.
    /// Each zone: its centre, its radius, the fighter's spot and facing (outwards), so the foe spawns further out.
    /// </summary>
    public static List<(Vector3 center, float radius, Vector3 spot, Quaternion rot)> Zones(Vector3 arenaCenter, Quaternion baseRot, int n, float arenaRadius)
    {
        var list = new List<(Vector3, float, Vector3, Quaternion)>();
        var ring = Mathf.Min(13f, arenaRadius * 0.5f);                       // zone centres on this circle
        var radius = Mathf.Min(10f, ring * Mathf.Sin(Mathf.PI / n) - 1.5f);  // half the gap to the next centre, less a margin
        radius = Mathf.Min(radius, arenaRadius - 1.5f - ring);               // and inside the ring of stakes
        for (var k = 0; k < n; k++)
        {
            var rot = baseRot * Quaternion.Euler(0f, k * 360f / n, 0f);
            var dir = rot * Vector3.forward;
            var center = arenaCenter + dir * ring;
            var spot = center - dir * Mathf.Min(4f, radius * 0.45f);
            if (ZoneSystem.instance != null) { center.y = ZoneSystem.instance.GetGroundHeight(center); spot.y = ZoneSystem.instance.GetGroundHeight(spot); }
            list.Add((center, radius, spot, rot));
        }
        return list;
    }

    public static void Lock(Character foe, Character owner) => FoeOwner[foe] = owner;

    public static void Unlock(Character foe) => FoeOwner.Remove(foe);

    /// <summary>Removes every lane player and foe. Safe to call more than once.</summary>
    public static void Clear()
    {
        foreach (var l in All)
        {
            l.BotDriver?.Release();
            foreach (var f in l.Foes)
                if (f != null) UnityEngine.Object.Destroy(f.gameObject);
        }
        foreach (var p in LanePlayers)
            if (p != null) ZNetScene.instance?.Destroy(p.gameObject);
        All.Clear();
        FoeOwner.Clear();
        TeamFoes.Clear();
        TeamAlive = null;
        LanePlayers.Clear();
    }

    public static void NoteDeath(Character c)
    {
        foreach (var l in All)
            if (l.Player == c && !l.Died) { l.Died = true; l.DiedAt = CombatRecorder.Now; }
    }

    /// <summary>Gives and equips the loadout's items on a lane player (the local player's path goes through ItemGiver).</summary>
    public static void Equip(Player p, Loadout l)
    {
        var inv = p.GetInventory();
        foreach (var item in l.Items)
        {
            var prefab = ObjectDB.instance?.GetItemPrefab(item.Prefab);
            var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            var added = false;
            if (drop != null)
            {
                // the same way ItemGiver makes an item for the local player
                var data = drop.m_itemData.Clone();
                data.m_dropPrefab = prefab;
                data.m_stack = Math.Max(1, Math.Min(item.Count, Math.Max(1, data.m_shared.m_maxStackSize)));
                data.m_quality = Math.Max(1, item.Quality);
                data.m_durability = data.GetMaxDurability();
                added = inv.AddItem(data);
            }
            if (!added)
                TestLabPlugin.Log?.LogWarning($"Combat lanes: could not give '{item.Prefab}' to {p.GetPlayerName()}.");
        }
        CombatPrep.SetSkills(p, l);
        EquipMissing(p, l);
    }

    public static void EquipMissing(Player p, Loadout l)
    {
        WithLocal(p, () =>
        {
            foreach (var it in p.GetInventory().GetAllItems().ToList())
                if (!it.m_equipped && l.Items.Any(x => x.Equip && x.Prefab == it.m_dropPrefab?.name))
                    p.EquipItem(it, false);
        });
    }

    public static bool GearReady(Player p, Loadout l)
    {
        var items = p.GetInventory().GetAllItems();
        return l.Items.Where(w => w.Equip).All(w => items.Any(it => it.m_dropPrefab != null && it.m_dropPrefab.name == w.Prefab && it.m_equipped));
    }

    private static readonly FieldInfo? StaminaField = AccessTools.Field(typeof(Player), "m_stamina");

    /// <summary>Lane players eat the same meals as the local player (when the loadout has meals), else get the same pinned numbers.</summary>
    public static void Fill(Player p, IList<string> meals, float hp, float stamina)
    {
        if (meals.Count > 0) { WithLocal(p, () => CombatPrep.Feed(p, meals)); return; }
        if (hp > 0f) p.SetMaxHealth(hp, false);
        if (stamina > 0f) p.SetMaxStamina(stamina, false);
        p.SetHealth(p.GetMaxHealth());
        StaminaField?.SetValue(p, p.GetMaxStamina());
    }

    /// <summary>Runs <paramref name="a"/> with Player.m_localPlayer pointing at <paramref name="p"/>.</summary>
    public static void WithLocal(Player p, Action a)
    {
        var before = Player.m_localPlayer;
        Player.m_localPlayer = p;
        try { a(); }
        finally { Player.m_localPlayer = before; }
    }
}

/// <summary>Lane players run the game's own owner update with themselves as the local player (else the game deletes them).</summary>
[HarmonyPatch(typeof(Player), "FixedUpdate")]
internal static class CombatLanes_Player_FixedUpdate
{
    private static void Prefix(Player __instance, out Player? __state)
    {
        __state = null;
        if (!CombatLanes.Any || !CombatLanes.IsLanePlayer(__instance))
            return;
        __state = Player.m_localPlayer;
        Player.m_localPlayer = __instance;
    }

    private static Exception? Finalizer(Exception? __exception, Player? __state)
    {
        if (__state != null)
            Player.m_localPlayer = __state;
        return __exception;
    }
}

/// <summary>Player.Update is keyboard input and hover text: lane players skip it.</summary>
[HarmonyPatch(typeof(Player), "Update")]
internal static class CombatLanes_Player_Update
{
    private static bool Prefix(Player __instance) => !CombatLanes.Any || !CombatLanes.IsLanePlayer(__instance);
}

/// <summary>A lane's foes only ever pick their own lane player as the enemy.</summary>
[HarmonyPatch(typeof(BaseAI), "FindEnemy")]
internal static class CombatLanes_BaseAI_FindEnemy
{
    private static bool Prefix(BaseAI __instance, ref Character? __result)
    {
        if (!CombatLanes.Any)
            return true;
        var self = __instance.GetComponent<Character>();
        if (self != null && CombatLanes.TeamFoes.Contains(self))
        {
            __result = CombatLanes.NearestAlive(self);
            return false;
        }
        var owner = self != null ? CombatLanes.OwnerOf(self) : null;
        if (owner == null)
            return true;
        __result = owner.IsDead() ? null : owner;
        return false;
    }
}

/// <summary>... and never switch to anyone else (a hit from another lane, a passing player).</summary>
[HarmonyPatch(typeof(MonsterAI), "SetTarget", typeof(Character))]
internal static class CombatLanes_MonsterAI_SetTarget
{
    private static void Prefix(MonsterAI __instance, ref Character attacker)
    {
        if (!CombatLanes.Any || attacker == null)
            return;
        var self = __instance.GetComponent<Character>();
        if (self != null && CombatLanes.TeamFoes.Contains(self))
        {
            // a fallen team member is out of the fight: switch to the nearest one still standing
            var alive = CombatLanes.TeamAlive?.Invoke();
            if (alive != null && !alive.Contains(attacker)) attacker = CombatLanes.NearestAlive(self) ?? attacker;
            return;
        }
        var owner = self != null ? CombatLanes.OwnerOf(self) : null;
        if (owner != null && attacker != owner)
            attacker = owner;
    }
}

/// <summary>
/// Valheim makes creatures tougher for every player within 200 m (Game.GetPlayerDifficulty: more health scaling and more
/// damage per extra player). Lanes are separate solo fights side by side, so the bot players of the other lanes must not
/// count: during a combat fight the lab says how many players the fight really has (1 per lane; the team size in a team
/// fight, where vanilla scaling is the point). Does nothing outside a combat fight.
/// </summary>
[HarmonyPatch(typeof(Game), "GetPlayerDifficulty")]
internal static class CombatLanes_Game_GetPlayerDifficulty
{
    private static bool Prefix(ref int __result)
    {
        if (!CombatRunner.Fighting || CombatRunner.DifficultyPlayers <= 0)
            return true;
        __result = CombatRunner.DifficultyPlayers;
        return false;
    }
}

/// <summary>
/// Sleepers (Morgen and others) wake to player noise, and lane players make none (their Player.Update is skipped), so a
/// sleeping foe in a lane or a team fight never woke up and the fight timed out. While a combat fight runs, foes are woken
/// at once and do not fall asleep again: the fight starts where a real player's fight starts, with the monster up.
/// </summary>
[HarmonyPatch(typeof(MonsterAI), "UpdateSleep")]
internal static class CombatLanes_MonsterAI_UpdateSleep
{
    private static readonly MethodInfo? Wakeup = AccessTools.Method(typeof(MonsterAI), "Wakeup");

    private static bool Prefix(MonsterAI __instance)
    {
        if (!CombatRunner.Fighting) return true;
        if (__instance.IsSleeping()) Wakeup?.Invoke(__instance, null);
        return false;
    }
}

/// <summary>
/// Explosions and heavy attacks dig the ground (Lava Blob blasts, Morgen and Charred hits carry pickaxe damage). Over a
/// batch of fights the craters piled up in the arena until a player stood 6 m below its foe and neither side could hit:
/// every fight after the first Lava Blob batch stalled. While a combat fight runs, the arena's ground is left as it is.
/// </summary>
[HarmonyPatch(typeof(TerrainComp), "InternalDoOperation")]
internal static class CombatLanes_TerrainComp_InternalDoOperation
{
    private static bool Prefix(Vector3 pos)
    {
        if (!CombatRunner.Fighting || !CombatArena.Built) return true;
        var off = pos - CombatArena.Center;
        off.y = 0f;
        return off.magnitude > CombatArena.Radius + 12f;
    }
}

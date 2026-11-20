using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Automation;

/// <summary>
/// PvP between you (player 1) and a virtual player 2, through the game's own rules:
///  - the PvP flag is the "pvp" value on each character's ZDO (Player.SetPVP); a hit from a player is dropped by the
///    receiving character when its owner has PvP off (Character.RPC_Damage).
///  - you attack with the real attack code (Humanoid.StartAttack with your weapon): the hit is found by the game's own
///    melee/ranged checks and sent to player 2's client, which applies it (her proxy runs Character.RPC_Damage).
///  - player 2 attacks by sending what her client's attack would send: RPC_Damage with a HitData (attacker = her
///    character, damage from a weapon prefab or given numbers) to the owner of what she hits.
/// Steps: pvp, peerPvp, attack, peerAttack, peerHealth. Checks: pvp, health.
/// Player 2 never really dies here: her death would run on this game (respawning you); it is recorded instead (Downed).
/// </summary>
internal static class PvpSteps
{
    public static readonly string[] StepNames = { "pvp", "peerPvp", "attack", "peerAttack", "peerHealth" };
    public static readonly string[] CheckNames = { "pvp", "health" };

    private static Player Me => Player.m_localPlayer ?? throw new StepFailed("no player");

    // ---- PvP flag ----

    public static string SetMine(bool on)
    {
        Me.SetPVP(on);
        return $"your PvP {(on ? "on" : "off")}";
    }

    public static string SetHers(VirtualPeer vp, bool on)
    {
        var zdo = vp.PuppetZdo ?? throw new StepFailed($"{vp.Name} has no character (peerJoin with \"puppet\": true)");
        zdo.Set(ZDOVars.s_pvp, on);
        return $"{vp.Name}'s PvP {(on ? "on" : "off")}";
    }

    // ---- you attack ----

    /// <summary>
    /// Turns you to face the target and swings your weapon with the game's attack code ("weapon": a prefab to give and
    /// equip first; "secondary": the secondary attack). Waits for the swing to finish and the hit to arrive.
    /// </summary>
    public static IEnumerator Attack(Dictionary<string, object?> step, RunContext ctx, GameObject target)
    {
        var me = Me;
        var weapon = step.Str("weapon");
        if (weapon.Length > 0)
            EquipWeapon(me, weapon);
        if (me.GetCurrentWeapon() == null)
            throw new StepFailed("attack: nothing to attack with (give a weapon, or \"weapon\": \"SwordBronze\")");
        var to = target.transform.position - me.transform.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.0001f)
        {
            var rot = Quaternion.LookRotation(to);
            me.transform.rotation = rot;
            me.SetLookDir(to.normalized);
        }
        var character = target.GetComponent<Character>();
        // a frame for the turn to settle before the swing
        yield return null;
        if (!me.StartAttack(character, step.Bool("secondary")))
            throw new StepFailed("attack: the game refused to start the attack (busy, no stamina, or the weapon has no such attack)");
        ctx.Log($"  you attack {Readout(target)} with {me.GetCurrentWeapon()?.m_shared.m_name}");
        // "finish": false returns mid-swing (after "wait" seconds), so a check can catch the animation while it plays
        if (!step.Bool("finish", true))
        {
            var mid = Time.realtimeSinceStartup + step.Num("wait", 0.25f);
            while (Time.realtimeSinceStartup < mid && !ctx.TimedOut)
                yield return null;
            yield break;
        }
        var until = Time.realtimeSinceStartup + step.Num("timeout", 4f);
        yield return null;
        while (me.InAttack() && Time.realtimeSinceStartup < until && !ctx.TimedOut)
            yield return null;
        // the hit goes to the target's client and back
        var settle = Time.realtimeSinceStartup + step.Num("wait", 0.3f);
        while (Time.realtimeSinceStartup < settle && !ctx.TimedOut)
            yield return null;
    }

    private static void EquipWeapon(Player me, string prefab)
    {
        var inv = me.GetInventory();
        ItemDrop.ItemData? item = null;
        foreach (var i in inv.GetAllItems())
            if (i.m_dropPrefab != null && i.m_dropPrefab.name == prefab)
                item = i;
        if (item == null)
        {
            var go = ObjectDB.instance?.GetItemPrefab(prefab) ?? throw new StepFailed($"attack: no item prefab '{prefab}'");
            if (!inv.AddItem(go, 1))
                throw new StepFailed($"attack: '{prefab}' does not fit in your inventory");
            foreach (var i in inv.GetAllItems())
                if (i.m_dropPrefab != null && i.m_dropPrefab.name == prefab)
                    item = i;
            if (item == null)
                throw new StepFailed($"attack: '{prefab}' was added but not found in your inventory");
        }
        if (!item.m_equipped)
            me.EquipItem(item, false);
    }

    private static string Readout(GameObject go) => go.GetComponent<Player>() is { } p ? p.GetPlayerName() : go.name.Replace("(Clone)", "");

    // ---- player 2 attacks ----

    /// <summary>
    /// What her client's attack sends when it lands: RPC_Damage to the owner of what she hits. "target": me (default), a
    /// tag or another virtual player; "weapon": an item prefab whose damage she deals, or "damage" + "type" (slash, blunt,
    /// pierce, chop, pickaxe, fire, frost, lightning, poison, spirit); "ignorePvp" (a hit that bypasses the PvP rule).
    /// </summary>
    public static string PeerAttack(VirtualPeer vp, Dictionary<string, object?> step, GameObject target)
    {
        var self = vp.Puppet ?? throw new StepFailed($"{vp.Name}'s character is not in the world");
        var view = target.GetComponent<ZNetView>();
        var zdo = view != null && view.IsValid() ? view.GetZDO() : throw new StepFailed("peerAttack: the target is not a networked object");
        var hit = new HitData
        {
            m_attacker = vp.PuppetZdo!.m_uid,
            m_hitType = HitData.HitType.PlayerHit,
            m_ignorePVP = step.Bool("ignorePvp"),
            m_toolTier = (short)step.Num("toolTier", 0f),
            m_pushForce = step.Num("push", 0f)
        };
        var weapon = step.Str("weapon");
        string what;
        if (weapon.Length > 0)
        {
            var go = ObjectDB.instance?.GetItemPrefab(weapon) ?? throw new StepFailed($"peerAttack: no item prefab '{weapon}'");
            var shared = go.GetComponent<ItemDrop>().m_itemData.m_shared;
            hit.m_damage = shared.m_damages;
            hit.m_skill = shared.m_skillType;
            hit.m_toolTier = (short)shared.m_toolTier;
            hit.m_pushForce = shared.m_attackForce;
            what = weapon;
        }
        else
        {
            var amount = step.Num("damage", 10f);
            var type = step.Str("type", "slash").ToLowerInvariant();
            switch (type)
            {
                case "slash": hit.m_damage.m_slash = amount; break;
                case "blunt": hit.m_damage.m_blunt = amount; break;
                case "pierce": hit.m_damage.m_pierce = amount; break;
                case "chop": hit.m_damage.m_chop = amount; break;
                case "pickaxe": hit.m_damage.m_pickaxe = amount; break;
                case "fire": hit.m_damage.m_fire = amount; break;
                case "frost": hit.m_damage.m_frost = amount; break;
                case "lightning": hit.m_damage.m_lightning = amount; break;
                case "poison": hit.m_damage.m_poison = amount; break;
                case "spirit": hit.m_damage.m_spirit = amount; break;
                default: throw new StepFailed($"peerAttack: unknown damage type '{type}'");
            }
            what = $"{amount.ToString("0.#", CultureInfo.InvariantCulture)} {type}";
        }
        var ch = target.GetComponent<Character>();
        var center = ch != null ? ch.GetCenterPoint() : target.transform.position + Vector3.up;
        hit.m_point = center;
        var dir = center - (self.transform.position + Vector3.up);
        hit.m_dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : self.transform.forward;
        var owner = zdo.GetOwner();
        if (owner == vp.Uid)
            throw new StepFailed($"peerAttack: {vp.Name} owns the target herself: her own client would apply it, nothing reaches you");
        vp.SendRoutedRpc(owner != 0L ? owner : ZDOMan.GetSessionID(), zdo.m_uid, "RPC_Damage", new object[] { hit });
        return $"hits {Readout(target)} for {what}{(hit.m_ignorePVP ? " (ignoring PvP)" : "")}";
    }

    /// <summary>Her health and max health, as her client keeps them on her character's ZDO.</summary>
    public static string SetHealth(VirtualPeer vp, Dictionary<string, object?> step)
    {
        var zdo = vp.PuppetZdo ?? throw new StepFailed($"{vp.Name} has no character");
        if (step.Has("max"))
            zdo.Set(ZDOVars.s_maxHealth, step.Num("max"));
        var max = zdo.GetFloat(ZDOVars.s_maxHealth, 25f);
        var hp = step.Has("health") ? step.Num("health") : max;
        zdo.Set(ZDOVars.s_health, hp);
        vp.Downed = false;
        return $"{vp.Name} health {hp:0}/{max:0}";
    }

    /// <summary>
    /// Her client would die the frame her health reaches 0 (Character.CustomFixedUpdate runs OnDeath for the owner, and
    /// she is the owner): record it, once, since no client of hers runs here.
    /// </summary>
    public static void WatchDeaths()
    {
        foreach (var vp in VirtualPeers.All)
        {
            if (vp.Downed || vp.PuppetZdo == null || vp.Puppet == null)
                continue;
            if (vp.PuppetZdo.GetFloat(ZDOVars.s_health, 1f) > 0f)
                continue;
            vp.Downed = true;
            TestLabPlugin.Log?.LogInfo($"Virtual player {vp.Name} was killed (recorded, not played out: her death would run on this game).");
        }
    }

    // ---- checks ----

    private static Character Who(string who)
    {
        if (who.Length == 0 || who.Equals("me", StringComparison.OrdinalIgnoreCase))
            return Me;
        var vp = VirtualPeers.Find(who) ?? throw new StepFailed($"no virtual player '{who}'");
        var go = vp.Puppet ?? throw new StepFailed($"{vp.Name}'s character is not in the world");
        return go.GetComponent<Character>();
    }

    public static (bool, string) PvpCheck(Dictionary<string, object?> a)
    {
        var who = a.First("who", "peer");
        var c = Who(who);
        var on = c is Player p && p.IsPVPEnabled();
        var want = a.Bool("expect", true);
        return (on == want, $"{(who.Length == 0 ? "your" : who + "'s")} PvP is {(on ? "on" : "off")}");
    }

    /// <summary>"who"; "expect" / "min" / "max" in HP, or "hurt": true (below max) / false (at max), or "downed" (player 2 only).</summary>
    public static (bool, string) HealthCheck(Dictionary<string, object?> a)
    {
        var who = a.First("who", "peer");
        var c = Who(who);
        var hp = c.GetHealth();
        var max = c.GetMaxHealth();
        var detail = $"{(who.Length == 0 ? "you" : who)}: {hp:0.#}/{max:0.#} HP";
        if (a.Has("downed"))
        {
            var vp = VirtualPeers.Find(who) ?? throw new StepFailed("\"downed\" is for a virtual player");
            return (vp.Downed == a.Bool("downed"), detail + (vp.Downed ? " (downed)" : ""));
        }
        if (a.Has("hurt"))
            return ((hp < max - 0.01f) == a.Bool("hurt"), detail);
        var ok = true;
        if (a.Has("min")) ok &= hp >= a.Num("min");
        if (a.Has("max")) ok &= hp <= a.Num("max");
        if (a.Has("expect") && !a.Has("min") && !a.Has("max")) ok &= Mathf.Abs(hp - a.Num("expect")) <= a.Num("tolerance", 0.5f);
        return (ok, detail);
    }
}

/// <summary>
/// Player 2's death would run here, on this game (tombstone, respawn: the local game's, i.e. yours). Record it instead and
/// leave her standing at 0 HP; peerHealth brings her back.
/// </summary>
[HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
internal static class PvpSteps_PuppetDeath_Patch
{
    private static bool Prefix(Player __instance)
    {
        foreach (var vp in VirtualPeers.All)
        {
            if (vp.Puppet == null || vp.Puppet != __instance.gameObject)
                continue;
            vp.Downed = true;
            TestLabPlugin.Log?.LogInfo($"Virtual player {vp.Name} was killed (recorded, not played out: her death would run on this game).");
            return false;
        }
        return true;
    }
}

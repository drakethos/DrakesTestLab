using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Combat;

/// <summary>
/// Collects what happens in one combat run: every hit (raw and final damage, blocks), swings and consumables.
/// Dormant: the Harmony patches below return at once unless <see cref="Active"/> is true, so a game that never runs a
/// combat scenario pays nothing. It never changes what the game does.
/// </summary>
internal static class CombatRecorder
{
    public static bool Active { get; private set; }
    public static RunRecord? Current { get; private set; }

    private static float _start;
    private static readonly HashSet<Character> Gear = new();
    private static readonly Dictionary<Character, RunRecord> RunOf = new();   // lanes: which run a character's hits belong to

    /// <summary>Starts recording into <paramref name="run"/>. <paramref name="gearSide"/> are the characters being tested (the rest are foes).</summary>
    public static void Begin(RunRecord run, IEnumerable<Character> gearSide)
    {
        Gear.Clear();
        foreach (var c in gearSide)
            if (c != null) Gear.Add(c);
        Current = run;
        RunOf.Clear();
        _start = Time.time;
        Active = true;
    }

    /// <summary>Lanes: another gear character and its foes, recorded into their own run.</summary>
    public static void AddLane(RunRecord run, Character gear, IEnumerable<Character> foes)
    {
        Gear.Add(gear);
        RunOf[gear] = run;
        foreach (var f in foes)
            if (f != null) RunOf[f] = run;
    }

    private static RunRecord? RunFor(Character? a, Character? b = null)
    {
        if (a != null && RunOf.TryGetValue(a, out var r)) return r;
        if (b != null && RunOf.TryGetValue(b, out r)) return r;
        return Current;
    }

    public static void End()
    {
        Active = false;
        RunOf.Clear();
        Gear.Clear();
        Current = null;
    }

    public static bool IsGear(Character? c) => c != null && Gear.Contains(c);

    public static float Now => Time.time - _start;

    internal static void OnHit(Character victim, HitData hit, float rawBefore, float healthBefore, bool blocked, bool backstab = false, bool alertedBefore = false)
    {
        if (Current == null || victim == null)
            return;
        var after = victim.GetHealth();
        var final = Mathf.Max(0f, healthBefore - after);
        var attacker = hit.GetAttacker();
        var run = RunFor(victim, attacker)!;
        var h = new HitRecord
        {
            T = Now,
            Attacker = attacker != null ? NameOf(attacker) : "?",
            Victim = NameOf(victim),
            VictimIsGear = IsGear(victim),
            Raw = rawBefore,
            Final = final,
            Types = Types(hit.m_damage),
            Blocked = blocked,
            Backstab = backstab,
            HpAfter = after
        };
        run.Hits.Add(h);
        if (attacker != null) CombatBot.NoteHit(victim, attacker, final, blocked);
        if (h.VictimIsGear)
        {
            run.DamageTaken += final;
            if (blocked) run.Blocks++;
            if (blocked && CombatRecorder_Block.LastParried == victim) run.Parries++;
        }
        else if (attacker != null && IsGear(attacker))
        {
            run.DamageDealt += final;
            if (backstab) run.Backstabs++;
            if (run.FirstHit.Length == 0)
                run.FirstHit = $"first hit {final:0} dmg, foe alerted before it {alertedBefore}, weapon backstab x{hit.m_backstabBonus:0.#}, backstab {backstab}";
        }
    }

    /// <summary>Damage over time (poison, burning, ...): status effects call Character.ApplyDamage directly, never RPC_Damage.</summary>
    internal static void OnTick(Character victim, HitData hit, float healthBefore)
    {
        if (Current == null || victim == null)
            return;
        var after = victim.GetHealth();
        var final = Mathf.Max(0f, healthBefore - after);
        if (final <= 0f) return;
        var run = RunFor(victim)!;
        var gear = IsGear(victim);
        run.Hits.Add(new HitRecord
        {
            T = Now, Attacker = hit.m_hitType.ToString().ToLowerInvariant(), Victim = NameOf(victim), VictimIsGear = gear,
            Raw = hit.GetTotalDamage(), Final = final, Types = Types(hit.m_damage), HpAfter = after
        });
        if (gear) run.DamageTaken += final;
        else run.DamageDealt += final;
    }

    internal static void OnSwing(Character who)
    {
        if (Current != null && IsGear(who)) RunFor(who)!.Swings++;
    }

    internal static void OnConsume(Character who)
    {
        if (Current != null && IsGear(who)) RunFor(who)!.Consumables++;
    }

    private static string NameOf(Character c) => c is Player p ? p.GetPlayerName() : c.name.Replace("(Clone)", "");

    private static string Types(HitData.DamageTypes d)
    {
        var sb = new StringBuilder();
        void Add(string n, float v)
        {
            if (v <= 0.01f) return;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(n).Append(' ').Append(Mathf.RoundToInt(v));
        }

        Add("blunt", d.m_blunt); Add("slash", d.m_slash); Add("pierce", d.m_pierce); Add("chop", d.m_chop); Add("pickaxe", d.m_pickaxe);
        Add("fire", d.m_fire); Add("frost", d.m_frost); Add("lightning", d.m_lightning); Add("poison", d.m_poison); Add("spirit", d.m_spirit);
        return sb.ToString();
    }
}

/// <summary>
/// Final damage per hit: health before and after the game's own damage code (armor, resistances, blocking, parry), so what is
/// recorded is what the victim really lost. Character.RPC_Damage is where every hit lands, whoever owns the victim.
/// </summary>
[HarmonyPatch]
internal static class CombatRecorder_RpcDamage
{
    private static MethodBase? TargetMethod() => AccessTools.Method(typeof(Character), "RPC_Damage");

    private static bool Prepare() => TargetMethod() != null;

    private static readonly FieldInfo? BackstabTime = AccessTools.Field(typeof(Character), "m_backstabTime");
    private static bool _alertedBefore;
    /// <summary>Inside RPC_Damage: its own ApplyDamage call is a hit recorded here, not a damage-over-time tick.</summary>
    internal static int Depth;

    private static void Finalizer() { if (Depth > 0) Depth--; }

    private static void Prefix(Character __instance, HitData hit, out (bool on, float raw, float hp, float bt) __state)
    {
        Depth++;
        __state = CombatRecorder.Active && hit != null
            ? (true, hit.GetTotalDamage(), __instance.GetHealth(), BackstabTime != null ? (float)BackstabTime.GetValue(__instance) : 0f)
            : (false, 0f, 0f, 0f);
        _alertedBefore = CombatRecorder.Active && __instance.GetComponent<BaseAI>() is { } ai && ai.IsAlerted();
    }

    private static void Postfix(Character __instance, HitData hit, (bool on, float raw, float hp, float bt) __state)
    {
        // the game stamps m_backstabTime when it applies a backstab: that is the real "was it a backstab"
        var backstab = __state.on && BackstabTime != null && (float)BackstabTime.GetValue(__instance) != __state.bt;
        if (__state.on)
            CombatRecorder.OnHit(__instance, hit, __state.raw, __state.hp, CombatRecorder_Block.LastBlocked == __instance, backstab, _alertedBefore);
        if (__state.on && CombatRecorder_Block.LastBlocked == __instance)
        {
            CombatRecorder_Block.LastBlocked = null;
            CombatRecorder_Block.LastParried = null;
        }
    }
}

/// <summary>Damage-over-time ticks (poison, burning): SE_Poison / SE_Burning call ApplyDamage without RPC_Damage.</summary>
[HarmonyPatch]
internal static class CombatRecorder_ApplyDamage
{
    private static MethodBase? TargetMethod() => AccessTools.Method(typeof(Character), "ApplyDamage");

    private static bool Prepare() => TargetMethod() != null;

    private static void Prefix(Character __instance, out float __state) =>
        __state = CombatRecorder.Active && CombatRecorder_RpcDamage.Depth == 0 ? __instance.GetHealth() : -1f;

    private static void Postfix(Character __instance, HitData hit, float __state)
    {
        if (__state >= 0f && hit != null)
            CombatRecorder.OnTick(__instance, hit, __state);
    }
}

/// <summary>Remembers which character just blocked a hit (Humanoid.BlockAttack runs inside RPC_Damage, before the postfix above).</summary>
[HarmonyPatch]
internal static class CombatRecorder_Block
{
    public static Character? LastBlocked;
    public static Character? LastParried;     // the block was raised under 0.25 s before the hit: Valheim's parry
    private static readonly FieldInfo? BlockTimer = AccessTools.Field(typeof(Humanoid), "m_blockTimer");

    private static MethodBase? TargetMethod() => AccessTools.Method(typeof(Humanoid), "BlockAttack");

    private static bool Prepare() => TargetMethod() != null;

    private static void Prefix(Humanoid __instance, out float __state) =>
        __state = CombatRecorder.Active && BlockTimer != null ? (float)BlockTimer.GetValue(__instance) : -1f;

    private static void Postfix(Humanoid __instance, bool __result, float __state)
    {
        if (!CombatRecorder.Active || !__result) return;
        LastBlocked = __instance;
        LastParried = __state >= 0f && __state < 0.25f ? __instance : null;
    }
}

[HarmonyPatch]
internal static class CombatRecorder_Swing
{
    private static MethodBase? TargetMethod() => AccessTools.Method(typeof(Humanoid), "StartAttack");

    private static bool Prepare() => TargetMethod() != null;

    private static void Postfix(Humanoid __instance, bool __result)
    {
        if (CombatRecorder.Active && __result)
            CombatRecorder.OnSwing(__instance);
    }
}

/// <summary>Food and potions the gear side uses mid-fight.</summary>
[HarmonyPatch]
internal static class CombatRecorder_Consume
{
    private static MethodBase? TargetMethod() => AccessTools.Method(typeof(Player), "ConsumeItem");

    private static bool Prepare() => TargetMethod() != null;

    private static void Postfix(Player __instance, bool __result)
    {
        if (CombatRecorder.Active && __result)
            CombatRecorder.OnConsume(__instance);
    }
}

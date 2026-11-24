using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Combat;

/// <summary>
/// Raw-data switches for a combat scenario (no stagger, no knockback, damage multipliers). Dormant: nothing changes unless
/// <see cref="Active"/> is set by the runner for the length of a fight, and every value defaults to real Valheim.
/// </summary>
internal static class CombatRules
{
    public static bool Active { get; private set; }
    public static string NoStagger = "none";     // none | foes | gear | all
    public static bool NoKnockback;
    public static float FoeDamage = 1f;
    public static float GearDamage = 1f;

    public static void Begin(ScenarioSpec sc)
    {
        NoStagger = sc.NoStagger;
        NoKnockback = sc.NoKnockback;
        FoeDamage = sc.FoeDamage;
        GearDamage = sc.GearDamage;
        Active = NoStagger != "none" || NoKnockback || !Mathf.Approximately(FoeDamage, 1f) || !Mathf.Approximately(GearDamage, 1f);
    }

    public static void End() => Active = false;

    /// <summary>Whether the no-stagger rule covers this character (the gear side, or a foe).</summary>
    public static bool NoStaggerFor(Character c)
    {
        if (!Active || NoStagger == "none") return false;
        return NoStagger == "all" || (NoStagger == "gear" ? CombatRecorder.IsGear(c) : NoStagger == "foes" && !CombatRecorder.IsGear(c));
    }
}

/// <summary>Scales damage, zeroes knockback and stagger on the hit before the game's own damage code runs.</summary>
[HarmonyPatch]
internal static class CombatRules_RpcDamage
{
    private static MethodBase? TargetMethod() => AccessTools.Method(typeof(Character), "RPC_Damage");

    private static bool Prepare() => TargetMethod() != null;

    [HarmonyPriority(Priority.First)]
    private static void Prefix(Character __instance, HitData hit)
    {
        if (!CombatRules.Active || hit == null || !CombatRecorder.Active)
            return;
        var victimIsGear = CombatRecorder.IsGear(__instance);
        var mult = victimIsGear ? CombatRules.FoeDamage : CombatRules.GearDamage;
        if (!Mathf.Approximately(mult, 1f))
            hit.m_damage.Modify(mult);
        if (CombatRules.NoKnockback)
            hit.m_pushForce = 0f;
        if (CombatRules.NoStaggerFor(__instance))
            hit.m_staggerMultiplier = 0f;
    }
}

/// <summary>Blocks and parries stagger through Character.Stagger directly; the rule covers those too.</summary>
[HarmonyPatch]
internal static class CombatRules_Stagger
{
    private static MethodBase? TargetMethod() => AccessTools.Method(typeof(Character), "Stagger");

    private static bool Prepare() => TargetMethod() != null;

    private static bool Prefix(Character __instance) => !CombatRules.NoStaggerFor(__instance);
}

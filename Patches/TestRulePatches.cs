using System;
using System.Reflection;
using DrakesTestLab.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Patches;

[HarmonyPatch(typeof(CraftingStation), "GetLevel")]
internal static class CraftingStation_GetLevel_Patch
{
    private static void Postfix(ref int __result)
    {
        if (TestRules.StationLevel > 0)
            __result = TestRules.StationLevel;
    }
}

/// <summary>Super stations ignore the roof / fire requirements too.</summary>
[HarmonyPatch(typeof(CraftingStation), "CheckUsable")]
internal static class CraftingStation_CheckUsable_Patch
{
    private static void Postfix(ref bool __result)
    {
        if (TestRules.StationLevel > 0)
            __result = true;
    }
}

[HarmonyPatch(typeof(Piece.Requirement), "GetAmount")]
internal static class Requirement_GetAmount_Patch
{
    private static void Postfix(ref int __result)
    {
        var mult = TestRules.CostMultiplier;
        if (Mathf.Approximately(mult, 1f) || __result <= 0)
            return;

        __result = mult <= 0f ? 0 : Mathf.Max(1, Mathf.RoundToInt(__result * mult));
    }
}

/// <summary>Swallows the unlock popups while the Discovery helper unlocks content in bulk.</summary>
[HarmonyPatch(typeof(MessageHud), "QueueUnlockMsg")]
internal static class MessageHud_QueueUnlockMsg_Patch
{
    private static bool Prefix() => !Discovery.Quiet;
}

[HarmonyPatch(typeof(Player), "GetMaxCarryWeight")]
internal static class Player_GetMaxCarryWeight_Patch
{
    private static void Postfix(ref float __result)
    {
        if (TestRules.MaxCarryWeight > 0f)
            __result = TestRules.MaxCarryWeight;
    }
}

[HarmonyPatch(typeof(Player), "SetMaxHealth", typeof(float), typeof(bool))]
internal static class Player_SetMaxHealth_Patch
{
    private static void Prefix(ref float health)
    {
        if (TestRules.MaxHealth > 0f)
            health = TestRules.MaxHealth;
    }
}

[HarmonyPatch(typeof(Player), "SetMaxStamina")]
internal static class Player_SetMaxStamina_Patch
{
    private static void Prefix(ref float stamina)
    {
        if (TestRules.MaxStamina > 0f)
            stamina = TestRules.MaxStamina;
    }
}

[HarmonyPatch(typeof(Player), "SetMaxEitr")]
internal static class Player_SetMaxEitr_Patch
{
    private static void Prefix(ref float eitr)
    {
        if (TestRules.MaxEitr > 0f)
            eitr = TestRules.MaxEitr;
    }
}

/// <summary>Lets the test lab shrink, grow or remove the vanilla no-build zones (spawn temple, altars...).</summary>
[HarmonyPatch(typeof(Location), "IsInsideNoBuildLocation")]
internal static class Location_IsInsideNoBuildLocation_Patch
{
    private static readonly FieldInfo? AllLocations = AccessTools.Field(typeof(Location), "s_allLocations");

    private static bool Prefix(Vector3 point, ref bool __result)
    {
        if (TestRules.NoBuild == NoBuildMode.Vanilla)
            return true;

        if (TestRules.NoBuild == NoBuildMode.Off || AllLocations?.GetValue(null) is not System.Collections.IEnumerable all)
        {
            __result = false;
            return false;
        }

        foreach (var obj in all)
        {
            if (obj is not Location loc || !loc.m_noBuild)
                continue;

            var radius = TestRules.NoBuildRadius > 0f
                ? TestRules.NoBuildRadius
                : (loc.m_noBuildRadiusOverride > 0f ? loc.m_noBuildRadiusOverride : loc.GetMaxRadius()) * TestRules.NoBuildScale;
            if (Utils.DistanceXZ(loc.transform.position, point) < radius)
            {
                __result = true;
                return false;
            }
        }

        __result = false;
        return false;
    }
}

/// <summary>Fall damage multiplier plus "you never get hurt by a height you can jump".</summary>
[HarmonyPatch(typeof(Character), "Damage")]
internal static class Character_Damage_Fall_Patch
{
    private static readonly FieldInfo? MaxAirAltitude = AccessTools.Field(typeof(Character), "m_maxAirAltitude");

    private static bool Prefix(Character __instance, HitData hit)
    {
        if (hit == null || __instance != Player.m_localPlayer || hit.m_hitType.ToString() != "Fall")
            return true;

        var safe = TestRules.SafeFallHeight();
        var mult = TestRules.FallDamageMultiplier;
        if (Mathf.Approximately(mult, 1f) && safe <= 4.001f)
            return true;

        var fell = Mathf.Max(0f, (MaxAirAltitude?.GetValue(__instance) is float f ? f : 0f) - __instance.transform.position.y);
        var vanilla = Mathf.Clamp01((fell - 4f) / 16f) * 100f;
        var wanted = Mathf.Clamp01((fell - safe) / 16f) * 100f * mult;
        var ratio = vanilla > 0.01f ? hit.m_damage.m_damage / vanilla : 1f; // keeps feather-fall style modifiers
        var damage = wanted * ratio;
        if (damage <= 0.01f)
            return false;

        hit.m_damage.m_damage = damage;
        return true;
    }
}

/// <summary>The "mystical force": keeps building inside (or outside) the test arena.</summary>
[HarmonyPatch(typeof(Player), "TryPlacePiece")]
internal static class Player_TryPlacePiece_Patch
{
    private static bool Prefix(Player __instance)
    {
        if (TestRules.BuildRule == BuildRule.Off || !TestRules.HasArena)
            return true;

        var ghost = AccessTools.Field(typeof(Player), "m_placementGhost")?.GetValue(__instance) as GameObject;
        var pos = ghost != null ? ghost.transform.position : __instance.transform.position;
        var inside = TestRules.InsideArena(pos);
        var blocked = TestRules.BuildRule == BuildRule.OnlyInsideArena ? !inside : inside;
        if (!blocked)
            return true;

        Notify(__instance, TestRules.BuildRule == BuildRule.OnlyInsideArena
            ? "A mystical force stops you building outside the test arena"
            : "A mystical force stops you building inside the test arena");
        return false;
    }

    // Character.Message gained a parameter in 1.0, so bind by name and pad the arguments.
    private static MethodInfo? FindMessage()
    {
        foreach (var m in typeof(Character).GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            var ps = m.GetParameters();
            if (m.Name == "Message" && ps.Length >= 2 && ps[1].ParameterType == typeof(string) && ps[0].ParameterType.IsEnum)
                return m;
        }

        return null;
    }

    private static void Notify(Player player, string text)
    {
        try
        {
            var MessageMethod = FindMessage();
            if (MessageMethod == null)
                return;

            var ps = MessageMethod.GetParameters();
            var args = new object?[ps.Length];
            args[0] = Enum.Parse(ps[0].ParameterType, "Center");
            args[1] = text;
            for (var i = 2; i < ps.Length; i++)
                args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : (ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null);

            MessageMethod.Invoke(player, args);
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Build-block message failed: {ex.Message}");
        }
    }
}

using UnityEngine;

namespace DrakesTestLab.Bootstrap;

internal enum NoBuildMode
{
    Vanilla,
    Off,
    Scaled
}

internal enum BuildRule
{
    Off,
    OnlyInsideArena,
    BlockInsideArena
}

/// <summary>Runtime test switches read by the Harmony patches. Set from the toolbox or from JSON test cases.</summary>
internal static class TestRules
{
    /// <summary>0 = vanilla. &gt;0 forces every crafting station to this level and ignores roof/fire requirements.</summary>
    public static int StationLevel;

    /// <summary>Multiplies every recipe/piece ingredient amount. 1 = vanilla, 0 = free.</summary>
    public static float CostMultiplier = 1f;

    /// <summary>0 = vanilla. &gt;0 replaces the player's max carry weight.</summary>
    public static float MaxCarryWeight;

    /// <summary>0 = vanilla. Keeps the max stat pinned even though food recalculates it.</summary>
    public static float MaxHealth;
    public static float MaxStamina;
    public static float MaxEitr;

    public static float RunSpeedMultiplier = 1f;
    public static float JumpMultiplier = 1f;

    /// <summary>The vanilla "a mystical force in this area stops you" zones (spawn temple, boss altars, ruins...).</summary>
    public static NoBuildMode NoBuild = NoBuildMode.Vanilla;

    /// <summary>Scaled mode: multiplies every no-build zone's radius (0.5 = half size, 2 = double).</summary>
    public static float NoBuildScale = 1f;

    /// <summary>Scaled mode: if above 0, every no-build zone uses exactly this radius in metres.</summary>
    public static float NoBuildRadius;

    /// <summary>1 = vanilla fall damage, 0 = none.</summary>
    public static float FallDamageMultiplier = 1f;

    /// <summary>With a raised jump height, falls up to roughly the height you can jump never hurt.</summary>
    public static bool FallAutoSafe = true;

    /// <summary>Extra metres of fall that never hurt, on top of vanilla (4 m) / the jump-based height.</summary>
    public static float FallSafeExtra;

    public static float SafeFallHeight()
    {
        var m = JumpMultiplier;
        var jumpBased = FallAutoSafe && m > 1f ? 2.2f * m * m : 0f;
        return Mathf.Max(4f, jumpBased) + FallSafeExtra;
    }

    public static void ApplyConfigDefaults()
    {
        var cfg = TestLabConfig.Instance;
        NoBuild = cfg.NoBuildZones.Value;
        NoBuildScale = cfg.NoBuildScale.Value;
        NoBuildRadius = cfg.NoBuildRadius.Value;
        FallDamageMultiplier = cfg.FallDamage.Value;
        FallAutoSafe = cfg.FallAutoSafe.Value;
        HideUnlockPopups = cfg.HideUnlockPopups.Value;
        HideTutorials = cfg.HideTutorials.Value;
        AutoDiscovery = cfg.AutoDiscovery.Value;
    }

    /// <summary>Hide "New crafting recipe / item / station" pop-ups (still recorded for the unlockShown check).</summary>
    public static bool HideUnlockPopups = true;

    /// <summary>Hide the raven (Hugin) tutorials (still recorded for the message check).</summary>
    public static bool HideTutorials = true;

    /// <summary>Off = picking items up does not teach recipes; only the toolbox / cases discover things.</summary>
    public static bool AutoDiscovery = true;

    public static BuildRule BuildRule = BuildRule.Off;
    public static Vector3 ArenaCenter;
    public static float ArenaRadius = 40f;
    public static bool HasArena;

    /// <summary>Where the player spawned when the arena was built around the spawn point.</summary>
    public static Vector3? SpawnPoint;

    public static bool InsideArena(Vector3 pos)
    {
        if (!HasArena)
            return false;

        var dx = pos.x - ArenaCenter.x;
        var dz = pos.z - ArenaCenter.z;
        return dx * dx + dz * dz <= ArenaRadius * ArenaRadius;
    }

    public static void ResetRules()
    {
        StationLevel = 0;
        CostMultiplier = 1f;
        MaxCarryWeight = 0f;
        MaxHealth = 0f;
        MaxStamina = 0f;
        MaxEitr = 0f;
        RunSpeedMultiplier = 1f;
        JumpMultiplier = 1f;
        BuildRule = BuildRule.Off;
        NoBuild = NoBuildMode.Vanilla;
        NoBuildScale = 1f;
        NoBuildRadius = 0f;
        FallDamageMultiplier = 1f;
        FallAutoSafe = true;
        FallSafeExtra = 0f;
    }
}

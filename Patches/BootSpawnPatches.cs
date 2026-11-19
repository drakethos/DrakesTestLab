using DrakesTestLab.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Patches;

/// <summary>
/// The test character is created fresh every launch (EphemeralFresh), so it has no logout point and no bed. For such a
/// character the game's Game.FindSpawnPoint only accepts the world's start temple (ZoneSystem.GetLocationIcon("StartTemple"))
/// and returns false until it finds one: in a world where the game cannot see a start temple the player never spawns and the
/// game sits on the loading screen forever (found on Linux, 2026-11-19: every run hung on "Starting respawn" behind the intro).
/// After a short wait with no temple, spawn at the world centre instead; the arena is built around wherever the player lands.
/// </summary>
[HarmonyPatch(typeof(Game), "FindSpawnPoint")]
internal static class Game_FindSpawnPoint_Fallback_Patch
{
    private const float GraceSeconds = 3f;
    private static float _waited;
    private static bool _logged;

    private static void Postfix(Game __instance, ref bool __result, ref Vector3 point, ref bool usedLogoutPoint, float dt)
    {
        if (__result)
        {
            _waited = 0f;
            return;
        }
        if (TestLabConfig.Instance == null || !TestLabConfig.Instance.SpawnFallback.Value)
            return;
        var zs = ZoneSystem.instance;
        var scene = ZNetScene.instance;
        var profile = __instance.GetPlayerProfile();
        if (zs == null || scene == null || ZNet.instance == null || profile == null)
            return;
        // a character with its own place to go, or a world with a temple, is the game's to handle
        if (profile.HaveLogoutPoint() || profile.HaveCustomSpawnPoint() || zs.GetLocationIcon(__instance.m_StartLocation, out _))
        {
            _waited = 0f;
            return;
        }
        _waited += dt;
        if (_waited < GraceSeconds)
            return;
        if (!_logged)
        {
            _logged = true;
            TestLabPlugin.Log?.LogWarning($"Spawn: the world has no '{__instance.m_StartLocation}' the game can find and the character has no saved spawn point; spawning at the world centre ([Bootstrap] SpawnFallback).");
        }
        var target = Vector3.zero;
        ZNet.instance.SetReferencePosition(target);
        if (!scene.IsAreaReady(target) || !zs.GetGroundHeight(target, out var height))
            return;
        point = new Vector3(0f, Mathf.Max(height, zs.m_waterLevel) + 0.5f, 0f);
        usedLogoutPoint = false;
        __result = true;
        _waited = 0f;
        TestLabPlugin.Log?.LogInfo($"Spawn: fallback spawn point {point}.");
    }
}

/// <summary>
/// [Bootstrap] SkipIntro used to skip the Valkyrie intro only from Player.OnSpawned, which runs after the intro has already
/// started, and never when the spawn itself is stuck. Drop the queued intro before Game.Update shows it.
/// </summary>
[HarmonyPatch(typeof(Game), "Update")]
internal static class Game_Update_SkipIntro_Patch
{
    private static readonly AccessTools.FieldRef<Game, bool> QueuedIntro = AccessTools.FieldRefAccess<Game, bool>("m_queuedIntro");

    private static void Prefix(Game __instance)
    {
        if (TestLabConfig.Instance == null || !TestLabConfig.Instance.SkipIntro.Value || !QueuedIntro(__instance))
            return;
        QueuedIntro(__instance) = false;
        TestLabPlugin.Log?.LogInfo("Intro skipped ([Bootstrap] SkipIntro).");
    }
}

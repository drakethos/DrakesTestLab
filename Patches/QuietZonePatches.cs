using System;
using DrakesTestLab.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Patches;

// The three places the game makes wild creatures. Inside the quiet zone they do nothing; everywhere else the creatures they
// make are flagged "wild" (the zone may remove those later if they wander in). Random events spawn through SpawnSystem too.

[HarmonyPatch(typeof(SpawnSystem), "Spawn")]
internal static class QuietZone_SpawnSystem_Patch
{
    private static bool Prefix(Vector3 spawnPoint, out bool __state)
    {
        __state = false;
        if (!QuietZone.AllowSpawn(spawnPoint))
            return false;
        QuietZone.NaturalDepth++;
        __state = true;
        return true;
    }

    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        if (__state)
            QuietZone.NaturalDepth--;
        return __exception;
    }
}

[HarmonyPatch(typeof(SpawnArea), "SpawnOne")]
internal static class QuietZone_SpawnArea_Patch
{
    private static bool Prefix(SpawnArea __instance, out bool __state)
    {
        __state = false;
        if (!QuietZone.AllowSpawn(__instance.transform.position))
            return false;
        QuietZone.NaturalDepth++;
        __state = true;
        return true;
    }

    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        if (__state)
            QuietZone.NaturalDepth--;
        return __exception;
    }
}

[HarmonyPatch(typeof(CreatureSpawner), "Spawn")]
internal static class QuietZone_CreatureSpawner_Patch
{
    private static bool Prefix(CreatureSpawner __instance, out bool __state)
    {
        __state = false;
        if (!QuietZone.AllowSpawn(__instance.transform.position))
            return false; // the game treats a null result as "did not spawn" and tries again later
        QuietZone.NaturalDepth++;
        __state = true;
        return true;
    }

    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        if (__state)
            QuietZone.NaturalDepth--;
        return __exception;
    }
}

/// <summary>A creature born while a natural spawner runs is wild.</summary>
[HarmonyPatch(typeof(Character), "Awake")]
internal static class QuietZone_Character_Awake_Patch
{
    private static void Postfix(Character __instance)
    {
        if (QuietZone.NaturalDepth > 0)
            QuietZone.MarkWild(__instance);
    }
}

// Random events: only the game's own timer is stopped. An event a test starts on purpose (the console's "event" and
// "randomevent" commands) goes through other methods and still works.

[HarmonyPatch(typeof(RandEventSystem), "UpdateRandomEvent")]
internal static class QuietZone_EventTimer_Patch
{
    private static void Prefix() => QuietZone.InEventTimer = true;

    private static Exception? Finalizer(Exception? __exception)
    {
        QuietZone.InEventTimer = false;
        return __exception;
    }
}

[HarmonyPatch(typeof(RandEventSystem), "StartRandomEvent")]
internal static class QuietZone_StartEvent_Patch
{
    private static bool Prefix() => !QuietZone.InEventTimer || QuietZone.AllowAutoEvent();
}

using DrakesTestLab.Bootstrap;
using DrakesTestLab.Commands;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Patches;

[HarmonyPatch(typeof(Player), "OnSpawned")]
internal static class Player_OnSpawned_Patch
{
    private static void Postfix(Player __instance)
    {
        if (__instance != Player.m_localPlayer)
            return;

        if (TestLabConfig.Instance.ApplyControlsOnWorldReady.Value)
            TestControlsApplier.Apply();

        PlayerItemRepair.RepairLocalPlayer();
        InitialConditionsApplier.ApplyIfReady();

        // the lab gear is built and placed once at ZNetScene.Awake, before any real player exists (ReadPlayerJoints
        // there reads ZNetScene's "Player" prefab template, not the spawned character); re-seat it now that the real
        // local player (and VisEquipment.m_helmet/Player.m_eye on it) actually exist - found in game, 2026-11-18, the
        // glasses sat using data read from that template the whole session and never visually appeared.
        Gear.LabGear.Reload();

        if (TestLabConfig.Instance.SkipIntro.Value && __instance.InIntro())
        {
            try
            {
                Game.instance?.SkipIntro();
            }
            catch
            {
                // SkipIntro may be unavailable during some load states.
            }
        }
    }
}

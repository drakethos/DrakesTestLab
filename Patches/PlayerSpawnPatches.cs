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

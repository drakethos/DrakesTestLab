using DrakesTestLab.Gear;
using HarmonyLib;

namespace DrakesTestLab.Patches;

// The lab gear items are registered without Jotunn: into ObjectDB (items, recipes) and ZNetScene (spawn, drop, sync).

[HarmonyPatch(typeof(ObjectDB), "Awake")]
internal static class LabGear_ObjectDB_Awake_Patch
{
    private static void Postfix(ObjectDB __instance) => LabGear.RegisterIn(__instance);
}

[HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
internal static class LabGear_ObjectDB_CopyOtherDB_Patch
{
    private static void Postfix(ObjectDB __instance) => LabGear.RegisterIn(__instance);
}

[HarmonyPatch(typeof(ZNetScene), "Awake")]
internal static class LabGear_ZNetScene_Awake_Patch
{
    private static void Postfix(ZNetScene __instance) => LabGear.RegisterIn(__instance);
}

/// <summary>
/// The clipboard is a tool with no build pieces, so the game would answer its clicks with a punch and a block. Its clicks belong to
/// the clipboard (GearHost reads the mouse itself): the attack and block controls are dropped while it is held.
/// </summary>
[HarmonyPatch(typeof(Player), "SetControls")]
internal static class LabGear_Player_SetControls_Patch
{
    private static void Prefix(Player __instance, ref bool attack, ref bool attackHold, ref bool secondaryAttack, ref bool secondaryAttackHold, ref bool block, ref bool blockHold)
    {
        if (__instance != Player.m_localPlayer || !Clipboard.Held)
            return;
        attack = attackHold = secondaryAttack = secondaryAttackHold = block = blockHold = false;
    }
}

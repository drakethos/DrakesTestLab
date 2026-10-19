using System.Reflection;
using DrakesTestLab.UI.Toolbox;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Patches;

// While the toolbox is open the game must not read movement, look or action input, and the cursor must be free.
// (This replaces Jotunn's GUIManager.BlockInput so the mod has no Jotunn dependency.)

[HarmonyPatch(typeof(Player), "TakeInput")]
internal static class Player_TakeInput_Patch
{
    private static void Postfix(ref bool __result)
    {
        if (ToolboxHost.IsOpen)
            __result = false;
    }
}

[HarmonyPatch]
internal static class PlayerController_TakeInput_Patch
{
    private static MethodBase? TargetMethod() => AccessTools.Method(typeof(PlayerController), "TakeInput");

    private static bool Prepare() => TargetMethod() != null;

    private static void Postfix(ref bool __result)
    {
        if (ToolboxHost.IsOpen)
            __result = false;
    }
}

[HarmonyPatch]
internal static class GameCamera_UpdateMouseCapture_Patch
{
    private static MethodBase? TargetMethod() => AccessTools.Method(typeof(GameCamera), "UpdateMouseCapture");

    private static bool Prepare() => TargetMethod() != null;

    private static void Postfix()
    {
        if (!ToolboxHost.IsOpen)
            return;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}

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

/// <summary>Scrolling the lab window must not zoom the camera: the game's scroll wheel reads 0 while the window is open.</summary>
[HarmonyPatch]
internal static class ZInput_ScrollWheel_Patch
{
    private static MethodBase? TargetMethod() => AccessTools.Method(typeof(ZInput), "GetMouseScrollWheel");

    private static bool Prepare() => TargetMethod() != null;

    private static void Postfix(ref float __result)
    {
        if (ToolboxHost.IsOpen)
            __result = 0f;
    }
}

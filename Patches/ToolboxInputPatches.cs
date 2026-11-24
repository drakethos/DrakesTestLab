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
        if (ToolboxHost.BlocksInput)
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
        if (ToolboxHost.BlocksInput)
            __result = false;
    }
}

/// <summary>
/// While the lab window is open the cursor is freed the way the game's own menus free it, and the game's lock is
/// skipped. The game locks the cursor every frame unless one of its own menus is up; undoing that lock in a Postfix
/// worked on Windows, but on Linux (X11) every lock grabs the pointer and warps it to the window centre, so the mouse
/// could not move or click in the window until the inventory had been opened once.
/// </summary>
[HarmonyPatch]
internal static class GameCamera_UpdateMouseCapture_Patch
{
    // ZCursor (assembly_utils) is the game's cursor state; menus call ZCursor.Show(). Not in the stub refs, so by name.
    private static readonly MethodInfo? ZCursorShow = AccessTools.Method(AccessTools.TypeByName("ZCursor"), "Show");

    private static MethodBase? TargetMethod() => AccessTools.Method(typeof(GameCamera), "UpdateMouseCapture");

    private static bool Prepare() => TargetMethod() != null;

    private static bool Prefix()
    {
        if (!ToolboxHost.BlocksInput)
            return true;

        // set only on change: each lock-state write is a pointer grab or release on X11
        if (Cursor.lockState != CursorLockMode.None)
            Cursor.lockState = CursorLockMode.None;
        try { ZCursorShow?.Invoke(null, null); }
        catch { /* older game without ZCursor: the lines below are enough */ }
        Cursor.visible = true; // ZCursor.Show hides it when the game thinks a gamepad is in use; the window needs the mouse
        return false;
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
        if (ToolboxHost.BlocksInput)
            __result = 0f;
    }
}

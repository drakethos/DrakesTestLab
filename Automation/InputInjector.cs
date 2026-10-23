using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Automation;

/// <summary>
/// Fake key / button presses for tests: the game's ZInput reports the injected key or button as pressed for the next
/// frame (GetKeyDown / GetButtonDown) and as held for a few frames (GetKey / GetButton).
/// </summary>
internal static class InputInjector
{
    private sealed class Press
    {
        public int DownFrame;
        public int UntilFrame;
    }

    private static readonly Dictionary<string, Press> Buttons = new(System.StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<KeyCode, Press> Keys = new();

    public static void PressButton(string name, int holdFrames = 0) =>
        Buttons[name] = new Press { DownFrame = Time.frameCount + 1, UntilFrame = Time.frameCount + 1 + holdFrames };

    public static void PressKey(KeyCode key, int holdFrames = 0) =>
        Keys[key] = new Press { DownFrame = Time.frameCount + 1, UntilFrame = Time.frameCount + 1 + holdFrames };

    public static bool ButtonDown(string name) => Buttons.TryGetValue(name, out var p) && Time.frameCount == p.DownFrame;
    public static bool ButtonHeld(string name) => Buttons.TryGetValue(name, out var p) && Time.frameCount >= p.DownFrame && Time.frameCount <= p.UntilFrame;
    public static bool KeyDown(KeyCode key) => Keys.TryGetValue(key, out var p) && Time.frameCount == p.DownFrame;
    public static bool KeyHeld(KeyCode key) => Keys.TryGetValue(key, out var p) && Time.frameCount >= p.DownFrame && Time.frameCount <= p.UntilFrame;

    /// <summary>"down", "up" or "held", from the name of the lambda ZInput's public wrapper passed in.</summary>
    public static string Kind(System.Delegate? check)
    {
        var name = check?.Method?.Name ?? "";
        if (name.Contains("Down>")) return "down";
        if (name.Contains("Up>")) return "up";
        return "held";
    }

    public static bool Busy
    {
        get
        {
            foreach (var p in Buttons.Values)
                if (Time.frameCount <= p.UntilFrame) return true;
            foreach (var p in Keys.Values)
                if (Time.frameCount <= p.UntilFrame) return true;
            return false;
        }
    }
}

// ZInput.GetButtonDown / GetKeyDown are one-line static wrappers that the runtime inlines into the game's code, so a
// patch on them never runs. Every one of them funnels into these two instance methods, which are patched instead.
// The lambda each wrapper passes is named after it (<GetButtonDown>b__..., <GetKeyUp>b__...), which tells down / held / up apart.

[HarmonyPatch]
internal static class ZInput_TryGetButtonState_Patch
{
    private static MethodBase? TargetMethod() => AccessTools.Method(typeof(ZInput), "TryGetButtonState");
    private static bool Prepare() => TargetMethod() != null;

    // __args avoids declaring the game's nested types; [0] = button name, [1] = the state lambda
    private static void Postfix(object[] __args, ref bool __result)
    {
        if (__result || __args.Length < 2 || __args[0] is not string name)
            return;
        var kind = InputInjector.Kind(__args[1] as System.Delegate);
        if (kind == "down" && InputInjector.ButtonDown(name)) __result = true;
        else if (kind == "held" && InputInjector.ButtonHeld(name)) __result = true;
    }
}

[HarmonyPatch]
internal static class ZInput_TryGetKeyState_Patch
{
    private static MethodBase? TargetMethod() => AccessTools.Method(typeof(ZInput), "TryGetKeyStateLowLevel");
    private static bool Prepare() => TargetMethod() != null;

    // [0] = KeyCode, [1] = the state lambda
    private static void Postfix(object[] __args, ref bool __result)
    {
        if (__result || __args.Length < 2 || __args[0] is not KeyCode key)
            return;
        var kind = InputInjector.Kind(__args[1] as System.Delegate);
        if (kind == "down" && InputInjector.KeyDown(key)) __result = true;
        else if (kind == "held" && InputInjector.KeyHeld(key)) __result = true;
    }
}

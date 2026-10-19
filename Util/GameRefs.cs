using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Util;

internal static class GameRefs
{
    // Valheim 1.0: Terminal.TryRunCommand(string, bool silentFail, bool skipAllowedCheck). The CI stubs only know
    // the 1-arg form, so bind by name and pad the arguments.
    private static readonly MethodInfo? TryRun = AccessTools.Method(typeof(Terminal), "TryRunCommand");
    private static readonly FieldInfo? CheatField = AccessTools.Field(typeof(Terminal), "m_cheat");
    private static readonly FieldInfo? DebugModeField = AccessTools.Field(typeof(Player), "m_debugMode");
    private static bool _cheatsConfirmed;

    public static bool CheatsEnabled => CheatField?.GetValue(null) is true;

    /// <summary>Turns dev commands on and accepts the 1.0 "you are cheating" prompt so cheat commands run.</summary>
    public static void EnableCheats()
    {
        CheatField?.SetValue(null, true);
        if (_cheatsConfirmed || Player.m_localPlayer == null)
            return;

        _cheatsConfirmed = true;
        RunConsoleCommand("confirmcheats");
    }

    /// <summary>A new character has to accept the cheat prompt again.</summary>
    public static void ForgetCheatConfirm() => _cheatsConfirmed = false;

    public static void SetDebugMode(bool on) => DebugModeField?.SetValue(null, on);

    /// <summary>World keys set by the toolbox or by cases, so a state reset can take them off again.</summary>
    public static readonly System.Collections.Generic.HashSet<string> KeysWeSet = new(StringComparer.OrdinalIgnoreCase);

    private static void TrackKey(string command)
    {
        var parts = command.Trim().Split(new[] { ' ' }, 2);
        if (parts.Length < 2)
            return;

        if (parts[0].Equals("setkey", StringComparison.OrdinalIgnoreCase))
            KeysWeSet.Add(parts[1].Trim());
        else if (parts[0].Equals("removekey", StringComparison.OrdinalIgnoreCase))
            KeysWeSet.Remove(parts[1].Trim());
    }

    public static void RunConsoleCommand(string command)
    {
        TrackKey(command);
        try
        {
            if (TryRun == null || Console.instance == null)
                return;

            var ps = TryRun.GetParameters();
            var args = new object[ps.Length];
            args[0] = command;
            if (ps.Length > 1) args[1] = true;   // silentFail
            if (ps.Length > 2) args[2] = true;   // skipAllowedCheck
            TryRun.Invoke(Console.instance, args);
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Console command '{command}' failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Teleports the local player to world x/z, standing on the ground there. The vanilla "goto" command takes two separate
    /// numbers ("goto 100 200", not "100,200") and clamps the height to 30 m, which drops you in the sea or inside terrain.
    /// </summary>
    public static bool Teleport(float x, float z)
    {
        var p = Player.m_localPlayer;
        if (p == null || ZoneSystem.instance == null)
            return false;

        var pos = new Vector3(x, 0f, z);
        pos.y = Mathf.Max(ZoneSystem.instance.GetGroundHeight(pos), 30f) + 2f;
        return p.TeleportTo(pos, p.transform.rotation, true);
    }

    public static void LogToConsole(string message)
    {
        try
        {
            if (Console.instance != null)
            {
                Console.instance.AddString(message);
                return;
            }
        }
        catch
        {
            // fall through to the BepInEx log
        }

        TestLabPlugin.Log?.LogInfo(message);
    }
}

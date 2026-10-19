using System;
using UnityEngine;

namespace DrakesTestLab.Bootstrap;

/// <summary>
/// Puts the test session back to a neutral state so a test case starts from a known baseline instead of stacking on the last one:
/// rules, modes, inventory, skills, discovery, boss keys, test wards, identity, time and weather.
/// The arena and house stay where they are.
/// </summary>
internal static class StateReset
{
    public static void Run()
    {
        var player = Player.m_localPlayer;
        if (player == null)
            return;

        // rules and switches kept in memory
        TestRules.ResetRules();
        FireTools.InfiniteFuel = false;
        UI.Toolbox.PlayerTab.InfiniteStamina = false;
        UI.Toolbox.WorldTab.TimeScale = 1f;
        Time.timeScale = 1f;

        // who you are
        WardTools.RestoreOriginal();

        // player modes (these are toggles in game)
        Off(player.InGodMode(), "god");
        Off(player.InGhostMode(), "ghost");
        Off(player.InDebugFlyMode(), "fly");
        Off(player.NoCostCheat(), "nocost");
        if (TestLabConfig.Instance.CreaturesHostile.Value)
            TestControlsApplier.ToggleHostile();

        // body and belongings
        player.UnequipAllItems();
        player.GetInventory().RemoveAll();
        StatSetter.FillAll();

        // knowledge
        player.ResetCharacter();
        player.SetGuardianPower("");

        // world keys that unlock everything (set from the toolbox or a case) would keep all recipes and pieces visible
        foreach (var key in new System.Collections.Generic.List<string>(Util.GameRefs.KeysWeSet))
            Util.GameRefs.RunConsoleCommand("removekey " + key);
        Util.GameRefs.RunConsoleCommand("removekey AllPiecesUnlocked");
        Util.GameRefs.RunConsoleCommand("removekey AllRecipesUnlocked");
        Discovery.Refresh();

        // world state we set
        BossTools.ClearAll();
        foreach (var ward in UnityEngine.Object.FindObjectsOfType<PrivateArea>())
        {
            if (ZNetScene.instance != null)
                ZNetScene.instance.Destroy(ward.gameObject);
        }

        var env = EnvMan.instance;
        if (env != null)
        {
            env.m_debugTimeOfDay = false;
            env.m_debugEnv = "";
        }

        TestLabPlugin.Log?.LogInfo("Test state reset.");
    }

    private static void Off(bool isOn, string command)
    {
        if (isOn)
            Util.GameRefs.RunConsoleCommand(command);
    }
}

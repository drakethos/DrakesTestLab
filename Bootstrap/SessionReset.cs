using System;
using DrakesTestLab.UI.Toolbox;
using UnityEngine;

namespace DrakesTestLab.Bootstrap;

/// <summary>
/// Reset without leaving the game: log out to the main menu inside the running process. The fast-boot patches then
/// redeploy a fresh world and character and load straight back in, with every mod still loaded.
/// The mod never starts or relaunches any other process.
/// </summary>
internal static class SessionReset
{
    public static void Execute()
    {
        try
        {
            if (Game.instance == null || ZNet.instance == null)
            {
                TestLabPlugin.Log?.LogWarning("Reset: no world is running, nothing to reset.");
                return;
            }

            ToolboxHost.Set(false);

            // everything the previous world left behind in memory
            TestRules.ResetRules();
            TestRules.HasArena = false;
            TestRules.SpawnPoint = null;
            FireTools.InfiniteFuel = false;
            PlayerTab.InfiniteStamina = false;
            WorldTab.TimeScale = 1f;
            Time.timeScale = 1f;
            WardTools.ForgetOriginal();
            InitialConditionsApplier.ForgetApplied();
            TestControlsApplier.ForgetApplied();
            Util.GameRefs.ForgetCheatConfirm();

            TestLabPlugin.Log?.LogInfo("Reset: returning to the main menu.");
            Game.instance.Logout();
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Reset failed: {ex.Message}");
        }
    }
}

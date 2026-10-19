using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace DrakesTestLab.Bootstrap;

internal static class HardRestart
{
    /// <summary>
    /// Reset without leaving the game: log out to the main menu in-process. The fast-boot patches then redeploy a fresh
    /// world and character and load straight back in, with every mod still loaded. (Relaunching valheim.exe directly can
    /// come back without the mod profile's doorstop settings.)
    /// </summary>
    private static bool TrySoftReset()
    {
        try
        {
            if (!TestLabConfig.Instance.ReloadInProcess.Value || Game.instance == null || ZNet.instance == null)
                return false;

            UI.Toolbox.ToolboxHost.Set(false);

            TestRules.ResetRules();
            TestRules.HasArena = false;
            TestRules.SpawnPoint = null;
            FireTools.InfiniteFuel = false;
            UI.Toolbox.PlayerTab.InfiniteStamina = false;
            UI.Toolbox.WorldTab.TimeScale = 1f;
            Time.timeScale = 1f;
            WardTools.ForgetOriginal();
            InitialConditionsApplier.ForgetApplied();
            TestControlsApplier.ForgetApplied();
            Util.GameRefs.ForgetCheatConfirm();

            TestLabPlugin.Log?.LogInfo("Reset: returning to the main menu in-process.");
            Game.instance.Logout();
            return true;
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"In-process reset failed ({ex.Message}); falling back to relaunch.");
            return false;
        }
    }

    public static void Execute()
    {
        if (TrySoftReset())
            return;

        try
        {
            if (TestLabConfig.Instance.SaveBeforeRestart.Value)
                TrySaveWorld();

            var exe = ResolveValheimExe();
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                TestLabPlugin.Log?.LogError($"Hard restart failed: valheim.exe not found ({exe}).");
                return;
            }

            var args = string.Join(" ", Environment.GetCommandLineArgs());
            if (args.StartsWith(exe, StringComparison.OrdinalIgnoreCase))
                args = args.Substring(exe.Length).Trim();

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? ""
            });

            TestLabPlugin.Log?.LogInfo("Hard restart: relaunching Valheim.");
            Application.Quit();
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogError($"Hard restart failed: {ex}");
        }
    }

    private static void TrySaveWorld()
    {
        try
        {
            if (ZNet.instance != null && ZNet.instance.IsServer())
                ZNet.instance.Save(false);
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Pre-restart save failed: {ex.Message}");
        }
    }

    private static string ResolveValheimExe()
    {
        var cfg = TestLabConfig.Instance.ValheimExePath.Value;
        if (!string.IsNullOrWhiteSpace(cfg))
            return cfg.Trim();

        var dataPath = Application.dataPath;
        var parent = Directory.GetParent(dataPath)?.FullName;
        if (parent != null)
        {
            var candidate = Path.Combine(parent, "valheim.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(dataPath, "valheim.exe");
    }
}

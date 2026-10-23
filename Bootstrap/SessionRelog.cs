using System;
using DrakesTestLab.UI.Toolbox;

namespace DrakesTestLab.Bootstrap;

/// <summary>
/// Log out to the main menu and load the SAME world and character back in (both saved by the logout), inside the running
/// process: what a player does to check that something persists. Unlike <see cref="SessionReset"/> nothing is redeployed.
/// </summary>
internal static class SessionRelog
{
    /// <summary>True from the logout until the world is loaded again: the main-menu boot keeps the saves.</summary>
    public static bool Active { get; private set; }

    public static bool Begin()
    {
        if (Game.instance == null || ZNet.instance == null)
            return false;

        try
        {
            Active = true;
            ToolboxHost.Set(false);
            TestLabPlugin.Log?.LogInfo("Relog: saving and returning to the main menu; the same world and character load back in.");
            Game.instance.Logout();
            return true;
        }
        catch (Exception ex)
        {
            Active = false;
            TestLabPlugin.Log?.LogWarning($"Relog failed: {ex.Message}");
            return false;
        }
    }

    public static void Finished() => Active = false;
}

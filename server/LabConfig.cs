using BepInEx.Configuration;

namespace DrakesTestLabServer;

internal enum WorldMode
{
    /// <summary>Replace the server's world with a fresh copy of the lab world on every start.</summary>
    FreshEveryStart,

    /// <summary>Put the lab world in place only if the server has no world yet; then keep whatever the server saves.</summary>
    CreateIfMissing,

    /// <summary>Never touch the server's world.</summary>
    Off
}

internal static class LabConfig
{
    public static ConfigEntry<WorldMode> Mode { get; private set; } = null!;
    public static ConfigEntry<string> WorldName { get; private set; } = null!;
    public static ConfigEntry<int> KeepBackups { get; private set; } = null!;
    public static ConfigEntry<bool> ControlEnabled { get; private set; } = null!;
    public static ConfigEntry<bool> QueriesEnabled { get; private set; } = null!;

    public static void Bind(ConfigFile config)
    {
        Mode = config.Bind("World", "Mode", WorldMode.FreshEveryStart,
            "FreshEveryStart = a clean lab world on every server start. CreateIfMissing = lab world only when the server has none, then the server keeps its own saves. Off = never touch the world.");
        WorldName = config.Bind("World", "WorldName", "",
            "World to replace. Empty = the server's own -world name.");
        KeepBackups = config.Bind("World", "KeepBackups", 3,
            "How many replaced worlds to keep (in testlab_world_backups next to the save folders). 0 = keep none.");
        QueriesEnabled = config.Bind("Control", "Queries", true,
            "Let the client that holds the Player1 role ask the server what it sees: the connected players, the ZDOs in the world (count, owner, stored values) and the routed RPC traffic. Read-only. A test can compare the answer with what Player 1 and Player 2 report.");
        ControlEnabled = config.Bind("Control", "Enabled", false,
            "Let the client that holds the Player1 role change the server from a test: the admin list, whitelist and blacklist, kicks, a mod's settings, a mod's own reset hooks, and a restart. OFF by default: on, the first lab client to claim Player1 (anyone who can join) can make itself an admin. Turn it on only on a test server.");
    }
}

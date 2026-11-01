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

    public static void Bind(ConfigFile config)
    {
        Mode = config.Bind("World", "Mode", WorldMode.FreshEveryStart,
            "FreshEveryStart = a clean lab world on every server start. CreateIfMissing = lab world only when the server has none, then the server keeps its own saves. Off = never touch the world.");
        WorldName = config.Bind("World", "WorldName", "",
            "World to replace. Empty = the server's own -world name.");
        KeepBackups = config.Bind("World", "KeepBackups", 3,
            "How many replaced worlds to keep (in testlab_world_backups next to the save folders). 0 = keep none.");
    }
}

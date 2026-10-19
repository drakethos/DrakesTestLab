using DrakesTestLab.Util;

namespace DrakesTestLab.Bootstrap;

internal static class TestControlsApplier
{
    private static bool _devEnabled;
    private static bool _debugEnabled;

    public static void ForgetApplied()
    {
        _devEnabled = false;
        _debugEnabled = false;
    }

    public static void Apply()
    {
        if (!IsLocalHost())
            return;

        var cfg = TestLabConfig.Instance;

        if (cfg.AutoEnableDevCommands.Value != _devEnabled)
        {
            if (cfg.AutoEnableDevCommands.Value)
                GameRefs.EnableCheats();
            _devEnabled = cfg.AutoEnableDevCommands.Value;
        }

        if (cfg.AutoEnableDebugMode.Value != _debugEnabled)
        {
            GameRefs.SetDebugMode(cfg.AutoEnableDebugMode.Value);
            _debugEnabled = cfg.AutoEnableDebugMode.Value;
        }

        CreatureHostilityState.Enabled = !cfg.CreaturesHostile.Value;
    }

    public static void ToggleDevCommands()
    {
        var cfg = TestLabConfig.Instance;
        cfg.AutoEnableDevCommands.Value = !cfg.AutoEnableDevCommands.Value;
        if (cfg.PanelWritesConfig.Value)
            cfg.AutoEnableDevCommands.ConfigFile.Save();
        _devEnabled = false;
        Apply();
    }

    public static void ToggleHostile()
    {
        var cfg = TestLabConfig.Instance;
        cfg.CreaturesHostile.Value = !cfg.CreaturesHostile.Value;
        if (cfg.PanelWritesConfig.Value)
            cfg.CreaturesHostile.ConfigFile.Save();
        CreatureHostilityState.Enabled = !cfg.CreaturesHostile.Value;
    }

    private static bool IsLocalHost()
    {
        return ZNet.instance == null || ZNet.instance.IsServer();
    }
}

internal static class CreatureHostilityState
{
    public static bool Enabled;
}

using BepInEx;
using BepInEx.Logging;
using DrakesTestLab.Bootstrap;
using DrakesTestLab.UI;
using DrakesTestLab.UI.Toolbox;
using HarmonyLib;

namespace DrakesTestLab;

[BepInPlugin(GUID, ModName, Version)]
public partial class TestLabPlugin : BaseUnityPlugin
{
    public static TestLabPlugin? Instance { get; private set; }
    public static ManualLogSource? Log { get; private set; }

    private readonly Harmony _harmony = new("drakemods.DrakesTestLab");
    private ConfigHotReload? _hotReload;

    private void Awake()
    {
        Instance = this;
        Log = Logger;

        TestLabConfig.Bind(Config);
        CreatureHostilityState.Enabled = !TestLabConfig.Instance.CreaturesHostile.Value;
        _harmony.PatchAll();

        _hotReload = new ConfigHotReload();
        _hotReload.Start();

        var inputGo = new UnityEngine.GameObject("DrakesTestLab_Input");
        UnityEngine.Object.DontDestroyOnLoad(inputGo);
        inputGo.AddComponent<TestLabInputDriver>();
        inputGo.AddComponent<ToolboxHost>();

        Logger.LogInfo($"{ModName} {Version} loaded — Phase 1 QA boot.");
    }

    private void OnDestroy()
    {
        _hotReload?.Dispose();
        _harmony.UnpatchSelf();
    }
}

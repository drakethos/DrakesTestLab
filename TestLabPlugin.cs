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

        // BepInEx rewrites the whole .cfg on every Bind by default: batch them into one save.
        var saveOnSet = Config.SaveOnConfigSet;
        Config.SaveOnConfigSet = false;
        try { TestLabConfig.Bind(Config); }
        finally { Config.SaveOnConfigSet = saveOnSet; }
        Config.Save();
        CreatureHostilityState.Enabled = !TestLabConfig.Instance.CreaturesHostile.Value;
        _harmony.PatchAll();

        _hotReload = new ConfigHotReload();
        _hotReload.Start();

        var inputGo = new UnityEngine.GameObject("DrakesTestLab_Input");
        UnityEngine.Object.DontDestroyOnLoad(inputGo);
        inputGo.AddComponent<TestLabInputDriver>();
        inputGo.AddComponent<ToolboxHost>();
        inputGo.AddComponent<Gear.GearHost>();
        inputGo.AddComponent<Util.FrameSampler>();
        inputGo.AddComponent<QuietZoneSweeper>();

        Logger.LogInfo($"{ModName} {Version} loaded — Phase 1 QA boot.");
    }

    // Start runs after every plugin's Awake: the last plugin's load time ends here.
    // Every other plugin's assembly is guaranteed loaded by now, unlike at our own Awake() above —
    // Automation.ModMessageCard_Patch.Ready gates on that for the same reason (see its own comment).
    private void Start()
    {
        Util.ModLoadTimer.Close();
        Automation.ModMessageCard_Patch.Ready = true;
        _harmony.CreateClassProcessor(typeof(Automation.ModMessageCard_Patch)).Patch();
    }

    // a config file change seen by the hot-reload watcher (off the main thread) is applied here
    private void Update() => ConfigHotReload.RunPending();

    private void OnDestroy()
    {
        _hotReload?.Dispose();
        _harmony.UnpatchSelf();
    }
}

using BepInEx;
using HarmonyLib;
using Jotunn;

namespace DrakesTestLab;

[BepInPlugin(GUID, ModName, Version)]
[BepInDependency(Main.ModGuid)]
public partial class TestLabPlugin : BaseUnityPlugin
{
    private readonly Harmony _harmony = new("drakemods.DrakesTestLab");

    private void Awake()
    {
        _harmony.PatchAll();
        Logger.LogInfo($"{ModName} {Version} loaded (test lab stub).");
    }
}

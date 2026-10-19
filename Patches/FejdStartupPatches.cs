using DrakesTestLab.Bootstrap;
using HarmonyLib;

namespace DrakesTestLab.Patches;

[HarmonyPatch(typeof(FejdStartup), "Awake")]
internal static class FejdStartup_Awake_Patch
{
    private static void Postfix(FejdStartup __instance)
    {
        if (!TestLabConfig.Instance.BootstrapEnabled.Value)
            return;

        var target = SaveModeResolver.Resolve();
        PresetDeployer.DeployBeforeLoad(target);
        FejdStartupBridge.RefreshSaveLists(__instance, force: true);
        FastBootDriver.Attach(__instance);
    }
}

[HarmonyPatch(typeof(FejdStartup), "SetupGui")]
internal static class FejdStartup_SetupGui_Patch
{
    private static void Postfix(FejdStartup __instance)
    {
        if (!TestLabConfig.Instance.BootstrapEnabled.Value)
            return;

        MenuSuppressor.Suppress(__instance);
    }
}

[HarmonyPatch(typeof(FejdStartup), "Start")]
internal static class FejdStartup_Start_Patch
{
    private static void Postfix(FejdStartup __instance)
    {
        if (!TestLabConfig.Instance.BootstrapEnabled.Value)
            return;

        MenuSuppressor.Suppress(__instance);
    }
}

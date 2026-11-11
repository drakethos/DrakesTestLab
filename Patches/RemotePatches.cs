using DrakesTestLab.Automation;
using HarmonyLib;

namespace DrakesTestLab.Patches;

[HarmonyPatch(typeof(ZNet), "Awake")]
internal static class ZNet_Awake_Remote_Patch
{
    // The routed RPC is created in ZNet.Awake, so the role / command handlers can only be registered after it.
    // A game that is itself the server (the local lab world) keeps using the in-process virtual players instead.
    private static void Postfix(ZNet __instance)
    {
        if (!__instance.IsServer())
            RemotePeers.Register();
    }
}

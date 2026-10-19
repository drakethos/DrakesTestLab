using DrakesTestLab.Bootstrap;
using DrakesTestLab.UI;
using HarmonyLib;

namespace DrakesTestLab.Patches;

[HarmonyPatch(typeof(Menu))]
internal static class Menu_Logout_Patch
{
    [HarmonyTargetMethod]
    private static System.Reflection.MethodBase? TargetMethod()
    {
        return HarmonyLib.AccessTools.Method(typeof(Menu), "OnLogout")
               ?? HarmonyLib.AccessTools.Method(typeof(Menu), "Logout")
               ?? HarmonyLib.AccessTools.Method(typeof(Menu), "OnLogOut");
    }

    [HarmonyPrefix]
    private static bool Prefix()
    {
        if (!TestLabConfig.Instance.HijackLogout.Value)
            return true;

        if (TestLabConfig.Instance.ShowMenuOnLogout.Value)
        {
            TestControlPanel.Show();
            return false;
        }

        if (TestLabConfig.Instance.LogoutMeansReset.Value)
            HardRestart.Execute();

        return false;
    }
}

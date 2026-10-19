using DrakesTestLab.Bootstrap;
using HarmonyLib;

namespace DrakesTestLab.Patches;

[HarmonyPatch(typeof(BaseAI), nameof(BaseAI.SetAggravated), typeof(bool), typeof(BaseAI.AggravatedReason))]
internal static class BaseAI_SetAggravated_Patch
{
    private static bool Prefix(bool aggro)
    {
        if (aggro && CreatureHostilityState.Enabled)
            return false;

        return true;
    }
}

[HarmonyPatch(typeof(BaseAI), nameof(BaseAI.SetAlerted), typeof(bool))]
internal static class BaseAI_SetAlerted_Patch
{
    private static bool Prefix(bool alert)
    {
        if (alert && CreatureHostilityState.Enabled)
            return false;

        return true;
    }
}

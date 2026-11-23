using System.Collections.Generic;
using System.Reflection;
using DrakesTestLab.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Patches;

// Vanilla clamps a stored terrain change to the generated height +-8 m (when levelling/raising, and again on every load in
// ApplyToHeightmap). Template worlds such as KydlFlat store changes of up to ~36 m, so with the vanilla limit their flat floor
// comes back as 8 m steps. These transpilers send each 16 m wide Mathf.Clamp in those methods to WideClamp, which widens it
// while [World] LiftTerrainLimit is on. Clamps of any other width (0..1 weights, indices) are left alone.
[HarmonyPatch]
internal static class TerrainLimitPatches
{
    private const float Lift = 1000f;
    private static readonly MethodInfo Clamp = AccessTools.Method(typeof(Mathf), nameof(Mathf.Clamp), new[] { typeof(float), typeof(float), typeof(float) });
    private static readonly MethodInfo Wide = AccessTools.Method(typeof(TerrainLimitPatches), nameof(WideClamp));

    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(TerrainComp), "LevelTerrain");
        yield return AccessTools.Method(typeof(TerrainComp), "RaiseTerrain");
        yield return AccessTools.Method(typeof(TerrainComp), "ApplyToHeightmap");
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code, MethodBase original)
    {
        var swapped = 0;
        foreach (var ci in code)
        {
            if (ci.Calls(Clamp))
            {
                ci.operand = Wide;
                swapped++;
            }
            yield return ci;
        }
        TestLabPlugin.Log?.LogInfo($"Terrain limit: {original.Name} has {swapped} clamp call(s) routed through the lift.");
    }

    public static float WideClamp(float value, float min, float max)
    {
        if (Mathf.Abs(max - min - 16f) < 0.01f && TestLabConfig.Instance?.LiftTerrainLimit?.Value == true)
        {
            min -= Lift;
            max += Lift;
        }
        return value < min ? min : value > max ? max : value;
    }
}

using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Bootstrap;

/// <summary>Keeps fires and fuel-powered cooking stations topped up.</summary>
internal static class FireTools
{
    private static readonly FieldInfo? FuelKey = AccessTools.Field(typeof(ZDOVars), "s_fuel");
    private static float _nextRefuel;

    /// <summary>When on, every fire and fuel station in the world is refilled every few seconds.</summary>
    public static bool InfiniteFuel { get; set; }

    public static void Tick()
    {
        if (!InfiniteFuel || Time.unscaledTime < _nextRefuel)
            return;

        _nextRefuel = Time.unscaledTime + 3f;
        Refuel();
    }

    /// <summary>Fills fire pits, hearths, braziers etc. and fuel-driven cooking stations to their maximum.</summary>
    public static int Refuel()
    {
        var count = 0;
        foreach (var fire in Object.FindObjectsOfType<Fireplace>())
        {
            if (SetFuel(fire, fire.m_maxFuel))
                count++;
        }

        foreach (var station in Object.FindObjectsOfType<CookingStation>())
        {
            if (station.m_useFuel && SetFuel(station, station.m_maxFuel))
                count++;
        }

        return count;
    }

    private static bool SetFuel(Component target, float amount)
    {
        if (FuelKey == null || FuelKey.GetValue(null) is not int key)
            return false;

        var view = AccessTools.Field(target.GetType(), "m_nview")?.GetValue(target) as ZNetView;
        var zdo = view != null && view.IsValid() ? view.GetZDO() : null;
        if (zdo == null)
            return false;

        if (!view!.IsOwner())
            view.ClaimOwnership();

        zdo.Set(key, amount);
        return true;
    }
}

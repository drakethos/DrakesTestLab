using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Bootstrap;

/// <summary>
/// Ensures inventory/equipped ItemData rows have a valid m_dropPrefab before SetupVisEquipment runs.
/// </summary>
internal static class PlayerItemRepair
{
    private static readonly MethodInfo? SetupEquipment =
        AccessTools.Method(typeof(Humanoid), "SetupEquipment");

    public static void RepairLocalPlayer()
    {
        var player = Player.m_localPlayer;
        if (player == null || ObjectDB.instance == null)
            return;

        var repaired = 0;
        var inventory = player.GetInventory();
        if (inventory != null)
        {
            foreach (var item in inventory.GetAllItems())
            {
                if (TryRepairItem(item))
                    repaired++;
            }
        }

        RefreshEquipment(player);

        if (repaired > 0)
            TestLabPlugin.Log?.LogInfo($"Repaired {repaired} item prefab reference(s) on local player.");
    }

    private static bool TryRepairItem(ItemDrop.ItemData? item)
    {
        if (item?.m_shared == null || item.m_dropPrefab != null)
            return false;

        GameObject? prefab = null;
        if (!ObjectDB.instance.TryGetItemPrefab(item.m_shared, out prefab) || prefab == null)
            prefab = ObjectDB.instance.GetItemPrefab(item.m_shared.m_name);

        if (prefab == null)
            return false;

        item.m_dropPrefab = prefab;
        return true;
    }

    private static void RefreshEquipment(Humanoid humanoid)
    {
        if (SetupEquipment == null)
            return;

        try
        {
            SetupEquipment.Invoke(humanoid, null);
        }
        catch
        {
            // Equipment refresh is best-effort; missing vis gear should not block spawn.
        }
    }
}

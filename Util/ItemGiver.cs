using UnityEngine;

namespace DrakesTestLab.Util;

internal static class ItemGiver
{
    /// <summary>Adds items straight to the local player's inventory (stacks split by max stack size); overflow is dropped.</summary>
    /// <param name="crafterId">With <paramref name="crafterName"/>: the item counts as crafted by that player (0 = unowned, like loot).</param>
    public static int Give(string prefabName, int count, int quality = 1, bool equip = false, long crafterId = 0L, string crafterName = "")
    {
        var player = Player.m_localPlayer;
        var prefab = ObjectDB.instance?.GetItemPrefab(prefabName);
        var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        if (player == null || drop == null)
        {
            TestLabPlugin.Log?.LogWarning($"Give item: '{prefabName}' is not a known item prefab" + (player == null ? " (no player)" : "") + ".");
            return 0;
        }

        var inventory = player.GetInventory();
        var maxStack = Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize);
        var given = 0;
        while (count > 0)
        {
            var stack = Mathf.Min(count, maxStack);
            var data = drop.m_itemData.Clone();
            data.m_dropPrefab = prefab;
            data.m_stack = stack;
            data.m_quality = Mathf.Max(1, quality);
            data.m_durability = data.GetMaxDurability();
            if (crafterName.Length > 0)
            {
                data.m_crafterID = crafterId;
                data.m_crafterName = crafterName;
            }
            if (!inventory.AddItem(data))
            {
                TestLabPlugin.Log?.LogWarning($"Give item: inventory full, dropped {stack} x {prefabName} on the ground.");
                player.DropItem(inventory, data, stack);
            }
            else if (equip && count == stack)
                player.EquipItem(data, true);

            given += stack;
            count -= stack;
        }

        return given;
    }

    public static void GiveKit(params (string name, int count)[] items)
    {
        foreach (var (name, count) in items)
            Give(name, count);
    }
}

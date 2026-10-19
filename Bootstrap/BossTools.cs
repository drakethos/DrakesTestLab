using System;
using System.Collections.Generic;
using DrakesTestLab.Util;
using UnityEngine;

namespace DrakesTestLab.Bootstrap;

/// <summary>Boss progression shortcuts: world key (what boss stones set), guardian power and the trophy that hooks into the stone.</summary>
internal static class BossTools
{
    public sealed class Boss
    {
        public string Name = "";
        public string Key = "";
        public string Power = "";
        public string Trophy = "";
    }

    public static readonly Boss[] All =
    {
        new() { Name = "Eikthyr", Key = "defeated_eikthyr", Power = "GP_Eikthyr", Trophy = "TrophyEikthyr" },
        new() { Name = "The Elder", Key = "defeated_gdking", Power = "GP_TheElder", Trophy = "TrophyTheElder" },
        new() { Name = "Bonemass", Key = "defeated_bonemass", Power = "GP_Bonemass", Trophy = "TrophyBonemass" },
        new() { Name = "Moder", Key = "defeated_dragon", Power = "GP_Moder", Trophy = "TrophyDragonQueen" },
        new() { Name = "Yagluth", Key = "defeated_goblinking", Power = "GP_Yagluth", Trophy = "TrophyGoblinKing" },
        new() { Name = "The Queen", Key = "defeated_queen", Power = "GP_Queen", Trophy = "TrophySeekerQueen" },
        new() { Name = "Fader", Key = "defeated_fader", Power = "GP_Ashlands", Trophy = "TrophyFader" }
    };

    public static Boss? Find(string nameOrKey)
    {
        foreach (var b in All)
        {
            if (string.Equals(b.Name, nameOrKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(b.Key, nameOrKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(b.Name.Replace("The ", ""), nameOrKey, StringComparison.OrdinalIgnoreCase))
                return b;
        }

        return null;
    }

    public static bool IsDefeated(Boss boss) =>
        ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(boss.Key);

    /// <summary>Sets the world key, makes the boss power the active guardian power and gives the trophy.</summary>
    public static void Defeat(Boss boss, bool giveTrophy = true, bool setPower = true)
    {
        GameRefs.RunConsoleCommand("setkey " + boss.Key);

        var player = Player.m_localPlayer;
        if (player == null)
            return;

        if (setPower && ObjectDB.instance != null && ObjectDB.instance.GetStatusEffect(boss.Power.GetStableHashCode()) != null)
            player.SetGuardianPower(boss.Power);

        if (giveTrophy)
            ItemGiver.Give(boss.Trophy, 1);
    }

    public static void Undo(Boss boss) => GameRefs.RunConsoleCommand("removekey " + boss.Key);

    /// <summary>Hooks the trophy into the closest loaded boss stone (an item stand that accepts it) within 80 m.</summary>
    public static string HookNearest(Boss boss)
    {
        var player = Player.m_localPlayer;
        if (player == null)
            return "No player.";

        var data = FindTrophy(player, boss);
        if (data == null)
        {
            ItemGiver.Give(boss.Trophy, 1);
            data = FindTrophy(player, boss);
        }

        if (data == null)
            return $"Could not find or create {boss.Trophy}.";

        ItemStand? best = null;
        var bestD = 80f;
        foreach (var stand in UnityEngine.Object.FindObjectsOfType<ItemStand>())
        {
            if (stand.HaveAttachment() || !(HarmonyLib.AccessTools.Method(typeof(ItemStand), "CanAttach")?.Invoke(stand, new object[] { data }) is true))
                continue;

            var d = Vector3.Distance(stand.transform.position, player.transform.position);
            if (d < bestD)
            {
                best = stand;
                bestD = d;
            }
        }

        if (best == null)
            return "No free boss stone within 80 m that takes this trophy - walk to the altar first.";

        best.UseItem(player, data);
        return $"Hooked {boss.Trophy} into the stone {bestD:0} m away.";
    }

    private static ItemDrop.ItemData? FindTrophy(Player player, Boss boss)
    {
        var prefab = ObjectDB.instance?.GetItemPrefab(boss.Trophy);
        var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        return drop == null ? null : player.GetInventory().GetItem(drop.m_itemData.m_shared.m_name);
    }

    public static void DefeatAll()
    {
        foreach (var b in All)
            GameRefs.RunConsoleCommand("setkey " + b.Key);
    }

    public static void ClearAll()
    {
        foreach (var b in All)
            GameRefs.RunConsoleCommand("removekey " + b.Key);
    }
}

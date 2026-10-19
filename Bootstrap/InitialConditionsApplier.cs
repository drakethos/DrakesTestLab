using System;
using System.Collections.Generic;
using DrakesTestLab.Util;
using UnityEngine;

namespace DrakesTestLab.Bootstrap;

internal static class InitialConditionsApplier
{
    private static bool _applied;

    public static void ForgetApplied() => _applied = false;

    public static void TryReapplyLocalPlayer()
    {
        _applied = false;
        ApplyIfReady();
    }

    public static void ApplyIfReady()
    {
        if (_applied)
            return;

        var player = Player.m_localPlayer;
        if (player == null)
            return;

        var cfg = TestLabConfig.Instance;

        if (cfg.GodMode.Value)
            GameRefs.RunConsoleCommand("god");

        if (cfg.StartTimeOfDay.Value >= 0f && EnvMan.instance != null)
        {
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = Mathf.Clamp01(cfg.StartTimeOfDay.Value);
        }

        ApplySkills(player, cfg.SkillLevels.Value);
        ApplyItems(player, cfg.StartingItems.Value);

        if (cfg.OverrideSpawnPosition.Value)
        {
            var pos = new Vector3(cfg.SpawnX.Value, cfg.SpawnY.Value, cfg.SpawnZ.Value);
            player.transform.position = pos;
        }

        PlayerItemRepair.RepairLocalPlayer();

        _applied = true;
        TestLabPlugin.Log?.LogInfo("Initial conditions applied to local player.");

        DrakesTestLab.Cases.CaseRunner.EnsureExamples();
        DrakesTestLab.Cases.CaseRunner.RunOnLoad();
    }

    private static void ApplySkills(Player player, string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            return;

        var skills = player.GetSkills();
        if (skills == null)
            return;

        foreach (var part in spec.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(':');
            if (kv.Length != 2)
                continue;

            if (!float.TryParse(kv[1].Trim(), out var level))
                continue;

            var skillName = kv[0].Trim();
            if (!Enum.TryParse<Skills.SkillType>(skillName, true, out var skillType))
                continue;

            try
            {
                skills.RaiseSkill(skillType, level);
            }
            catch
            {
                try
                {
                    skills.CheatRaiseSkill(skillName, level);
                }
                catch
                {
                    // Skill API may differ by version
                }
            }
        }
    }

    private static void ApplyItems(Player player, string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            return;

        var inventory = player.GetInventory();
        if (inventory == null)
            return;

        foreach (var part in spec.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(':');
            if (kv.Length != 2 || !int.TryParse(kv[1].Trim(), out var count))
                continue;

            var prefabName = kv[0].Trim();
            GameObject? prefab = null;
            if (ObjectDB.instance != null)
                prefab = ObjectDB.instance.GetItemPrefab(prefabName);

            prefab ??= ZNetScene.instance?.GetPrefab(prefabName);
            if (prefab?.GetComponent<ItemDrop>() == null)
                continue;

            if (!inventory.AddItem(prefab, count))
            {
                var itemDrop = prefab.GetComponent<ItemDrop>();
                for (var i = 0; i < count; i++)
                {
                    var clone = itemDrop.m_itemData.Clone();
                    clone.m_dropPrefab = prefab;
                    if (!inventory.AddItem(clone))
                        player.DropItem(inventory, clone, 1);
                }
            }
        }
    }
}

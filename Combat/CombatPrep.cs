using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DrakesTestLab.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Combat;

/// <summary>
/// Gets a fighter ready the way a player would be: real meals eaten (the game's own food code then gives max health and
/// stamina, regen ticks and food decay), and weapon skills set from the loadout, or from the weapon's level when the
/// loadout names none. Works for the local player and for lane players alike.
/// </summary>
internal static class CombatPrep
{
    private static readonly MethodInfo? UpdateFood = AccessTools.Method(typeof(Player), "UpdateFood", new[] { typeof(float), typeof(bool) });
    private static readonly FieldInfo? StaminaField = AccessTools.Field(typeof(Player), "m_stamina");

    /// <summary>The meals for this run: the food profile's list when it is one, else the loadout's own meals. Empty = no meals.</summary>
    public static List<string> MealsFor(Loadout l, string food) =>
        food.Length > 0 && l.FoodMeals.TryGetValue(food, out var m) ? m : l.Meals;

    /// <summary>Empties the stomach, eats each meal fresh, then fills health and stamina to the new max.</summary>
    public static void Feed(Player p, IList<string> meals)
    {
        p.ClearFood();
        foreach (var name in meals.Take(3))
        {
            var prefab = ObjectDB.instance?.GetItemPrefab(name);
            var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null || drop.m_itemData.m_shared.m_food <= 0f && drop.m_itemData.m_shared.m_foodStamina <= 0f)
            {
                TestLabPlugin.Log?.LogWarning($"Combat food: '{name}' is not a food item; skipped.");
                continue;
            }
            var item = drop.m_itemData.Clone();
            item.m_dropPrefab = prefab;
            item.m_stack = 1;
            if (!p.EatFood(item))
                TestLabPlugin.Log?.LogWarning($"Combat food: {p.GetPlayerName()} could not eat '{name}'.");
        }
        UpdateFood?.Invoke(p, new object[] { 0f, true });      // max health and stamina from the foods now, not on the next tick
        p.SetHealth(p.GetMaxHealth());
        StaminaField?.SetValue(p, p.GetMaxStamina());
    }

    /// <summary>Base skill for a tier from [Combat] TierSkills ("meadows=1, bronze=15, ..."); unknown tier = 1.</summary>
    public static float TierBase(string tier)
    {
        foreach (var part in TestLabConfig.Instance.CombatTierSkills.Value.Split(','))
        {
            var kv = part.Split('=');
            if (kv.Length == 2 && kv[0].Trim().Equals(tier.Trim(), StringComparison.OrdinalIgnoreCase)
                && float.TryParse(kv[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                return Mathf.Clamp(v, 0f, 100f);
        }
        if (tier.Length > 0) TestLabPlugin.Log?.LogWarning($"Combat skills: tier '{tier}' is not in [Combat] TierSkills; using 1.");
        return 1f;
    }

    /// <summary>
    /// The loadout's own skills when it names any. Otherwise every skill at the tier's base (a player who has reached that
    /// biome), and the equipped weapon's skill and Blocking moved by [Combat] SkillPerQuality per weapon level from 2.
    /// Returns what was set apart from the base, for the log.
    /// </summary>
    public static string SetSkills(Player p, Loadout l)
    {
        var skills = p.GetSkills();
        var set = new Dictionary<string, float>();
        var baseLevel = -1f;
        if (l.Skills.Count > 0)
            foreach (var kv in l.Skills) set[kv.Key] = kv.Value;
        else
        {
            baseLevel = TierBase(l.Tier);
            foreach (Skills.SkillType t in Enum.GetValues(typeof(Skills.SkillType)))
                if (t != Skills.SkillType.None && t != Skills.SkillType.All) set[t.ToString()] = baseLevel;
            var step = TestLabConfig.Instance.CombatSkillPerQuality.Value;
            foreach (var it in l.Items.Where(i => i.Equip))
            {
                var prefab = ObjectDB.instance?.GetItemPrefab(it.Prefab);
                var shared = prefab != null ? prefab.GetComponent<ItemDrop>()?.m_itemData.m_shared : null;
                if (shared == null) continue;
                var level = Mathf.Clamp(baseLevel + (Mathf.Max(1, it.Quality) - 2) * step, 1f, 100f);
                if (shared.m_itemType == ItemDrop.ItemData.ItemType.Shield)
                    set["Blocking"] = level;
                else if (shared.m_skillType != Skills.SkillType.None && IsWeapon(shared.m_itemType))
                {
                    set[shared.m_skillType.ToString()] = level;
                    if (!l.Items.Any(x => x.Equip && IsShield(x.Prefab))) set["Blocking"] = level;   // parrying with the weapon uses Blocking too
                }
            }
        }
        foreach (var kv in set)
        {
            try { skills.CheatResetSkill(kv.Key); if (kv.Value > 0f) skills.CheatRaiseSkill(kv.Key, kv.Value, false); }
            catch (Exception ex) { TestLabPlugin.Log?.LogWarning($"Combat skills: '{kv.Key}': {ex.Message}"); }
        }
        var special = set.Where(kv => baseLevel < 0f || Math.Abs(kv.Value - baseLevel) > 0.01f).Select(kv => $"{kv.Key} {kv.Value:0}");
        var parts = new List<string>();
        if (baseLevel >= 0f) parts.Add($"all {baseLevel:0} ({l.Tier})");
        parts.AddRange(special);
        return string.Join(", ", parts);
    }

    private static bool IsShield(string prefabName)
    {
        var prefab = ObjectDB.instance?.GetItemPrefab(prefabName);
        return prefab != null && prefab.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield;
    }

    private static bool IsWeapon(ItemDrop.ItemData.ItemType t) =>
        t is ItemDrop.ItemData.ItemType.OneHandedWeapon or ItemDrop.ItemData.ItemType.TwoHandedWeapon
            or ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft or ItemDrop.ItemData.ItemType.Bow;
}

/// <summary>During a combat run skills still rise, silently: no "skill increased" message, no level-up flash. Does nothing outside a run.</summary>
[HarmonyPatch]
internal static class CombatPrep_Mute_SkillUpMessage
{
    private static MethodBase? TargetMethod() => AccessTools.Method(typeof(Player), "Message");   // Player overrides Character.Message

    private static bool Prepare() => TargetMethod() != null;

    private static bool Prefix(object[] __args) =>
        !CombatRunner.Running || __args.Length < 2 || __args[1] is not string msg || !msg.StartsWith("$msg_skillup");
}

[HarmonyPatch(typeof(Player), "OnSkillLevelup")]
internal static class CombatPrep_Mute_SkillLevelupEffect
{
    private static bool Prefix() => !CombatRunner.Running;
}

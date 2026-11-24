using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DrakesTestLab.Cases;
using UnityEngine;

namespace DrakesTestLab.Combat;

/// <summary>
/// Writes the game's own combat numbers to combat/gamedata.json: every weapon, armor piece, shield and food (damage by
/// type and per upgrade level, armor, block power, parry bonus, backstab bonus, attack stamina and range, food values)
/// and every creature (health, resistances by damage type, the attacks of its own weapons). The difficulty model uses
/// these with the calibration fights to calculate combinations nobody fought. Includes modded items and creatures,
/// so a modded game gets its own table. Started by -testlab-combat-dump.
/// </summary>
internal static class CombatGameData
{
    public static string FilePath => Path.Combine(CombatData.Folder, "gamedata.json");

    public static string Write()
    {
        var items = new Dictionary<string, object?>();
        foreach (var go in ObjectDB.instance.m_items)
        {
            var drop = go != null ? go.GetComponent<ItemDrop>() : null;
            if (drop == null) continue;
            var sh = drop.m_itemData.m_shared;
            var t = sh.m_itemType;
            var isWeapon = t is ItemDrop.ItemData.ItemType.OneHandedWeapon or ItemDrop.ItemData.ItemType.TwoHandedWeapon
                or ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft or ItemDrop.ItemData.ItemType.Bow or ItemDrop.ItemData.ItemType.Torch;
            var isArmor = t is ItemDrop.ItemData.ItemType.Chest or ItemDrop.ItemData.ItemType.Legs or ItemDrop.ItemData.ItemType.Helmet
                or ItemDrop.ItemData.ItemType.Shoulder;
            var isShield = t == ItemDrop.ItemData.ItemType.Shield;
            var isFood = sh.m_food > 0f || sh.m_foodStamina > 0f;
            if (!isWeapon && !isArmor && !isShield && !isFood) continue;
            var e = new Dictionary<string, object?> { ["type"] = t.ToString(), ["name"] = sh.m_name, ["maxQuality"] = sh.m_maxQuality };
            if (isWeapon || isShield)
            {
                e["skill"] = sh.m_skillType.ToString();
                e["damage"] = Damages(sh.m_damages);
                e["damagePerLevel"] = Damages(sh.m_damagesPerLevel);
                e["backstab"] = R(sh.m_backstabBonus);
                e["attackForce"] = R(sh.m_attackForce);
                e["primary"] = AttackInfo(sh.m_attack);
                e["secondary"] = AttackInfo(sh.m_secondaryAttack);
            }
            if (isWeapon || isShield)
            {
                e["blockPower"] = R(sh.m_blockPower);
                e["blockPowerPerLevel"] = R(sh.m_blockPowerPerLevel);
                e["parryBonus"] = R(sh.m_timedBlockBonus);
                e["deflection"] = R(sh.m_deflectionForce);
            }
            if (isArmor)
            {
                e["armor"] = R(sh.m_armor);
                e["armorPerLevel"] = R(sh.m_armorPerLevel);
                e["movement"] = R(sh.m_movementModifier);
            }
            if (isFood)
            {
                e["health"] = R(sh.m_food);
                e["stamina"] = R(sh.m_foodStamina);
                e["eitr"] = R(sh.m_foodEitr);
                e["regen"] = R(sh.m_foodRegen);
                e["seconds"] = R(sh.m_foodBurnTime);
            }
            items[go.name] = e;
        }

        var creatures = new Dictionary<string, object?>();
        foreach (var go in ZNetScene.instance.m_prefabs)
        {
            var c = go != null ? go.GetComponent<Character>() : null;
            if (c == null || c is Player) continue;
            var e = new Dictionary<string, object?>
            {
                ["name"] = c.m_name, ["faction"] = c.m_faction.ToString(), ["health"] = R(c.m_health), ["boss"] = c.IsBoss(),
                ["resist"] = Modifiers(c.m_damageModifiers)
            };
            if (c is Humanoid h)
            {
                var weapons = new List<object?>();
                // own weapons: default items, random weapons, and random item sets (trolls, golems use sets)
                var sets = (h.m_randomSets ?? Array.Empty<Humanoid.ItemSet>()).SelectMany(set => set.m_items ?? Array.Empty<GameObject>());
                foreach (var w in (h.m_defaultItems ?? Array.Empty<GameObject>()).Concat(h.m_randomWeapon ?? Array.Empty<GameObject>()).Concat(sets).Distinct())
                {
                    var wd = w != null ? w.GetComponent<ItemDrop>() : null;
                    if (wd == null) continue;
                    var ws = wd.m_itemData.m_shared;
                    if (ws.m_damages.GetTotalDamage() <= 0f) continue;
                    weapons.Add(new Dictionary<string, object?> { ["item"] = w.name, ["damage"] = Damages(ws.m_damages), ["attack"] = AttackInfo(ws.m_attack) });
                }
                e["attacks"] = weapons;
            }
            creatures[go.name] = e;
        }

        var doc = new Dictionary<string, object?>
        {
            ["about"] = "DrakesTestLab Combat Lab: the game's own combat numbers (items and creatures, modded ones included), for the difficulty model.",
            ["game"] = Version.GetVersionString(), ["when"] = DateTime.Now.ToString("s"),
            ["items"] = items, ["creatures"] = creatures
        };
        Directory.CreateDirectory(CombatData.Folder);
        File.WriteAllText(FilePath, JsonWriter.Write(doc));
        TestLabPlugin.Log?.LogInfo($"Combat: game data written ({items.Count} items, {creatures.Count} creatures): {FilePath}");
        return FilePath;
    }

    private static double R(float v) => Math.Round(v, 3);

    private static Dictionary<string, object?> Damages(HitData.DamageTypes d)
    {
        var m = new Dictionary<string, object?>();
        void Add(string n, float v) { if (Math.Abs(v) > 0.001f) m[n] = R(v); }
        Add("blunt", d.m_blunt); Add("slash", d.m_slash); Add("pierce", d.m_pierce); Add("chop", d.m_chop); Add("pickaxe", d.m_pickaxe);
        Add("fire", d.m_fire); Add("frost", d.m_frost); Add("lightning", d.m_lightning); Add("poison", d.m_poison); Add("spirit", d.m_spirit);
        return m;
    }

    private static Dictionary<string, object?> Modifiers(HitData.DamageModifiers d) => new()
    {
        ["blunt"] = d.m_blunt.ToString(), ["slash"] = d.m_slash.ToString(), ["pierce"] = d.m_pierce.ToString(), ["chop"] = d.m_chop.ToString(),
        ["pickaxe"] = d.m_pickaxe.ToString(), ["fire"] = d.m_fire.ToString(), ["frost"] = d.m_frost.ToString(), ["lightning"] = d.m_lightning.ToString(),
        ["poison"] = d.m_poison.ToString(), ["spirit"] = d.m_spirit.ToString()
    };

    private static Dictionary<string, object?>? AttackInfo(Attack? a) => a == null ? null : new Dictionary<string, object?>
    {
        ["type"] = a.m_attackType.ToString(), ["animation"] = a.m_attackAnimation, ["stamina"] = R(a.m_attackStamina),
        ["multiplier"] = R(a.m_damageMultiplier), ["range"] = R(a.m_attackRange), ["angle"] = R(a.m_attackAngle)
    };
}

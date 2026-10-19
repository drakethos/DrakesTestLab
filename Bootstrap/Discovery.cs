using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace DrakesTestLab.Bootstrap;

/// <summary>Unlocks (or wipes) what the character has discovered, without spamming a popup per item.</summary>
internal static class Discovery
{
    public static readonly string[] TierNames =
        { "Meadows", "Black Forest", "Swamp", "Mountain", "Plains", "Mistlands", "Ashlands" };

    // Rough progression gates. An item/recipe's tier is the highest tier among its ingredients (and the recipes that
    // make those ingredients), so a hint list of the raw materials per biome is enough.
    private static readonly string[][] TierHints =
    {
        new string[0],
        new[] { "Copper", "Tin", "Bronze", "Crystal", "TrollHide", "AncientSeed", "Coal", "SurtlingCore", "ElderBark" },
        new[] { "Iron", "Chain", "Bloodbag", "Entrails", "Ooze", "Guck", "WitheredBone", "Root", "Ironscrap", "Thistle" },
        new[] { "Silver", "Obsidian", "WolfPelt", "WolfFang", "WolfClaw", "FreezeGland", "DragonTear", "DragonEgg", "Crystal" },
        new[] { "BlackMetal", "Flax", "Barley", "LoxPelt", "Tar", "Needle", "Cloudberry", "Linen", "Bone" },
        new[] { "Eitr", "Carapace", "Softtissue", "YggdrasilWood", "Sap", "BlackMarble", "RoyalJelly", "Mistlands", "Mushroom_Magecap" },
        new[] { "Flametal", "Charred", "MoltenCore", "Grausten", "Ask", "Fiery", "Lava", "FlametalOre" },
    };

    private static readonly FieldInfo? KnownRecipes = AccessTools.Field(typeof(Player), "m_knownRecipes");
    private static readonly FieldInfo? KnownMaterial = AccessTools.Field(typeof(Player), "m_knownMaterial");
    private static readonly FieldInfo? KnownStations = AccessTools.Field(typeof(Player), "m_knownStations");
    private static readonly FieldInfo? KnownBiome = AccessTools.Field(typeof(Player), "m_knownBiome");
    private static readonly MethodInfo? UpdateRecipes = AccessTools.Method(typeof(Player), "UpdateKnownRecipesList");
    private static readonly MethodInfo? UpdatePieces = AccessTools.Method(typeof(Player), "UpdateAvailablePiecesList");

    /// <summary>True while discovering, so the "New crafting recipe" popups for hundreds of unlocks are swallowed.</summary>
    public static bool Quiet { get; private set; }

    public static void DiscoverAll() => DiscoverUpToTier(TierHints.Length - 1);

    public static void Reset()
    {
        Player.m_localPlayer?.ResetCharacterKnownItems();
        Refresh();
    }

    /// <summary>Discovers every item, recipe, station and biome up to and including the given tier (0 = Meadows).</summary>
    public static int DiscoverUpToTier(int maxTier)
    {
        Quiet = true;
        try
        {
            return DiscoverCore(maxTier);
        }
        finally
        {
            Quiet = false;
        }
    }

    private static int DiscoverCore(int maxTier)
    {
        var player = Player.m_localPlayer;
        var db = ObjectDB.instance;
        if (player == null || db == null)
            return 0;

        var materials = KnownMaterial?.GetValue(player) as HashSet<string>;
        var recipes = KnownRecipes?.GetValue(player) as HashSet<string>;
        var stations = KnownStations?.GetValue(player) as Dictionary<string, int>;
        var biomes = KnownBiome?.GetValue(player) as HashSet<string>;

        var itemTier = new Dictionary<string, int>();
        var count = 0;

        foreach (var go in db.m_items)
        {
            var drop = go != null ? go.GetComponent<ItemDrop>() : null;
            if (drop == null)
                continue;

            var tier = RawTier(go!.name);
            itemTier[go.name] = tier;
            if (tier <= maxTier)
            {
                materials?.Add(drop.m_itemData.m_shared.m_name);
                count++;
            }
        }

        foreach (var recipe in db.m_recipes)
        {
            if (recipe == null || recipe.m_item == null)
                continue;

            var tier = 0;
            foreach (var req in recipe.m_resources)
            {
                if (req.m_resItem != null && itemTier.TryGetValue(req.m_resItem.gameObject.name, out var t) && t > tier)
                    tier = t;
            }

            if (recipe.m_craftingStation != null)
                tier = Math.Max(tier, StationTier(recipe.m_craftingStation.name));

            tier = Math.Max(tier, RawTier(recipe.m_item.gameObject.name));
            if (tier > maxTier)
                continue;

            recipes?.Add(recipe.m_item.m_itemData.m_shared.m_name);
            if (recipe.m_craftingStation != null && stations != null)
                stations[recipe.m_craftingStation.m_name] = Math.Max(
                    stations.TryGetValue(recipe.m_craftingStation.m_name, out var lvl) ? lvl : 1,
                    recipe.m_minStationLevel);
        }

        if (biomes != null)
        {
            for (var i = 0; i <= maxTier && i < TierBiomeNames.Length; i++)
            {
                foreach (var b in TierBiomeNames[i])
                    biomes.Add(b);
            }
        }

        Refresh();
        TestLabPlugin.Log?.LogInfo($"Discovered content up to tier {maxTier} ({count} items).");
        return count;
    }

    private static readonly string[][] TierBiomeNames =
    {
        new[] { "Meadows" }, new[] { "BlackForest" }, new[] { "Swamp" }, new[] { "Mountain" },
        new[] { "Plains" }, new[] { "Mistlands" }, new[] { "AshLands" }
    };

    private static int RawTier(string prefabName)
    {
        for (var tier = TierHints.Length - 1; tier >= 1; tier--)
        {
            foreach (var hint in TierHints[tier])
            {
                if (prefabName.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                    return tier;
            }
        }

        return 0;
    }

    private static int StationTier(string stationName)
    {
        if (stationName.IndexOf("blackforge", StringComparison.OrdinalIgnoreCase) >= 0) return 6;
        if (stationName.IndexOf("magetable", StringComparison.OrdinalIgnoreCase) >= 0) return 5;
        if (stationName.IndexOf("artisan", StringComparison.OrdinalIgnoreCase) >= 0) return 4;
        if (stationName.IndexOf("stonecutter", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
        if (stationName.IndexOf("forge", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
        return 0;
    }

    /// <summary>Rebuilds the recipe and build-piece lists after the known sets changed.</summary>
    public static void Refresh()
    {
        var player = Player.m_localPlayer;
        if (player == null)
            return;

        UpdateRecipes?.Invoke(player, null);
        UpdatePieces?.Invoke(player, null);
    }
}

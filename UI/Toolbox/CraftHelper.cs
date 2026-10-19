using System;
using System.Reflection;
using DrakesTestLab.Bootstrap;
using DrakesTestLab.Util;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.UI.Toolbox;

/// <summary>Small overlay shown next to the crafting window / build menu: station level, cost, "give materials".</summary>
internal static class CraftHelper
{
    private const int WindowId = 0x7E58;

    private static readonly FieldInfo? SelectedRecipeField = AccessTools.Field(typeof(InventoryGui), "m_selectedRecipe");
    private static Rect _rect = new(0f, 90f, 250f, 10f);
    private static bool _placed;

    public static bool ShouldShow()
    {
        var p = Player.m_localPlayer;
        if (p == null)
            return false;

        if (InventoryGui.IsVisible())
            return true;

        // build menu: cursor is only free while the piece picker is open
        return Cursor.visible && p.InPlaceMode();
    }

    public static void Draw(float scale)
    {
        if (!_placed)
        {
            _rect.x = Screen.width / scale - 270f;
            _placed = true;
        }

        _rect = GUILayout.Window(WindowId, _rect, _ => DrawContents(), "Test helper", GUILayout.Width(250));
    }

    private static void DrawContents()
    {
        var p = Player.m_localPlayer;
        if (p == null)
            return;

        GUILayout.Label(TestRules.StationLevel > 0
            ? $"Station level: <b>{TestRules.StationLevel}</b> (override)"
            : "Station level: vanilla");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Off")) TestRules.StationLevel = 0;
        if (GUILayout.Button("-")) TestRules.StationLevel = Mathf.Max(0, TestRules.StationLevel - 1);
        if (GUILayout.Button("+")) TestRules.StationLevel = Mathf.Min(20, TestRules.StationLevel + 1);
        GUILayout.EndHorizontal();

        GUILayout.Label($"Cost: <b>{TestRules.CostMultiplier:0.##}x</b>");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Free")) TestRules.CostMultiplier = 0f;
        if (GUILayout.Button("-")) TestRules.CostMultiplier = Mathf.Max(0f, TestRules.CostMultiplier - 0.25f);
        if (GUILayout.Button("+")) TestRules.CostMultiplier += 0.25f;
        if (GUILayout.Button("1x")) TestRules.CostMultiplier = 1f;
        GUILayout.EndHorizontal();

        if (InventoryGui.IsVisible() && TrySelectedRecipe(out var recipe, out var quality))
        {
            GUILayout.Label($"Selected: {recipe.m_item.m_itemData.m_shared.m_name}");
            if (GUILayout.Button("Give materials for this"))
                GiveFor(recipe.m_resources, quality);
        }
        else if (p.InPlaceMode() && p.GetSelectedPiece() is { } piece)
        {
            GUILayout.Label($"Selected: {piece.m_name}");
            if (GUILayout.Button("Give materials for this piece"))
                GiveFor(piece.m_resources, 1);
        }

        GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
    }

    private static void GiveFor(Piece.Requirement[] requirements, int quality)
    {
        foreach (var req in requirements)
        {
            if (req.m_resItem == null)
                continue;

            var amount = req.GetAmount(quality);
            if (amount > 0)
                ItemGiver.Give(req.m_resItem.gameObject.name, amount);
        }
    }

    /// <summary>Reads InventoryGui's private selected recipe (a struct of Recipe + ItemData).</summary>
    private static bool TrySelectedRecipe(out Recipe recipe, out int quality)
    {
        recipe = null!;
        quality = 1;
        var gui = InventoryGui.instance;
        if (gui == null || SelectedRecipeField == null)
            return false;

        var pair = SelectedRecipeField.GetValue(gui);
        if (pair == null)
            return false;

        var type = pair.GetType();
        var r = (type.GetField("Recipe") ?? (MemberInfo?)type.GetProperty("Recipe")) switch
        {
            FieldInfo f => f.GetValue(pair) as Recipe,
            PropertyInfo pr => pr.GetValue(pair, null) as Recipe,
            _ => null
        };
        if (r == null || r.m_item == null)
            return false;

        var data = (type.GetField("ItemData") ?? (MemberInfo?)type.GetProperty("ItemData")) switch
        {
            FieldInfo f => f.GetValue(pair) as ItemDrop.ItemData,
            PropertyInfo pr => pr.GetValue(pair, null) as ItemDrop.ItemData,
            _ => null
        };

        recipe = r;
        quality = data != null ? data.m_quality + 1 : 1;
        return true;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DrakesTestLab.Automation;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Patches;

// What the recorder listens to. Every hook returns at once unless a recording is running (Recorder.Active), and never changes
// what the game does. Methods whose signature differs between game builds are bound by name (TargetMethod) and skipped when
// missing.

/// <summary>Crafting at a station or from the inventory: a craft step (real cost unless no-cost mode is on).</summary>
[HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
internal static class RecordCraft
{
    private static readonly FieldInfo? CraftRecipe = AccessTools.Field(typeof(InventoryGui), "m_craftRecipe");

    private static void Prefix(InventoryGui __instance, Player player, out (Recipe? recipe, int count) __state)
    {
        var recipe = Recorder.Active ? CraftRecipe?.GetValue(__instance) as Recipe : null;
        __state = (recipe, recipe != null && recipe.m_item != null ? player.GetInventory().CountItems(recipe.m_item.m_itemData.m_shared.m_name) : 0);
    }

    private static void Postfix(Player player, (Recipe? recipe, int count) __state)
    {
        var recipe = __state.recipe;
        if (recipe == null || recipe.m_item == null || !Recorder.Active)
            return;
        if (player.GetInventory().CountItems(recipe.m_item.m_itemData.m_shared.m_name) <= __state.count)
            return;

        var step = new Dictionary<string, object?> { ["do"] = "craft", ["prefab"] = recipe.m_item.gameObject.name };
        if (!player.NoCostCheat())
        {
            step["free"] = false;
            if (player.GetCurrentCraftingStation() is { } station)
            {
                Recorder.Pose();
                step["station"] = Recorder.NameFor(station.gameObject);
            }
        }

        Recorder.Add(step);
    }
}

/// <summary>Building with the hammer: a place step, the new piece tagged (chest1, door1...).</summary>
[HarmonyPatch]
internal static class RecordPlace
{
    private static MethodBase? TargetMethod() => AccessTools.Method(typeof(Player), "PlacePiece");

    private static bool Prepare() => TargetMethod() != null;

    private static void Prefix(out HashSet<int> __state)
    {
        __state = Recorder.Active ? new HashSet<int>(UnityEngine.Object.FindObjectsOfType<Piece>().Select(p => p.GetInstanceID())) : new HashSet<int>();
    }

    private static void Postfix(Player __instance, object[] __args, HashSet<int> __state)
    {
        if (!Recorder.Active || __instance != Player.m_localPlayer || __args.Length == 0 || __args[0] is not Piece prefab)
            return;
        // the piece that appeared: same prefab, not there before, closest to the player
        Piece? made = null;
        var best = 30f;
        foreach (var piece in UnityEngine.Object.FindObjectsOfType<Piece>())
        {
            if (__state.Contains(piece.GetInstanceID()) || Utils.GetPrefabName(piece.gameObject) != prefab.name)
                continue;
            var d = Vector3.Distance(piece.transform.position, __instance.transform.position);
            if (d < best) { best = d; made = piece; }
        }

        if (made == null)
            return;
        Recorder.Pose();
        var (dx, _, dz, yaw) = Cases.CaseCapture.Local(__instance, made.transform);
        var step = new Dictionary<string, object?> { ["do"] = "place", ["prefab"] = prefab.name, ["dz"] = Recorder.Round(dz), ["dx"] = Recorder.Round(dx) };
        if (Mathf.Abs(yaw) > 1f)
            step["yaw"] = Recorder.Round(yaw);
        if (!__instance.NoCostCheat())
            step["free"] = false;
        step["as"] = Recorder.Tag(made.gameObject);
        Recorder.Add(step);
    }
}

/// <summary>Pressing E (or Shift+E, hold E) on something: an interact step on its tag or prefab.</summary>
[HarmonyPatch]
internal static class RecordInteract
{
    private static MethodBase? TargetMethod() => AccessTools.Method(typeof(Player), "Interact", new[] { typeof(GameObject), typeof(bool), typeof(bool) });

    private static bool Prepare() => TargetMethod() != null;

    private static void Postfix(Player __instance, GameObject go, bool hold, bool alt)
    {
        if (!Recorder.Active || __instance != Player.m_localPlayer || go == null)
            return;
        // holding E repeats every frame: one step
        if (hold && Recorder.Steps.Count > 0 && Recorder.Steps[Recorder.Steps.Count - 1] is { } last && Equals(last["do"], "interact") && Equals(last["hold"], true))
            return;
        Recorder.Pose();
        var step = new Dictionary<string, object?> { ["do"] = "interact", ["target"] = Recorder.NameFor(go) };
        if (alt) step["alt"] = true;
        if (hold) step["hold"] = true;
        Recorder.Add(step);
    }
}

/// <summary>Right-clicking an inventory slot (with Shift / Ctrl / Alt held): an itemClick step.</summary>
[HarmonyPatch(typeof(InventoryGui), "OnRightClickItem")]
internal static class RecordItemClick
{
    private static void Prefix(InventoryGrid grid, ItemDrop.ItemData item)
    {
        var p = Player.m_localPlayer;
        if (!Recorder.Active || item == null || item.m_dropPrefab == null || p == null || grid == null || grid.GetInventory() != p.GetInventory())
            return;
        var stacks = p.GetInventory().GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == item.m_dropPrefab.name)
            .OrderBy(i => i.m_gridPos.y).ThenBy(i => i.m_gridPos.x).ToList();
        var step = new Dictionary<string, object?> { ["do"] = "itemClick", ["prefab"] = item.m_dropPrefab.name };
        var index = stacks.IndexOf(item);
        if (index > 0) step["index"] = index;
        var mods = new List<string>();
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) mods.Add("Shift");
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) mods.Add("Ctrl");
        if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) mods.Add("Alt");
        if (mods.Count > 0) step["mods"] = string.Join("+", mods);
        Recorder.Add(step);
    }
}

/// <summary>Dropping a stack on the ground: a drop step, the item tagged.</summary>
[HarmonyPatch]
internal static class RecordDrop
{
    private static MethodBase? TargetMethod() =>
        AccessTools.Method(typeof(Humanoid), "DropItem", new[] { typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int) });

    private static bool Prepare() => TargetMethod() != null;

    private static void Prefix(Humanoid __instance, ItemDrop.ItemData item, int amount)
    {
        if (!Recorder.Active || __instance != Player.m_localPlayer || item == null || item.m_dropPrefab == null)
            return;
        Recorder.Pose();
        var step = new Dictionary<string, object?> { ["do"] = "drop", ["prefab"] = item.m_dropPrefab.name };
        Recorder.Add(step);
    }
}

/// <summary>Moving items into or out of an open chest: store / take steps, worked out when the chest closes.</summary>
[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
internal static class RecordContainerOpen
{
    internal static Container? Open;
    internal static Dictionary<string, int> Before = new();

    internal static Dictionary<string, int> Count(Inventory inv) =>
        inv.GetAllItems().Where(i => i.m_dropPrefab != null).GroupBy(i => i.m_dropPrefab.name).ToDictionary(g => g.Key, g => g.Sum(i => i.m_stack));

    private static void Postfix(Container container)
    {
        if (!Recorder.Active || container == null || container.GetInventory() == null)
            return;
        Open = container;
        Before = Count(container.GetInventory());
    }
}

[HarmonyPatch]
internal static class RecordContainerClose
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        if (AccessTools.Method(typeof(InventoryGui), "CloseContainer") is { } close) yield return close;
        if (AccessTools.Method(typeof(InventoryGui), nameof(InventoryGui.Hide)) is { } hide) yield return hide;
    }

    private static void Prefix()
    {
        var container = RecordContainerOpen.Open;
        RecordContainerOpen.Open = null;
        if (container == null || !Recorder.Active || container.GetInventory() == null)
            return;
        var after = RecordContainerOpen.Count(container.GetInventory());
        var name = Recorder.NameFor(container.gameObject);
        foreach (var prefab in after.Keys.Union(RecordContainerOpen.Before.Keys))
        {
            after.TryGetValue(prefab, out var now);
            RecordContainerOpen.Before.TryGetValue(prefab, out var was);
            if (now > was)
                Recorder.Add(new Dictionary<string, object?> { ["do"] = "store", ["target"] = name, ["prefab"] = prefab, ["count"] = now - was });
            else if (now < was)
                Recorder.Add(new Dictionary<string, object?> { ["do"] = "take", ["target"] = name, ["prefab"] = prefab, ["count"] = was - now });
        }
    }
}

/// <summary>Confirming the game's text prompt (a sign, a portal name): a type step that submits.</summary>
[HarmonyPatch(typeof(TextInput), "OnEnter")]
internal static class RecordTextInput
{
    private static readonly FieldInfo? InputField = AccessTools.Field(typeof(TextInput), "m_inputField");

    private static void Prefix(TextInput __instance)
    {
        if (!Recorder.Active)
            return;
        var field = InputField?.GetValue(__instance);
        var text = field?.GetType().GetProperty("text")?.GetValue(field) as string ?? "";
        Recorder.Add(new Dictionary<string, object?> { ["do"] = "type", ["field"] = "*", ["text"] = text, ["submit"] = true });
    }
}

/// <summary>Console commands (spawn, setkey...): a console step. The lab's own testlab.* commands are left out.</summary>
[HarmonyPatch(typeof(Terminal), "TryRunCommand")]
internal static class RecordConsole
{
    private static void Prefix(string text)
    {
        if (!Recorder.Active || string.IsNullOrWhiteSpace(text) || text.TrimStart().StartsWith("testlab", StringComparison.OrdinalIgnoreCase))
            return;
        Recorder.Pose();
        Recorder.Add(new Dictionary<string, object?> { ["do"] = "console", ["command"] = text.Trim() });
    }
}

/// <summary>Buttons in the game's own (Unity UI) windows: a click step on the button's text. Crafting buttons are left out (crafts are recorded).</summary>
[HarmonyPatch(typeof(UnityEngine.UI.Button), nameof(UnityEngine.UI.Button.OnPointerClick))]
internal static class RecordUnityClick
{
    private static void Postfix(UnityEngine.UI.Button __instance)
    {
        if (!Recorder.Active || __instance == null || __instance.GetComponentInParent<InventoryGui>() != null)
            return;
        var label = __instance.GetComponentInChildren<TMPro.TMP_Text>()?.text;
        if (string.IsNullOrWhiteSpace(label))
            label = __instance.gameObject.name;
        Recorder.Add(new Dictionary<string, object?> { ["do"] = "click", ["button"] = label!.Trim() });
    }
}

/// <summary>Closing the inventory / chest window: a closeUi step, so the replay is not left with a window open.</summary>
[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
internal static class RecordCloseWindow
{
    private static void Prefix(out bool __state) => __state = Recorder.Active && InventoryGui.IsVisible();

    private static void Postfix(bool __state)
    {
        // the game still reports the window as visible for a frame after Hide, so the call itself is the signal
        if (__state)
            Recorder.Add(new Dictionary<string, object?> { ["do"] = "closeUi" });
    }
}

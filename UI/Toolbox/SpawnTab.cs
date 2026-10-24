using System.Collections.Generic;
using DrakesTestLab.Util;
using UnityEngine;

namespace DrakesTestLab.UI.Toolbox;

internal sealed class SpawnTab : ToolboxTab
{
    private static readonly string[] Kinds = { "Creatures", "Items", "Pieces", "Everything" };
    private static readonly string[] QuickCreatures =
        { "Boar", "Neck", "Greyling", "Deer", "Skeleton", "Draugr", "Troll", "Wolf", "Lox", "Goblin", "Serpent", "Greydwarf" };

    private int _kind;
    private string _filter = "";
    private string _selected = "";
    private int _amount = 1;
    private int _level = 1;
    private int _quality = 1;

    public override string Title => "Spawn";

    public override string Group => "Play";

    public void Select(string prefab) => _selected = prefab;

    public override void Draw()
    {
        if (ZNetScene.instance == null)
        {
            GUILayout.Label("No world loaded yet.");
            return;
        }

        PrefabIndex.Ensure();

        Ui.Header("Quick creatures (click to spawn)");
        var quick = new List<string>();
        var creatures = PrefabIndex.Of(PrefabKind.Creature);
        foreach (var name in QuickCreatures)
        {
            if (creatures.Contains(name))
                quick.Add(name);
        }

        Ui.Grid(quick, 4, n => { _selected = n; Spawn(); }, _selected);

        Ui.Header("Browse");
        _kind = GUILayout.Toolbar(_kind, Kinds);
        _filter = Ui.Field("Search", _filter, 60);

        var source = _kind switch
        {
            0 => PrefabIndex.Of(PrefabKind.Creature),
            1 => PrefabIndex.Of(PrefabKind.Item),
            2 => PrefabIndex.Of(PrefabKind.Piece),
            _ => PrefabIndex.All
        };
        var hits = PrefabIndex.Search(source, _filter, 24);
        Ui.Grid(hits, 3, n => _selected = n, _selected);
        GUILayout.Label(hits.Count >= 24 ? "Showing first 24 - refine the search." : $"{hits.Count} match(es)");

        Ui.Header($"Selected: {(_selected.Length == 0 ? "-" : _selected)}");
        _amount = Mathf.RoundToInt(Ui.Slider("Amount", _amount, 1, 50, "0"));
        _level = Mathf.RoundToInt(Ui.Slider("Creature level", _level, 1, 5, "0"));
        _quality = Mathf.RoundToInt(Ui.Slider("Item quality", _quality, 1, 4, "0"));

        GUI.enabled = _selected.Length > 0;
        Ui.Row(
            ("Spawn in front of me", Spawn),
            ("Give to inventory", () => ItemGiver.Give(_selected, _amount, _quality)),
            ("Copy name", () => Ui.Copy(_selected)));
        GUI.enabled = true;

        Ui.Header("Clean up");
        Ui.Row(
            ("Kill nearby creatures", () => GameRefs.RunConsoleCommand("killall")),
            ("Tame nearby", () => GameRefs.RunConsoleCommand("tame")),
            ("Remove dropped items", () => GameRefs.RunConsoleCommand("removedrops")));
    }

    private void Spawn() => GameRefs.RunConsoleCommand($"spawn {_selected} {_amount} {_level}");
}

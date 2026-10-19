using System.Collections.Generic;
using DrakesTestLab.Cases;
using DrakesTestLab.Bootstrap;
using UnityEngine;

namespace DrakesTestLab.UI.Toolbox;

internal sealed class CasesTab : ToolboxTab
{
    private List<string> _cases = new();
    private bool _loaded;
    private readonly Dictionary<string, string> _descriptions = new();

    public override string Title => "Cases";

    public override void Draw()
    {
        if (!_loaded)
            Reload();

        var cfg = TestLabConfig.Instance;
        GUILayout.Label($"Folder: {CaseRunner.Folder}");
        GUILayout.Label("A test case is a JSON file that sets up the world, player, items, skills and spawns in one go. " +
                        "Ask Claude to write one, drop it in the folder, then press Apply.");
        Ui.Row(
            ("Refresh list", Reload),
            ("Open folder", () => Application.OpenURL(CaseRunner.Folder)),
            ("Open the wiki (all fields)", () => Application.OpenURL("https://github.com/drakethos/DrakesTestLab/wiki/Test-Cases")));

        var reset = GUILayout.Toggle(cfg.CaseResetFirst.Value, "Reset state first (rules, modes, inventory, skills, discovery, boss keys, wards, identity) - cases never stack");
        if (reset != cfg.CaseResetFirst.Value)
        {
            cfg.CaseResetFirst.Value = reset;
            cfg.CaseResetFirst.ConfigFile.Save();
        }

        Ui.Row(("Reset state now", StateReset.Run));

        Ui.Header($"Cases ({_cases.Count})");
        foreach (var name in _cases)
        {
            GUILayout.BeginHorizontal();
            var isAuto = cfg.CaseOnLoad.Value == name;
            GUILayout.Label(isAuto ? $"<b>{name}</b>  <color=#80ff80>(auto on load)</color>" : name);
            GUI.enabled = !CaseRunner.Running;
            if (GUILayout.Button("Apply", GUILayout.Width(80)))
                CaseRunner.Run(name);
            GUI.enabled = true;
            if (GUILayout.Button(isAuto ? "Unset auto" : "Auto-load", GUILayout.Width(100)))
            {
                cfg.CaseOnLoad.Value = isAuto ? "" : name;
                cfg.CaseOnLoad.ConfigFile.Save();
            }

            GUILayout.EndHorizontal();

            if (_descriptions.TryGetValue(name, out var description) && description.Length > 0)
                GUILayout.Label($"<color=#999999>{description}</color>");
        }

        if (CaseRunner.LastResult.Length > 0)
            GUILayout.Label(CaseRunner.LastResult);

        Ui.Header("Startup");
        var auto = GUILayout.Toggle(cfg.AutoBuildArena.Value, "Build arena + test house on every load (when no auto case is set)");
        if (auto != cfg.AutoBuildArena.Value)
        {
            cfg.AutoBuildArena.Value = auto;
            cfg.AutoBuildArena.ConfigFile.Save();
        }
    }

    private void Reload()
    {
        CaseRunner.EnsureExamples();
        _cases = CaseRunner.List();
        _descriptions.Clear();
        foreach (var name in _cases)
            _descriptions[name] = CaseRunner.Describe(name);
        _loaded = true;
    }
}

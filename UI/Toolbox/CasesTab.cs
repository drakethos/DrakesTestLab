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

    public override string Group => "Scenario";

    private readonly CaptureOptions _capture = new();
    private string _saveName = "my-setup";
    private string _saveDescription = "";
    private string _saveMessage = "";

    public override void Draw()
    {
        if (!_loaded)
            Reload();

        var cfg = TestLabConfig.Instance;

        Ui.Header("Save the current setup as a case");
        Ui.Hint("Set the dials, fill your bag, build what you need, then save it all as a case you can apply again or use as a test's setup.");
        _saveName = Ui.Field("Name", _saveName, 90);
        _saveDescription = Ui.Field("Description", _saveDescription, 90);
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical();
        _capture.World = Ui.Toggle(_capture.World, "World (time, weather, keys, bosses)");
        _capture.Rules = Ui.Toggle(_capture.Rules, "Rules (stations, cost, carry, no-build, noise)");
        _capture.Modes = Ui.Toggle(_capture.Modes, "Modes (god, ghost, fly, no cost)");
        _capture.Identity = Ui.Toggle(_capture.Identity, "Who you play as");
        _capture.Position = Ui.Toggle(_capture.Position, "Where you stand");
        GUILayout.EndVertical();
        GUILayout.BeginVertical();
        _capture.Inventory = Ui.Toggle(_capture.Inventory, "Inventory (with crafter and item data)");
        _capture.Skills = Ui.Toggle(_capture.Skills, "Skills");
        _capture.Wards = Ui.Toggle(_capture.Wards, "Wards around you");
        _capture.Buildings = Ui.Toggle(_capture.Buildings, "Buildings around you (chests, signs)");
        _capture.ModSettings = Ui.Toggle(_capture.ModSettings, "Mod settings you changed");
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        if (_capture.Wards || _capture.Buildings)
            _capture.Radius = Mathf.Round(Ui.Slider("Around you (m)", _capture.Radius, 5f, 60f, "0"));
        GUILayout.BeginHorizontal();
        GUI.enabled = Player.m_localPlayer != null;
        if (Ui.PrimaryButton("Save as case", GUILayout.Width(140)))
            SaveCase();
        if (GUILayout.Button("Copy JSON", GUILayout.Width(110)))
            CopyCase();
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        if (_saveMessage.Length > 0)
            Ui.Hint(_saveMessage);

        Ui.Header("Cases");
        Ui.Hint("A case sets up the world, player, items, skills, wards, buildings and mod settings in one go.");
        Ui.Row(
            ("Refresh list", Reload),
            ("Copy folder path", () => Ui.Copy(CaseRunner.Folder)),
            ("All fields (wiki)", () => Application.OpenURL("https://github.com/drakethos/DrakesTestLab/wiki/Test-Cases")));

        var reset = GUILayout.Toggle(cfg.CaseResetFirst.Value, "Reset state first (rules, modes, inventory, skills, discovery, boss keys, wards, identity) - cases never stack");
        if (reset != cfg.CaseResetFirst.Value)
        {
            cfg.CaseResetFirst.Value = reset;
            cfg.CaseResetFirst.ConfigFile.Save();
        }

        Ui.Row(("Reset state now", StateReset.Run));
        GUILayout.Space(6);
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
                Ui.Hint(description);
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

    private void SaveCase()
    {
        try
        {
            var data = CaseCapture.Capture(_capture, _saveName, _saveDescription);
            var path = CaseCapture.Save(data, _saveName);
            _saveMessage = $"Saved {Summary(data)} to {path}";
            Reload();
        }
        catch (System.Exception ex)
        {
            _saveMessage = "Could not save: " + ex.Message;
        }
    }

    private void CopyCase()
    {
        try
        {
            var data = CaseCapture.Capture(_capture, _saveName, _saveDescription);
            Ui.Copy(JsonWriter.Write(data));
            _saveMessage = $"Copied {Summary(data)} to the clipboard.";
        }
        catch (System.Exception ex)
        {
            _saveMessage = "Could not copy: " + ex.Message;
        }
    }

    private static string Summary(Dictionary<string, object?> data)
    {
        int Count(string key) => data.TryGetValue(key, out var v) && v is System.Collections.ICollection c ? c.Count : 0;
        return $"{Count("items")} item stacks, {Count("pieces")} pieces, {Count("wards")} wards, {Count("configs")} mod settings";
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

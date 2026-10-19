using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Bootstrap;
using DrakesTestLab.Bootstrap;
using UnityEngine;

namespace DrakesTestLab.UI.Toolbox;

internal sealed class ModsTab : ToolboxTab
{
    private string _filter = "";

    public override string Title => "Mods";

    public override void Draw()
    {
        Ui.Header("Config & files");
        Ui.Row(
            ("Reload all mod configs", () => ConfigHotReload.ReloadNow()),
            ("Open config folder", () => Application.OpenURL(Paths.ConfigPath)),
            ("Open BepInEx log", () => Application.OpenURL(Path.Combine(Paths.BepInExRootPath, "LogOutput.log"))));

        var plugins = new List<PluginInfo>(Chainloader.PluginInfos.Values);
        plugins.Sort((a, b) => string.Compare(a.Metadata.Name, b.Metadata.Name, System.StringComparison.OrdinalIgnoreCase));

        Ui.Header($"Loaded plugins ({plugins.Count})");
        _filter = Ui.Field("Filter", _filter, 60);
        foreach (var info in plugins)
        {
            var m = info.Metadata;
            if (_filter.Length > 0
                && m.Name.IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) < 0
                && m.GUID.IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{m.Name}</b>  {m.Version}", GUILayout.Width(260));
            GUILayout.Label($"<color=#999999>{m.GUID}</color>");
            if (GUILayout.Button("Copy GUID", GUILayout.Width(90)))
                Ui.Copy(m.GUID);
            GUILayout.EndHorizontal();
        }
    }
}

using System.Collections.Generic;
using DrakesTestLab.Bootstrap;
using DrakesTestLab.Util;
using UnityEngine;

namespace DrakesTestLab.UI.Toolbox;

internal sealed class SessionTab : ToolboxTab
{
    private readonly List<string> _history = new();
    private string _command = "";

    public override string Title => "Session";

    public override void Draw()
    {
        var cfg = TestLabConfig.Instance;
        var target = SaveModeResolver.Resolve();

        Ui.Header("Reset");
        GUILayout.Label($"Mode: {target.Mode}\nWorld: {target.WorldName}  (template {target.TemplateWorldFile})\n" +
                        $"Character: {target.CharacterName}  ({(cfg.FreshCharacter.Value ? "fresh clean character" : "template " + target.TemplateCharacterFile)})");
        Ui.Row(
            ("Reset world + player (back to menu, straight into a fresh world)", SessionReset.Execute),
            ("Save world now", () =>
            {
                if (ZNet.instance != null)
                    ZNet.instance.Save(false);
            }));
        Ui.Row(("Quit game", Application.Quit), ("Documentation (wiki)", () => Application.OpenURL("https://github.com/drakethos/DrakesTestLab/wiki")));
        if (GUILayout.Button("Re-bake blank world template (generates a brand-new world, saves it as the template, quits)"))
        {
            TemplateBaker.Request();
            SessionReset.Execute();
        }

        Ui.Header("Startup behaviour (saved to config)");
        Toggle(cfg.KeepWorld, "KEEP the world between launches / resets (off = fresh world every time)");
        Toggle(cfg.KeepCharacter, "KEEP the character between launches / resets (off = fresh character every time)");
        Toggle(cfg.FreshCharacter, "New characters start clean (off = copy the template character instead)");
        Toggle(cfg.AutoEnableDevCommands, "Dev commands on at start");
        Toggle(cfg.AutoEnableDebugMode, "Debug mode on at start");
        Toggle(cfg.SkipIntro, "Skip the intro sequence");
        Toggle(cfg.ShowOverlay, "Show info overlay");

        GUILayout.BeginHorizontal();
        GUILayout.Label($"UI scale: {cfg.UiScale.Value:0.00}", GUILayout.Width(190));
        var scale = GUILayout.HorizontalSlider(cfg.UiScale.Value, 0.6f, 2.5f);
        if (!Mathf.Approximately(scale, cfg.UiScale.Value))
            cfg.UiScale.Value = scale;
        GUILayout.EndHorizontal();

        Ui.Header("Multiplayer (share the test world)");
        Ui.Hint("The game logs straight in, so the Start Game choices live here. They apply when the world starts: change them, then press Reset.");
        Toggle(cfg.OpenToFriends, "Open to friends (Steam: Friends list > Join game)");
        Toggle(cfg.PublicListing, "List in the community server browser (needs a password, 5+ characters)");
        Toggle(cfg.Crossplay, "Crossplay (PlayFab, join codes)");
        cfg.ServerPassword.Value = Ui.Field("Password", cfg.ServerPassword.Value, 90);
        GUILayout.Label($"Applied this launch: {ServerSettings.LastApplied}");
        var online = ServerSettings.OnlinePlayers();
        GUILayout.Label($"Players online ({online.Count}): " + (online.Count == 0 ? "-" : string.Join(", ", online)));
        Ui.Row(("Copy mod + version list for guests", () => Ui.Copy(ServerSettings.ModList())));
        Ui.Hint("Guests do not need this mod, only the same Valheim version (and any mods that change the world). Cheats stay host-only.");

        Ui.Header("Console");
        Ui.Hint("Runs any Valheim console command (the output shows in the F5 console).");
        GUILayout.BeginHorizontal();
        _command = GUILayout.TextField(_command);
        if (GUILayout.Button("Run", GUILayout.Width(70)) || EnterPressed())
            RunCommand();
        GUILayout.EndHorizontal();

        for (var i = _history.Count - 1; i >= 0 && i >= _history.Count - 8; i--)
        {
            if (GUILayout.Button(_history[i]))
                _command = _history[i];
        }
    }

    private void RunCommand()
    {
        var text = _command.Trim();
        if (text.Length == 0)
            return;

        GameRefs.RunConsoleCommand(text);
        _history.Remove(text);
        _history.Add(text);
        _command = "";
    }

    private static bool EnterPressed()
    {
        var e = Event.current;
        return e != null && e.type == EventType.KeyDown && e.keyCode == KeyCode.Return && GUI.GetNameOfFocusedControl() == "";
    }

    private static void Toggle(BepInEx.Configuration.ConfigEntry<bool> entry, string label)
    {
        var next = GUILayout.Toggle(entry.Value, label);
        if (next == entry.Value)
            return;

        entry.Value = next;
        if (TestLabConfig.Instance.PanelWritesConfig.Value)
            entry.ConfigFile.Save();
    }
}

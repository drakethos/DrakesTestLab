using System;
using System.Collections.Generic;
using System.Reflection;
using DrakesTestLab.Bootstrap;
using DrakesTestLab.Util;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.UI.Toolbox;

internal sealed class PlayerTab : ToolboxTab
{
    private static readonly FieldInfo? StaminaField = AccessTools.Field(typeof(Player), "m_stamina");

    private readonly List<string> _skills = new(Enum.GetNames(typeof(Skills.SkillType)));
    private string _skill = "";
    private float _skillAmount = 10f;
    private string _gotoX = "0";
    private string _gotoZ = "0";
    private Vector3? _savedPos;

    public static bool InfiniteStamina { get; set; }

    public override string Title => "Player";

    public override void Tick()
    {
        if (!InfiniteStamina || Player.m_localPlayer == null || StaminaField == null)
            return;

        StaminaField.SetValue(Player.m_localPlayer, Player.m_localPlayer.GetMaxStamina());
    }

    public override void Draw()
    {
        var p = Player.m_localPlayer;
        if (p == null)
        {
            GUILayout.Label("No player yet.");
            return;
        }

        if (!GameRefs.CheatsEnabled)
        {
            GUILayout.Label("<color=#ff8080>Dev commands are off - most buttons will do nothing.</color>");
            if (GUILayout.Button("Enable dev commands"))
                GameRefs.EnableCheats();
        }

        Ui.Header("Modes");
        Toggle("God mode", p.InGodMode(), "god");
        Toggle("Ghost mode (no aggro)", p.InGhostMode(), "ghost");
        Toggle("Fly", p.InDebugFlyMode(), "fly");
        Toggle("No build cost", p.NoCostCheat(), "nocost");

        var hostile = TestLabConfig.Instance.CreaturesHostile.Value;
        if (GUILayout.Toggle(hostile, "Creatures hostile") != hostile)
            TestControlsApplier.ToggleHostile();

        InfiniteStamina = GUILayout.Toggle(InfiniteStamina, "Infinite stamina");

        Ui.Row(
            ("Heal", () => GameRefs.RunConsoleCommand("heal")),
            ("Empty stomach", () => GameRefs.RunConsoleCommand("puke")),
            ("Free-fly camera", () => GameRefs.RunConsoleCommand("freefly")));

        Ui.Header("Kits");
        Ui.Row(
            ("Builder kit", GiveBuilderKit),
            ("Combat kit", GiveCombatKit),
            ("Clear inventory", () => p.GetInventory().RemoveAll()));
        Ui.Row(("Reset character data", () => GameRefs.RunConsoleCommand("resetcharacter")));

        Ui.Header("Skills");
        Ui.Grid(_skills, 4, n => _skill = n, _skill);
        _skillAmount = Ui.Slider("Raise by", _skillAmount, 1f, 100f, "0");
        GUI.enabled = _skill.Length > 0;
        Ui.Row(
            ("Raise selected", () => GameRefs.RunConsoleCommand($"raiseskill {_skill} {Mathf.RoundToInt(_skillAmount)}")),
            ("Max selected", () => GameRefs.RunConsoleCommand($"raiseskill {_skill} 100")));
        GUI.enabled = true;
        if (GUILayout.Button("Max all skills"))
        {
            foreach (var s in _skills)
            {
                if (s != "None" && s != "All")
                    GameRefs.RunConsoleCommand($"raiseskill {s} 100");
            }
        }

        Ui.Header("Teleport");
        GUILayout.BeginHorizontal();
        _gotoX = GUILayout.TextField(_gotoX, GUILayout.Width(90));
        _gotoZ = GUILayout.TextField(_gotoZ, GUILayout.Width(90));
        if (GUILayout.Button("Go to X,Z") && float.TryParse(_gotoX.Trim(), out var gx) && float.TryParse(_gotoZ.Trim(), out var gz))
            GameRefs.Teleport(gx, gz);
        GUILayout.EndHorizontal();
        Ui.Row(
            ("World origin", () => GameRefs.Teleport(0f, 0f)),
            ("Save position", () => _savedPos = p.transform.position),
            ("Back to saved", () =>
            {
                if (_savedPos is { } pos)
                    p.TeleportTo(pos, p.transform.rotation, false);
            }));
    }

    private static void Toggle(string label, bool current, string command)
    {
        if (GUILayout.Toggle(current, label) != current)
            GameRefs.RunConsoleCommand(command);
    }

    private static void GiveBuilderKit() => ItemGiver.GiveKit(
        ("Hammer", 1), ("Hoe", 1), ("Cultivator", 1), ("Wood", 300), ("FineWood", 150), ("RoundLog", 100),
        ("Stone", 300), ("Resin", 50), ("Coal", 100), ("Flint", 30), ("Iron", 100), ("BronzeNails", 200), ("IronNails", 200));

    private static void GiveCombatKit() => ItemGiver.GiveKit(
        ("SwordIron", 1), ("ShieldBronzeBuckler", 1), ("Bow", 1), ("ArrowWood", 100), ("ArrowIron", 50),
        ("ArmorIronChest", 1), ("ArmorIronLegs", 1), ("HelmetIron", 1), ("MeadHealthMedium", 10), ("CookedMeat", 20));
}

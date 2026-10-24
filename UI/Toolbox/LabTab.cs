using System;
using DrakesTestLab.Bootstrap;
using DrakesTestLab.Util;
using UnityEngine;

namespace DrakesTestLab.UI.Toolbox;

/// <summary>Crafting rules, carry weight, vitals, discovery and the flat test arena.</summary>
internal sealed class LabTab : ToolboxTab
{
    private string _hp = "500";
    private string _stamina = "500";
    private string _eitr = "500";
    private string _carry = "9999";
    private float _arenaRadius = 40f;
    private string _fireMessage = "";
    private string _bossMessage = "";

    public override string Title => "Rules";

    public override string Group => "Scenario";

    public override void Tick()
    {
        StatSetter.Tick();
        FireTools.Tick();
    }

    public override void Draw()
    {
        if (Player.m_localPlayer == null)
        {
            GUILayout.Label("No player yet.");
            return;
        }

        Ui.Header("Crafting stations");
        GUILayout.Label(TestRules.StationLevel > 0
            ? $"<color=#80ff80>Super stations ON: every station is level {TestRules.StationLevel}, no roof/fire needed.</color>"
            : "Stations behave like vanilla (needs extensions, roof and fire).");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Off")) TestRules.StationLevel = 0;
        if (GUILayout.Button("-")) TestRules.StationLevel = Mathf.Max(0, TestRules.StationLevel - 1);
        if (GUILayout.Button("+")) TestRules.StationLevel = Mathf.Min(20, TestRules.StationLevel + 1);
        if (GUILayout.Button("Max (7)")) TestRules.StationLevel = 7;
        GUILayout.EndHorizontal();

        Ui.Header("Recipe & build cost");
        TestRules.CostMultiplier = Ui.Slider("Cost multiplier", TestRules.CostMultiplier, 0f, 5f, "0.00x");
        Ui.Row(
            ("Free", () => TestRules.CostMultiplier = 0f),
            ("Half", () => TestRules.CostMultiplier = 0.5f),
            ("Normal", () => TestRules.CostMultiplier = 1f),
            ("Double", () => TestRules.CostMultiplier = 2f));

        Ui.Header("Carry weight");
        GUILayout.Label(TestRules.MaxCarryWeight > 0f ? $"Override: {TestRules.MaxCarryWeight:0}" : "Vanilla");
        GUILayout.BeginHorizontal();
        _carry = GUILayout.TextField(_carry, GUILayout.Width(100));
        if (GUILayout.Button("Set") && float.TryParse(_carry, out var cw))
            TestRules.MaxCarryWeight = Mathf.Max(0f, cw);
        if (GUILayout.Button("9999")) TestRules.MaxCarryWeight = 9999f;
        if (GUILayout.Button("Reset")) TestRules.MaxCarryWeight = 0f;
        GUILayout.EndHorizontal();

        Ui.Header("Vitals (max values stay pinned)");
        StatRow("Max health", ref _hp, StatSetter.SetMaxHealth, TestRules.MaxHealth);
        StatRow("Max stamina", ref _stamina, StatSetter.SetMaxStamina, TestRules.MaxStamina);
        StatRow("Max eitr", ref _eitr, StatSetter.SetMaxEitr, TestRules.MaxEitr);
        Ui.Row(
            ("Fill health/stamina/eitr", StatSetter.FillAll),
            ("Reset max stats", () =>
            {
                TestRules.MaxHealth = TestRules.MaxStamina = TestRules.MaxEitr = 0f;
            }));
        TestRules.RunSpeedMultiplier = Ui.Slider("Run speed", TestRules.RunSpeedMultiplier, 0.5f, 5f, "0.0x");
        TestRules.JumpMultiplier = Ui.Slider("Jump height", TestRules.JumpMultiplier, 0.5f, 5f, "0.0x");

        Ui.Header("Noise");
        CfgToggle(TestLabConfig.Instance.HideUnlockPopups, v => TestRules.HideUnlockPopups = v, "Hide 'new recipe / item / station' pop-ups");
        CfgToggle(TestLabConfig.Instance.HideTutorials, v => TestRules.HideTutorials = v, "Hide raven tutorials");
        CfgToggle(TestLabConfig.Instance.AutoDiscovery, v => TestRules.AutoDiscovery = v, "Auto-discovery (picking items up teaches recipes)");

        Ui.Header("Discovery");
        Ui.Row(
            ("Discover everything", Discovery.DiscoverAll),
            ("Forget everything", Discovery.Reset));
        GUILayout.Label("Discover up to biome (items, recipes, stations):");
        for (var i = 0; i < Discovery.TierNames.Length; i += 4)
        {
            GUILayout.BeginHorizontal();
            for (var j = i; j < Math.Min(i + 4, Discovery.TierNames.Length); j++)
            {
                var tier = j;
                if (GUILayout.Button(Discovery.TierNames[j]))
                    Discovery.DiscoverUpToTier(tier);
            }

            GUILayout.EndHorizontal();
        }

        Ui.Header("Test arena");
        GUILayout.Label(TestRules.HasArena
            ? $"Arena at {TestRules.ArenaCenter.x:0},{TestRules.ArenaCenter.z:0}  radius {TestRules.ArenaRadius:0}   build rule: {TestRules.BuildRule}"
            : "No arena built yet.");
        _arenaRadius = Ui.Slider("Radius", _arenaRadius, 15f, 90f, "0");
        Ui.Row(
            ("Find flat ground + build + go there", () => Run(ArenaBuilder.BuildAt(ArenaBuilder.FindFlatSite(Player.m_localPlayer.transform.position), true, true, _arenaRadius))),
            ("Flatten + house HERE", () => Run(ArenaBuilder.BuildAt(Player.m_localPlayer.transform.position, false, true, _arenaRadius))));
        Ui.Row(("Rebuild at the remembered spawn (clear + flatten, keep stones, house beside it)", RebuildAtSpawn));
        Ui.Row(
            ("Flatten here only", () => Run(ArenaBuilder.BuildAt(Player.m_localPlayer.transform.position, false, false, _arenaRadius))),
            ("Go to arena", GoToArena));

        Ui.Hint("Arena-only building rule (separate from the vanilla zones above):");
        GUILayout.BeginHorizontal();
        BuildRuleButton("Anywhere", BuildRule.Off);
        BuildRuleButton("Only inside arena", BuildRule.OnlyInsideArena);
        BuildRuleButton("Not inside arena", BuildRule.BlockInsideArena);
        GUILayout.EndHorizontal();

        Ui.Header("Vanilla no-build zones (spawn temple, altars...)");
        GUILayout.BeginHorizontal();
        NoBuildButton("Vanilla", NoBuildMode.Vanilla);
        NoBuildButton("Off (build anywhere)", NoBuildMode.Off);
        NoBuildButton("Scaled", NoBuildMode.Scaled);
        GUILayout.EndHorizontal();
        if (TestRules.NoBuild == NoBuildMode.Scaled)
        {
            TestRules.NoBuildScale = Ui.Slider("Zone size multiplier", TestRules.NoBuildScale, 0f, 4f, "0.00x");
            TestRules.NoBuildRadius = Ui.Slider("Or fixed radius (0 = use multiplier)", TestRules.NoBuildRadius, 0f, 100f, "0");
        }

        Ui.Header("Fall damage");
        TestRules.FallDamageMultiplier = Ui.Slider("Fall damage", TestRules.FallDamageMultiplier, 0f, 2f, "0.00x");
        TestRules.FallAutoSafe = GUILayout.Toggle(TestRules.FallAutoSafe, "Auto-safe: never hurt by a height you can jump (needs raised jump height)");
        TestRules.FallSafeExtra = Ui.Slider("Extra safe fall height", TestRules.FallSafeExtra, 0f, 100f, "0");
        GUILayout.Label($"Currently safe to fall: {TestRules.SafeFallHeight():0.#} m (vanilla 4)");
        Ui.Row(("No fall damage", () => TestRules.FallDamageMultiplier = 0f), ("Vanilla", () => TestRules.FallDamageMultiplier = 1f));

        Ui.Header("Fire & fuel (cauldron, mead ketill, spit, hearth)");
        FireTools.InfiniteFuel = GUILayout.Toggle(FireTools.InfiniteFuel, "Infinite fuel - keep every fire and fuel station full");
        Ui.Row(
            ("Refuel all fires now", () => _fireMessage = $"Refuelled {FireTools.Refuel()} fire(s)/station(s)."),
            ("Give 100 wood", () => ItemGiver.Give("Wood", 100)),
            ("Give 50 coal", () => ItemGiver.Give("Coal", 50)),
            ("Give 25 resin", () => ItemGiver.Give("Resin", 25)));
        GUILayout.Label(_fireMessage);

        Ui.Header("Bosses (world key + guardian power + trophy)");
        foreach (var boss in BossTools.All)
        {
            GUILayout.BeginHorizontal();
            var defeated = BossTools.IsDefeated(boss);
            GUILayout.Label(defeated ? $"<color=#80ff80>{boss.Name}</color>" : boss.Name, GUILayout.Width(110));
            if (GUILayout.Button(defeated ? "Defeated" : "Defeat", GUILayout.Width(80)))
                BossTools.Defeat(boss);
            if (GUILayout.Button("Undo", GUILayout.Width(55)))
                BossTools.Undo(boss);
            if (GUILayout.Button("Hook in nearest stone"))
                _bossMessage = BossTools.HookNearest(boss);
            GUILayout.EndHorizontal();
        }

        Ui.Row(
            ("Defeat all (keys only)", BossTools.DefeatAll),
            ("Clear all boss keys", BossTools.ClearAll));
        GUILayout.Label(_bossMessage);

        Ui.Header("Everything back to normal");
        if (GUILayout.Button("Reset all test rules (stations, cost, carry, stats, speed, build rule)"))
            TestRules.ResetRules();
    }

    private static void CfgToggle(BepInEx.Configuration.ConfigEntry<bool> entry, Action<bool> apply, string label)
    {
        var next = GUILayout.Toggle(entry.Value, label);
        if (next == entry.Value)
            return;
        entry.Value = next;
        apply(next);
        if (TestLabConfig.Instance.PanelWritesConfig.Value)
            entry.ConfigFile.Save();
    }

    private static void StatRow(string label, ref string text, Action<float> apply, float pinned)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{label}{(pinned > 0f ? $" [{pinned:0}]" : "")}", GUILayout.Width(190));
        text = GUILayout.TextField(text, GUILayout.Width(90));
        if (GUILayout.Button("Set") && float.TryParse(text, out var v))
            apply(Mathf.Max(1f, v));
        if (GUILayout.Button("Vanilla"))
            apply(0f);
        GUILayout.EndHorizontal();
    }

    private static void NoBuildButton(string label, NoBuildMode mode)
    {
        var old = GUI.backgroundColor;
        if (TestRules.NoBuild == mode)
            GUI.backgroundColor = new Color(0.95f, 0.65f, 0.25f);
        if (GUILayout.Button(label))
            TestRules.NoBuild = mode;
        GUI.backgroundColor = old;
    }

    private static void BuildRuleButton(string label, BuildRule rule)
    {
        var old = GUI.backgroundColor;
        if (TestRules.BuildRule == rule)
            GUI.backgroundColor = new Color(0.95f, 0.65f, 0.25f);
        if (GUILayout.Button(label))
            TestRules.BuildRule = rule;
        GUI.backgroundColor = old;
    }

    private void RebuildAtSpawn()
    {
        var p = Player.m_localPlayer;
        if (p == null)
            return;

        var cfg = TestLabConfig.Instance;
        var spawn = TestRules.SpawnPoint ?? p.transform.position;
        Run(ArenaBuilder.BuildAt(spawn, false, cfg.BuildHouse.Value, _arenaRadius,
            spawn + new Vector3(cfg.HouseOffsetX.Value, 0f, cfg.HouseOffsetZ.Value), cfg.KeepRadius.Value));
    }

    private static void GoToArena()
    {
        var p = Player.m_localPlayer;
        if (p != null && TestRules.HasArena)
            p.TeleportTo(TestRules.ArenaCenter + new Vector3(0f, 1.5f, -9f), p.transform.rotation, false);
    }

    private static void Run(System.Collections.IEnumerator routine)
    {
        if (ToolboxHost.Instance != null && !ArenaBuilder.Busy)
            ToolboxHost.Instance.StartCoroutine(routine);
    }
}

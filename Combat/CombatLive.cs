using System;
using System.Collections.Generic;
using System.Linq;

namespace DrakesTestLab.Combat;

/// <summary>What the Combat tab's "Fight it yourself" card asks for.</summary>
internal sealed class LiveSpec
{
    public bool Pvp;                    // false = a monster, true = Bjornita / Bjorn
    public string Foe = "Troll";
    public int Stars;
    public int Count = 1;
    public int Allies;                  // bot teammates beside you (monster fights)
    public string AllyBot = "fighter";
    public string Opponent = "Bjornita";
    public string OpponentBot = "fighter";
    public string OpponentGear = "";    // loadout name; "" = the same gear as yours
    public string Me = "you";           // "you" = you play; or a bot name (watch two bots)
    public string MyGear = "";          // loadout name; "" = the gear you have on now
    public int Fights = 3;
    public float Timeout = 180f;
    public string Scaling = "auto";
}

/// <summary>
/// Live fights from the Combat tab: you play, the opponent is a monster or a bot player. It is an ordinary combat
/// scenario run (same arena, recorder, report), built in memory, with your bot set to "you" so nothing drives you.
/// "My current gear" is captured first and put back on you when the session ends, because every fight starts from a
/// reset (empty inventory, fresh stats) like the simulator's.
/// </summary>
internal static class CombatLive
{
    public const string ScenarioName = "live-fight";
    public const string CurrentGear = "my current gear";

    public static LiveSpec? Last { get; private set; }
    private static Loadout? _restore;

    public static bool Start(LiveSpec spec)
    {
        if (CombatRunner.Running || Automation.TestRunner.Running) return false;
        var player = Player.m_localPlayer;
        if (player == null) return false;

        var mine = CaptureCurrent(player);
        CombatData.LiveLoadouts[CurrentGear] = mine;
        _restore = mine;
        var myGear = spec.MyGear.Length > 0 ? spec.MyGear : CurrentGear;

        var sc = new ScenarioSpec
        {
            Name = ScenarioName,
            Loadout = myGear,
            Runs = Math.Max(1, spec.Fights),
            Timeout = Math.Max(20f, spec.Timeout),
            Bot = spec.Me,
            AllyBot = spec.AllyBot,
            TimeScale = 1f,
            Lean = 0,
            AutoRuns = false,
            StallSeconds = spec.Me == "you" ? 0f : 15f,   // a person standing back is not a stuck fight
            Countdown = 3f,
            PlayerScaling = spec.Scaling,
            Variants = { new VariantSpec { Name = "live" } }
        };
        if (spec.Pvp)
            sc.Pvp = new PvpSpec { Name = spec.Opponent, Bot = spec.OpponentBot, Loadout = spec.OpponentGear.Length > 0 ? spec.OpponentGear : myGear };
        else
        {
            sc.Foes.Add(new FoeSpec { Prefab = spec.Foe, Stars = Math.Max(0, Math.Min(2, spec.Stars)), Count = Math.Max(1, spec.Count), Distance = 12f });
            if (spec.Allies > 0) sc.TeamFrom = sc.TeamTo = Math.Min(4, 1 + spec.Allies);
        }
        CombatData.LiveScenarios[ScenarioName] = sc;
        Last = spec;
        if (!CombatRunner.Start(new[] { ScenarioName })) { _restore = null; return false; }
        UI.Toolbox.ToolboxHost.Set(false);
        return true;
    }

    /// <summary>Called when any combat run ends: a live session puts your own gear, skills and food back.</summary>
    internal static void SessionEnded()
    {
        var l = _restore;
        _restore = null;
        var player = Player.m_localPlayer;
        if (l == null || player == null) return;
        try
        {
            CombatRunner.ApplyLoadout(player, l, "");
            CombatPrep.SetSkills(player, l);
        }
        catch (Exception ex) { TestLabPlugin.Log?.LogWarning("Combat live: could not give your gear back: " + ex.Message); }
    }

    /// <summary>What you carry and wear now, your skills and your meals, as a loadout.</summary>
    public static Loadout CaptureCurrent(Player p)
    {
        var l = new Loadout { Name = CurrentGear };
        foreach (var it in p.GetInventory().GetAllItems())
        {
            if (it.m_dropPrefab == null) continue;
            l.Items.Add(new LoadoutItem { Prefab = it.m_dropPrefab.name, Count = Math.Max(1, it.m_stack), Quality = Math.Max(1, it.m_quality), Equip = it.m_equipped });
        }
        foreach (var sk in p.GetSkills().GetSkillList())
            if (sk.m_level >= 1f) l.Skills[sk.m_info.m_skill.ToString()] = (int)sk.m_level;
        foreach (var food in p.GetFoods())
            if (food.m_item?.m_dropPrefab != null) l.Meals.Add(food.m_item.m_dropPrefab.name);
        if (l.Skills.Count == 0) l.Skills["Run"] = 1;      // keep "no skills" as no skills (an empty list means tier skills)
        return l;
    }

    /// <summary>Your current gear as combat/loadouts/&lt;name&gt;.json, ready for scenarios and for Bjornita.</summary>
    public static string SaveCurrent(string name)
    {
        var player = Player.m_localPlayer ?? throw new InvalidOperationException("no player");
        var safe = Cases.CaseCapture.Sanitize(name.Trim().Length > 0 ? name.Trim() : "my-gear");
        var l = CaptureCurrent(player);
        l.Skills.Remove("Run");
        return CombatData.SaveLoadout(l, safe);
    }

    /// <summary>Shipped presets: a mid player of each biome (quality 2 gear of the tier, its meals, its skills).</summary>
    public static readonly (string Label, string Loadout)[] BiomePresets =
    {
        ("Meadows", "game-meadows-mid"), ("Black Forest", "game-blackforest-mid"), ("Swamp", "game-swamp-mid"),
        ("Mountain", "game-mountain-mid"), ("Plains", "game-plains-mid"), ("Mistlands", "game-mistlands-mid"), ("Ashlands", "game-ashlands-mid")
    };

    public static List<string> AvailablePresets() =>
        BiomePresets.Where(b => CombatData.LoadoutExists(b.Loadout)).Select(b => b.Label).ToList();

    public static string PresetLoadout(string label) =>
        BiomePresets.FirstOrDefault(b => b.Label == label).Loadout ?? "";
}

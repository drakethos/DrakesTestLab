using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DrakesTestLab.Automation;
using DrakesTestLab.Cases;
using DrakesTestLab.Combat;
using UnityEngine;

namespace DrakesTestLab.UI.Toolbox;

/// <summary>
/// Combat Lab in game: fight a monster or a bot player yourself (live fights, recorded like the simulator's), run the
/// simulator's scenarios, save your gear as a loadout, and look a monster up in the difficulty database.
/// </summary>
internal sealed class CombatTab : ToolboxTab
{
    private static readonly string[] Biomes = { "Meadows", "Black Forest", "Swamp", "Mountain", "Plains", "Mistlands", "Ashlands" };
    private static readonly Dictionary<string, string[]> FallbackFoes = new()
    {
        ["Meadows"] = new[] { "Boar", "Neck", "Greyling", "Deer" },
        ["Black Forest"] = new[] { "Greydwarf", "Greydwarf_Elite", "Greydwarf_Shaman", "Skeleton", "Ghost", "Troll" },
        ["Swamp"] = new[] { "Draugr", "Draugr_Elite", "Blob", "BlobElite", "Leech", "Wraith", "Surtling", "Abomination" },
        ["Mountain"] = new[] { "Wolf", "Ulv", "Fenring", "Fenring_Cultist", "StoneGolem" },
        ["Plains"] = new[] { "Goblin", "GoblinBrute", "GoblinShaman", "BlobTar", "Lox" },
        ["Mistlands"] = new[] { "Seeker", "SeekerBrute", "Tick", "Dverger" },
        ["Ashlands"] = new[] { "Charred_Melee", "Asksvin", "BlobLava", "Morgen" }
    };
    private static readonly string[] Scalings = { "auto", "vanilla", "off", "2", "3", "4" };
    private static readonly string[] ScalingLabels = { "Auto", "Vanilla", "Off (solo)", "As 2", "As 3", "As 4" };

    private readonly LiveSpec _spec = new();
    private int _kind;                  // 0 monster, 1 player
    private int _biome = 1;
    private string _foeSearch = "";
    private int _meIndex;               // 0 = you play, 1.. = bot
    private int _oppBot = 2;            // fighter
    private int _allyBot = 2;
    private int _scaling;
    private string _myGearLabel = "Current gear";
    private string _herGearLabel = "Same as mine";
    private string _gearSearch = "";
    private bool _browseMine, _browseHers;
    private string _saveName = "my-gear";
    private string _saveMessage = "";
    private string _scenarioFilter = "";
    private readonly HashSet<string> _picked = new(StringComparer.OrdinalIgnoreCase);
    private int _runsOverride;

    private static Dictionary<string, object?>? _db;
    private static DateTime _dbStamp;

    public CombatTab()
    {
        // docs pictures and tests: set the "Fight it yourself" card up without clicking ({ "pvp": true, "bot": "veteran", "foe": "Troll", "stars": 1 })
        TestHooks.Register("testlab.combatTabPreset", args =>
        {
            if (args.TryGetValue("pvp", out var pvp)) _kind = TestHooks.Truthy(pvp) ? 1 : 0;
            if (args.TryGetValue("bot", out var bot)) _oppBot = Mathf.Max(0, Array.FindIndex(BotProfile.All, b => b.Name == Convert.ToString(bot)));
            if (args.TryGetValue("foe", out var foe)) _spec.Foe = Convert.ToString(foe) ?? _spec.Foe;
            if (args.TryGetValue("biome", out var biome)) _biome = Mathf.Max(0, Array.IndexOf(Biomes, Convert.ToString(biome)));
            if (args.TryGetValue("stars", out var stars)) _spec.Stars = Convert.ToInt32(stars);
            if (args.TryGetValue("gear", out var gear))
            {
                _herGearLabel = Convert.ToString(gear) ?? _herGearLabel;
                _spec.OpponentGear = CombatLive.PresetLoadout(_herGearLabel);
            }
            return true;
        }, "DrakesTestLab");
    }

    public override string Title => "Combat";

    public override string Group => "Scenario";

    public override void Draw()
    {
        if (ZNetScene.instance == null || Player.m_localPlayer == null)
        {
            GUILayout.Label("No world loaded yet.");
            return;
        }
        CombatRunner.EnsureShippedCombat();

        if (CombatRunner.Running)
            DrawRunning();
        else
        {
            DrawFight();
            DrawYou();
            DrawRules();
        }
        DrawLast();
        DrawSave();
        DrawSimulator();
    }

    // ---------------------------------------------------------------- live fight

    private void DrawRunning()
    {
        Ui.Header("Combat running");
        GUILayout.Label($"<b>{CombatRunner.Status}</b>");
        if (CombatRunner.LiveLine.Length > 0) GUILayout.Label(CombatRunner.LiveLine);
        if (CombatRunner.Tally.Length > 0) Ui.State(CombatRunner.Tally, true);
        Ui.Hint($"Close this window to fight. {Bootstrap.TestLabConfig.Instance.StopTestsKey.Value} or Stop ends the session and writes the report.");
        if (Ui.DangerButton("Stop")) CombatRunner.Stop();
    }

    private void DrawFight()
    {
        Ui.Header("Fight it yourself");
        Ui.Hint("Pick who you fight and press Start fight: the window closes and a 3 second countdown starts. Every fight is recorded " +
                "(damage, blocks, parries, time) and ends in a report, just like the simulator's.");
        _kind = Pick(_kind, new[] { "A monster", "A player (PvP)" });
        _spec.Pvp = _kind == 1;
        if (!_spec.Pvp) DrawMonster(); else DrawOpponent();
    }

    private void DrawMonster()
    {
        _biome = Pick(_biome, Biomes);
        var foes = FoesOf(Biomes[_biome]);
        Ui.Grid(foes, 4, n => _spec.Foe = n, _spec.Foe);
        _foeSearch = Ui.Field("Other creature", _foeSearch, 110);
        if (_foeSearch.Trim().Length > 1)
        {
            Util.PrefabIndex.Ensure();
            var hits = Util.PrefabIndex.Search(Util.PrefabIndex.Of(Util.PrefabKind.Creature), _foeSearch, 12);
            Ui.Grid(hits, 4, n => _spec.Foe = n, _spec.Foe);
        }
        _spec.Stars = Mathf.RoundToInt(Ui.Slider("Stars", _spec.Stars, 0, 2, "0"));
        _spec.Count = Mathf.RoundToInt(Ui.Slider("How many", _spec.Count, 1, 6, "0"));
        _spec.Allies = Mathf.RoundToInt(Ui.Slider("Bot teammates", _spec.Allies, 0, 3, "0"));
        if (_spec.Allies > 0)
        {
            _allyBot = Pick(_allyBot, BotProfile.All.Select(b => b.Name).ToArray());
            _spec.AllyBot = BotProfile.All[_allyBot].Name;
            Ui.Hint($"Teammates wear your gear and play as '{_spec.AllyBot}': {BotProfile.All[_allyBot].What}.");
        }
        DrawDbLine();
    }

    private void DrawOpponent()
    {
        var names = new[] { "Bjornita", "Bjorn" };
        var who = Array.IndexOf(names, _spec.Opponent);
        _spec.Opponent = names[Pick(Mathf.Max(0, who), names)];
        GUILayout.Label("Difficulty (her / his bot):");
        _oppBot = Pick(_oppBot, BotProfile.All.Select(b => b.Name).ToArray());
        _spec.OpponentBot = BotProfile.All[_oppBot].Name;
        Ui.Hint(BotProfile.All[_oppBot].What);
        GUILayout.Label($"{_spec.Opponent}'s gear: <b>{_herGearLabel}</b>");
        var presets = new List<string> { "Same as mine" };
        presets.AddRange(CombatLive.AvailablePresets());
        Ui.Grid(presets, 4, p => { _herGearLabel = p; _spec.OpponentGear = p == "Same as mine" ? "" : CombatLive.PresetLoadout(p); _browseHers = false; }, _herGearLabel);
        _browseHers = Ui.Toggle(_browseHers, "Browse all loadouts");
        if (_browseHers) DrawLoadoutPicker(l => { _herGearLabel = l; _spec.OpponentGear = l; });
    }

    private void DrawYou()
    {
        Ui.Header("You");
        var who = new List<string> { "I play" };
        who.AddRange(BotProfile.All.Select(b => "bot: " + b.Name));
        _meIndex = Pick(_meIndex, who.ToArray());
        _spec.Me = _meIndex == 0 ? "you" : BotProfile.All[_meIndex - 1].Name;
        Ui.Hint(_meIndex == 0 ? "You fight with your own keys and mouse." : "A bot plays you, so you can watch (bots against bots is what the simulator does, at 1x here).");
        GUILayout.Label($"Your gear: <b>{_myGearLabel}</b>");
        var presets = new List<string> { "Current gear" };
        presets.AddRange(CombatLive.AvailablePresets());
        Ui.Grid(presets, 4, p => { _myGearLabel = p; _spec.MyGear = p == "Current gear" ? "" : CombatLive.PresetLoadout(p); _browseMine = false; }, _myGearLabel);
        _browseMine = Ui.Toggle(_browseMine, "Browse all loadouts");
        if (_browseMine) DrawLoadoutPicker(l => { _myGearLabel = l; _spec.MyGear = l; });
        Ui.Hint("Current gear: what you carry and wear now, your skills and your food. Every fight starts from a fresh copy of it, " +
                "and it is given back when the session ends. A preset is a mid player of that biome (quality 2 gear, its meals and skills).");
    }

    private void DrawRules()
    {
        Ui.Header("Rules");
        _spec.Fights = Mathf.RoundToInt(Ui.Slider("Fights in a row", _spec.Fights, 1, 10, "0"));
        _spec.Timeout = Mathf.Round(Ui.Slider("Time limit (s)", _spec.Timeout, 30, 600, "0") / 10f) * 10f;
        GUILayout.Label("Player-count scaling:");
        _scaling = Pick(_scaling, ScalingLabels);
        _spec.Scaling = Scalings[_scaling];
        Ui.Hint(_scaling switch
        {
            0 => "Auto: the monster scales for the players really fighting it (you, plus your bot teammates). PvP: none.",
            1 => "Vanilla: the game counts every player within 200 m itself, as in normal play.",
            2 => "Off: always a solo-strength monster, even with teammates.",
            _ => $"The monster has the health and damage it would have with {Scalings[_scaling]} players nearby, whoever fights it."
        });
        GUI.enabled = !_spec.Pvp ? _spec.Foe.Length > 0 : true;
        if (Ui.PrimaryButton("Start fight", GUILayout.Height(30f)))
        {
            if (!CombatLive.Start(_spec))
                _saveMessage = "Could not start: a test or combat run is already going, or there is no player.";
        }
        GUI.enabled = true;
    }

    private void DrawDbLine()
    {
        var row = DbRow(Biomes[_biome], _spec.Foe, _spec.Stars);
        if (row == null)
        {
            Ui.Hint("Difficulty database: no row for this creature.");
            return;
        }
        var solo = row.Obj("solo");
        var win = solo != null ? solo.Num("winRate", -1f) : -1f;
        var ttk = solo != null ? solo.Num("ttk", 0f) : 0f;
        var need = (int)row.Num("playersNeeded", 0f);
        var tried = (int)row.Num("teamTried", 4f);
        var src = row.Str("source");
        var needText = need > 0 ? $"needs {need} player{(need > 1 ? "s" : "")}" : $"even {tried} players lose more often than they win";
        Ui.State($"Database: a mid {Biomes[_biome]} player wins {(win >= 0 ? Mathf.RoundToInt(win * 100f) + "%" : "?")} alone" +
                 (ttk > 0f ? $", kills in ~{ttk:0} s" : "") + $"; {needText}" + (src.Length > 0 ? $" ({src})." : "."), win >= 0.5f);
    }

    // ---------------------------------------------------------------- results, save, simulator

    private void DrawLast()
    {
        if (CombatRunner.LastReport.Length == 0 && CombatRunner.Tally.Length == 0) return;
        Ui.Header("Last result");
        if (CombatRunner.Tally.Length > 0) GUILayout.Label(CombatRunner.Tally);
        if (!CombatRunner.Running) GUILayout.Label(CombatRunner.Status);
        GUILayout.BeginHorizontal();
        GUI.enabled = CombatRunner.LastReport.Length > 0;
        if (GUILayout.Button("Open report")) Application.OpenURL("file:///" + CombatRunner.LastReport.Replace('\\', '/'));
        if (GUILayout.Button("Copy report path")) Ui.Copy(CombatRunner.LastReport);
        GUI.enabled = !CombatRunner.Running && CombatLive.Last != null;
        if (GUILayout.Button("Rematch (same setup)") && CombatLive.Last != null) CombatLive.Start(CombatLive.Last);
        GUI.enabled = true;
        GUILayout.EndHorizontal();
    }

    private void DrawSave()
    {
        Ui.Header("Save my gear as a loadout");
        Ui.Hint("Writes what you carry and wear now, your skills and your food to combat/loadouts/<name>.json. " +
                "Use it as Bjornita's gear, in your own scenarios, or as your side in the simulator.");
        GUILayout.BeginHorizontal();
        _saveName = GUILayout.TextField(_saveName, GUILayout.Width(220f));
        GUI.enabled = !CombatRunner.Running;
        if (GUILayout.Button("Save loadout", GUILayout.Width(120f)))
        {
            try { _saveMessage = "Saved " + CombatLive.SaveCurrent(_saveName); }
            catch (Exception ex) { _saveMessage = "Could not save: " + ex.Message; }
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        if (_saveMessage.Length > 0) Ui.Hint(_saveMessage);
    }

    private void DrawSimulator()
    {
        Ui.Header("Simulator: run scenarios");
        Ui.Hint("Scenario files in combat/scenarios: bots fight, many times, and the report gives win rates, kill times and balance flags. " +
                "Long batches belong on the command line (-testlab-combat), where speed settings and lanes are meant to be used.");
        _scenarioFilter = Ui.Field("Filter", _scenarioFilter, 60);
        var names = CombatData.ScenarioNames();
        var shown = names.Where(n => _scenarioFilter.Length == 0 || n.IndexOf(_scenarioFilter, StringComparison.OrdinalIgnoreCase) >= 0).Take(30).ToList();
        for (var i = 0; i < shown.Count; i += 2)
        {
            GUILayout.BeginHorizontal();
            for (var c = i; c < Math.Min(i + 2, shown.Count); c++)
            {
                var on = _picked.Contains(shown[c]);
                var next = GUILayout.Toggle(on, " " + shown[c], GUILayout.Width(300f));
                if (next != on) { if (next) _picked.Add(shown[c]); else _picked.Remove(shown[c]); }
            }
            GUILayout.EndHorizontal();
        }
        var more = names.Count(n => _scenarioFilter.Length == 0 || n.IndexOf(_scenarioFilter, StringComparison.OrdinalIgnoreCase) >= 0) - shown.Count;
        if (more > 0) Ui.Hint($"{more} more: narrow the filter.");
        _runsOverride = Mathf.RoundToInt(Ui.Slider(_runsOverride == 0 ? "Runs: each scenario's own" : "Runs per cell", _runsOverride, 0, 30, "0"));
        GUILayout.BeginHorizontal();
        GUI.enabled = !CombatRunner.Running && _picked.Count > 0;
        if (Ui.PrimaryButton($"Run {_picked.Count} scenario(s)"))
        {
            CombatRunner.RunsOverride = _runsOverride;
            if (CombatRunner.StartNamed(string.Join(",", _picked), false)) ToolboxHost.Set(false);
        }
        GUI.enabled = _picked.Count > 0;
        if (GUILayout.Button("Clear selection")) _picked.Clear();
        GUI.enabled = true;
        GUILayout.EndHorizontal();
    }

    private void DrawLoadoutPicker(Action<string> pick)
    {
        _gearSearch = Ui.Field("Search", _gearSearch, 60);
        var hits = CombatData.LoadoutNames().Where(n => _gearSearch.Length == 0 || n.IndexOf(_gearSearch, StringComparison.OrdinalIgnoreCase) >= 0).Take(24).ToList();
        Ui.Grid(hits, 3, pick);
    }

    /// <summary>A row of buttons, the chosen one drawn in the accent colour (the skin's toolbar shows no selection).</summary>
    private static int Pick(int selected, string[] labels)
    {
        GUILayout.BeginHorizontal();
        for (var i = 0; i < labels.Length; i++)
            if (GUILayout.Button(labels[i], i == selected ? Ui.Primary : GUI.skin.button)) selected = i;
        GUILayout.EndHorizontal();
        return selected;
    }

    // ---------------------------------------------------------------- difficulty database

    private static string DbBiome(string label) => label.Replace(" ", "").ToLowerInvariant();

    private static List<string> FoesOf(string biome)
    {
        var rows = DbRows();
        var key = DbBiome(biome);
        var fromDb = rows?.Values.OfType<Dictionary<string, object?>>().Where(r => r.Str("biome") == key).Select(r => r.Str("foe")).Distinct().ToList();
        var list = FallbackFoes[biome].Concat(fromDb ?? new List<string>()).Distinct().ToList();
        return list.Where(n => ZNetScene.instance.GetPrefab(n) != null).ToList();
    }

    private static Dictionary<string, object?>? DbRow(string biome, string foe, int stars) =>
        DbRows()?.TryGetValue($"{DbBiome(biome)}/{foe}/{stars}", out var r) == true ? r as Dictionary<string, object?> : null;

    private static Dictionary<string, object?>? DbRows()
    {
        try
        {
            var path = CombatDifficultyDb.FilePath;
            if (!File.Exists(path)) return null;
            var stamp = File.GetLastWriteTimeUtc(path);
            if (_db == null || stamp != _dbStamp)
            {
                _db = MiniJson.Parse(File.ReadAllText(path)) as Dictionary<string, object?>;
                _dbStamp = stamp;
            }
            return _db?.Obj("rows");
        }
        catch
        {
            return null;
        }
    }
}

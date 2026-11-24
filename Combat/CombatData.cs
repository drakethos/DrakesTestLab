using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using DrakesTestLab.Automation;
using DrakesTestLab.Cases;

namespace DrakesTestLab.Combat;

internal sealed class LoadoutItem
{
    public string Prefab = "";
    public int Count = 1;
    public int Quality = 1;
    public bool Equip = true;
}

/// <summary>What the gear side carries and wears. Reusable: scenarios refer to it by name.</summary>
internal sealed class Loadout
{
    public string Name = "";
    public string Tier = "";
    public List<LoadoutItem> Items = new();
    public Dictionary<string, int> Skills = new();     // skill name (Swords, Blocking...) -> level
    public float MaxHealth = -1f;                       // stands in for food; -1 = leave as is
    public float MaxStamina = -1f;
    // Named food profiles (balanced, health, stamina...): the health and stamina a player of this tier has with that food set.
    public Dictionary<string, (float health, float stamina)> Food = new();
    // Real meals (item prefabs the player eats before each fight, up to 3). When set, health and stamina come from the
    // game's own food code (regen ticks, food decay), not from fixed numbers. A food profile can also be a list of meals.
    public List<string> Meals = new();
    public Dictionary<string, List<string>> FoodMeals = new();
}

/// <summary>How the bot attacks: always the primary, always the secondary, or a mix (every Nth swing, or after an interval).</summary>
internal sealed class AttackSpec
{
    public enum Kind { Primary, Secondary, Mix }
    public Kind Mode = Kind.Primary;
    public float Seconds;        // mix: use the secondary when this long has passed since the last one
    public int EverySwings;      // mix: every Nth swing is the secondary (2 = alternate)
    public string Label = "primary";

    /// <summary>"primary", "secondary", "mix" (alternate), "mix:every3" (every 3rd swing), "mix:4s" (one per 4 seconds).</summary>
    public static AttackSpec Parse(string text)
    {
        var t = (text ?? "").Trim().ToLowerInvariant();
        var a = new AttackSpec { Label = t.Length == 0 ? "primary" : t };
        if (t.StartsWith("sec")) { a.Mode = Kind.Secondary; a.Label = "secondary"; return a; }
        if (!t.StartsWith("mix")) return a;
        a.Mode = Kind.Mix;
        var arg = t.Contains(":") ? t.Substring(t.IndexOf(':') + 1) : "";
        if (arg.EndsWith("s") && float.TryParse(arg.TrimEnd('s'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var secs)) a.Seconds = secs;
        else if (arg.StartsWith("every") && int.TryParse(arg.Substring(5), out var n)) a.EverySwings = Math.Max(2, n);
        else a.EverySwings = 2;
        return a;
    }
}

/// <summary>Calibration: how many of the foe the gear side can fight at once. Counts go up until it mostly loses.</summary>
internal sealed class CountSpec
{
    public int From = 1, To = 10, Step = 1;
    public double StopBelow = 0.2;        // stop climbing once the win rate falls under this
    public double CapacityAt = 0.5;       // "can handle N" = the largest N still won at least this often
}

internal sealed class FoeSpec
{
    public string Prefab = "";
    public int Stars;                                   // 0 = a plain creature. Valheim's own stars go to 2; more needs a level mod.
    public int Count = 1;
    public float Distance = 12f;

    public string Label => Stars > 0 ? $"{Prefab} {Stars}*" : Prefab;
}

/// <summary>
/// Mixed lanes: one lane's setup (its own loadout, foes and bot). Each fight the setups rotate between the lanes, so no
/// setup always gets the same spot or always gets the local player; each setup gets its own result row.
/// </summary>
internal sealed class LaneMix
{
    public string Label = "";
    public string Loadout = "";            // empty = the scenario's
    public List<FoeSpec> Foes = new();     // empty = the scenario's
    public string Bot = "";                // empty = the scenario's
}

/// <summary>PvP: the local player (Bjorn, scenario loadout and bot) against a bot player (Bjornita) with her own loadout and bot.</summary>
internal sealed class PvpSpec
{
    public string Loadout = "";
    public string Bot = "fighter";
    public string Name = "Bjornita";
}

/// <summary>A picture taken during a fight ("screenshots": [ { "at": 5, "name": "troll-lanes", "tab": "Combat", "clean": true } ]).</summary>
internal sealed class ShotSpec
{
    public float At;            // seconds of fight time
    public string Name = "";    // file name (combat/results/shots/<name>.png)
    public string Tab = "";     // open the toolbox on this tab for the picture ("" = no window)
    public bool Clean;          // hide the Combat Lab banner
    public int Run = 1;         // which fight of the scenario (1 = the first; 0 = every fight, numbered)
}

internal sealed class ConfigChange
{
    public string Guid = "", Section = "", Key = "", Value = "";
}

internal sealed class VariantSpec
{
    public string Name = "vanilla";
    public List<ConfigChange> Changes = new();
}

internal sealed class ScenarioSpec
{
    public string Name = "";
    public string Loadout = "";
    public List<FoeSpec> Foes = new();
    public int Runs = 20;
    public float Timeout = 60f;
    public string Bot = "trained";
    public int Seed = 1;
    public List<VariantSpec> Variants = new();
    public Band? Band;
    // When a fight ends. 0 = until someone dies. A target above 0 ends it earlier: the foes are "dead enough" when their
    // total health is at or below FoeEndHealth (fraction of their max), the gear side "lost" at or below GearEndHealth.
    // Unsticker. No damage dealt by either side for StallSeconds = somebody is stuck (a foe that cannot reach, a body wedged in a rock).
    // Everybody is moved to a new clear spot in the arena, up to MaxUnsticks times; if it is still stuck after the last one the fight ends
    // as "stalled". 0 = off.
    public float StallSeconds = 15f;
    public int MaxUnsticks = 2;
    public int Lean = -1;              // lean rendering during fights: 1 on, 0 off, -1 = the [Combat] Lean setting
    public List<LaneMix> Mix = new();  // mixed lanes ("lanes": [ {...}, {...} ]); empty = every lane the same
    public PvpSpec? Pvp;
    // Team fights: if one player cannot beat the foes, how many can? Team sizes From..To (max 4), stopping at the first
    // size that wins at least NeedWin of its runs. "team": 2 runs just that size.
    public int TeamFrom = 1, TeamTo = 1;
    // Difficulty baseline: "difficulty": true = a 1..4 player climb whose result goes into the game difficulty DB
    // (combat/difficulty-db.json) under biome / foe / stars.
    public bool Difficulty;
    public string Biome = "";
    public double TeamNeedWin = 0.5;               // PvP instead of monsters ("pvp": { "loadout": ..., "bot": ... })
    public bool? AutoRuns;             // stop early once the results are settled ("runs" is then the cap); null = [Combat] AutoRuns
    public int Lanes = 1;
    public bool LaneKeep = true;
    public bool Trace;                 // "trace": true logs every bot's view and choices every 2 s
    public bool LaneLock = true;       // lanes: each lane's foes only target their own lane player ("laneLock": false = their own AI picks)
    public bool ZoneOnly;              // diagnosis: lane-zone geometry for the local fight, with no other lanes ("zoneOnly": true)
    public string AllyBot = "";        // team fights: the teammates' bot ("" = the scenario's bot)
    public float Countdown;            // seconds of "Fight starts in 3..." before the foes come (live fights)
    // Valheim's player-count scaling (more health and damage per player within 200 m). "auto": a team fight counts its
    // team, lanes and PvP count 1 each. "vanilla": the game counts every player nearby itself. "off": always 1.
    // A number: always that many (a solo fight against a monster scaled for a group of 3).
    public string PlayerScaling = "auto";
    public List<ShotSpec> Shots = new();
    public string Opener = "";         // "sneak": foes spawn facing away and unaware, so a stealthy player can open with a backstab       // lanes: put fighters back inside their own zone when they leave it ("laneKeep": false = only the outer ring)              // spike: players fighting at once (the local player plus bot lane players), each with its own foes
    public float TimeScale = -1f;      // game clock speed for the fights (1-4); -1 = the [Combat] TimeScale setting
    public float FoeEndHealth;
    public float GearEndHealth;

    // Raw-data switches. Off by default: real Valheim behaviour.
    public bool Fearless;              // foes never flee (low health, fire, not alerted) and always fight
    public bool Aggro;                 // foes start hunting the player at once, even the passive ones
    public string NoStagger = "none";  // none | foes | gear | all
    public bool NoKnockback;
    public float FoeDamage = 1f;       // multiplier on what the foes deal
    public float GearDamage = 1f;      // multiplier on what the gear side deals
    public List<string> Foods = new() { "" };                       // food profiles to run ("" = the loadout's own numbers)
    public List<AttackSpec> Attacks = new() { new AttackSpec() };   // attack modes to run
    public CountSpec? Counts;                                        // calibration, instead of the foe's own count

    public string FoeLabel => Foes.Count == 0 ? "?" : string.Join(" + ", Foes.ConvertAll(f => f.Label + (f.Count > 1 ? " x" + f.Count : "")));
}

/// <summary>Reads combat/loadouts/*.json and combat/scenarios/*.json (same JSON dialect and helpers as suites).</summary>
internal static class CombatData
{
    public static string Folder => Path.Combine(Paths.ConfigPath, "DrakesTestLab", "combat");
    public static string ResultsFolder => Path.Combine(Folder, "results");

    /// <summary>Scenarios and loadouts made in game (the Combat tab's live fights, "my current gear"): looked up before the files.</summary>
    public static readonly Dictionary<string, ScenarioSpec> LiveScenarios = new(StringComparer.OrdinalIgnoreCase);
    public static readonly Dictionary<string, Loadout> LiveLoadouts = new(StringComparer.OrdinalIgnoreCase);

    public static Loadout ReadLoadout(string name)
    {
        if (LiveLoadouts.TryGetValue(name, out var live)) return live;
        var path = FindFile(Path.Combine(Folder, "loadouts"), name);
        var d = Read(path);
        var l = new Loadout { Name = d.Str("name", name), Tier = d.Str("tier"), MaxHealth = d.Num("maxHealth", -1f), MaxStamina = d.Num("maxStamina", -1f) };
        foreach (var o in d.Arr("items"))
        {
            if (o is string s) { l.Items.Add(new LoadoutItem { Prefab = s }); continue; }
            if (o is not Dictionary<string, object?> id) continue;
            l.Items.Add(new LoadoutItem { Prefab = id.Str("prefab"), Count = (int)id.Num("count", 1), Quality = (int)id.Num("quality", 1), Equip = id.Bool("equip", true) });
        }
        var food = d.Obj("food");
        if (food != null)
            foreach (var kv in food)
                if (kv.Value is Dictionary<string, object?> fd)
                    l.Food[kv.Key] = (fd.Num("health", -1f), fd.Num("stamina", -1f));
                else if (kv.Value is List<object?> meals)
                    l.FoodMeals[kv.Key] = meals.OfType<string>().ToList();
        foreach (var o in d.Arr("meals"))
            if (o is string m) l.Meals.Add(m);
        var skills = d.Obj("skills");
        if (skills != null)
            foreach (var kv in skills)
                l.Skills[kv.Key] = (int)J.Num(skills, kv.Key, 0f);
        return l;
    }

    private static List<FoeSpec> ParseFoes(Dictionary<string, object?> d)
    {
        var list = new List<FoeSpec>();
        var foes = d.Obj("foes");
        var foeList = foes != null ? new List<object?> { foes } : d.Arr("foes");
        foreach (var o in foeList)
            if (o is Dictionary<string, object?> fd)
                list.Add(new FoeSpec { Prefab = fd.Str("prefab"), Stars = (int)fd.Num("stars", 0f), Count = Math.Max(1, (int)fd.Num("count", 1f)), Distance = fd.Num("distance", 12f) });
        return list;
    }

    public static ScenarioSpec ReadScenario(string name)
    {
        if (LiveScenarios.TryGetValue(name, out var live)) return live;
        var path = FindFile(Path.Combine(Folder, "scenarios"), name);
        var d = Read(path);
        var s = new ScenarioSpec
        {
            Name = d.Str("name", name),
            Runs = Math.Max(1, (int)d.Num("runs", 20f)),
            Timeout = d.Num("timeout", 60f),
            Bot = d.Str("bot", "trained"),
            Seed = (int)d.Num("seed", 1f),
            StallSeconds = d.Num("stall", 15f),
            MaxUnsticks = Math.Max(0, (int)d.Num("unsticks", 2f)),
            TimeScale = d.Num("timeScale", -1f),
            Lanes = d.Arr("lanes").Count > 0 ? 1 : Math.Max(1, Math.Min(4, (int)d.Num("lanes", 1f))),
            AutoRuns = d.Has("autoRuns") ? d.Bool("autoRuns") : null,
            LaneKeep = !d.Has("laneKeep") || d.Bool("laneKeep"),
            LaneLock = !d.Has("laneLock") || d.Bool("laneLock"),
            Trace = d.Has("trace") && d.Bool("trace"),
            ZoneOnly = d.Bool("zoneOnly"),
            Opener = d.Str("opener").ToLowerInvariant(),
            Lean = d.Has("lean") ? (d.Bool("lean") ? 1 : 0) : -1,
            FoeEndHealth = Math.Max(0f, Math.Min(0.99f, d.Num("foeEndHealth", 0f))),
            GearEndHealth = Math.Max(0f, Math.Min(0.99f, d.Num("gearEndHealth", 0f)))
        };
        s.AllyBot = d.Str("allyBot");
        s.Countdown = Math.Max(0f, d.Num("countdown", 0f));
        if (d.TryGetValue("playerScaling", out var ps) && ps != null)
            s.PlayerScaling = (Convert.ToString(ps, System.Globalization.CultureInfo.InvariantCulture) ?? "auto").Trim().ToLowerInvariant();
        foreach (var o in d.Arr("screenshots"))
            if (o is Dictionary<string, object?> sd)
                s.Shots.Add(new ShotSpec { At = sd.Num("at", 0f), Name = sd.Str("name", "shot"), Tab = sd.Str("tab"), Clean = sd.Bool("clean"), Run = (int)sd.Num("run", 1f) });
        s.Fearless = d.Bool("fearless");
        s.Aggro = d.Bool("aggro");
        s.NoStagger = d.Bool("noStagger") ? "all" : d.Str("noStagger", "none").ToLowerInvariant();
        if (s.NoStagger is "false" or "") s.NoStagger = "none";
        s.NoKnockback = d.Bool("noKnockback");
        s.FoeDamage = d.Num("foeDamage", 1f);
        s.GearDamage = d.Num("gearDamage", 1f);
        var foods = StrList(d, "food");
        if (foods.Count > 0) s.Foods = foods;
        var attacks = StrList(d, "attack");
        if (attacks.Count > 0) s.Attacks = attacks.ConvertAll(AttackSpec.Parse);
        var calib = d.Obj("calibrate");
        if (calib != null)
            s.Counts = new CountSpec { From = Math.Max(1, (int)calib.Num("from", 1f)), To = Math.Max(1, (int)calib.Num("to", 10f)), Step = Math.Max(1, (int)calib.Num("step", 1f)),
                                       StopBelow = calib.Num("stopBelow", 0.2f), CapacityAt = calib.Num("capacityAt", 0.5f) };
        s.Loadout = (d.Obj("gear") ?? d).Str("loadout");
        if (s.Loadout.Length == 0) throw new FormatException("the scenario needs gear.loadout");

        s.Foes = ParseFoes(d);
        s.Biome = d.Str("biome");
        s.Difficulty = d.Bool("difficulty");
        if (s.Difficulty) { s.TeamFrom = 1; s.TeamTo = 4; s.TeamNeedWin = 0.5; }
        var team = d.Obj("team");
        if (team != null)
        {
            s.TeamFrom = Math.Max(1, Math.Min(4, (int)team.Num("from", 1f)));
            s.TeamTo = Math.Max(s.TeamFrom, Math.Min(4, (int)team.Num("to", 4f)));
            s.TeamNeedWin = team.Num("needWin", 0.5f);
        }
        else if (d.Has("team"))
            s.TeamFrom = s.TeamTo = Math.Max(1, Math.Min(4, (int)d.Num("team", 1f)));
        var pvp = d.Obj("pvp");
        if (pvp != null)
            s.Pvp = new PvpSpec { Loadout = pvp.Str("loadout", s.Loadout), Bot = pvp.Str("bot", "fighter"), Name = pvp.Str("name", "Bjornita") };
        foreach (var o in d.Arr("lanes"))
        {
            if (o is not Dictionary<string, object?> ld) continue;
            var m = new LaneMix { Loadout = ld.Str("loadout"), Foes = ParseFoes(ld), Bot = ld.Str("bot") };
            m.Label = ld.Str("label");
            s.Mix.Add(m);
        }
        if (s.Mix.Count > 4) throw new FormatException("at most 4 lanes");
        if (s.Mix.Count > 0) s.Lanes = s.Mix.Count;
        foreach (var m in s.Mix)
        {
            if (m.Loadout.Length == 0) m.Loadout = s.Loadout;
            if (m.Foes.Count == 0) m.Foes = s.Foes;
            if (m.Bot.Length == 0) m.Bot = s.Bot;
            if (m.Label.Length == 0) m.Label = m.Loadout + (m.Bot != s.Bot ? $" ({m.Bot})" : "");     // the foe has its own column
        }
        if (s.Foes.Count == 0 && s.Pvp == null && s.Mix.Count == 0) throw new FormatException("the scenario needs foes: { \"prefab\": \"Troll\" } (or pvp, or lanes)");

        foreach (var o in d.Arr("variants"))
        {
            if (o is string vn) { s.Variants.Add(new VariantSpec { Name = vn }); continue; }
            if (o is not Dictionary<string, object?> vd) continue;
            var v = new VariantSpec { Name = vd.Str("name", "variant") };
            foreach (var c in vd.Arr("configOverrides"))
                if (c is Dictionary<string, object?> cd)
                    v.Changes.Add(new ConfigChange { Guid = cd.Str("guid"), Section = cd.Str("section"), Key = cd.Str("key"), Value = cd.Str("value") });
            s.Variants.Add(v);
        }
        if (s.Variants.Count == 0) s.Variants.Add(new VariantSpec());

        var b = d.Obj("band");
        if (b != null)
            s.Band = new Band { WinMin = b.Num("winMin", -1f), WinMax = b.Num("winMax", -1f), TtkMin = b.Num("ttkMin", -1f), TtkMax = b.Num("ttkMax", -1f) };
        return s;
    }

    /// <summary>A key that is one string or a list of strings.</summary>
    private static List<string> StrList(Dictionary<string, object?> d, string key)
    {
        var list = new List<string>();
        if (d.TryGetValue(key, out var v) && v != null)
        {
            if (v is List<object?> l) { foreach (var o in l) if (o != null) list.Add(Convert.ToString(o) ?? ""); }
            else list.Add(Convert.ToString(v) ?? "");
        }
        return list;
    }

    public static List<string> ScenarioNames()
    {
        var list = new List<string>();
        var dir = Path.Combine(Folder, "scenarios");
        if (Directory.Exists(dir))
            foreach (var f in Directory.GetFiles(dir, "*.json"))
                list.Add(Path.GetFileNameWithoutExtension(f));
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    public static List<string> LoadoutNames()
    {
        var list = new List<string>();
        var dir = Path.Combine(Folder, "loadouts");
        if (Directory.Exists(dir))
            foreach (var f in Directory.GetFiles(dir, "*.json"))
                list.Add(Path.GetFileNameWithoutExtension(f));
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    public static bool LoadoutExists(string name) =>
        LiveLoadouts.ContainsKey(name) || File.Exists(Path.Combine(Folder, "loadouts", name + ".json"));

    /// <summary>Writes a loadout as combat/loadouts/&lt;file&gt;.json (the same format the files are read in). Returns the path.</summary>
    public static string SaveLoadout(Loadout l, string file)
    {
        var dir = Path.Combine(Folder, "loadouts");
        Directory.CreateDirectory(dir);
        var items = l.Items.Select(i => (object?)new Dictionary<string, object?> { ["prefab"] = i.Prefab, ["count"] = i.Count, ["quality"] = i.Quality, ["equip"] = i.Equip }).ToList();
        var doc = new Dictionary<string, object?> { ["name"] = file, ["items"] = items };
        if (l.Tier.Length > 0) doc["tier"] = l.Tier;
        if (l.Meals.Count > 0) doc["meals"] = l.Meals.Cast<object?>().ToList();
        if (l.Skills.Count > 0) doc["skills"] = l.Skills.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
        if (l.MaxHealth > 0f) doc["maxHealth"] = l.MaxHealth;
        if (l.MaxStamina > 0f) doc["maxStamina"] = l.MaxStamina;
        var path = Path.Combine(dir, file + ".json");
        File.WriteAllText(path, JsonWriter.Write(doc));
        return path;
    }

    private static string FindFile(string dir, string name)
    {
        var p = Path.Combine(dir, name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name : name + ".json");
        if (!File.Exists(p)) throw new FileNotFoundException($"not found: {p}");
        return p;
    }

    private static Dictionary<string, object?> Read(string path) =>
        MiniJson.Parse(File.ReadAllText(path)) as Dictionary<string, object?>
        ?? throw new FormatException($"{Path.GetFileName(path)} must contain one JSON object { "{ ... }" }");
}

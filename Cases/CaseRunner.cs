using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using DrakesTestLab.Bootstrap;
using DrakesTestLab.UI.Toolbox;
using DrakesTestLab.Util;
using UnityEngine;

namespace DrakesTestLab.Cases;

/// <summary>Loads JSON test cases from BepInEx/config/DrakesTestLab/cases and applies them to the running world.</summary>
internal static class CaseRunner
{
    public static string Folder => Path.Combine(Paths.ConfigPath, "DrakesTestLab", "cases");

    public static bool Running { get; private set; }
    public static string LastResult { get; private set; } = "";

    public static List<string> List()
    {
        var names = new List<string>();
        try
        {
            if (Directory.Exists(Folder))
            {
                foreach (var file in Directory.GetFiles(Folder, "*.json"))
                    names.Add(Path.GetFileNameWithoutExtension(file));
            }
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Listing test cases failed: {ex.Message}");
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    public static TestCase? Load(string name)
    {
        try
        {
            var path = Path.Combine(Folder, name + ".json");
            if (MiniJson.Parse(File.ReadAllText(path)) is not Dictionary<string, object?> root)
                throw new FormatException("the file must contain one JSON object { ... }");

            var warnings = new List<string>();
            var c = new TestCase();
            CaseMapper.Populate(c, root, warnings);
            foreach (var w in warnings)
                TestLabPlugin.Log?.LogWarning($"Case '{name}': {w}");
            TestLabPlugin.Log?.LogInfo(
                $"Loaded case '{name}': {c.items.Count} items, {c.skills.Count} skills, {c.spawns.Count} spawns, {c.wards.Count} wards, {c.bosses.Count} bosses" +
                (warnings.Count > 0 ? $", {warnings.Count} warning(s)" : ""));
            return c;
        }
        catch (Exception ex)
        {
            LastResult = $"Case '{name}' could not be read: {ex.Message}";
            TestLabPlugin.Log?.LogError(LastResult);
            return null;
        }
    }

    /// <summary>Just the description line, for the Cases tab (reads the file once, not every frame).</summary>
    public static string Describe(string name)
    {
        try
        {
            var path = Path.Combine(Folder, name + ".json");
            if (MiniJson.Parse(File.ReadAllText(path)) is Dictionary<string, object?> root
                && root.TryGetValue("description", out var d) && d is string text)
                return text;
        }
        catch
        {
            // shown as an error when the case is applied
        }

        return "";
    }

    public static void Run(string name)
    {
        var c = Load(name);
        if (c != null && ToolboxHost.Instance != null && !Running)
            ToolboxHost.Instance.StartCoroutine(Apply(c, name));
    }

    /// <summary>Applies the case named in the Case.OnLoad config entry (and builds the default arena when enabled).</summary>
    public static void RunOnLoad()
    {
        var cfg = TestLabConfig.Instance;
        TestRules.ApplyConfigDefaults();
        var name = cfg.CaseOnLoad.Value.Trim();
        var host = ToolboxHost.Instance;
        if (host == null)
            return;

        PieceDump.RunIfFlagged();

        if (TemplateBaker.Pending)
        {
            var target = SaveModeResolver.Resolve();
            host.StartCoroutine(TemplateBaker.Bake(target.WorldName, target.TemplateWorldFile));
            return;
        }

        if (name.Length > 0 && File.Exists(Path.Combine(Folder, name + ".json")))
        {
            Run(name);
            Automation.TestRunner.MaybeRunOnLaunch();
            return;
        }

        if (cfg.AutoBuildArena.Value)
            host.StartCoroutine(Apply(new TestCase { name = "default", arena = "build", stationLevel = 7 }, "default"));

        // [Automation] RunOnLaunch: the runner waits until the arena and any case above are finished
        Automation.TestRunner.MaybeRunOnLaunch();
    }

    /// <summary>Writes the README and copies every shipped test case that is not already in the config folder.</summary>
    public static void EnsureExamples()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            WriteIfMissing("README.md", Readme);

            var shipped = Path.Combine(ValheimPaths.TemplatesFolder, "cases");
            if (!Directory.Exists(shipped))
                return;

            foreach (var file in Directory.GetFiles(shipped, "*.json"))
            {
                var dest = Path.Combine(Folder, Path.GetFileName(file));
                if (!File.Exists(dest))
                    File.Copy(file, dest);
            }
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Could not write example test cases: {ex.Message}");
        }
    }

    private static void WriteIfMissing(string file, string text)
    {
        var path = Path.Combine(Folder, file);
        if (!File.Exists(path))
            File.WriteAllText(path, text);
    }

    /// <summary>Applies a case from code (the automation runner). quick: do not wait for the player to settle.</summary>
    public static IEnumerator ApplyCase(TestCase c, string label, bool quick) => Apply(c, label, quick);

    private static IEnumerator Apply(TestCase c, string label, bool quick = false)
    {
        Running = true;
        LastResult = $"Applying '{label}'...";
        TestLabPlugin.Log?.LogInfo(LastResult);

        // The first Player object is the intro/valkyrie one and gets destroyed on respawn; wait for a stable one.
        Player? last = null;
        var stable = 0f;
        var waited = 0f;
        while (waited < 90f)
        {
            var cur = Player.m_localPlayer;
            if (cur != null && ZNetScene.instance != null && EnvMan.instance != null && !cur.InIntro() && !cur.IsTeleporting())
            {
                if (ReferenceEquals(cur, last)) stable += Time.unscaledDeltaTime;
                else { last = cur; stable = 0f; }
                if (stable >= (quick ? 0f : 3f))
                    break;
            }
            else
            {
                last = null;
                stable = 0f;
            }

            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        var player = Player.m_localPlayer;
        if (player == null)
        {
            LastResult = $"'{label}' not applied - no player.";
            Running = false;
            yield break;
        }

        GameRefs.EnableCheats();

        // start from a known baseline unless the case (or the Cases-tab setting) says otherwise
        if (c.reset == 1 || (c.reset < 0 && TestLabConfig.Instance.CaseResetFirst.Value))
        {
            StateReset.Run();
            yield return null;
        }

        // rules first so everything after sees them
        switch (c.noBuild.ToLowerInvariant())
        {
            case "vanilla": TestRules.NoBuild = NoBuildMode.Vanilla; break;
            case "off": TestRules.NoBuild = NoBuildMode.Off; break;
            case "scaled": TestRules.NoBuild = NoBuildMode.Scaled; break;
        }

        if (c.noBuildScale >= 0f) TestRules.NoBuildScale = c.noBuildScale;
        if (c.noBuildRadius >= 0f) TestRules.NoBuildRadius = c.noBuildRadius;
        if (c.fallDamageMultiplier >= 0f) TestRules.FallDamageMultiplier = c.fallDamageMultiplier;
        if (c.fallAutoSafe >= 0) TestRules.FallAutoSafe = c.fallAutoSafe == 1;
        if (c.fallSafeExtra >= 0f) TestRules.FallSafeExtra = c.fallSafeExtra;
        if (c.hideUnlockPopups >= 0) TestRules.HideUnlockPopups = c.hideUnlockPopups == 1;
        if (c.hideTutorials >= 0) TestRules.HideTutorials = c.hideTutorials == 1;
        if (c.autoDiscovery >= 0) TestRules.AutoDiscovery = c.autoDiscovery == 1;

        if (c.stationLevel >= 0) TestRules.StationLevel = c.stationLevel;
        if (c.costMultiplier >= 0f) TestRules.CostMultiplier = c.costMultiplier;
        if (c.carryWeight >= 0f) TestRules.MaxCarryWeight = c.carryWeight;
        if (c.runSpeedMultiplier >= 0f) TestRules.RunSpeedMultiplier = c.runSpeedMultiplier;
        if (c.jumpMultiplier >= 0f) TestRules.JumpMultiplier = c.jumpMultiplier;
        if (c.infiniteStamina >= 0) PlayerTab.InfiniteStamina = c.infiniteStamina == 1;

        // arena / teleport
        var arenaMode = c.arena.ToLowerInvariant();
        if (arenaMode == "build")
            arenaMode = TestLabConfig.Instance.ArenaPlacement.Value == ArenaMode.Spawn ? "spawn" : "findflat";

        if (arenaMode == "findflat")
        {
            var site = ArenaBuilder.FindFlatSite(player.transform.position);
            yield return ArenaBuilder.BuildAt(site, true, TestLabConfig.Instance.BuildHouse.Value, Mathf.Max(10f, c.arenaRadius));
        }
        else if (arenaMode == "spawn")
        {
            var cfg = TestLabConfig.Instance;
            var spawn = player.transform.position;
            TestRules.SpawnPoint = spawn;
            var houseAt = spawn + new Vector3(cfg.HouseOffsetX.Value, 0f, cfg.HouseOffsetZ.Value);
            var radius = Mathf.Max(10f, c.arenaRadius == 40f ? cfg.ArenaRadius.Value : c.arenaRadius);
            yield return ArenaBuilder.BuildAt(spawn, false, cfg.BuildHouse.Value, radius, houseAt, cfg.KeepRadius.Value);
        }
        else if (arenaMode == "here")
        {
            yield return ArenaBuilder.BuildAt(player.transform.position, false, true, Mathf.Max(10f, c.arenaRadius));
        }
        else if (c.teleportX > -9000f && c.teleportZ > -9000f)
        {
            GameRefs.Teleport(c.teleportX, c.teleportZ);
            yield return new WaitForSeconds(2f);
        }

        switch (c.buildRule.ToLowerInvariant())
        {
            case "off": TestRules.BuildRule = BuildRule.Off; break;
            case "onlyinside": TestRules.BuildRule = BuildRule.OnlyInsideArena; break;
            case "blockinside": TestRules.BuildRule = BuildRule.BlockInsideArena; break;
        }

        // world
        var env = EnvMan.instance;
        if (env != null)
        {
            if (c.timeOfDay >= 0f)
            {
                env.m_debugTime = Mathf.Clamp01(c.timeOfDay);
                env.m_debugTimeOfDay = c.freezeTime != 0;
            }
            else if (c.freezeTime >= 0)
            {
                env.m_debugTimeOfDay = c.freezeTime == 1;
            }

            if (c.weather.Length > 0)
                env.m_debugEnv = c.weather.Equals("auto", StringComparison.OrdinalIgnoreCase) ? "" : c.weather;
        }

        foreach (var key in c.globalKeys) GameRefs.RunConsoleCommand("setkey " + key);
        foreach (var key in c.removeKeys) GameRefs.RunConsoleCommand("removekey " + key);

        // modes (these are toggles in-game, so only flip when the state differs)
        Sync(c.god, player.InGodMode(), "god");
        Sync(c.ghost, player.InGhostMode(), "ghost");
        Sync(c.fly, player.InDebugFlyMode(), "fly");
        Sync(c.noCost, player.NoCostCheat(), "nocost");
        if (c.hostile >= 0 && TestLabConfig.Instance.CreaturesHostile.Value != (c.hostile == 1))
            TestControlsApplier.ToggleHostile();

        // knowledge
        var discover = c.discover.Trim().ToLowerInvariant();
        if (discover == "all") Discovery.DiscoverAll();
        else if (discover == "none") Discovery.Reset();
        else if (discover.StartsWith("tier:") && int.TryParse(discover.Substring(5), out var tier)) Discovery.DiscoverUpToTier(tier);

        // bosses
        foreach (var name in c.bosses)
        {
            if (name.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var b in BossTools.All)
                    BossTools.Defeat(b, giveTrophy: false, setPower: false);
                continue;
            }

            var boss = BossTools.Find(name);
            if (boss != null)
                BossTools.Defeat(boss);
            else
                TestLabPlugin.Log?.LogWarning($"Case boss '{name}' not recognised.");
        }

        // fires
        if (c.infiniteFuel >= 0) FireTools.InfiniteFuel = c.infiniteFuel == 1;
        if (c.refuel) FireTools.Refuel();

        // inventory
        if (c.clearInventory)
            player.GetInventory().RemoveAll();
        var givenSummary = new List<string>();
        foreach (var item in c.items)
        {
            var given = ItemGiver.Give(item.prefab, Mathf.Max(1, item.count), Mathf.Max(1, item.quality), item.equip);
            givenSummary.Add($"{item.prefab} x{given}");
        }

        if (givenSummary.Count > 0)
            TestLabPlugin.Log?.LogInfo("Case items: " + string.Join(", ", givenSummary));
        foreach (var skill in c.skills)
            GameRefs.RunConsoleCommand($"raiseskill {skill.name} {skill.level}");

        // vitals last so food/equipment recalculation does not undo them
        if (c.maxHealth >= 0f) StatSetter.SetMaxHealth(c.maxHealth);
        if (c.maxStamina >= 0f) StatSetter.SetMaxStamina(c.maxStamina);
        if (c.maxEitr >= 0f) StatSetter.SetMaxEitr(c.maxEitr);
        yield return null;
        if (c.health >= 0f) player.SetHealth(c.health);

        // identity
        if (c.playAs.Length > 0)
        {
            if (c.playAs.Equals("me", StringComparison.OrdinalIgnoreCase))
                WardTools.RestoreOriginal();
            else
                WardTools.PlayAs(WardTools.Parse(c.playAs), c.playAsLook);
        }

        // wards
        foreach (var w in c.wards)
        {
            var origin = player.transform.position + player.transform.right * w.dx + player.transform.forward * w.dz;
            var permitted = new List<WardTools.Identity>();
            foreach (var who in w.permitted)
                permitted.Add(WardTools.Parse(who));
            WardTools.Spawn(origin, WardTools.Parse(w.owner), w.enabled, permitted, w.radius);
        }

        // spawns
        foreach (var s in c.spawns)
            SpawnCreature(player, s);

        foreach (var cmd in c.commands)
            GameRefs.RunConsoleCommand(cmd);

        LastResult = $"Applied '{label}'.";
        TestLabPlugin.Log?.LogInfo(LastResult);
        Running = false;
    }

    private static void Sync(int wanted, bool current, string command)
    {
        if (wanted >= 0 && (wanted == 1) != current)
            GameRefs.RunConsoleCommand(command);
    }

    private static void SpawnCreature(Player player, SpawnSpec s)
    {
        var prefab = ZNetScene.instance?.GetPrefab(s.prefab);
        if (prefab == null)
        {
            TestLabPlugin.Log?.LogWarning($"Case spawn: prefab '{s.prefab}' not found.");
            return;
        }

        var t = player.transform;
        for (var i = 0; i < Mathf.Max(1, s.count); i++)
        {
            var pos = t.position + t.right * (s.dx + i * 2f) + t.forward * s.dz;
            pos.y = ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(pos) + 0.5f : pos.y;
            var go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
            var character = go.GetComponent<Character>();
            if (character != null && s.level > 1)
                character.SetLevel(s.level);
            if (s.tamed)
                HarmonyLib.AccessTools.Method(typeof(Tameable), "Tame")?.Invoke(go.GetComponent<Tameable>(), null); // private in 1.0
        }
    }

    private const string Readme = @"# DrakesTestLab test cases

Each *.json file in this folder is a test case. Every field is optional - a case only changes what it names.
Apply one from the toolbox (F9 > Cases), or set [Case] OnLoad = <file name without .json> in com.drakemods.testlab.cfg
to apply it automatically on every load.

Tri-state values (god, ghost, fly, noCost, hostile, infiniteStamina, freezeTime): -1 leave alone, 0 off, 1 on.

| field | meaning |
|---|---|
| reset | -1 follows the Cases-tab reset-first setting (default on), 0 never, 1 always: clears rules, modes, inventory, skills, discovery, boss keys, wards, identity, time and weather before the case is applied |
| timeOfDay, freezeTime | 0 = midnight, 0.5 = noon; freeze keeps it there |
| weather | environment name (e.g. Clear, Rain, Snow) or ""auto"" |
| globalKeys / removeKeys | world keys to set or remove |
| arena | ""build"" follows [Arena] Placement in the config (default: level and clear the spawn area, keep the standing stones, house beside it); ""spawn"", ""findflat"" force a mode; ""here"" builds at your feet |
| arenaRadius | size of the flat area |
| buildRule | ""off"", ""onlyInside"" (mystical force blocks building outside the arena), ""blockInside"" |
| teleportX / teleportZ | teleport to world coordinates (set both) |
| health, maxHealth, stamina, maxStamina, eitr, maxEitr | vitals (max values stay pinned) |
| bosses | boss names to mark defeated: Eikthyr, The Elder, Bonemass, Moder, Yagluth, The Queen, Fader or all (sets the world key, the guardian power and gives the trophy) |
| noBuild, noBuildScale, noBuildRadius | the vanilla ""mystical force"" no-build zones: vanilla, off or scaled (scale multiplies every zone radius, radius forces one size in metres) |
| fallDamageMultiplier, fallAutoSafe, fallSafeExtra | fall damage: 0 none, 1 vanilla; auto-safe means falls up to the height you can jump (with a raised jumpMultiplier) never hurt; extra safe metres |
| playAs, playAsLook | become another player: Alice, Bob, Carol, any name or Name:12345 (me switches back); playAsLook also changes model and hair |
| wards | list of {dx, dz, owner, enabled, permitted, radius} test wards; owner and permitted accept me, Alice, Bob, Carol, any name or Name:12345; they start OFF unless enabled is true |
| refuel / infiniteFuel | top up fires and fuel stations once / keep them topped up |
| carryWeight | max carry weight, e.g. 9999 |
| runSpeedMultiplier, jumpMultiplier | movement |
| stationLevel | >0: every crafting station is this level and ignores roof/fire |
| costMultiplier | ingredient cost multiplier, 0 = free |
| discover | ""all"", ""none"", ""tier:N"" (0 Meadows, 1 Black Forest, 2 Swamp, 3 Mountain, 4 Plains, 5 Mistlands, 6 Ashlands) |
| clearInventory | wipe inventory first |
| items | [{prefab, count, quality, equip}] |
| skills | [{name, level}] raises the skill by that much |
| spawns | [{prefab, count, level, dx, dz, tamed}] relative to the player (dz = metres in front) |
| commands | raw console commands, run last |

See the shipped cases for working examples. Full documentation: https://github.com/drakethos/DrakesTestLab/wiki/Test-Cases
";
}

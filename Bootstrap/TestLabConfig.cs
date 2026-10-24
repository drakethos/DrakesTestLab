using BepInEx.Configuration;

namespace DrakesTestLab.Bootstrap;

internal enum ArenaMode
{
    Spawn,
    FindFlat
}

internal sealed class TestLabConfig
{
    public static TestLabConfig Instance { get; private set; } = null!;

    public ConfigEntry<bool> BootstrapEnabled { get; private set; } = null!;
    public ConfigEntry<bool> SkipIntro { get; private set; } = null!;
    public ConfigEntry<bool> FailOpenToMenu { get; private set; } = null!;

    public ConfigEntry<SaveModeKind> SaveMode { get; private set; } = null!;
    public ConfigEntry<string> EphemeralWorldName { get; private set; } = null!;
    public ConfigEntry<string> EphemeralCharacterName { get; private set; } = null!;
    public ConfigEntry<string> RunOnLaunch { get; private set; } = null!;
    public ConfigEntry<bool> QuitWhenDone { get; private set; } = null!;
    public ConfigEntry<float> StepDelay { get; private set; } = null!;
    public ConfigEntry<string> Screenshots { get; private set; } = null!;
    public ConfigEntry<int> KeepRuns { get; private set; } = null!;
    public ConfigEntry<float> TestTimeout { get; private set; } = null!;
    public ConfigEntry<string> PickerKey { get; private set; } = null!;
    public ConfigEntry<string> NextStepKey { get; private set; } = null!;
    public ConfigEntry<string> StopTestsKey { get; private set; } = null!;
    public ConfigEntry<string> RecorderCheckKey { get; private set; } = null!;
    public ConfigEntry<string> RecorderInventoryKey { get; private set; } = null!;
    public ConfigEntry<string> RecorderScreenshotKey { get; private set; } = null!;
    public ConfigEntry<string> RecorderPauseKey { get; private set; } = null!;
    public ConfigEntry<string> ManualPassKey { get; private set; } = null!;
    public ConfigEntry<string> ManualFailKey { get; private set; } = null!;
    public ConfigEntry<bool> OpenToFriends { get; private set; } = null!;
    public ConfigEntry<bool> PublicListing { get; private set; } = null!;
    public ConfigEntry<bool> Crossplay { get; private set; } = null!;
    public ConfigEntry<string> ServerPassword { get; private set; } = null!;
    public ConfigEntry<NoBuildMode> NoBuildZones { get; private set; } = null!;
    public ConfigEntry<float> NoBuildScale { get; private set; } = null!;
    public ConfigEntry<float> NoBuildRadius { get; private set; } = null!;
    public ConfigEntry<float> FallDamage { get; private set; } = null!;
    public ConfigEntry<bool> HideUnlockPopups { get; private set; } = null!;
    public ConfigEntry<bool> HideTutorials { get; private set; } = null!;
    public ConfigEntry<bool> AutoDiscovery { get; private set; } = null!;
    public ConfigEntry<bool> FallAutoSafe { get; private set; } = null!;
    public ConfigEntry<ArenaMode> ArenaPlacement { get; private set; } = null!;
    public ConfigEntry<float> ArenaRadius { get; private set; } = null!;
    public ConfigEntry<float> KeepRadius { get; private set; } = null!;
    public ConfigEntry<bool> ClearRocks { get; private set; } = null!;
    public ConfigEntry<bool> BuildHouse { get; private set; } = null!;
    public ConfigEntry<float> HouseOffsetX { get; private set; } = null!;
    public ConfigEntry<float> HouseOffsetZ { get; private set; } = null!;
    public ConfigEntry<string> CaseOnLoad { get; private set; } = null!;
    public ConfigEntry<bool> CaseResetFirst { get; private set; } = null!;
    public ConfigEntry<bool> AutoBuildArena { get; private set; } = null!;
    public ConfigEntry<bool> KeepWorld { get; private set; } = null!;
    public ConfigEntry<bool> KeepCharacter { get; private set; } = null!;
    public ConfigEntry<bool> FreshCharacter { get; private set; } = null!;
    public ConfigEntry<string> CharacterDisplayName { get; private set; } = null!;
    public ConfigEntry<string> OverlayKey { get; private set; } = null!;
    public ConfigEntry<bool> ShowOverlay { get; private set; } = null!;
    public ConfigEntry<float> UiScale { get; private set; } = null!;
    public ConfigEntry<string> TemplateWorldFile { get; private set; } = null!;
    public ConfigEntry<string> TemplateCharacterFile { get; private set; } = null!;
    public ConfigEntry<string> PersistentWorldName { get; private set; } = null!;
    public ConfigEntry<string> PersistentCharacterName { get; private set; } = null!;

    public ConfigEntry<bool> HotReloadEnabled { get; private set; } = null!;
    public ConfigEntry<int> HotReloadDebounceMs { get; private set; } = null!;
    public ConfigEntry<string> MenuKey { get; private set; } = null!;
    public ConfigEntry<bool> PanelWritesConfig { get; private set; } = null!;

    public ConfigEntry<bool> HijackLogout { get; private set; } = null!;
    public ConfigEntry<bool> LogoutMeansReset { get; private set; } = null!;
    public ConfigEntry<bool> ShowMenuOnLogout { get; private set; } = null!;

    public ConfigEntry<bool> AutoEnableDevCommands { get; private set; } = null!;
    public ConfigEntry<bool> AutoEnableDebugMode { get; private set; } = null!;
    public ConfigEntry<bool> CreaturesHostile { get; private set; } = null!;
    public ConfigEntry<bool> ApplyControlsOnWorldReady { get; private set; } = null!;

    public ConfigEntry<float> StartTimeOfDay { get; private set; } = null!;
    public ConfigEntry<bool> OverrideSpawnPosition { get; private set; } = null!;
    public ConfigEntry<float> SpawnX { get; private set; } = null!;
    public ConfigEntry<float> SpawnY { get; private set; } = null!;
    public ConfigEntry<float> SpawnZ { get; private set; } = null!;

    public ConfigEntry<bool> GodMode { get; private set; } = null!;
    public ConfigEntry<string> SkillLevels { get; private set; } = null!;
    public ConfigEntry<string> StartingItems { get; private set; } = null!;

    private SaveModeKind _lastSaveMode;
    private bool _lastBootstrapEnabled;

    public static void Bind(ConfigFile config)
    {
        Instance = new TestLabConfig();
        var c = Instance;

        c.BootstrapEnabled = config.Bind("Bootstrap", "Enabled", true, "Auto-skip menu and load test world/character.");
        c.SkipIntro = config.Bind("Bootstrap", "SkipIntro", true, "Try to skip intro logos when bootstrapping.");
        c.FailOpenToMenu = config.Bind("Bootstrap", "FailOpenToMenu", true, "If fast boot fails, leave the main menu available.");

        c.SaveMode = config.Bind("Save", "Mode", SaveModeKind.EphemeralFresh,
            "EphemeralFresh = new world + character every launch. EphemeralReuse = copy templates once, then keep them. Persistent = separate persistent world/character names.");
        c.EphemeralWorldName = config.Bind("Save", "EphemeralWorldName", PresetDeployer.DefaultEphemeralWorldName,
            "World file basename copied into worlds_local for ephemeral modes (e.g. NACHO_WORLD). Empty = TestLab.");
        c.EphemeralCharacterName = config.Bind("Save", "EphemeralCharacterName", PresetDeployer.DefaultEphemeralCharacterName,
            "Character file basename copied into characters_local for ephemeral modes (e.g. NACHO_TEST_BOI). Empty = TestBjorn.");
        c.RunOnLaunch = config.Bind("Automation", "RunOnLaunch", "",
            "Run automated test suites by themselves once the world is ready: empty = off, all = every suite, or suite folder names separated by commas.");
        c.QuitWhenDone = config.Bind("Automation", "QuitWhenDone", true,
            "After a RunOnLaunch run, quit the game with exit code 0 (all passed) or 1 (something failed). Results are in config/DrakesTestLab/results.");
        c.StepDelay = config.Bind("Automation", "StepDelay", 0.25f, "Pause in seconds between test steps (0 = as fast as possible; raise it to watch).");
        c.Screenshots = config.Bind("Automation", "Screenshots", "Failures",
            new ConfigDescription("Screenshots saved with the results: Off, Failures (a test that failed), or Always (proof of every test's end state).",
                new AcceptableValueList<string>("Off", "Failures", "Always")));
        c.KeepRuns = config.Bind("Automation", "KeepRuns", 5,
            "How many runs' screenshot folders (results/run-<date>) to keep; older ones are deleted when a run starts. 0 = keep all.");
        c.TestTimeout = config.Bind("Automation", "TestTimeout", 120f, "Seconds a single test may take before it fails (a test can set its own \"timeout\").");
        c.PickerKey = config.Bind("Automation", "PickerKey", "F8", "With the UI picker on, this key copies the name of the game button or window under the mouse.");
        c.NextStepKey = config.Bind("Automation", "NextStepKey", "F7", "In step mode, run the next test step.");
        c.StopTestsKey = config.Bind("Automation", "StopTestsKey", "F6", "Stop the running tests.");
        c.RecorderCheckKey = config.Bind("Automation", "RecorderCheckKey", "F7", "While recording a test: add checks for what the crosshair is on (hover text, who built it, what a chest holds). Keys can have modifiers: Shift+F7, Ctrl+F7, Alt+F7.");
        c.RecorderInventoryKey = config.Bind("Automation", "RecorderInventoryKey", "Shift+F7", "While recording: add checks for everything you carry.");
        c.RecorderScreenshotKey = config.Bind("Automation", "RecorderScreenshotKey", "Ctrl+F7", "While recording: add a screenshot step.");
        c.RecorderPauseKey = config.Bind("Automation", "RecorderPauseKey", "Alt+F7", "While recording: pause / resume (nothing is recorded while paused).");
        c.ManualPassKey = config.Bind("Automation", "ManualPassKey", "F7", "At a manual check (checkpoint): it passed.");
        c.ManualFailKey = config.Bind("Automation", "ManualFailKey", "Shift+F7", "At a manual check (checkpoint): it failed.");
        c.OpenToFriends = config.Bind("Server", "OpenToFriends", false,
            "Share the test world: friends can join through Steam (Friends list > Join game) while you play. Off = single player. Applied when the world starts (use Reset to apply). The server terms must have been accepted once in the normal Start Game screen.");
        c.PublicListing = config.Bind("Server", "PublicListing", false,
            "List the world in the community server browser. Needs a password of 5+ characters (a default is used if ServerPassword is shorter).");
        c.Crossplay = config.Bind("Server", "Crossplay", false,
            "Allow crossplay (PlayFab) joiners. Needs a PlayFab login and uses join codes instead of the Steam friends list.");
        c.ServerPassword = config.Bind("Server", "ServerPassword", "",
            "Password guests must enter. Empty = none (fine for friends-only). Must be 5+ characters if PublicListing is on.");
        c.NoBuildZones = config.Bind("Rules", "NoBuildZones", NoBuildMode.Off,
            "Vanilla = keep the game's no-build zones around the spawn temple/altars. Off = build anywhere. Scaled = use NoBuildScale / NoBuildRadius.");
        c.NoBuildScale = config.Bind("Rules", "NoBuildScale", 1f, "Scaled mode: multiplier for every no-build zone radius (0.5 half, 2 double).");
        c.NoBuildRadius = config.Bind("Rules", "NoBuildRadius", 0f, "Scaled mode: if above 0, every no-build zone uses exactly this radius (metres).");
        c.HideUnlockPopups = config.Bind("Rules", "HideUnlockPopups", true,
            "Hide the 'New crafting recipe / item / station' pop-ups (noise when the test house is built). Tests can still check them with unlockShown.");
        c.HideTutorials = config.Bind("Rules", "HideTutorials", true, "Hide the raven (Hugin) tutorial pop-ups. Tests can still check them with the message check.");
        c.AutoDiscovery = config.Bind("Rules", "AutoDiscovery", true,
            "Off = picking items up no longer teaches materials and recipes; only the toolbox and test cases discover things.");
        c.FallDamage = config.Bind("Rules", "FallDamageMultiplier", 1f, "1 = vanilla fall damage, 0 = none.");
        c.FallAutoSafe = config.Bind("Rules", "FallAutoSafe", true, "With a raised jump multiplier, falls up to the height you can jump do no damage.");
        c.ArenaPlacement = config.Bind("Arena", "Placement", ArenaMode.Spawn,
            "Spawn = level and clear the area around where you spawn (standing stones kept). FindFlat = search for naturally flat meadow and teleport there.");
        c.ArenaRadius = config.Bind("Arena", "Radius", 40f, "Radius of the levelled, cleared test area in metres.");
        c.KeepRadius = config.Bind("Arena", "KeepRadius", 20f,
            "Spawn mode: nothing within this many metres of the spawn point is removed (keeps the spawn standing stones).");
        c.ClearRocks = config.Bind("Arena", "ClearRocks", true, "Also remove boulders and mineable rocks from the test area (outside KeepRadius).");
        c.BuildHouse = config.Bind("Arena", "BuildHouse", true, "Build the test house with all crafting stations in the arena.");
        c.HouseOffsetX = config.Bind("Arena", "HouseOffsetX", 0f, "Spawn mode: east/west offset of the test house from the spawn point.");
        c.HouseOffsetZ = config.Bind("Arena", "HouseOffsetZ", 26f, "Spawn mode: north/south offset of the test house from the spawn point.");
        c.CaseResetFirst = config.Bind("Case", "ResetFirst", true,
            "Reset the test state (rules, modes, inventory, skills, discovery, boss keys, wards, identity, time) before applying a test case, so cases never stack on each other.");
        c.CaseOnLoad = config.Bind("Case", "OnLoad", "",
            "Name (no .json) of a test case in config/DrakesTestLab/cases to apply on every world load.");
        c.AutoBuildArena = config.Bind("Arena", "AutoBuild", true,
            "On load (when no OnLoad case is set): build the test arena and house the way Placement says (levelled spawn area, or a flat-ground search).");
        c.KeepWorld = config.Bind("Save", "KeepWorld", false,
            "On = do not reset the test world between launches (or on Reset): whatever you built stays. Off = a fresh world every launch.");
        c.KeepCharacter = config.Bind("Save", "KeepCharacter", false,
            "On = do not reset the test character between launches: inventory, skills and position stay. Off = a fresh character every launch.");
        c.FreshCharacter = config.Bind("Save", "FreshCharacter", true,
            "Create a brand-new clean character on every reset instead of copying the template .fch (which carries gear/skills).");
        c.CharacterDisplayName = config.Bind("Save", "CharacterDisplayName", "Bjorn",
            "In-game name of the freshly created test character.");
        c.OverlayKey = config.Bind("Dev", "OverlayKey", "F10", "Key to toggle the compact info overlay (Unity KeyCode name).");
        c.ShowOverlay = config.Bind("Dev", "ShowOverlay", false, "Show the compact info overlay (FPS, position, biome, time).");
        c.UiScale = config.Bind("Dev", "UiScale", 1f, new ConfigDescription("Toolbox UI scale.", new AcceptableValueRange<float>(0.6f, 2.5f)));
        c.TemplateWorldFile = config.Bind("Save", "TemplateWorldFile", PresetDeployer.DefaultTemplateWorldFile,
            "Template world name in Assets/Presets/templates/: a Valheim 1.0 world folder (preferred) or a legacy .fwl/.db pair. Empty = TestLab.");
        c.TemplateCharacterFile = config.Bind("Save", "TemplateCharacterFile", PresetDeployer.DefaultTemplateCharacterFile,
            "Template character basename in Assets/Presets/templates/ (.fch). Empty = MasterBjorn. Falls back if missing.");
        c.PersistentWorldName = config.Bind("Save", "PersistentWorldName", "TestWorldPersistent",
            "World file basename copied from TemplateWorldFile for Persistent mode. Empty = TestWorldPersistent.");
        c.PersistentCharacterName = config.Bind("Save", "PersistentCharacterName", "BjornPersistent",
            "Character file basename copied from TemplateCharacterFile for Persistent mode. Empty = BjornPersistent.");

        c.HotReloadEnabled = config.Bind("Dev", "HotReloadEnabled", true, "Watch cfg file and reload on save.");
        c.HotReloadDebounceMs = config.Bind("Dev", "HotReloadDebounceMs", 500, "Debounce ms for cfg file watcher.");
        c.MenuKey = config.Bind("Dev", "MenuKey", "F9", "Key to open test control panel (Unity KeyCode name).");
        c.PanelWritesConfig = config.Bind("Dev", "PanelWritesConfig", true, "Panel toggles write back to cfg file.");

        c.HijackLogout = config.Bind("Menu", "HijackLogout", true, "Intercept logout-to-main-menu.");
        c.LogoutMeansReset = config.Bind("Menu", "LogoutMeansReset", true, "Vanilla logout triggers hard reset instead of FejdStartup.");
        c.ShowMenuOnLogout = config.Bind("Menu", "ShowMenuOnLogout", false, "Open test panel on logout instead of immediate reset.");

        c.AutoEnableDevCommands = config.Bind("TestControls", "AutoEnableDevCommands", true, "Run devcommands on local host.");
        c.AutoEnableDebugMode = config.Bind("TestControls", "AutoEnableDebugMode", true, "Run debugmode on local host.");
        c.CreaturesHostile = config.Bind("TestControls", "CreaturesHostile", false, "When false, suppress monster aggro.");
        c.ApplyControlsOnWorldReady = config.Bind("TestControls", "ApplyOnWorldReady", true, "Apply controls after local player exists.");

        c.StartTimeOfDay = config.Bind("Spawn", "StartTimeOfDay", 0.5f,
            "Freeze the world clock at this fraction of the day on load (0 midnight, 0.5 noon). -1 = leave the clock running.");
        c.OverrideSpawnPosition = config.Bind("Spawn", "OverridePosition", false, "Teleport local player after spawn.");
        c.SpawnX = config.Bind("Spawn", "PositionX", 0f, "Spawn X.");
        c.SpawnY = config.Bind("Spawn", "PositionY", 30f, "Spawn Y.");
        c.SpawnZ = config.Bind("Spawn", "PositionZ", 0f, "Spawn Z.");

        c.GodMode = config.Bind("Player", "GodMode", false, "Enable god mode on local player.");
        c.SkillLevels = config.Bind("Player", "SkillLevels", "", "Skills e.g. run:10,jump:5");
        c.StartingItems = config.Bind("Player", "StartingItems", "", "Items e.g. Hammer:1,Wood:50 (inventory only).");

        c._lastSaveMode = c.SaveMode.Value;
        c._lastBootstrapEnabled = c.BootstrapEnabled.Value;
    }

    public void CaptureWarmTierBaseline()
    {
        _lastSaveMode = SaveMode.Value;
        _lastBootstrapEnabled = BootstrapEnabled.Value;
    }

    public bool WarmTierChanged()
    {
        return SaveMode.Value != _lastSaveMode || BootstrapEnabled.Value != _lastBootstrapEnabled;
    }

    public void SyncWarmTierBaseline()
    {
        _lastSaveMode = SaveMode.Value;
        _lastBootstrapEnabled = BootstrapEnabled.Value;
    }
}

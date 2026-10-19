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
    public ConfigEntry<bool> OpenToFriends { get; private set; } = null!;
    public ConfigEntry<bool> PublicListing { get; private set; } = null!;
    public ConfigEntry<bool> Crossplay { get; private set; } = null!;
    public ConfigEntry<string> ServerPassword { get; private set; } = null!;
    public ConfigEntry<NoBuildMode> NoBuildZones { get; private set; } = null!;
    public ConfigEntry<float> NoBuildScale { get; private set; } = null!;
    public ConfigEntry<float> NoBuildRadius { get; private set; } = null!;
    public ConfigEntry<float> FallDamage { get; private set; } = null!;
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
    public ConfigEntry<string> ValheimExePath { get; private set; } = null!;
    public ConfigEntry<bool> ReloadInProcess { get; private set; } = null!;
    public ConfigEntry<bool> PanelWritesConfig { get; private set; } = null!;
    public ConfigEntry<bool> SaveBeforeRestart { get; private set; } = null!;

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
        c.ValheimExePath = config.Bind("Dev", "ValheimExePath", "", "Optional full path to valheim.exe for hard reset.");
        c.ReloadInProcess = config.Bind("Dev", "ReloadInProcess", true,
            "Reset by returning to the main menu inside the running game (all mods stay loaded) instead of relaunching valheim.exe, which can come back without your mod profile.");
        c.PanelWritesConfig = config.Bind("Dev", "PanelWritesConfig", true, "Panel toggles write back to cfg file.");
        c.SaveBeforeRestart = config.Bind("Dev", "SaveBeforeRestart", true, "Flush world save before hard reset.");

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

using System;
using System.Collections.Generic;

namespace DrakesTestLab.Cases;

// JsonUtility-friendly. Every "unset" default is a sentinel (-1 / "" / empty list) so a case only changes what it names.
// Tri-state ints: -1 = leave alone, 0 = off, 1 = on.

[Serializable]
public sealed class ItemSpec
{
    public string prefab = "";
    public int count = 1;
    public int quality = 1;
    public bool equip;
}

[Serializable]
public sealed class SkillSpec
{
    public string name = "";
    public int level = 100;
}

[Serializable]
public sealed class SpawnSpec
{
    public string prefab = "";
    public int count = 1;
    public int level = 1;
    public float dx;
    public float dz = 6f;
    public bool tamed;
}

[Serializable]
public sealed class WardSpec
{
    public float dx;
    public float dz = 8f;
    public string owner = "me";                 // "me", Alice, Bob, Carol, any name, or "Name:12345"
    public bool enabled;                        // wards start OFF so you can stage the scenario
    public List<string> permitted = new();      // same name format as owner
    public float radius = -1f;                  // -1 = the ward's own radius
}

[Serializable]
public sealed class TestCase
{
    public string name = "";
    public string description = "";

    // reset the test state before applying: -1 follow the Cases-tab setting, 0 never, 1 always
    public int reset = -1;

    // world
    public float timeOfDay = -1f;
    public int freezeTime = -1;
    public string weather = "";
    public List<string> globalKeys = new();
    public List<string> removeKeys = new();

    // arena: "" (leave) | "build" (as set in the Arena config: spawn area or flat-ground search) | "spawn" (level + clear the spawn area,
    // house beside it) | "findflat" (search flat meadow, teleport there) | "here" (build at the player)
    public string arena = "";
    public float arenaRadius = 40f;
    public string buildRule = "";        // "" | "off" | "onlyInside" | "blockInside"
    public float teleportX = -9999f;     // set both to teleport (y is looked up)
    public float teleportZ = -9999f;

    // player modes
    public int god = -1;
    public int ghost = -1;
    public int fly = -1;
    public int noCost = -1;
    public int hostile = -1;
    public int infiniteStamina = -1;

    // vitals / movement (-1 = unchanged)
    public float health = -1f;
    public float maxHealth = -1f;
    public float stamina = -1f;
    public float maxStamina = -1f;
    public float eitr = -1f;
    public float maxEitr = -1f;
    public float carryWeight = -1f;
    public float runSpeedMultiplier = -1f;
    public float jumpMultiplier = -1f;

    // rules: "" leave | "vanilla" | "off" | "scaled" ; scale and radius only matter for "scaled" (-1 = leave)
    public string noBuild = "";
    public float noBuildScale = -1f;
    public float noBuildRadius = -1f;

    // fall damage: multiplier (-1 leave, 0 none, 1 vanilla); auto-safe tri-state; extra safe metres (-1 leave)
    public float fallDamageMultiplier = -1f;
    public int fallAutoSafe = -1;
    public float fallSafeExtra = -1f;

    // play as another player for ward tests: Alice, Bob, Carol, any name, "Name:12345", or "me" to switch back
    public string playAs = "";
    public bool playAsLook = true;

    // ward test bench: wards spawned relative to the player
    public List<WardSpec> wards = new();

    // crafting
    public int stationLevel = -1;        // 0 = vanilla, >0 = every station is this level and ignores roof/fire
    public float costMultiplier = -1f;   // 0 = free

    // knowledge: "" | "all" | "none" | "tier:N" (0 Meadows, 1 Black Forest, 2 Swamp, 3 Mountain, 4 Plains, 5 Mistlands, 6 Ashlands)
    public string discover = "";

    // bosses to mark defeated (world key + guardian power + trophy): names like "Eikthyr", "The Elder", or "all"
    public List<string> bosses = new();

    // fires and fuel stations: refuel once / keep refuelling (-1 leave, 0 off, 1 on)
    public bool refuel;
    public int infiniteFuel = -1;

    public bool clearInventory;
    public List<ItemSpec> items = new();
    public List<SkillSpec> skills = new();
    public List<SpawnSpec> spawns = new();

    // raw console commands, run last
    public List<string> commands = new();
}

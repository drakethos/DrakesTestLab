using System.Collections.Generic;

namespace DrakesTestLab.Combat;

/// <summary>One damage event the recorder saw during a combat run.</summary>
internal sealed class HitRecord
{
    public float T;                 // seconds since the run started
    public string Attacker = "";
    public string Victim = "";
    public bool VictimIsGear;       // true = the gear side (the side being tested) took the hit
    public float Raw;               // damage before armor / resistances
    public float Final;             // what the victim actually lost
    public string Types = "";       // e.g. "slash 42, fire 10"
    public bool Blocked;
    public bool Parried;
    public bool Backstab;
    public bool Crit;
    public bool Staggered;
    public float HpAfter;
}

/// <summary>The outcome of one fight (one repetition of a scenario under one variant).</summary>
internal sealed class RunRecord
{
    public string Scenario = "";
    public string Variant = "";
    public string Bot = "";
    public int Index;
    public int Lane;
    public int Mix = -1;
    public int TeamSize = 1;             // team fight: players fighting the foes together
    public int Deaths;                   // team fight: how many of them fell                 // mixed lanes: which setup (scenario "lanes" entry) this run used                     // 0 = the local player, 1+ = a bot lane player (spike)
    public string Outcome = "timeout";   // win | loss | timeout
    public float Seconds;
    public float TimeToKill = -1f;       // seconds until the last foe died (-1 = never)
    public float TimeToDie = -1f;        // seconds until the gear side died (-1 = never)
    public float DamageDealt;
    public float DamageTaken;
    public float GearMaxHealth;
    public float FoeMaxHealth;           // total max health of this run's foes (a win should have dealt about this much)
    public int Punishes;
    public int Backstabs;
    public string FirstHit = "";         // diagnosis: the gear side's first landed hit (alerted? backstab bonus? applied?)                // hits the game counted as backstabs (target unaware)                 // times the bot went all in on a staggered foe
    public int Blocks;
    public int Parries;                  // blocks raised in the parry window (Valheim: under 0.25 s before the hit)
    public int Swings;
    public int PrimarySwings;
    public int SecondarySwings;
    public int Consumables;
    public int Unsticks;                 // times the unsticker moved everybody to a new spot
    public List<HitRecord> Hits = new();
}

/// <summary>Numbers for one scenario under one variant, over all its runs.</summary>
internal sealed class Summary
{
    public string Scenario = "";
    public string Variant = "";
    public int Runs;
    public int Wins;
    public int Losses;
    public int Timeouts;
    public double WinRate;
    public double WinLow;            // Wilson 95% interval
    public double WinHigh;
    public Spread Ttk = new();       // time to kill, over won runs
    public Spread DamageTakenPct = new();   // % of max health lost, over all runs
    public Spread DpsDealt = new();
    public double Blocks;            // mean per run
    public double Parries;
    public double Consumables;
    public double Unsticks;          // mean per run
    public double SecondaryShare;    // share of swings that were the secondary attack
}

internal sealed class Spread
{
    public int N;
    public double Mean;
    public double Median;
    public double Sd;
    public double P10;
    public double P90;
    public double Min;
    public double Max;
}

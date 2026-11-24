using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DrakesTestLab.Automation;
using DrakesTestLab.Cases;

namespace DrakesTestLab.Combat;

/// <summary>
/// Baseline runs: a combat.json from a clean run (vanilla bronze and iron weapons, say) copied into
/// <c>combat/baselines/</c>. Every report then lines the new numbers up against them: for each loadout and foe that a
/// baseline also has, the baseline cell is added as its own variant ("baseline: bronze-vanilla"), so the compare view and the
/// clear-change counts work with no setup. A baseline is only numbers, so it needs no game and no mods.
/// </summary>
internal static class CombatBaseline
{
    public static string Folder => Path.Combine(CombatData.Folder, "baselines");

    public static void AddTo(CombatResult result)
    {
        if (!Directory.Exists(Folder))
            return;
        var mine = result.Cells.ToList();
        foreach (var file in Directory.GetFiles(Folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var label = "baseline: " + Path.GetFileNameWithoutExtension(file);
            try
            {
                if (MiniJson.Parse(File.ReadAllText(file)) is not Dictionary<string, object?> d)
                    continue;
                foreach (var o in d.Arr("cells"))
                {
                    if (o is not Dictionary<string, object?> c) continue;
                    var loadout = c.Str("loadout");
                    var foe = c.Str("foe");
                    // only where the new run has the same matchup, and not twice
                    if (!mine.Any(m => m.Loadout == loadout && m.Foe == foe)) continue;
                    if (result.Cells.Any(m => m.Loadout == loadout && m.Foe == foe && m.Variant == label)) continue;
                    var template = mine.First(m => m.Loadout == loadout && m.Foe == foe);
                    result.Cells.Add(new CombatCell
                    {
                        Loadout = loadout, Foe = foe, Variant = label, Summary = FromJson(c, template.Summary.Scenario, label),
                        Band = template.Band, Verdict = CombatVerdict.Judge(FromJson(c, template.Summary.Scenario, label), template.Band)
                    });
                }
            }
            catch (Exception ex)
            {
                TestLabPlugin.Log?.LogWarning($"Combat baseline {Path.GetFileName(file)} could not be read: {ex.Message}");
            }
        }
    }

    /// <summary>Rebuilds the numbers a comparison needs from a saved cell (win counts and time-to-kill spread).</summary>
    private static Summary FromJson(Dictionary<string, object?> c, string scenario, string variant)
    {
        var runs = (int)c.Num("runs");
        var wins = (int)c.Num("wins");
        var s = new Summary
        {
            Scenario = scenario, Variant = variant, Runs = runs, Wins = wins, Losses = (int)c.Num("losses"), Timeouts = (int)c.Num("timeouts"),
            WinRate = runs == 0 ? 0 : (double)wins / runs
        };
        (s.WinLow, s.WinHigh) = CombatStats.Wilson(wins, runs);
        s.Ttk = new Spread
        {
            N = wins, Mean = c.Num("ttkMean"), Median = c.Num("ttkMedian"), Sd = c.Num("ttkSd"), P10 = c.Num("ttkP10"), P90 = c.Num("ttkP90")
        };
        s.DamageTakenPct = new Spread { N = runs, Mean = c.Num("damageTakenPct") };
        s.DpsDealt = new Spread { N = runs, Mean = c.Num("dpsDealt") };
        s.Blocks = c.Num("blocks");
        return s;
    }
}

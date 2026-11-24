using System;
using System.Collections.Generic;
using System.Linq;

namespace DrakesTestLab.Combat;

/// <summary>Averages, spread and win-rate intervals for a set of runs. Pure maths, no game types.</summary>
internal static class CombatStats
{
    /// <summary>Under this many runs the report warns that the numbers are thin.</summary>
    public const int EnoughRuns = 20;

    /// <summary>
    /// Enough runs? At least 3; the outcome unanimous over 5 runs, or the win rate's 95% range narrower than 30 points;
    /// and the mean fight time (of the usual outcome) and the damage taken known to within 10% at 95%.
    /// </summary>
    public static bool Settled(IList<RunRecord> runs, out string why)
    {
        why = "";
        var n = runs.Count;
        if (n < 3) return false;
        var wins = runs.Count(r => r.Outcome == "win");
        var unanimous = wins == 0 || wins == n;
        var (lo, hi) = Wilson(wins, n);
        if (!(unanimous && n >= 5) && hi - lo > 0.30) return false;
        var usual = wins * 2 >= n ? "win" : "loss";
        var times = runs.Where(r => r.Outcome == usual).Select(r => (double)r.Seconds).ToList();
        if (!Tight(times, 0.10)) return false;
        var taken = runs.Select(r => (double)r.DamageTaken).ToList();
        if (taken.Average() > 5 && !Tight(taken, 0.10)) return false;      // near-zero damage taken: nothing to pin down
        why = $"{wins}/{n} wins, win range {100 * lo:0}-{100 * hi:0}%, {usual} time and damage taken within 10%";
        return true;
    }

    /// <summary>True when the 95% interval of the mean is within ±<paramref name="rel"/> of the mean (needs 3+ values).</summary>
    private static bool Tight(IList<double> v, double rel)
    {
        if (v.Count < 3) return false;
        var mean = v.Average();
        if (mean <= 0) return true;
        var sd = Math.Sqrt(v.Sum(x => (x - mean) * (x - mean)) / (v.Count - 1));
        var t = v.Count >= 30 ? 2.0 : new[] { 0, 0, 0, 4.30, 3.18, 2.78, 2.57, 2.45, 2.36, 2.31, 2.26, 2.23, 2.20, 2.18, 2.16, 2.14, 2.13, 2.12, 2.11, 2.10, 2.09, 2.09, 2.08, 2.07, 2.07, 2.06, 2.06, 2.06, 2.05, 2.05 }[v.Count];
        return t * sd / Math.Sqrt(v.Count) <= rel * mean;
    }

    public static Summary Summarize(string scenario, string variant, IList<RunRecord> runs)
    {
        var s = new Summary { Scenario = scenario, Variant = variant, Runs = runs.Count };
        foreach (var r in runs)
        {
            if (r.Outcome == "win") s.Wins++;
            else if (r.Outcome == "loss") s.Losses++;
            else s.Timeouts++;
        }

        s.WinRate = runs.Count == 0 ? 0 : (double)s.Wins / runs.Count;
        (s.WinLow, s.WinHigh) = Wilson(s.Wins, runs.Count);
        s.Ttk = Describe(runs.Where(r => r.Outcome == "win" && r.TimeToKill >= 0f).Select(r => (double)r.TimeToKill));
        s.DamageTakenPct = Describe(runs.Where(r => r.GearMaxHealth > 0f).Select(r => 100.0 * r.DamageTaken / r.GearMaxHealth));
        s.DpsDealt = Describe(runs.Where(r => r.Seconds > 0.1f).Select(r => (double)r.DamageDealt / r.Seconds));
        s.Blocks = runs.Count == 0 ? 0 : runs.Average(r => (double)r.Blocks);
        s.Parries = runs.Count == 0 ? 0 : runs.Average(r => (double)r.Parries);
        s.Consumables = runs.Count == 0 ? 0 : runs.Average(r => (double)r.Consumables);
        s.Unsticks = runs.Count == 0 ? 0 : runs.Average(r => (double)r.Unsticks);
        var swings = runs.Sum(r => r.PrimarySwings + r.SecondarySwings);
        s.SecondaryShare = swings == 0 ? 0 : (double)runs.Sum(r => r.SecondarySwings) / swings;
        return s;
    }

    public static Spread Describe(IEnumerable<double> values)
    {
        var v = values.Where(x => !double.IsNaN(x) && !double.IsInfinity(x)).OrderBy(x => x).ToList();
        var sp = new Spread { N = v.Count };
        if (v.Count == 0)
            return sp;
        sp.Mean = v.Average();
        sp.Median = Percentile(v, 0.5);
        sp.P10 = Percentile(v, 0.1);
        sp.P90 = Percentile(v, 0.9);
        sp.Min = v[0];
        sp.Max = v[v.Count - 1];
        sp.Sd = v.Count < 2 ? 0 : Math.Sqrt(v.Sum(x => (x - sp.Mean) * (x - sp.Mean)) / (v.Count - 1));
        return sp;
    }

    /// <summary>Linear-interpolated percentile of an ascending list.</summary>
    public static double Percentile(IList<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        if (sorted.Count == 1) return sorted[0];
        var pos = p * (sorted.Count - 1);
        var lo = (int)Math.Floor(pos);
        var hi = (int)Math.Ceiling(pos);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
    }

    /// <summary>Wilson score interval (95%) for wins out of n. Honest at small n and at 0% / 100%.</summary>
    public static (double Low, double High) Wilson(int wins, int n)
    {
        if (n <= 0) return (0, 1);
        const double z = 1.96;
        var p = (double)wins / n;
        var denom = 1 + z * z / n;
        var centre = (p + z * z / (2 * n)) / denom;
        var half = z * Math.Sqrt(p * (1 - p) / n + z * z / (4.0 * n * n)) / denom;
        return (Math.Max(0, centre - half), Math.Min(1, centre + half));
    }

    /// <summary>True when two win-rate intervals do not overlap: a difference worth believing at these run counts.</summary>
    public static bool ClearlyDifferent(Summary a, Summary b) => a.WinHigh < b.WinLow || b.WinHigh < a.WinLow;

    /// <summary>
    /// Time-to-kill difference test: means differ by more than ~2 standard errors (Welch). False when either side has
    /// fewer than 3 wins, since a spread cannot be trusted then.
    /// </summary>
    public static bool TtkClearlyDifferent(Summary a, Summary b)
    {
        if (a.Ttk.N < 3 || b.Ttk.N < 3) return false;
        var se = Math.Sqrt(a.Ttk.Sd * a.Ttk.Sd / a.Ttk.N + b.Ttk.Sd * b.Ttk.Sd / b.Ttk.N);
        return se > 0 ? Math.Abs(a.Ttk.Mean - b.Ttk.Mean) > 2 * se : Math.Abs(a.Ttk.Mean - b.Ttk.Mean) > 0.01;
    }
}

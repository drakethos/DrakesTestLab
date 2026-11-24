using System;

namespace DrakesTestLab.Combat;

internal enum Verdict
{
    None,           // no band to judge against
    MinusMinus,     // far too weak
    Minus,          // a bit too weak
    Normal,         // inside the band
    Plus,           // a bit too strong
    PlusPlus        // far too strong
}

/// <summary>The expected outcome for a gear tier against a foe. A bound left at -1 is not checked.</summary>
internal sealed class Band
{
    public double WinMin = -1;      // win rate, 0..1
    public double WinMax = -1;
    public double TtkMin = -1;      // seconds to kill (over wins)
    public double TtkMax = -1;
}

internal static class CombatVerdict
{
    /// <summary>How far outside the band (win rate points, or fraction of the time bound) still counts as only slightly off.</summary>
    public const double WinSlack = 0.10;
    public const double TtkSlack = 0.25;

    public static Verdict Judge(Summary s, Band? band)
    {
        if (band == null || s.Runs == 0)
            return Verdict.None;

        var worst = 0;   // signed: + too strong, - too weak; magnitude 0 normal, 1 slight, 2 far
        var checkedAny = false;

        if (band.WinMin >= 0 || band.WinMax >= 0)
        {
            checkedAny = true;
            // Judge on the interval, not the point: a cell is flagged only when even the kindest reading is outside the band.
            if (band.WinMax >= 0 && s.WinLow > band.WinMax)
                worst = Pick(worst, s.WinLow - band.WinMax > WinSlack ? 2 : 1);
            else if (band.WinMin >= 0 && s.WinHigh < band.WinMin)
                worst = Pick(worst, band.WinMin - s.WinHigh > WinSlack ? -2 : -1);
        }

        if ((band.TtkMin >= 0 || band.TtkMax >= 0) && s.Ttk.N >= 3)
        {
            checkedAny = true;
            if (band.TtkMin >= 0 && s.Ttk.Mean < band.TtkMin)         // kills faster than intended: too strong
                worst = Pick(worst, (band.TtkMin - s.Ttk.Mean) / band.TtkMin > TtkSlack ? 2 : 1);
            else if (band.TtkMax >= 0 && s.Ttk.Mean > band.TtkMax)    // kills slower than intended: too weak
                worst = Pick(worst, (s.Ttk.Mean - band.TtkMax) / band.TtkMax > TtkSlack ? -2 : -1);
        }

        if (!checkedAny)
            return Verdict.None;
        return worst switch
        {
            2 => Verdict.PlusPlus,
            1 => Verdict.Plus,
            -1 => Verdict.Minus,
            -2 => Verdict.MinusMinus,
            _ => Verdict.Normal
        };
    }

    // keep whichever deviation is larger in magnitude
    private static int Pick(int current, int candidate) => Math.Abs(candidate) > Math.Abs(current) ? candidate : current;

    public static string Symbol(Verdict v) => v switch
    {
        Verdict.Normal => "✓",
        Verdict.Plus => "+",
        Verdict.PlusPlus => "++",
        Verdict.Minus => "−",
        Verdict.MinusMinus => "−−",
        _ => ""
    };

    /// <summary>Stable name for combat.json and scripts.</summary>
    public static string Key(Verdict v) => v switch
    {
        Verdict.Normal => "normal",
        Verdict.Plus => "plus",
        Verdict.PlusPlus => "plusplus",
        Verdict.Minus => "minus",
        Verdict.MinusMinus => "minusminus",
        _ => "none"
    };

    public static string Meaning(Verdict v) => v switch
    {
        Verdict.Normal => "in the expected band",
        Verdict.Plus => "a bit too strong",
        Verdict.PlusPlus => "far too strong",
        Verdict.Minus => "a bit too weak",
        Verdict.MinusMinus => "far too weak",
        _ => "no band set"
    };

    /// <summary>Colour class used by the report: green normal, yellow slight, red far.</summary>
    public static string Css(Verdict v) => v switch
    {
        Verdict.Normal => "vOk",
        Verdict.Plus or Verdict.Minus => "vWarn",
        Verdict.PlusPlus or Verdict.MinusMinus => "vBad",
        _ => "vNone"
    };

    /// <summary>True when a script should treat the cell as a failure (far off in either direction).</summary>
    public static bool IsFlag(Verdict v) => v is Verdict.PlusPlus or Verdict.MinusMinus;
}

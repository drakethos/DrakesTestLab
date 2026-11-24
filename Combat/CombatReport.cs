using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace DrakesTestLab.Combat;

/// <summary>One matrix cell: a loadout against a foe under one variant, with its numbers and verdict.</summary>
internal sealed class CombatCell
{
    public string Loadout = "";
    public string Foe = "";          // e.g. "Troll", "Troll 2*", "Troll 5* Fire"
    public string Variant = "vanilla";
    public Summary Summary = new();
    public Band? Band;
    public Verdict Verdict;
}

/// <summary>How many of a foe a loadout can fight at once (calibration).</summary>
internal sealed class CapacityRow
{
    public string Loadout = "", Foe = "", Variant = "";
    public int Max;        // largest count still won at least At of the time (0 = lost even one)
    public int Tried;      // highest count tried
    public double At = 0.5;
}

/// <summary>Everything one combat run produced, ready to write as combat.json and combat.html.</summary>
internal sealed class CombatResult
{
    public DateTime When = DateTime.Now;
    public string Title = "Combat Lab results";
    public string Subtitle = "";
    public string Baseline = "vanilla";
    public bool LevelModLoaded;              // Creature Level and Loot Control
    public string LevelModVersion = "";
    public List<(string Name, string Version)> Mods = new();
    public List<CombatCell> Cells = new();
    public List<RunRecord> SampleRuns = new();
    public List<CapacityRow> Capacity = new();   // one fight per cell, for the timeline
}

/// <summary>Writes the self-contained combat.html. Same page style as <see cref="Automation.ResultsReport"/>.</summary>
internal static class CombatReport
{
    private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");
    private static string N(double v, string f = "0.#") => v.ToString(f, CultureInfo.InvariantCulture);

    public static string Html(CombatResult r)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append("<title>").Append(H(r.Title)).Append("</title><style>").Append(Css).Append("</style></head><body><main>");

        var cells = r.Cells;
        var fights = cells.Sum(c => c.Summary.Runs);
        var flags = cells.Count(c => CombatVerdict.IsFlag(c.Verdict));
        var clear = CountClearChanges(r);

        sb.Append("<h1>").Append(H(r.Title)).Append("</h1><div class=\"sum\">").Append(r.When.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        if (r.Subtitle.Length > 0) sb.Append(" &middot; ").Append(H(r.Subtitle));
        sb.Append(" &middot; Creature level mod: ").Append(r.LevelModLoaded ? "<span class=\"vOk tag\">✓ " + H(r.LevelModVersion) + "</span>" : "<span class=\"muted\">not loaded</span>");
        sb.Append("</div>");

        sb.Append("<div class=\"tiles\">");
        Tile(sb, "Scenarios", cells.Select(c => c.Loadout + "|" + c.Foe).Distinct().Count().ToString(CultureInfo.InvariantCulture), "");
        Tile(sb, "Fights run", fights.ToString("N0", CultureInfo.InvariantCulture), "");
        Tile(sb, "Clear changes vs " + r.Baseline, clear.ToString(CultureInfo.InvariantCulture), "");
        Tile(sb, "Balance flags", flags.ToString(CultureInfo.InvariantCulture), flags > 0 ? "vBad" : "vOk");
        sb.Append("</div>");

        Legend(sb);
        Capacity(sb, r);
        Matrix(sb, r);
        Flags(sb, r);
        Compare(sb, r);
        Samples(sb, r);

        if (r.Mods.Count > 0)
        {
            sb.Append("<details><summary><b>Setup</b><span class=\"muted\">mods and versions this run used</span></summary><ul>");
            foreach (var m in r.Mods) sb.Append("<li>").Append(H(m.Name)).Append(" <span class=\"muted\">").Append(H(m.Version)).Append("</span></li>");
            sb.Append("</ul></details>");
        }

        sb.Append("</main></body></html>");
        return sb.ToString();
    }

    private static void Tile(StringBuilder sb, string label, string value, string cls) =>
        sb.Append("<div class=\"tile ").Append(cls).Append("\"><div class=\"muted\">").Append(H(label)).Append("</div><div class=\"big\">").Append(H(value)).Append("</div></div>");

    private static void Legend(StringBuilder sb)
    {
        sb.Append("<div class=\"legend\"><span class=\"vOk tag\">✓ normal</span><span class=\"vWarn tag\">+ a bit too strong</span><span class=\"vWarn tag\">− a bit too weak</span>");
        sb.Append("<span class=\"vBad tag\">++ far too strong</span><span class=\"vBad tag\">−− far too weak</span><span class=\"vNone tag\">no band set</span></div>");
    }

    /// <summary>Loadouts down, foes across, first variant only (the compare section shows the rest).</summary>
    private static void Matrix(StringBuilder sb, CombatResult r)
    {
        var first = r.Cells.Count == 0 ? "" : r.Cells[0].Variant;
        var cells = r.Cells.Where(c => c.Variant == first).ToList();
        var loadouts = cells.Select(c => c.Loadout).Distinct().ToList();
        var foes = cells.Select(c => c.Foe).Distinct().ToList();
        sb.Append("<h2>Win rate and time to kill</h2><p class=\"muted\">Rows are gear loadouts, columns are foes. Colour and symbol show how the result sits against its tier band.</p>");
        sb.Append("<div class=\"scroll\"><table class=\"mx\"><tr><th class=\"r\">Loadout</th>");
        foreach (var f in foes) sb.Append("<th>").Append(H(f)).Append("</th>");
        sb.Append("</tr>");
        foreach (var l in loadouts)
        {
            sb.Append("<tr><th class=\"r\">").Append(H(l)).Append("</th>");
            foreach (var f in foes)
            {
                var c = cells.FirstOrDefault(x => x.Loadout == l && x.Foe == f);
                if (c == null) { sb.Append("<td></td>"); continue; }
                var s = c.Summary;
                var title = $"{s.Wins} of {s.Runs} won, 95% interval {N(s.WinLow * 100, "0")} to {N(s.WinHigh * 100, "0")}%. {CombatVerdict.Meaning(c.Verdict)}.";
                sb.Append("<td><div class=\"c ").Append(CombatVerdict.Css(c.Verdict)).Append("\" title=\"").Append(H(title)).Append("\">");
                sb.Append("<b>").Append(N(s.WinRate * 100, "0")).Append("%</b><span>").Append(s.Ttk.N > 0 ? N(s.Ttk.Mean, "0.#") + "s" : "-").Append("</span>");
                var sym = CombatVerdict.Symbol(c.Verdict);
                if (sym.Length > 0) sb.Append("<i>").Append(sym).Append("</i>");
                if (s.Runs < CombatStats.EnoughRuns) sb.Append("<em title=\"under ").Append(CombatStats.EnoughRuns).Append(" runs: thin data\">!</em>");
                sb.Append("</div></td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</table></div>");
    }

    private static void Capacity(StringBuilder sb, CombatResult r)
    {
        if (r.Capacity.Count == 0) return;
        sb.Append("<h2>How many can they fight at once?</h2><p class=\"muted\">The most foes the loadout still beats at least half the time. A trailing + means it never lost within the counts tried.</p>");
        sb.Append("<div class=\"scroll\"><table class=\"mx\"><tr><th class=\"r\">Loadout</th><th>Foe</th><th>Variant</th><th>Can handle</th><th>Tried up to</th></tr>");
        foreach (var c in r.Capacity)
        {
            var open = c.Max >= c.Tried && c.Tried > 0;
            sb.Append("<tr><th class=\"r\">").Append(H(c.Loadout)).Append("</th><td>").Append(H(c.Foe)).Append("</td><td>").Append(H(c.Variant)).Append("</td><td><div class=\"c ")
              .Append(c.Max == 0 ? "vBad" : "vOk").Append("\"><b>").Append(c.Max).Append(open ? "+" : "").Append("</b></div></td><td>").Append(c.Tried).Append("</td></tr>");
        }
        sb.Append("</table></div>");
    }

    private static void Flags(StringBuilder sb, CombatResult r)
    {
        var bad = r.Cells.Where(c => c.Verdict is Verdict.PlusPlus or Verdict.MinusMinus or Verdict.Plus or Verdict.Minus)
            .OrderByDescending(c => c.Verdict is Verdict.PlusPlus or Verdict.MinusMinus).ToList();
        if (bad.Count == 0) return;
        sb.Append("<div class=\"flags\">");
        foreach (var c in bad)
        {
            var s = c.Summary;
            var b = c.Band;
            var band = b != null && (b.WinMin >= 0 || b.WinMax >= 0)
                ? $", band {(b.WinMin >= 0 ? N(b.WinMin * 100, "0") : "0")} to {(b.WinMax >= 0 ? N(b.WinMax * 100, "0") : "100")}%"
                : "";
            sb.Append("<span class=\"tag ").Append(CombatVerdict.Css(c.Verdict)).Append("\">").Append(CombatVerdict.Symbol(c.Verdict)).Append(' ')
              .Append(H(c.Loadout)).Append(" vs ").Append(H(c.Foe)).Append(": ").Append(N(s.WinRate * 100, "0")).Append("% win").Append(H(band)).Append("</span>");
        }
        sb.Append("</div>");
    }

    /// <summary>For every loadout and foe that has more than one variant: bars with intervals and what clearly changed.</summary>
    private static void Compare(StringBuilder sb, CombatResult r)
    {
        var groups = r.Cells.GroupBy(c => c.Loadout + "\u0001" + c.Foe).Where(g => g.Count() > 1).ToList();
        if (groups.Count == 0) return;
        sb.Append("<h2>Compare variants</h2><p class=\"muted\">Bars are win rate, the dark line the 95% interval. A change counts only when the intervals do not overlap.</p>");
        foreach (var g in groups)
        {
            var list = g.ToList();
            var baseCell = PickBase(list, r);
            sb.Append("<details open><summary><b>").Append(H(baseCell.Loadout)).Append("</b> vs ").Append(H(baseCell.Foe)).Append("</summary>");
            for (var i = 0; i < list.Count; i++)
            {
                var s = list[i].Summary;
                var colour = i == 0 ? "#2a78d6" : i == 1 ? "#eb6834" : "#1baf7a";
                sb.Append("<div class=\"row\"><div>").Append(H(list[i].Variant)).Append("</div><div class=\"bar\"><i style=\"width:").Append(N(s.WinRate * 100, "0.#"))
                  .Append("%;background:").Append(colour).Append("\"></i><span class=\"ci\" style=\"left:").Append(N(s.WinLow * 100, "0.#")).Append("%;width:")
                  .Append(N((s.WinHigh - s.WinLow) * 100, "0.#")).Append("%\"></span></div><div>").Append(N(s.WinRate * 100, "0")).Append("% &middot; ")
                  .Append(s.Ttk.N > 0 ? N(s.Ttk.Mean, "0.0") + "s" : "-").Append("</div></div>");
            }
            foreach (var other in list.Where(c => c != baseCell))
            {
                var a = baseCell.Summary; var b = other.Summary;
                sb.Append("<div class=\"chg\">").Append(H(other.Variant)).Append(" vs ").Append(H(baseCell.Variant)).Append(": ");
                if (CombatStats.ClearlyDifferent(a, b))
                    sb.Append("<span class=\"tag vOk\">win rate ").Append(Signed((b.WinRate - a.WinRate) * 100, "0")).Append(" points, clear</span> ");
                else
                    sb.Append("<span class=\"tag vNone\">win rate: no clear difference at ").Append(Math.Min(a.Runs, b.Runs)).Append(" runs</span> ");
                if (a.Ttk.N > 0 && b.Ttk.N > 0)
                {
                    var d = b.Ttk.Mean - a.Ttk.Mean;
                    var pct = a.Ttk.Mean > 0 ? 100 * d / a.Ttk.Mean : 0;
                    sb.Append("<span class=\"tag ").Append(CombatStats.TtkClearlyDifferent(a, b) ? "vOk" : "vNone").Append("\">time to kill ").Append(Signed(d, "0.0")).Append("s (")
                      .Append(Signed(pct, "0")).Append("%)").Append(CombatStats.TtkClearlyDifferent(a, b) ? ", clear" : ", not clear").Append("</span>");
                }
                sb.Append("</div>");
            }
            sb.Append("</details>");
        }
    }

    private static int CountClearChanges(CombatResult r)
    {
        var n = 0;
        foreach (var g in r.Cells.GroupBy(c => c.Loadout + "\u0001" + c.Foe).Where(g => g.Count() > 1))
        {
            var list = g.ToList();
            var b = PickBase(list, r);
            foreach (var o in list.Where(c => c != b))
                if (CombatStats.ClearlyDifferent(b.Summary, o.Summary) || CombatStats.TtkClearlyDifferent(b.Summary, o.Summary))
                    n++;
        }
        return n;
    }

    /// <summary>The cell everything else is compared to: a saved baseline run if there is one, else the run's first variant.</summary>
    private static CombatCell PickBase(List<CombatCell> list, CombatResult r) =>
        list.FirstOrDefault(c => c.Variant.StartsWith("baseline:", StringComparison.Ordinal))
        ?? list.FirstOrDefault(c => c.Variant == r.Baseline) ?? list[0];

    private static string Signed(double v, string f) => (v >= 0 ? "+" : "−") + Math.Abs(v).ToString(f, CultureInfo.InvariantCulture);

    /// <summary>Health of both sides over time for one fight per cell, drawn from the recorded hits.</summary>
    private static void Samples(StringBuilder sb, CombatResult r)
    {
        if (r.SampleRuns.Count == 0) return;
        sb.Append("<h2>Sampled fights</h2>");
        foreach (var run in r.SampleRuns.Take(6))
        {
            var span = Math.Max(1f, run.Seconds);
            var gearHp = run.GearMaxHealth <= 0 ? 1f : run.GearMaxHealth;
            var foeTotal = run.Hits.Where(h => !h.VictimIsGear).Sum(h => h.Final);
            foeTotal = Math.Max(1f, foeTotal);
            var gear = new StringBuilder("0,10 ");
            var foe = new StringBuilder("0,10 ");
            float gTaken = 0, fTaken = 0;
            foreach (var h in run.Hits.OrderBy(h => h.T))
            {
                if (h.VictimIsGear) gTaken += h.Final; else fTaken += h.Final;
                var x = 600f * h.T / span;
                gear.Append(N(x, "0.#")).Append(',').Append(N(10 + 70 * Math.Min(1f, gTaken / gearHp), "0.#")).Append(' ');
                foe.Append(N(x, "0.#")).Append(',').Append(N(10 + 70 * Math.Min(1f, fTaken / foeTotal), "0.#")).Append(' ');
            }
            sb.Append("<details><summary><b>").Append(H(run.Scenario)).Append("</b><span class=\"muted\">").Append(H(run.Variant)).Append(" &middot; ").Append(H(run.Outcome))
              .Append(" &middot; ").Append(N(run.Seconds, "0.0")).Append("s</span></summary>");
            sb.Append("<svg viewBox=\"0 0 640 100\" width=\"100%\" role=\"img\" aria-label=\"Health of both sides over the fight\"><g transform=\"translate(20,5)\">");
            sb.Append("<line x1=\"0\" y1=\"82\" x2=\"600\" y2=\"82\" class=\"axis\"/>");
            sb.Append("<polyline fill=\"none\" stroke=\"#2a78d6\" stroke-width=\"2\" points=\"").Append(gear).Append("\"/>");
            sb.Append("<polyline fill=\"none\" stroke=\"#eb6834\" stroke-width=\"2\" points=\"").Append(foe).Append("\"/></g></svg>");
            sb.Append("<p class=\"muted\">Blue: gear side damage taken (share of max health). Orange: foes' damage taken (share of what they lost in this fight).</p></details>");
        }
    }

    private const string Css =
        ":root{--bg:#f6f4ef;--fg:#1f1d1a;--card:#fff;--muted:#6b665d;--line:#e2ddd2;--ok:#2f7d46;--okbg:#e3f2e7;--warn:#8a6d1f;--warnbg:#fbf1d3;--bad:#b3261e;--badbg:#fbe3e1;--none:#6b665d;--nonebg:#ecebe6;--surf:#efece4}" +
        "@media(prefers-color-scheme:dark){:root{--bg:#191714;--fg:#ece7dd;--card:#24211d;--muted:#a59e91;--line:#3a352e;--ok:#8fe0a6;--okbg:#1f3a28;--warn:#f0cf75;--warnbg:#3d3416;--bad:#ff9a92;--badbg:#4a2421;--none:#a59e91;--nonebg:#2d2a25;--surf:#2d2a25}}" +
        "body{margin:0;padding:16px;background:var(--bg);color:var(--fg);font:15px/1.45 system-ui,Segoe UI,sans-serif}" +
        "main{max-width:1100px;margin:0 auto}h1{font-size:22px;margin:0 0 4px}h2{font-size:17px;margin:26px 0 4px}.sum,.muted{color:var(--muted)}.sum{margin-bottom:12px}" +
        ".tiles{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:10px;margin:12px 0}.tile{background:var(--surf);border-radius:8px;padding:12px}.big{font-size:24px;font-weight:600}" +
        ".tag{display:inline-block;font-size:12px;padding:2px 8px;border-radius:6px;margin:0 6px 6px 0}.legend,.flags{margin:8px 0}" +
        ".vOk{background:var(--okbg);color:var(--ok)}.vWarn{background:var(--warnbg);color:var(--warn)}.vBad{background:var(--badbg);color:var(--bad)}.vNone{background:var(--nonebg);color:var(--none)}" +
        ".scroll{overflow-x:auto}.mx{border-collapse:separate;border-spacing:3px;min-width:560px;width:100%;font-size:13px}.mx th{font-weight:600;color:var(--muted);padding:4px 2px;text-align:center}.mx th.r{text-align:left}" +
        ".c{position:relative;border-radius:6px;padding:7px 2px;text-align:center}.c b{display:block;font-size:15px}.c span{font-size:11px;opacity:.85}" +
        ".c i{position:absolute;top:2px;right:5px;font-style:normal;font-weight:700;font-size:12px}.c em{position:absolute;top:2px;left:5px;font-style:normal;font-size:11px}" +
        "details{background:var(--card);border:1px solid var(--line);border-radius:8px;margin:8px 0;padding:8px 12px}summary{cursor:pointer;display:flex;gap:10px;align-items:baseline;flex-wrap:wrap}" +
        ".row{display:grid;grid-template-columns:150px 1fr 120px;gap:10px;align-items:center;margin:8px 0;font-size:13px}.bar{height:14px;border-radius:4px;background:var(--surf);position:relative;overflow:hidden}" +
        ".bar i{position:absolute;left:0;top:0;bottom:0}.ci{position:absolute;top:3px;height:8px;background:var(--fg);opacity:.55;border-radius:2px}.chg{margin:6px 0 2px;font-size:13px}.axis{stroke:var(--line)}";
}

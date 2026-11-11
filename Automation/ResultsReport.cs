using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace DrakesTestLab.Automation;

/// <summary>latest.html: a self-contained page with every test, its checks, the step log and the screenshots. Open it in any browser.</summary>
internal static class ResultsReport
{
    private static string H(string s) => WebUtility.HtmlEncode(s ?? "");

    public static string Html(List<TestResult> run, int passed, int failed, int skipped)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append("<title>DrakesTestLab results</title><style>");
        sb.Append(":root{--bg:#f6f4ef;--fg:#1f1d1a;--card:#fff;--muted:#6b665d;--line:#e2ddd2;--pass:#2f7d46;--fail:#b3261e;--skip:#8a6d1f}");
        sb.Append("@media(prefers-color-scheme:dark){:root{--bg:#191714;--fg:#ece7dd;--card:#24211d;--muted:#a59e91;--line:#3a352e;--pass:#6fcf8b;--fail:#ff8a80;--skip:#e0c068}}");
        sb.Append("body{margin:0;padding:16px;background:var(--bg);color:var(--fg);font:15px/1.45 system-ui,Segoe UI,sans-serif}");
        sb.Append("main{max-width:1100px;margin:0 auto}h1{font-size:22px;margin:0 0 4px}.sum{color:var(--muted);margin-bottom:16px}");
        sb.Append("details{background:var(--card);border:1px solid var(--line);border-radius:8px;margin:8px 0;padding:8px 12px}");
        sb.Append("summary{cursor:pointer;display:flex;gap:10px;align-items:baseline;flex-wrap:wrap}summary b{min-width:52px}");
        sb.Append(".Passed{color:var(--pass)}.Failed{color:var(--fail)}.Skipped{color:var(--skip)}.muted{color:var(--muted)}");
        sb.Append("ul{margin:6px 0;padding-left:20px}li{margin:2px 0;overflow-wrap:anywhere}pre{white-space:pre-wrap;overflow-wrap:anywhere;font-size:12px;color:var(--muted)}");
        sb.Append(".shots{display:flex;gap:8px;flex-wrap:wrap}.shots img{max-width:100%;width:340px;border-radius:6px;border:1px solid var(--line)}");
        sb.Append(".filters{margin:8px 0}.filters label{margin-right:12px}");
        sb.Append("</style></head><body><main>");
        sb.Append($"<h1>DrakesTestLab results</h1><div class=\"sum\">{DateTime.Now:yyyy-MM-dd HH:mm} &middot; ");
        sb.Append($"<span class=\"Passed\">{passed} passed</span>, <span class=\"Failed\">{failed} failed</span>, <span class=\"Skipped\">{skipped} skipped</span></div>");
        var total = TestRunner.RunStartedAt == default ? 0.0 : (DateTime.Now - TestRunner.RunStartedAt).TotalSeconds;
        sb.Append($"<div class=\"sum\">Started {TestRunner.RunStartedAt:HH:mm:ss} &middot; {total:0.0}s so far &middot; game start to run start {TestRunner.RunStartRealtime:0.0}s &middot; world ready after {TestRunner.RunWorldReadySeconds:0.0}s</div>");
        sb.Append("<div class=\"filters\"><label><input type=\"checkbox\" id=\"onlyFailed\"> Only failed</label></div>");

        // where the time went
        var slowTests = new List<TestResult>(run);
        slowTests.Sort((a, b) => b.Seconds.CompareTo(a.Seconds));
        var slowSteps = new List<(string Test, string Label, float Seconds)>();
        foreach (var r in run)
            foreach (var s in r.StepTimes)
                slowSteps.Add((r.Suite + " / " + r.Test, s.Label, s.Seconds));
        slowSteps.Sort((a, b) => b.Seconds.CompareTo(a.Seconds));
        var mods = new List<Util.ModLoadTimer.Entry>(Util.ModLoadTimer.All);
        mods.Sort((a, b) => b.Seconds.CompareTo(a.Seconds));
        if (slowTests.Count > 0)
        {
            sb.Append("<details><summary><b>Timing</b><span class=\"muted\">slowest tests, steps and mod load times</span></summary>");
            sb.Append("<p><strong>Slowest tests</strong></p><ul>");
            for (var i = 0; i < Math.Min(8, slowTests.Count); i++)
                sb.Append($"<li>{slowTests[i].Seconds:0.0}s &mdash; {H(slowTests[i].Suite)} / {H(slowTests[i].Test)}</li>");
            sb.Append("</ul>");
            if (slowSteps.Count > 0)
            {
                sb.Append("<p><strong>Slowest steps</strong></p><ul>");
                for (var i = 0; i < Math.Min(10, slowSteps.Count); i++)
                    sb.Append($"<li>{slowSteps[i].Seconds:0.0}s &mdash; {H(slowSteps[i].Label)} <span class=\"muted\">({H(slowSteps[i].Test)})</span></li>");
                sb.Append("</ul>");
            }

            // smoothness: lowest 1%-low fps per test, and the steps that had the single worst frames
            var smooth = run.FindAll(r => r.Frames.Frames > 0);
            smooth.Sort((a, b) => a.Frames.LowFps.CompareTo(b.Frames.LowFps));
            if (smooth.Count > 0)
            {
                sb.Append("<p><strong>Roughest tests</strong> <span class=\"muted\">(lowest 1% low fps; a hitch is a frame over " + Util.FrameProfiler.HitchMs.ToString("0") + " ms)</span></p><ul>");
                for (var i = 0; i < Math.Min(8, smooth.Count); i++)
                    sb.Append($"<li>{H(smooth[i].Frames.Summary())} &mdash; {H(smooth[i].Suite)} / {H(smooth[i].Test)}</li>");
                sb.Append("</ul>");
            }

            var spikes = new List<(string Test, string Label, float Ms)>();
            foreach (var r in run)
                for (var k = 0; k < r.StepFrames.Count && k < r.StepTimes.Count; k++)
                    spikes.Add((r.Suite + " / " + r.Test, r.StepTimes[k].Label, r.StepFrames[k].MaxMs));
            spikes.Sort((a, b) => b.Ms.CompareTo(a.Ms));
            if (spikes.Count > 0)
            {
                sb.Append("<p><strong>Worst single frames</strong> <span class=\"muted\">(which step it happened in)</span></p><ul>");
                for (var i = 0; i < Math.Min(10, spikes.Count); i++)
                    sb.Append($"<li>{spikes[i].Ms:0} ms &mdash; {H(spikes[i].Label)} <span class=\"muted\">({H(spikes[i].Test)})</span></li>");
                sb.Append("</ul>");
            }

            // memory: biggest heap growth (leak suspects), most allocation churn, and which object types a test left behind
            var withMem = run.FindAll(r => r.Mem.Valid);
            if (withMem.Count > 0)
            {
                withMem.Sort((a, b) => b.Mem.MonoGrowthMb.CompareTo(a.Mem.MonoGrowthMb));
                sb.Append("<p><strong>Heap growth</strong> <span class=\"muted\">(managed memory in use, end minus start of the test; a GC in between can hide growth)</span></p><ul>");
                for (var i = 0; i < Math.Min(8, withMem.Count); i++)
                    sb.Append($"<li>{H(withMem[i].Mem.Summary())} &mdash; {H(withMem[i].Suite)} / {H(withMem[i].Test)}</li>");
                sb.Append("</ul>");

                withMem.Sort((a, b) => b.Mem.ChurnMb.CompareTo(a.Mem.ChurnMb));
                sb.Append("<p><strong>Most allocation</strong> <span class=\"muted\">(churn: how much managed memory the test allocated, and how many GCs it caused)</span></p><ul>");
                for (var i = 0; i < Math.Min(5, withMem.Count); i++)
                    sb.Append($"<li>~{withMem[i].Mem.ChurnMb:0} MB, GC {withMem[i].Mem.Gen0}/{withMem[i].Mem.Gen1}/{withMem[i].Mem.Gen2} &mdash; {H(withMem[i].Suite)} / {H(withMem[i].Test)}</li>");
                sb.Append("</ul>");
            }

            var growers = new List<(TestResult R, int Delta)>();
            foreach (var r in run)
                if (r.CensusBefore != null && r.CensusAfter != null)
                    growers.Add((r, r.CensusAfter.Total - r.CensusBefore.Total));
            growers.Sort((a, b) => b.Delta.CompareTo(a.Delta));
            if (growers.Count > 0 && growers[0].Delta > 0)
            {
                sb.Append("<p><strong>Objects left behind</strong> <span class=\"muted\">(Unity objects after minus before the test, with the types that grew most)</span></p><ul>");
                for (var i = 0; i < Math.Min(8, growers.Count) && growers[i].Delta > 0; i++)
                {
                    var g = growers[i].R.CensusAfter!.GrowthSince(growers[i].R.CensusBefore!, 3);
                    var types = string.Join(", ", g.ConvertAll(x => $"{x.Type} {x.Delta:+0;-0}"));
                    sb.Append($"<li>+{growers[i].Delta} ({H(types)}) &mdash; {H(growers[i].R.Suite)} / {H(growers[i].R.Test)}</li>");
                }

                sb.Append("</ul>");
            }

            if (mods.Count > 0)
            {
                sb.Append($"<p><strong>Mod load times</strong> <span class=\"muted\">({(Util.ModLoadTimer.FromPatcher ? $"every mod; the first started {Util.ModLoadTimer.BeforeSeconds:0.0}s after the game process did" : $"only mods loaded after DrakesTestLab; {Util.ModLoadTimer.BeforeSeconds:0.0}s passed before them. Install DrakesTestLab.Preload for all")})</span></p><ul>");
                foreach (var m in mods)
                    sb.Append($"<li>{m.Seconds:0.00}s &mdash; {H(m.Name)}</li>");
                sb.Append("</ul>");
            }

            sb.Append("</details>");
        }

        foreach (var r in run)
        {
            var open = r.Status == TestStatus.Failed ? " open" : "";
            sb.Append($"<details class=\"t t-{r.Status}\"{open}><summary><b class=\"{r.Status}\">{H(r.Status.ToString().ToUpperInvariant())}</b>");
            sb.Append($"<span>{H(r.Suite)} / <strong>{H(r.Test)}</strong></span><span class=\"muted\">{r.Seconds:0.0}s{(r.StartedAt == default ? "" : " &middot; " + r.StartedAt.ToString("HH:mm:ss"))}{(r.Frames.Frames > 0 ? " &middot; " + H(r.Frames.Summary()) : "")}{(r.Mem.Valid ? " &middot; " + H(r.Mem.Summary()) : "")}</span>");
            if (r.Reason.Length > 0)
                sb.Append($"<span class=\"muted\">{H(r.Reason)}</span>");
            sb.Append("</summary>");

            if (r.Asserts.Count > 0)
            {
                sb.Append("<ul>");
                foreach (var a in r.Asserts)
                    sb.Append($"<li><span class=\"{(a.Passed ? "Passed" : "Failed")}\">{(a.Passed ? "&#10003;" : "&#10007;")}</span> {H(a.Description)} <span class=\"muted\">&rarr; {H(a.Detail)}</span></li>");
                sb.Append("</ul>");
            }

            if (r.Screenshots.Count > 0)
            {
                sb.Append("<div class=\"shots\">");
                foreach (var s in r.Screenshots)
                    sb.Append($"<a href=\"{H(s)}\"><img loading=\"lazy\" src=\"{H(s)}\" alt=\"{H(s)}\"></a>");
                sb.Append("</div>");
            }

            if (r.StepLog.Count > 0)
                sb.Append("<pre>").Append(H(string.Join("\n", r.StepLog))).Append("</pre>");
            sb.Append("</details>");
        }

        sb.Append("<script>document.getElementById('onlyFailed').onchange=function(e){document.querySelectorAll('details.t').forEach(function(d){d.style.display=e.target.checked&&!d.classList.contains('t-Failed')?'none':''})}</script>");
        sb.Append("</main></body></html>");
        return sb.ToString();
    }
}

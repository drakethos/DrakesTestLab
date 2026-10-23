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
        sb.Append("<div class=\"filters\"><label><input type=\"checkbox\" id=\"onlyFailed\"> Only failed</label></div>");

        foreach (var r in run)
        {
            var open = r.Status == TestStatus.Failed ? " open" : "";
            sb.Append($"<details class=\"t t-{r.Status}\"{open}><summary><b class=\"{r.Status}\">{H(r.Status.ToString().ToUpperInvariant())}</b>");
            sb.Append($"<span>{H(r.Suite)} / <strong>{H(r.Test)}</strong></span><span class=\"muted\">{r.Seconds:0.0}s</span>");
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

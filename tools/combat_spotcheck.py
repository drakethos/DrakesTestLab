#!/usr/bin/env python3
"""Compare spot-check fights with the model's predictions.

  combat_spotcheck.py spot-predictions.json results/latest.json
For each spot combo: predicted vs fought win rate (with the fought 95% range) and kill time (wins only).
A prediction "holds" when it falls inside the fought win rate's 95% range.
"""
import json, math, sys


def wilson(k, n, z=1.96):
    if n == 0: return (0, 1)
    p = k / n; d = 1 + z * z / n
    c = (p + z * z / (2 * n)) / d; h = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / d
    return max(0, c - h), min(1, c + h)


pred = {p["label"]: p for p in json.load(open(sys.argv[1]))}
cells = json.load(open(sys.argv[2]))["cells"]
held = 0; total = 0; kerr = []
for c in cells:
    p = pred.get(c["loadout"])
    if not p: continue
    n = c["wins"] + c.get("losses", c["runs"] - c["wins"] - c.get("timeouts", 0))
    if n * 2 < c["runs"]:
        print(f"  {c['loadout']:52} skipped: {c['runs'] - n} of {c['runs']} fights stalled or timed out"); continue
    lo, hi = wilson(c["wins"], n)
    ok = lo - 0.02 <= p["predictedWin"] <= hi + 0.02
    held += ok; total += 1
    kt = c.get("ttkMean", 0)
    if kt and p["predictedTtk"]: kerr.append(p["predictedTtk"] / kt - 1)
    print(f"  {c['loadout']:52} win: model {p['predictedWin']:4.0%}  fought {c['wins']}/{n} ({lo:.0%}-{hi:.0%})"
          f"  kill: model {p['predictedTtk']:5.1f}s fought {kt:5.1f}s   {'holds' if ok else 'MISSES'}")
if total:
    print(f"\n{held} of {total} win predictions hold"
          + (f"; kill time: model is {sum(kerr)/len(kerr):+.0%} on average (|error| {sum(abs(e) for e in kerr)/len(kerr):.0%})" if kerr else ""))

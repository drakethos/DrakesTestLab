#!/usr/bin/env python3
"""Combat Lab validation: does a speed-up or a setup change (faster clock, lanes, ...) change the results?

Reads the per-run lines CombatRunner writes to LogOutput.log. Scenario names look like <fight>-v-<arm>; for each fight
the arm named "ctl" is the control and every other arm is compared with it:
  win rate         Fisher exact test (two-sided)
  time to kill     Mann-Whitney U (wins only), plus difference of means with a 95% interval
  time to die      Mann-Whitney U (losses only)
  damage taken     Mann-Whitney U (all runs)
p < 0.05 marks a difference. Many comparisons at once: a lone p near 0.05 can be chance.

Measurement checks (they must pass before a comparison means anything):
  recorder   on a win, damage dealt should equal the foes' max health (final damage = health actually lost)
  clock      game seconds / real seconds should be the clock asked for
  frame      game time per drawn frame (clock / fps); flagged above 30 ms (fast foes' timing coarsens)
  setup      max health (from the meals) must be the same in every arm and lane
  lanes      lane 0 (the local player) and lanes 1+ (bot players) must not differ

Usage: combat_validate.py LogOutput.log [more logs]
"""
import math, re, sys, statistics as st
from collections import defaultdict

MAIN = re.compile(r"Combat: (\S+) run (\d+): (\w+) in ([\d.]+)s, dealt (\d+), took (\d+), wall ([\d.]+)s, (\d+) fps(.*)")
LANE = re.compile(r"Combat: (\S+) run (\d+) lane (\d+): (\w+) in ([\d.]+)s, dealt (\d+), took (\d+), foe hp (\d+), max hp (\d+)")
CLOCK = re.compile(r"clock ([\d.]+)x of ([\d.]+)x")
FOEHP = re.compile(r"foe hp (\d+), max hp (\d+)")
FWALL = re.compile(r"fight wall ([\d.]+)s")


def fisher(a, b, c, d):
    n = a + b + c + d; r1 = a + b; c1 = a + c
    def p(x): return math.comb(r1, x) * math.comb(n - r1, c1 - x) / math.comb(n, c1)
    p0 = p(a)
    return min(1.0, sum(p(x) for x in range(max(0, c1 - (n - r1)), min(r1, c1) + 1) if p(x) <= p0 * (1 + 1e-9)))


def mannwhitney(x, y):
    if len(x) < 3 or len(y) < 3: return float("nan")
    allv = sorted((v, i) for i, v in enumerate(x + y))
    ranks = [0.0] * len(allv); i = 0; ties = 0.0
    while i < len(allv):
        j = i
        while j + 1 < len(allv) and allv[j + 1][0] == allv[i][0]: j += 1
        r = (i + j) / 2 + 1
        for k in range(i, j + 1): ranks[allv[k][1]] = r
        t = j - i + 1; ties += t ** 3 - t; i = j + 1
    n1, n2 = len(x), len(y); n = n1 + n2
    u = sum(ranks[:n1]) - n1 * (n1 + 1) / 2
    sd = math.sqrt(n1 * n2 / 12 * ((n + 1) - ties / (n * (n - 1))))
    if sd == 0: return 1.0
    z = (abs(u - n1 * n2 / 2) - 0.5) / sd
    return math.erfc(z / math.sqrt(2))


def diff_ci(x, y):
    if len(x) < 2 or len(y) < 2: return (float("nan"),) * 3
    d = st.mean(y) - st.mean(x)
    se = math.sqrt(st.variance(x) / len(x) + st.variance(y) / len(y))
    return d, d - 1.96 * se, d + 1.96 * se


def wilson(k, n, z=1.96):
    if n == 0: return (0, 1)
    p = k / n; dd = 1 + z * z / n
    c = (p + z * z / (2 * n)) / dd; h = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / dd
    return max(0, c - h), min(1, c + h)


runs = defaultdict(list); wall = defaultdict(float); clock = defaultdict(list); frame = defaultdict(list)
for path in sys.argv[1:]:
    for line in open(path, errors="replace"):
        m = MAIN.search(line)
        if m:
            scn, _, out, g, dealt, took, w, fps, rest = m.groups()
            fh = FOEHP.search(rest); ck = CLOCK.search(rest); fw = FWALL.search(rest)
            runs[scn].append(dict(lane=0, out=out, t=float(g), dealt=int(dealt), took=int(took),
                                  foehp=int(fh.group(1)) if fh else 0, maxhp=int(fh.group(2)) if fh else 0))
            if ck:
                clock[scn].append((float(ck.group(1)), float(ck.group(2))))
                if int(fps) > 0: frame[scn].append(1000 * float(ck.group(1)) / int(fps))
            wall[scn] += float(fw.group(1)) if fw else float(w)
            continue
        m = LANE.search(line)
        if m:
            scn, _, ln, out, g, dealt, took, foehp, maxhp = m.groups()
            runs[scn].append(dict(lane=int(ln), out=out, t=float(g), dealt=int(dealt), took=int(took), foehp=int(foehp), maxhp=int(maxhp)))

ok = True
print("== Measurement checks")
for arm, rs in sorted(runs.items()):
    wins = [r for r in rs if r["out"] == "win" and r["foehp"] > 0]
    bad = [r for r in wins if abs(r["dealt"] - r["foehp"]) > 0.05 * r["foehp"]]
    hps = sorted({r["maxhp"] for r in rs})
    ck = clock.get(arm, [])
    ratio = st.mean(c[0] for c in ck) if ck else float("nan"); want = ck[0][1] if ck else float("nan")
    fm = st.mean(frame[arm]) if frame.get(arm) else float("nan")
    flags = []
    if bad: flags.append(f"RECORDER: {len(bad)}/{len(wins)} wins dealt != foe hp")
    if len(hps) > 1 and max(hps) - min(hps) > 1: flags.append(f"SETUP: max hp differs {hps}")
    if ck and ratio < 0.9 * want: flags.append(f"CLOCK: ran {ratio:.2f}x, asked {want:.0f}x")
    if fm == fm and fm > 30: flags.append(f"FRAME: {fm:.0f} ms of game per drawn frame")
    ok &= not flags
    print(f"  {arm:52} n={len(rs):3}  recorder {len(wins)-len(bad)}/{len(wins)}  max hp {hps}  clock {ratio:.2f}x/{want:.0f}x  "
          f"frame {fm:.0f} ms  real {wall[arm]/max(1,len(rs)):.1f} s/run  {'OK' if not flags else '; '.join(flags)}")

print("\n== Arms against the control (p < 0.05 = a real difference)")
for fight in sorted({a.split("-v-")[0] for a in runs if "-v-" in a}):
    ctl = runs.get(fight + "-v-ctl", [])
    if not ctl: continue
    print(f"\n  {fight}")
    for arm in sorted(a for a in runs if a.startswith(fight + "-v-")):
        rs = runs[arm]
        k = sum(r["out"] == "win" for r in rs); n = len(rs); kc = sum(r["out"] == "win" for r in ctl); nc = len(ctl)
        lo, hi = wilson(k, n)
        kill = [r["t"] for r in rs if r["out"] == "win"]; killc = [r["t"] for r in ctl if r["out"] == "win"]
        die = [r["t"] for r in rs if r["out"] == "loss"]; diec = [r["t"] for r in ctl if r["out"] == "loss"]
        took = [r["took"] for r in rs]; tookc = [r["took"] for r in ctl]
        line = (f"    {arm.split('-v-')[1]:22} win {k:2}/{n:<3} {100*k/n:3.0f}% ({100*lo:.0f}-{100*hi:.0f})"
                f"  kill {st.mean(kill) if kill else 0:5.1f}s  die {st.mean(die) if die else 0:5.1f}s  took {st.mean(took):4.0f}")
        if arm.endswith("-v-ctl"):
            print(line + "   (control)"); continue
        pw = fisher(k, n - k, kc, nc - kc); pk = mannwhitney(killc, kill); pd = mannwhitney(diec, die); pt = mannwhitney(tookc, took)
        d, dlo, dhi = diff_ci(killc, kill)
        diffs = [nm for nm, p in (("win", pw), ("kill", pk), ("die", pd), ("took", pt)) if p == p and p < 0.05]
        ok &= not diffs
        print(line + f"   p win {pw:.2f} kill {pk:.2f} die {pd:.2f} took {pt:.2f}   kill diff {d:+.1f}s ({dlo:+.1f}..{dhi:+.1f})"
              + ("   DIFFERENT: " + ", ".join(diffs) if diffs else "   same"))
        if len({r["lane"] for r in rs}) > 1:
            l0 = [r for r in rs if r["lane"] == 0]; lx = [r for r in rs if r["lane"] > 0]
            k0 = sum(r["out"] == "win" for r in l0); kx = sum(r["out"] == "win" for r in lx)
            pl = fisher(k0, len(l0) - k0, kx, len(lx) - kx)
            pk2 = mannwhitney([r["t"] for r in l0 if r["out"] == "win"], [r["t"] for r in lx if r["out"] == "win"])
            print(f"                           lane check: you {k0}/{len(l0)} vs bot lanes {kx}/{len(lx)}  p {pl:.2f}, kill p {pk2:.2f}"
                  + ("   DIFFERENT" if pl < 0.05 or (pk2 == pk2 and pk2 < 0.05) else "   same"))
print("\nVERDICT:", "all checks pass" if ok else "something differs; see above")

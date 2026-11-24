#!/usr/bin/env python3
"""Runs DrakesTestLab suites unattended and returns the result as the exit code (Linux port of Run-TestLabSuites.ps1).

 1. Starts Valheim through Steam (`steam -applaunch 892970 ...`) with the mod profile via the doorstop arguments
    the Valheim launch option `./start_game_bepinex.sh %command%` understands, plus DrakesTestLab's switches:
    -testlab-run <suites> -testlab-quit [-testlab-delay <s>]. No config file is changed (except launchConfig
    settings from a suite.json, which are restored afterwards).
 2. DrakesTestLab boots into the test world, runs the suites, writes results and quits.
 3. Prints the summary. Exit: 0 all passed, 1 a test failed, 2 could not start, 3 timed out / no results.

Waits its turn in the shared queue (scripts/valheim_queue.py) unless --no-queue or already inside a turn.

  run_testlab_suites.py --suites OmniConfig-Nodes
  run_testlab_suites.py --suites "LockSmith/L-02,LockSmith-Net/N-04"
  run_testlab_suites.py --stay            # leave the game open afterwards
"""
import argparse
import glob
import json
import os
import re
import shlex
import shutil
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPTS = os.path.normpath(os.path.join(HERE, "..", "..", "scripts"))
sys.path.insert(0, SCRIPTS)
import valheim_queue  # noqa: E402

GALE_PROFILES = os.path.expanduser("~/.local/share/com.kesomannen.gale/valheim/profiles")


def set_ini_value(path, section, key, value):
    lines = open(path, encoding="utf-8").read().splitlines() if os.path.exists(path) else []
    header = f"[{section}]"
    start = next((i for i, l in enumerate(lines) if l.strip() == header), -1)
    if start < 0:
        lines += ["", header, f"{key} = {value}"]
    else:
        for i in range(start + 1, len(lines)):
            t = lines[i].strip()
            if t.startswith("["):
                lines.insert(start + 1, f"{key} = {value}")
                break
            if re.match(rf"^{re.escape(key)}\s*=", t):
                lines[i] = f"{key} = {value}"
                break
        else:
            lines.insert(start + 1, f"{key} = {value}")
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")


def read_json(path):
    try:
        with open(path, encoding="utf-8-sig") as f:
            return json.load(f)
    except (OSError, ValueError):
        return None


def print_results(r, started, latest):
    print(f"\nResults: {r.get('passed')} passed, {r.get('failed')} failed, {r.get('skipped')} skipped")
    for t in r.get("tests", []):
        mark = {"Passed": "PASS", "Failed": "FAIL", "Skipped": "SKIP"}.get(t.get("status"), t.get("status"))
        secs = f" ({float(t['seconds']):.1f}s)" if t.get("seconds") is not None else ""
        line = f"  [{mark}] {t.get('suite')} / {t.get('test')}{secs}"
        if t.get("reason"):
            line += f" - {t['reason']}"
        print(line)
    print(f"\nTime: {time.time() - started:.0f}s total (launch to results)")
    if r.get("totalSeconds") is not None:
        print(f"  game start to run start {r.get('gameStartToRunSeconds')}s, world ready after "
              f"{r.get('worldReadySeconds')}s, tests {r.get('totalSeconds')}s")
        by_suite = {}
        for t in r.get("tests", []):
            by_suite[t.get("suite")] = by_suite.get(t.get("suite"), 0) + float(t.get("seconds") or 0)
        for s, secs in sorted(by_suite.items(), key=lambda kv: -kv[1]):
            print(f"  suite {s}: {secs:.1f}s")
    print(f"\nDetails: {latest}")


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--suites", default="all")
    p.add_argument("--combat", default="", help="combat testing (balance runs): scenario names from combat/scenarios, or all")
    p.add_argument("--runs", type=int, default=0, help="override every combat scenario's run count")
    p.add_argument("--combat-dump", action="store_true", help="combat: write the game's combat numbers to combat/gamedata.json first")
    p.add_argument("--combat-check", action="store_true", help="combat: only check that every item, meal and creature the scenarios name exists (no fights)")
    p.add_argument("--profile", default="drakeTest")
    p.add_argument("--profile-path", default="")
    p.add_argument("--steam", default=shutil.which("steam") or "/usr/games/steam")
    p.add_argument("--app-id", type=int, default=892970)
    p.add_argument("--timeout-sec", type=int, default=900)
    p.add_argument("--step-delay", type=float, default=-1)
    p.add_argument("--screenshots", default="", choices=["", "Off", "Failures", "Always"])
    p.add_argument("--stay", action="store_true")
    p.add_argument("--attended", action="store_true")
    p.add_argument("--no-kill-on-timeout", action="store_true")
    p.add_argument("--stall-sec", type=int, default=60,
                   help="a launch whose BepInEx log has not moved for this long (and has no results) is frozen: stop it and relaunch")
    p.add_argument("--gfx", default="auto", choices=["auto", "vulkan", "glcore"],
                   help="graphics API: auto = Vulkan first, OpenGL for the relaunches after a start-up freeze; vulkan; glcore")
    p.add_argument("--retries", type=int, default=4, help="relaunches after a frozen or crashed start (0 = never)")
    p.add_argument("--hot", action="store_true")
    p.add_argument("--no-queue", action="store_true")
    p.add_argument("--priority", action="store_true", help="only when the user says skip the line")
    p.add_argument("--queue-name", default="")
    a = p.parse_args(argv)

    if not a.no_queue and not os.environ.get("VALHEIM_QUEUE_HELD"):
        who = a.queue_name or f"TestLab run {a.suites}"
        passthru = [x for x in (argv if argv is not None else sys.argv[1:]) if x not in ("--priority",)]
        # drop the queue-only options (and --queue-name's value) when re-invoking inside the turn
        cleaned, skip = [], False
        for x in passthru:
            if skip:
                skip = False
            elif x == "--queue-name":
                skip = True
            elif not x.startswith("--queue-name="):
                cleaned.append(x)
        cmd = " ".join(shlex.quote(x) for x in [sys.executable, os.path.abspath(__file__), *cleaned])
        return valheim_queue.Queue().run(cmd, who, a.priority)

    profile = a.profile_path or os.path.join(GALE_PROFILES, a.profile)
    bepinex = os.path.join(profile, "BepInEx")
    preloader = os.path.join(bepinex, "core", "BepInEx.Preloader.dll")
    latest = os.path.join(bepinex, "config", "DrakesTestLab", "results", "latest.json")

    if a.combat:
        latest = os.path.join(bepinex, "config", "DrakesTestLab", "combat", "results", "latest.json")

    if not os.path.exists(preloader):
        print(f"Profile not found (no BepInEx preloader): {preloader}")
        return 2
    if not a.steam or not os.path.exists(a.steam):
        print(f"Steam not found: {a.steam}")
        return 2
    if valheim_queue.game_pids():
        print("Valheim is already running. Close it first (this script only stops a game it started itself).")
        return 2

    launch = ["-applaunch", str(a.app_id), "--doorstop-enabled", "true", "--doorstop-target-assembly", preloader,
              "-testlab-run", a.suites]
    if a.combat:
        launch = ["-applaunch", str(a.app_id), "--doorstop-enabled", "true", "--doorstop-target-assembly", preloader,
                  "-testlab-combat", a.combat]
        if a.runs > 0:
            launch += ["-testlab-combat-runs", str(a.runs)]
        if a.combat_check:
            launch += ["-testlab-combat-check"]
        if a.combat_dump:
            launch += ["-testlab-combat-dump"]
    stay = a.stay
    if a.attended:
        stay = True
        launch.append("-testlab-attended")
    if a.hot:
        stay = True
        launch.append("-testlab-hot")
    launch.append("-testlab-stay" if stay else "-testlab-quit")
    if a.screenshots:
        launch += ["-testlab-screenshots", a.screenshots]
    if a.step_delay >= 0:
        launch += ["-testlab-delay", repr(a.step_delay)]

    # settings some mods only read at game start ("launchConfig" in a suite.json): written before launch, restored after
    restore = {}
    wanted = None if a.suites == "all" else [s.strip().split("/")[0] for s in a.suites.split(",")]
    for sfile in glob.glob(os.path.join(bepinex, "config", "DrakesTestLab", "suites", "*", "suite.json")):
        name = os.path.basename(os.path.dirname(sfile))
        if name.startswith("_") or (wanted and name not in wanted):
            continue
        suite = read_json(sfile) or {}
        for c in suite.get("launchConfig") or []:
            if not c or not c.get("guid"):
                continue
            cfg = os.path.join(bepinex, "config", c["guid"] + ".cfg")
            if cfg not in restore:
                restore[cfg] = open(cfg, encoding="utf-8").read() if os.path.exists(cfg) else None
            set_ini_value(cfg, c["section"], c["key"], str(c["value"]))
            print(f"Launch setting for {name}: [{c['section']}] {c['key']} = {c['value']}")

    def finish(code):
        for path, text in restore.items():
            if text is None:
                if os.path.exists(path):
                    os.remove(path)
            else:
                with open(path, "w", encoding="utf-8") as f:
                    f.write(text)
        return code

    log_file = os.path.join(bepinex, "LogOutput.log")
    if a.combat:
        a.stall_sec = max(a.stall_sec, 150)   # fights can be quiet for a minute; the game logs a heartbeat every 15 s anyway

    def startup_stage():
        """Where the game's log stopped, so a freeze or crash reads as what it is (a Unity start-up fault, not a test failure)."""
        try:
            with open(log_file, "r", encoding="utf-8", errors="replace") as f:
                lines = [ln.strip() for ln in f.read().splitlines() if ln.strip()]
        except OSError:
            return "no game log was written"
        last = lines[-1] if lines else ""
        if "TestLab" in "".join(lines[-40:]) and "Steam initialized" in "".join(lines):
            return "after the menu loaded (a mod or test problem, not the start-up fault); last line: " + last[-120:]
        if "Activating first scene" in last or "Fast load counter" in last or "start.unity" in last or "UnityInput" in last or "Unloading" in last:
            return "inside Unity's start-scene load, before any mod code ran (known Valheim-on-Linux start-up fault, not a TestLab test)"
        return "last log line: " + last[-120:]

    started = time.time()
    print(f"DrakesTestLab: running suites '{a.suites}' with profile {profile}")

    def results(complete_only=False):
        if not os.path.exists(latest) or os.path.getmtime(latest) < started:
            return None
        j = read_json(latest)
        if j is None:
            return None
        if complete_only:
            return j if j.get("complete") else None
        return j if j.get("complete", True) else None

    def check_lines():
        """--combat-check writes no results file; its log says when it is done."""
        if not a.combat_check or not os.path.exists(log_file) or os.path.getmtime(log_file) < started:
            return None
        with open(log_file, encoding="utf-8", errors="replace") as f:
            lines = [l.rstrip() for l in f if "Combat check:" in l or "skipped, unknown prefab" in l]
        return lines if any("Combat check:" in l for l in lines) else None

    game = None
    base_launch = list(launch)
    for attempt in range(a.retries + 1):
        started = time.time()
        launch = list(base_launch)
        # Intel Mesa's Vulkan path sometimes deadlocks the graphics thread in the first scene load; OpenGL does not.
        # --gfx glcore forces it from the start; after a start-up freeze or crash the relaunches switch to it on their own.
        if a.gfx == "glcore" or (a.gfx == "auto" and attempt >= 1):
            launch.append("-force-glcore")
            if attempt >= 1:
                print("Relaunching with -force-glcore (OpenGL instead of Vulkan).", flush=True)
        if valheim_queue.game_pids():
            print("Valheim is already running; not launching a second one.")
            return finish(2)
        subprocess.Popen([a.steam, *launch], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)

        game = None
        deadline = time.time() + 120
        while not game and time.time() < deadline:
            time.sleep(2)
            pids = valheim_queue.game_pids()
            game = pids[0] if pids else None
        if not game:
            print("Valheim did not start within 2 minutes.")
            return finish(3)

        print(f"Valheim started (pid {game}); waiting for the tests...", flush=True)
        deadline = started + a.timeout_sec
        frozen = False
        while valheim_queue.alive(game) and game in valheim_queue.game_pids() and time.time() < deadline:
            if stay and results(True):
                break
            # a launch that freezes during start-up stops writing its log; nothing else will ever wake it
            idle = time.time() - os.path.getmtime(log_file) if os.path.exists(log_file) and os.path.getmtime(log_file) >= started - 5 else time.time() - started
            if a.stall_sec > 0 and not results() and not check_lines() and idle > a.stall_sec:
                frozen = True
                break
            time.sleep(3)

        # Valheim on Linux sometimes crashes (native SIGSEGV in Mono) a few seconds into start-up, before any test ran:
        # that is not a test result, so relaunch like a frozen start
        crashed_early = (not frozen and not results() and not check_lines() and not valheim_queue.alive(game) and time.time() - started < 90)
        if crashed_early:
            print(f"Valheim (pid {game}) crashed during start-up: {startup_stage()}.", flush=True)
            time.sleep(15)   # let Steam finish removing the dead game before launching again
        elif not frozen:
            break
        else:
            print(f"Valheim (pid {game}) froze: no log activity for {a.stall_sec} s, {startup_stage()}. Stopping it (it is the game this script started).", flush=True)
            try:
                os.kill(game, 9)
            except OSError:
                pass
            time.sleep(5)
        if attempt < a.retries:
            print(f"Relaunching (attempt {attempt + 2} of {a.retries + 1}).", flush=True)
        else:
            print("Still frozen after every attempt.")
            return finish(3)

    if game in valheim_queue.game_pids() and not results(True):
        print(f"Timed out after {a.timeout_sec} s.")
        if not a.no_kill_on_timeout:
            print("Stopping the game this script started.")
            try:
                os.kill(game, 9)
            except OSError:
                pass

    lines = check_lines()
    if lines is not None:
        print("\n".join(l.split("] ", 1)[-1] for l in lines))
        return finish(0 if not any("skipped, unknown prefab" in l for l in lines) else 1)

    r = results()
    if r and a.combat:
        print(f"\nCombat: {len(r.get('cells', []))} cells, {r.get('flags', 0)} balance flag(s)")
        marks = {"normal": "OK  ", "plus": "+   ", "plusplus": "++  ", "minus": "-   ", "minusminus": "--  "}
        for c in r.get("cells", []):
            print(f"  [{marks.get(c.get('verdict'), '    ')}] {c.get('loadout')} vs {c.get('foe')} [{c.get('variant')}]: "
                  f"{float(c.get('winRate', 0)) * 100:.0f}% win ({c.get('wins')}/{c.get('runs')}), ttk {float(c.get('ttkMean', 0)):.1f}s")
        print(f"\nReport: {os.path.join(os.path.dirname(latest), 'latest.html')}")
        return finish(1 if int(r.get("flags") or 0) > 0 else 0)
    if a.combat:
        print("No combat results were written (the game may have crashed or never reached the test world).")
        return finish(3)
    if r:
        print_results(r, started, latest)
        if int(r.get("failed") or 0) > 0:
            return finish(1)
        if r.get("complete") is False:
            print("The run did not finish (game closed or timed out): the results above are what it got to.")
            return finish(3)
        return finish(0)

    print("No new results were written (the game may have crashed or never reached the test world).")
    print(f"Check {os.path.join(bepinex, 'LogOutput.log')}")
    return finish(3)


if __name__ == "__main__":
    sys.exit(main())

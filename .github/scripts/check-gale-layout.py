#!/usr/bin/env python3
"""Fail if a Thunderstore zip would be flattened by Gale's / r2modman's BepInEx install rules.

Mirrors Gale's SubdirInstaller (src-tauri/src/profile/install/installers/subdir.rs): a file whose path has no
plugins/patchers/monomod/core/config folder falls into the default `plugins` target and is FLATTENED to its bare
file name. Under a plugins/ folder the sub-tree below it is kept.

Usage: check-gale-layout.py <zip> <Author-Name>
"""
import sys, zipfile

SUBDIRS = {"plugins": "BepInEx/plugins", "patchers": "BepInEx/patchers", "monomod": "BepInEx/monomod",
           "core": "BepInEx/core", "config": "BepInEx/config"}
ROOT_ONLY = {"manifest.json", "icon.png", "README.md", "CHANGELOG.md"}


def map_file(path, pkg):
    parts = [p for p in path.split("/") if p]
    for i, name in enumerate(parts):
        key = name.lower()
        if key in SUBDIRS or name.endswith(".mm.dll"):
            sub = key if key in SUBDIRS else "monomod"
            rest = parts[i + 1:]
            if sub == "config":
                return "/".join([SUBDIRS[sub], *rest])
            return "/".join([SUBDIRS[sub], pkg, *rest])
    return "/".join([SUBDIRS["plugins"], pkg, parts[-1]])  # flattened to the bare file name


def main():
    zpath, pkg = sys.argv[1], sys.argv[2]
    names = [n for n in zipfile.ZipFile(zpath).namelist() if not n.endswith("/")]
    mapped = {n: map_file(n, pkg) for n in names if n not in ROOT_ONLY}
    base = f"BepInEx/plugins/{pkg}/"
    want = [base + "DrakesTestLab.dll", base + "Assets/Presets/templates/TestLab/_main.1.fwl2",
            base + "Assets/Presets/templates/TestLab/_manifest.txt", base + "Assets/Presets/templates/MasterBjorn.fch"]
    got = set(mapped.values())
    missing = [w for w in want if w not in got]
    flattened = [n for n, m in mapped.items() if "/" in n and m.count("/") < n.count("/") + 2 and
                 m == base + n.split("/")[-1] and not n.startswith("plugins/")]
    clash = len(mapped) - len(got)
    if missing or flattened or clash:
        print("::error::Gale would mangle this package.", file=sys.stderr)
        for m in missing:
            print("  would not exist after install:", m, file=sys.stderr)
        for f in flattened[:10]:
            print("  flattened:", f, "->", mapped[f], file=sys.stderr)
        if clash:
            print(f"  {clash} file(s) would overwrite each other", file=sys.stderr)
        sys.exit(1)
    print(f"OK Gale layout: {len(mapped)} files keep their folders under {base}")


main()

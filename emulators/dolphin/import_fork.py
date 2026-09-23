"""Imports the Dolphin lightgun accuracy pack (VRLF's fork) into calibration/ and crosshairs/.

    python emulators/dolphin/import_fork.py <fork checkout> --version 1.1

calibration/: the fork's generated Wii Remote profiles, plus every key = value setting of its
GameSettings/<ID>.ini files as individual edits, so a user's own <ID>.ini keeps its other
settings. Code-list sections ([Gecko] and the like) are not settings and are skipped.
crosshairs/: the fork's Crosshair removal textures, plus GFX.ini's Load Custom Textures.
"""
import argparse
import json
import shutil
import subprocess
import sys
from pathlib import Path

FORK_URL = "https://github.com/dyzt/Dolphin-Lightguns-Accuracy-Inis"
AUTHORS = "Prof_gLX, PiperCalls, Ego_bizarro, Tovarichtch and Bratwurstmensch"
VRLF_DIR = "VRLF (VR Lightgun Framework)"
HERE = Path(__file__).resolve().parent


class ImportFailure(Exception):
    pass


def game_settings_edits(name: str, data: bytes) -> tuple[list, list[str]]:
    text = data.decode("latin-1")
    if text.startswith("\xef\xbb\xbf"):
        text = text[3:]
    edits, skipped, section = [], [], None
    for line in text.splitlines():
        s = line.strip()
        if not s or s.startswith(("#", ";")):
            continue
        if s.startswith("[") and s.endswith("]"):
            section = s[1:-1].strip()
            continue
        if section is None:
            continue
        if "=" in s:
            key, _, value = s.partition("=")
            edits.append({"file": f"GameSettings/{name}", "format": "ini", "section": section,
                          "key": key.strip(), "value": value.strip()})
        elif section not in skipped:
            skipped.append(section)
    return edits, skipped


def _commit(fork: Path) -> str:
    try:
        r = subprocess.run(["git", "-C", str(fork), "rev-parse", "--short", "HEAD"],
                           capture_output=True, text=True, check=True)
        return r.stdout.strip()
    except (OSError, subprocess.CalledProcessError):
        return "unknown"


def _readme(what: str, commit: str) -> str:
    return (f"# Dolphin lightgun {what} for VRLF\n\n"
            f"Installed by vrlf-mods.exe. Imported from {FORK_URL} at commit {commit}.\n\n"
            f"Calibration and crosshair textures by {AUTHORS}. GPL-3.0: see LICENSE.\n")


def _write_json(path: Path, obj) -> None:
    path.write_text(json.dumps(obj, indent=2) + "\n", encoding="utf-8")


def import_fork(fork: Path, dest: Path, version: str) -> list[str]:
    vrlf = fork / VRLF_DIR
    profiles = vrlf / "Config" / "Profiles" / "Wiimote"
    games = vrlf / "GameSettings"
    textures = fork / "Crosshair removal" / "Load" / "Textures"
    for p in (profiles, games, textures, fork / "LICENSE"):
        if not p.exists():
            raise ImportFailure(f"not found in the fork: {p}")

    cal, cross = dest / "calibration", dest / "crosshairs"
    for d in (cal, cross):
        shutil.rmtree(d / "files", ignore_errors=True)
        d.mkdir(parents=True, exist_ok=True)

    out = cal / "files" / "Config" / "Profiles" / "Wiimote"
    out.mkdir(parents=True)
    for p in sorted(profiles.glob("*.ini")):
        shutil.copyfile(p, out / p.name)

    notes, edits = [], []
    for g in sorted(games.glob("*.ini")):
        e, skipped = game_settings_edits(g.name, g.read_bytes())
        edits += e
        notes += [f"{g.name}: skipped [{s}] (a code list, not settings)" for s in skipped]
    _write_json(cal / "settings.src.json", {"version": version, "edits": edits})

    shutil.copytree(textures, cross / "files" / "Load" / "Textures")
    _write_json(cross / "settings.src.json", {"version": version, "edits": [
        {"file": "Config/GFX.ini", "format": "ini", "section": "Settings", "key": "HiresTextures", "value": "True"}]})

    commit = _commit(fork)
    for d, what in ((cal, "calibration"), (cross, "crosshair removal")):
        shutil.copyfile(fork / "LICENSE", d / "LICENSE")
        (d / "README.md").write_text(_readme(what, commit), encoding="utf-8")
    return notes


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("fork", type=Path)
    ap.add_argument("--version", required=True)
    args = ap.parse_args()
    try:
        for n in import_fork(args.fork, HERE, args.version):
            print(n)
    except ImportFailure as e:
        print(f"error: {e}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())

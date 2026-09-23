"""Builds the emulator packages vrlf-mods.exe installs.

Each emulators/<emu>/<option>/ folder holding a settings.src.json becomes
emulators/<emu>/dist/<emu>-<option>-v<version>.zip: files/ copied byte for byte, a
settings.json with every replaceFrom expanded, plus LICENSE and README.md when present.
Zips are deterministic (sorted entries, fixed timestamps), so --check can compare bytes.

    python emulators/build.py            build every package, print "<emu> <option> <version> <zip> <sha256>"
    python emulators/build.py --check    fail if a committed zip no longer matches its sources
"""
import argparse
import hashlib
import io
import json
import sys
import zipfile
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parent
FIXED_TIME = (1980, 1, 1, 0, 0, 0)
EXTRAS = ("LICENSE", "README.md")


class BuildError(Exception):
    pass


def section_lines(data: bytes, section: str) -> list[str]:
    """The non-blank, non-comment lines of one [section], verbatim."""
    text = data.decode("latin-1")
    if text.startswith("\xef\xbb\xbf"):
        text = text[3:]
    found, inside, out = False, False, []
    for line in text.splitlines():
        s = line.strip()
        if s.startswith("[") and s.endswith("]"):
            inside = s[1:-1].strip().lower() == section.lower()
            found = found or inside
            continue
        if inside and s and not s.startswith(("#", ";")):
            out.append(line)
    if not found:
        raise BuildError(f"section [{section}] not found")
    return out


def expand(option_dir: Path, edits: list) -> list:
    out = []
    for e in edits:
        e = dict(e)
        src = e.pop("replaceFrom", None)
        if src is not None:
            rel, _, section = src.partition("#")
            if not section:
                raise BuildError(f"replaceFrom needs <file>#<section>: {src}")
            path = option_dir / "files" / rel
            if not path.is_file():
                raise BuildError(f"replaceFrom file not found: files/{rel}")
            e["replace"] = section_lines(path.read_bytes(), section) + list(e.get("replace", []))
        out.append(e)
    return out


def _plain(s) -> bool:
    return s is None or all(c == "\t" or 32 <= ord(c) < 127 for c in s)


def validate(e: dict, where: str) -> None:
    fmt = e.get("format")
    if fmt not in ("ini", "mame"):
        raise BuildError(f"{where}: format must be ini or mame")
    f = e.get("file") or ""
    parts = PurePosixPath(f.replace("\\", "/")).parts
    if not f or f.startswith(("/", "\\")) or ":" in f or ".." in parts:
        raise BuildError(f"{where}: file must be a relative path inside the settings folder")
    is_set = "key" in e or "value" in e
    is_replace = "replace" in e
    if is_set == is_replace:
        raise BuildError(f"{where}: an edit is either key + value or replace")
    if is_set and (not e.get("key") or "value" not in e):
        raise BuildError(f"{where}: a set edit needs both key and value")
    if fmt == "mame" and (e.get("section") is not None or is_replace):
        raise BuildError(f"{where}: mame edits have no section and no replace")
    if fmt == "ini" and not e.get("section"):
        raise BuildError(f"{where}: ini edits need a section")
    strings = [f, e.get("section"), e.get("key"), e.get("value")] + list(e.get("replace", []))
    if not all(_plain(s) for s in strings):
        raise BuildError(f"{where}: edits must be plain ASCII on one line")
    key = e.get("key")
    if key is not None and "=" in key:
        raise BuildError(f"{where}: a key cannot contain '='")
    section = e.get("section")
    if section is not None and "]" in section:
        raise BuildError(f"{where}: a section name cannot contain ']'")


def _touches(e: dict):
    file = e["file"].replace("\\", "/").lower()
    if e["format"] == "mame":
        return file, None, e["key"].lower()
    return file, e["section"].lower(), None if "replace" in e else e["key"].lower()


def check_overlap(emu: str, options: list) -> None:
    """Two options of one emulator must never change the same setting."""
    seen = {}
    for opt, edits in options:
        for e in edits:
            file, section, key = _touches(e)
            for (f2, s2, k2), other in seen.items():
                if other == opt or f2 != file or s2 != section:
                    continue
                if key is None or k2 is None or key == k2:
                    raise BuildError(f"{emu}: {opt} and {other} both change [{e.get('section')}] in {e['file']}")
            seen.setdefault((file, section, key), opt)


def check_file_overlap(emu: str, options: list) -> None:
    """Two options of one emulator must never ship the same file path either."""
    seen = {}
    for opt, files_dir in options:
        if not files_dir.is_dir():
            continue
        for p in files_dir.rglob("*"):
            if not p.is_file():
                continue
            rel = p.relative_to(files_dir).as_posix().lower()
            other = seen.get(rel)
            if other is not None and other != opt:
                raise BuildError(f"{emu}: {opt} and {other} both ship files/{rel}")
            seen[rel] = opt


def build_option(option_dir: Path) -> tuple:
    src = json.loads((option_dir / "settings.src.json").read_text(encoding="utf-8"))
    version = src["version"]
    edits = expand(option_dir, src.get("edits", []))
    for i, e in enumerate(edits):
        validate(e, f"{option_dir.parent.name}/{option_dir.name} edit {i}")

    entries = {}
    files = option_dir / "files"
    if files.is_dir():
        for p in sorted(files.rglob("*")):
            if p.is_file():
                entries["files/" + p.relative_to(files).as_posix()] = p.read_bytes()
    for extra in EXTRAS:
        if (option_dir / extra).is_file():
            entries[extra] = (option_dir / extra).read_bytes()
    entries["settings.json"] = (json.dumps({"edits": edits}, indent=2) + "\n").encode("ascii")

    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w") as z:
        for name in sorted(entries):
            info = zipfile.ZipInfo(name, FIXED_TIME)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            z.writestr(info, entries[name])
    return version, edits, buf.getvalue()


def discover(root: Path):
    for emu_dir in sorted(p for p in root.iterdir() if p.is_dir()):
        for opt_dir in sorted(p for p in emu_dir.iterdir() if (p / "settings.src.json").is_file()):
            yield emu_dir.name, opt_dir.name, opt_dir


def build_all(root: Path, check: bool) -> list[str]:
    built, by_emu, files_by_emu = [], {}, {}
    for emu, opt, d in discover(root):
        version, edits, data = build_option(d)
        by_emu.setdefault(emu, []).append((opt, edits))
        files_by_emu.setdefault(emu, []).append((opt, d / "files"))
        built.append((emu, opt, version, data))
    for emu, options in by_emu.items():
        check_overlap(emu, options)
    for emu, options in files_by_emu.items():
        check_file_overlap(emu, options)

    lines = []
    for emu, opt, version, data in built:
        out = root / emu / "dist" / f"{emu}-{opt}-v{version}.zip"
        rel = out.relative_to(root.parent).as_posix()
        if check:
            if not out.is_file() or out.read_bytes() != data:
                raise BuildError(f"{rel} is out of date; run python emulators/build.py")
        else:
            out.parent.mkdir(exist_ok=True)
            out.write_bytes(data)
        lines.append(f"{emu} {opt} {version} {rel} {hashlib.sha256(data).hexdigest()}")
    return lines


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--check", action="store_true", help="fail if a committed zip is stale")
    args = ap.parse_args()
    try:
        print("\n".join(build_all(ROOT, args.check)))
    except BuildError as e:
        print(f"error: {e}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())

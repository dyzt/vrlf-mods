"""Tests for build.py. Run: python emulators/test_build.py -v"""
import io
import json
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build  # noqa: E402

PRESET = b"# header\r\n[Profile]\r\nDevice = XInput/0/Gamepad\r\n\r\n; note\r\nButtons/+ = `Start`\r\n[Other]\r\nx = 1\r\n"


def option(root: Path, emu: str, opt: str, src: dict, files: dict | None = None, extras: dict | None = None) -> Path:
    d = root / emu / opt
    (d / "files").mkdir(parents=True, exist_ok=True)
    for rel, data in (files or {}).items():
        p = d / "files" / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(data)
    for name, data in (extras or {}).items():
        (d / name).write_bytes(data)
    (d / "settings.src.json").write_text(json.dumps(src), encoding="utf-8")
    return d


class SectionLines(unittest.TestCase):
    def test_takes_setting_lines_verbatim_skipping_blanks_and_comments(self):
        self.assertEqual(build.section_lines(PRESET, "profile"), ["Device = XInput/0/Gamepad", "Buttons/+ = `Start`"])

    def test_missing_section_is_an_error(self):
        with self.assertRaises(build.BuildError):
            build.section_lines(PRESET, "Nope")

    def test_a_bom_does_not_hide_the_first_header(self):
        self.assertEqual(build.section_lines(b"\xef\xbb\xbf[A]\r\nk = v\r\n", "A"), ["k = v"])


class Expand(unittest.TestCase):
    def test_replace_from_expands_and_appends_extra_lines(self):
        with tempfile.TemporaryDirectory() as t:
            d = option(Path(t), "dolphin", "base", {}, {"Config/p.ini": PRESET})
            out = build.expand(d, [{"file": "W.ini", "format": "ini", "section": "Wiimote1",
                                    "replaceFrom": "Config/p.ini#Profile", "replace": ["Source = 1"]}])
            self.assertEqual(out[0]["replace"], ["Device = XInput/0/Gamepad", "Buttons/+ = `Start`", "Source = 1"])
            self.assertNotIn("replaceFrom", out[0])

    def test_replace_from_a_missing_file_is_an_error(self):
        with tempfile.TemporaryDirectory() as t:
            d = option(Path(t), "dolphin", "base", {})
            with self.assertRaises(build.BuildError):
                build.expand(d, [{"file": "W.ini", "format": "ini", "section": "S", "replaceFrom": "nope.ini#P"}])


class Validate(unittest.TestCase):
    GOOD = [
        {"file": "a.ini", "format": "ini", "section": "S", "key": "k", "value": "v"},
        {"file": "a.ini", "format": "ini", "section": "S", "replace": ["k = v"]},
        {"file": "mame.ini", "format": "mame", "key": "ctrlr", "value": "vrlf"},
        {"file": "config/config.yml", "format": "yaml", "section": "Input/Output", "key": "Camera type", "value": "PS Eye"},
    ]
    BAD = [
        {"file": "a.ini", "format": "toml", "section": "S", "key": "k", "value": "v"},
        {"file": "a.ini", "format": "ini", "key": "k", "value": "v"},
        {"file": "mame.ini", "format": "mame", "section": "S", "key": "k", "value": "v"},
        {"file": "a.ini", "format": "ini", "section": "S", "key": "k", "value": "v", "replace": ["x"]},
        {"file": "a.ini", "format": "ini", "section": "S"},
        {"file": "a.ini", "format": "ini", "section": "S", "key": "k"},
        {"file": "/abs.ini", "format": "ini", "section": "S", "key": "k", "value": "v"},
        {"file": "C:/abs.ini", "format": "ini", "section": "S", "key": "k", "value": "v"},
        {"file": "../up.ini", "format": "ini", "section": "S", "key": "k", "value": "v"},
        {"file": "a.ini", "format": "ini", "section": "S", "key": "k", "value": "caf\u00e9"},
        {"file": "a.ini", "format": "ini", "section": "S", "key": "k", "value": "a\nb"},
        {"file": "a.ini", "format": "ini", "section": "S", "key": "k=x", "value": "v"},
        {"file": "a.ini", "format": "ini", "section": "S]", "key": "k", "value": "v"},
        {"file": "a.yml", "format": "yaml", "key": "k", "value": "v"},
        {"file": "a.yml", "format": "yaml", "section": "S", "replace": ["k: v"]},
        {"file": "a.yml", "format": "yaml", "section": "S", "key": "a: b", "value": "v"},
        {"file": "a.yml", "format": "yaml", "section": "S: T", "key": "k", "value": "v"},
    ]

    def test_good_edits_pass(self):
        for e in self.GOOD:
            build.validate(e, "t")

    def test_bad_edits_fail(self):
        for e in self.BAD:
            with self.subTest(e=e), self.assertRaises(build.BuildError):
                build.validate(e, "t")


class Overlap(unittest.TestCase):
    def s(self, file, section, key):
        return {"file": file, "format": "ini", "section": section, "key": key, "value": "v"}

    def test_same_key_in_two_options_is_an_error_whatever_the_case(self):
        with self.assertRaises(build.BuildError):
            build.check_overlap("e", [("base", [self.s("Config/A.ini", "S", "k")]),
                                      ("addon", [self.s("config/a.ini", "s", "K")])])

    def test_a_replaced_section_clashes_with_any_key_in_it(self):
        with self.assertRaises(build.BuildError):
            build.check_overlap("e", [("base", [{"file": "a.ini", "format": "ini", "section": "S", "replace": ["x = 1"]}]),
                                      ("addon", [self.s("a.ini", "S", "other")])])

    def test_different_keys_or_sections_are_fine(self):
        build.check_overlap("e", [("base", [self.s("a.ini", "S", "k")]),
                                  ("addon", [self.s("a.ini", "S", "j"), self.s("a.ini", "T", "k")])])

    def test_two_options_shipping_the_same_file_is_an_error(self):
        with tempfile.TemporaryDirectory() as t:
            root = Path(t) / "emulators"
            option(root, "e", "base", {"version": "1.0", "edits": []}, {"Config/x.ini": b"a"})
            option(root, "e", "addon", {"version": "1.0", "edits": []}, {"config/X.ini": b"b"})
            with self.assertRaises(build.BuildError):
                build.build_all(root, check=False)


class BuildAll(unittest.TestCase):
    def make(self, root: Path):
        option(root, "dolphin", "base",
               {"version": "1.0", "edits": [{"file": "Config/W.ini", "format": "ini", "section": "Wiimote1",
                                              "replaceFrom": "Config/p.ini#Profile"}]},
               {"Config/p.ini": PRESET}, {"LICENSE": b"GPL"})

    def test_zip_layout_and_output_line(self):
        with tempfile.TemporaryDirectory() as t:
            root = Path(t) / "emulators"
            self.make(root)
            lines = build.build_all(root, check=False)
            zp = root / "dolphin" / "dist" / "dolphin-base-v1.0.zip"
            self.assertTrue(zp.is_file())
            emu, opt, ver, rel, sha = lines[0].split(" ")
            self.assertEqual((emu, opt, ver, rel), ("dolphin", "base", "1.0", "emulators/dolphin/dist/dolphin-base-v1.0.zip"))
            self.assertEqual(len(sha), 64)
            with zipfile.ZipFile(zp) as z:
                self.assertEqual(sorted(z.namelist()), ["LICENSE", "files/Config/p.ini", "settings.json"])
                self.assertEqual(z.read("files/Config/p.ini"), PRESET)
                settings = json.loads(z.read("settings.json"))
                self.assertEqual(settings["edits"][0]["replace"], ["Device = XInput/0/Gamepad", "Buttons/+ = `Start`"])

    def test_builds_are_byte_identical(self):
        with tempfile.TemporaryDirectory() as t:
            root = Path(t) / "emulators"
            self.make(root)
            first = build.build_all(root, check=False)
            self.assertEqual(first, build.build_all(root, check=False))

    def test_check_passes_when_fresh_and_fails_when_stale(self):
        with tempfile.TemporaryDirectory() as t:
            root = Path(t) / "emulators"
            self.make(root)
            build.build_all(root, check=False)
            build.build_all(root, check=True)
            (root / "dolphin" / "base" / "files" / "Config" / "p.ini").write_bytes(PRESET + b"y = 2\r\n")
            with self.assertRaises(build.BuildError):
                build.build_all(root, check=True)


if __name__ == "__main__":
    unittest.main()

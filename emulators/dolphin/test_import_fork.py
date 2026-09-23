"""Tests for import_fork.py. Run: python emulators/dolphin/test_import_fork.py -v"""
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import import_fork  # noqa: E402

GAME = (b"[Controls]\r\nWiimoteSource0 = 1\r\nWiimoteProfile1 = VRLF-X-P1\r\n"
        b"[Core]\r\nFastDiscSpeed = True\r\n[Gecko]\r\n$60FPS\r\n04158204 60000000\r\n*Some minor issues\r\n"
        b"[Video_Settings]\r\nWeird = a=b\r\n")


def fork(root: Path) -> Path:
    f = root / "fork"
    vrlf = f / import_fork.VRLF_DIR
    (vrlf / "Config" / "Profiles" / "Wiimote").mkdir(parents=True)
    (vrlf / "Config" / "Profiles" / "Wiimote" / "VRLF-X-P1.ini").write_bytes(b"[Profile]\r\nDevice = XInput/0/Gamepad\r\n")
    (vrlf / "Config" / "Profiles" / "Wiimote" / "VRLF-X-P2.ini").write_bytes(b"[Profile]\r\nDevice = XInput/1/Gamepad\r\n")
    (vrlf / "GameSettings").mkdir()
    (vrlf / "GameSettings" / "RBUE08.ini").write_bytes(GAME)
    tex = f / "Crosshair removal" / "Load" / "Textures" / "RBUE08"
    tex.mkdir(parents=True)
    (tex / "tex1_64x64_abc_3.png").write_bytes(b"\x89PNG")
    (f / "LICENSE").write_bytes(b"GNU GENERAL PUBLIC LICENSE")
    return f


class GameSettingsEdits(unittest.TestCase):
    def test_every_key_value_line_becomes_a_set_edit(self):
        edits, skipped = import_fork.game_settings_edits("RBUE08.ini", GAME)
        self.assertEqual([(e["section"], e["key"], e["value"]) for e in edits], [
            ("Controls", "WiimoteSource0", "1"),
            ("Controls", "WiimoteProfile1", "VRLF-X-P1"),
            ("Core", "FastDiscSpeed", "True"),
            ("Video_Settings", "Weird", "a=b"),
        ])
        self.assertTrue(all(e["file"] == "GameSettings/RBUE08.ini" and e["format"] == "ini" for e in edits))
        self.assertEqual(skipped, ["Gecko"])


class ImportFork(unittest.TestCase):
    def test_writes_both_options(self):
        with tempfile.TemporaryDirectory() as t:
            root = Path(t)
            dest = root / "dolphin"
            notes = import_fork.import_fork(fork(root), dest, "1.1")

            cal = dest / "calibration"
            self.assertEqual((cal / "files" / "Config" / "Profiles" / "Wiimote" / "VRLF-X-P1.ini").read_bytes(),
                             b"[Profile]\r\nDevice = XInput/0/Gamepad\r\n")
            src = json.loads((cal / "settings.src.json").read_text(encoding="utf-8"))
            self.assertEqual(src["version"], "1.1")
            self.assertEqual(len(src["edits"]), 4)
            self.assertEqual(notes, ["RBUE08.ini: skipped [Gecko] (a code list, not settings)"])

            cross = dest / "crosshairs"
            self.assertEqual((cross / "files" / "Load" / "Textures" / "RBUE08" / "tex1_64x64_abc_3.png").read_bytes(), b"\x89PNG")
            csrc = json.loads((cross / "settings.src.json").read_text(encoding="utf-8"))
            self.assertEqual(csrc["edits"], [{"file": "Config/GFX.ini", "format": "ini", "section": "Settings",
                                              "key": "HiresTextures", "value": "True"}])

            for d in (cal, cross):
                self.assertEqual((d / "LICENSE").read_bytes(), b"GNU GENERAL PUBLIC LICENSE")
                readme = (d / "README.md").read_text(encoding="utf-8")
                self.assertIn(import_fork.AUTHORS, readme)
                self.assertIn(import_fork.FORK_URL, readme)

    def test_a_rerun_removes_files_the_fork_no_longer_has(self):
        with tempfile.TemporaryDirectory() as t:
            root = Path(t)
            dest = root / "dolphin"
            f = fork(root)
            import_fork.import_fork(f, dest, "1.1")
            stale = dest / "calibration" / "files" / "Config" / "Profiles" / "Wiimote" / "VRLF-GONE-P1.ini"
            stale.write_bytes(b"x")
            import_fork.import_fork(f, dest, "1.1")
            self.assertFalse(stale.exists())

    def test_a_fork_missing_a_part_is_an_error(self):
        with tempfile.TemporaryDirectory() as t:
            root = Path(t)
            f = fork(root)
            (f / "LICENSE").unlink()
            with self.assertRaises(import_fork.ImportFailure):
                import_fork.import_fork(f, root / "dolphin", "1.1")


if __name__ == "__main__":
    unittest.main()

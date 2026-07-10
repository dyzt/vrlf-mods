# martian-panic — Martian Panic VRLF Mod

- **Game:** Martian Panic (Steam app **2343850** — Funbox Media; on-rails Unity/Wii port)
- **Type:** BepInEx 5 (Mono) — bundled inside the zip (winhttp.dll + doorstop + BepInEx tree)
- **Registry version:** `1.0`
- **Distribution zip:** `dist/martian-panic-v1.0.zip` (pulled from release `v1.0`)
- **Aim note:** no aim mod needed — the framework's absolute mouse aim is already 1:1 (the game
  reads the OS cursor, never locks it, no deadzone). Profile uses `aim_mode: "absolute"`. This
  mod ONLY hides the on-screen crosshair (in VR your aim is the pointer).
- **Config:** `HideCrosshair` (default on) and `VerboseLog` in `BepInEx/plugins/MartianPanicVrlf.cfg`.

## To populate (rollout)

1. Copy this mod's **current working tree** here (current snapshot only — no full history).
   Origin (private): `dyzt/martian-panic-vrlf-mod`. NB: `docs/investigation.md` references the
   Unity decompile — audit before any public source import.
2. ~~Build and place the release zip~~ — DONE: `dist/martian-panic-v1.0.zip` pulled from
   release `v1.0` (extracts into the game root).
3. ~~Compute its SHA-256 and fill the value~~ — DONE in root `mods.json`.

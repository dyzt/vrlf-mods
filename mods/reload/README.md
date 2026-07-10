# reload — Reload VRLF Mod

- **Game:** Reload (Steam app **330370** — Top3Line / Mastiff, 2015; *not* the delisted
  Digital Homicide title)
- **Type:** standalone `dinput8.dll` proxy (same scaffold as Heavy Fire) + cfg
- **Registry version:** `1.0`
- **Distribution zip:** `dist/reload-v1.0.zip`
- **Launch note:** start the game with `-windowed` (undocumented engine switch) so VRLF can
  capture it. Profile uses `aim_mode: "bus"`.

## To populate (rollout)

1. Copy this mod's **current working tree** here (current snapshot only — no full history).
   Origin (private): `dyzt/reload-vrlf-mod`.
2. Build and place the release zip at `dist/reload-v1.0.zip` (extracts into the game root).
   The origin repo had no GitHub release — build the first `dist/` zip here.
3. Compute its SHA-256 and replace the `TOFILL` value for `reload` in the root `mods.json`.

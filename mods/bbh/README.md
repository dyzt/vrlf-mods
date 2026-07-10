# bbh — Big Buck Hunter Ultimate VRLF Mod

- **Game:** Big Buck Hunter: Ultimate Trophy (Steam app **3102290**)
- **Type:** BepInEx 6 (IL2CPP) — bundled inside the zip
- **Registry version:** `1.0.0`
- **Distribution zip:** `dist/bbh-v1.0.0.zip`

## To populate (rollout)

1. Copy this mod's **current working tree** here (current snapshot only — no full history).
   Origin (private): `dyzt/bbh-vrlf-mod`.
2. Build and place the release zip at `dist/bbh-v1.0.0.zip` (extracts into the game root).
   The existing release already ships a self-contained zip (`BBH-VRLF-Mod-v1.0.0.zip`) — you
   can rename it to the `dist/bbh-v1.0.0.zip` convention.
3. Compute its SHA-256 and replace the `TOFILL` value for `bbh` in the root `mods.json`.

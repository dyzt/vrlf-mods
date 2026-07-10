# hotd1 — House of the Dead: Remake VRLF Mod

- **Game:** THE HOUSE OF THE DEAD: Remake (Steam app **1694600**)
- **Type:** BepInEx 5 (Mono) — bundled inside the zip (winhttp.dll + doorstop + BepInEx tree)
- **Registry version:** `1.0.1`
- **Distribution zip:** `dist/hotd1-v1.0.1.zip`

## To populate (rollout)

1. Copy this mod's **current working tree** here (source + build files). Import the current
   snapshot only — do **not** import full history (it may contain decompiled game source).
   Origin (private, pre-consolidation): `dyzt/hotd1-vrlf-mod`.
2. Build the mod and place the release zip at `dist/hotd1-v1.0.1.zip`. The zip must extract
   into the game root (it already bundles the full BepInEx tree).
3. Compute its SHA-256 and replace the `TOFILL` value for `hotd1` in the root `mods.json`.

> Note: the old `dyzt/hotd1-vrlf-mod` GitHub release (v1.0.1) lacked a zip asset — build a
> correct `dist/` zip here.

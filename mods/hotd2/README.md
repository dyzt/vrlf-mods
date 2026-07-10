# hotd2 — House of the Dead 2: Remake VRLF Mod

- **Game:** THE HOUSE OF THE DEAD 2: Remake (Steam app **3376690**)
- **Type:** BepInEx 5 (Mono) — bundled inside the zip (winhttp.dll + doorstop + BepInEx tree)
- **Registry version:** `1.0`
- **Distribution zip:** `dist/hotd2-v1.0.zip`
- **Co-op:** needs ViGEmBus for Player 2 (`vrlf-mods vigembus` installs it)

## To populate (rollout)

1. Copy this mod's **current working tree** here (current snapshot only — no full history;
   it may contain decompiled game source). Origin (private): `dyzt/hotd2-vrlf-mod`.
2. Build and place the release zip at `dist/hotd2-v1.0.zip` (extracts into the game root).
3. Compute its SHA-256 and replace the `TOFILL` value for `hotd2` in the root `mods.json`.

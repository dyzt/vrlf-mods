# heavy-fire — Heavy Fire VRLF Mod

- **Games (one mod, two titles):** Heavy Fire: Afghanistan (Steam app **305980**) and
  Heavy Fire: Shattered Spear (Steam app **385600**)
- **Type:** standalone `dinput8.dll` proxy (no BepInEx, no DemulShooter) + `heavyfire_vrlf.cfg`
- **Registry version:** `1.0`
- **Distribution zip:** `dist/heavy-fire-v1.0.zip`
- **Co-op:** up to 4 players — set `PatchPlayerTable=true` + `PlayerCount=N` in `heavyfire_vrlf.cfg`
  (needs ViGEmBus; `vrlf-mods vigembus` installs it)

## To populate (rollout)

1. Copy this mod's **current working tree** here (current snapshot only — no full history).
   Origin (private): `dyzt/heavy-fire-vrlf-mod`.
2. Build and place the release zip at `dist/heavy-fire-v1.0.zip`. One DLL covers both builds
   via per-MD5 caves; the zip extracts into the game root (`dinput8.dll` + cfg + profiles).
3. Compute its SHA-256 and replace the `TOFILL` value for `heavy-fire` in the root `mods.json`.

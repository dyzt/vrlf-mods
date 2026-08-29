# vrlf-mods

Companion mods and the mod manager for the **VR Lightgun Framework (VRLF)**.

This repository is the single public home for:

- **`manager/`** — `vrlf-mods.exe`, a .NET 8 CLI that installs/uninstalls/updates the
  companion mods below. It ships inside the VRLF Steam depot; run it to make a supported
  game VR-lightgun ready. It finds each game via Steam automatically;
  `vrlf-mods path <id> <dir>` (or the menu's **Game folder** row) points it at a copy Steam
  cannot see, and remembers it. See [`docs/vrlf-mod-manager.md`](../VR%20Lightgun%20Framework/docs/vrlf-mod-manager.md)
  in the framework repo for the command reference and rollout runbook.
- **`mods/<id>/`** — one folder per companion mod: the mod's source plus its built
  distribution zip under `dist/`.
- **`mods.json`** — the registry the manager reads (served via `raw.githubusercontent.com`).
  Each entry names a mod's `version`, its committed `zip` path, its `sha256`, and the Steam
  app id(s) it targets.

## Mods

| id | Game(s) (Steam appid) | Type |
|---|---|---|
| `hotd1` | THE HOUSE OF THE DEAD: Remake (1694600) | BepInEx 5 (Mono) |
| `hotd2` | THE HOUSE OF THE DEAD 2: Remake (3376690) | BepInEx 5 (Mono) |
| `bbh` | Big Buck Hunter: Ultimate Trophy (3102290) | BepInEx 6 (IL2CPP) |
| `heavy-fire` | Heavy Fire: Afghanistan (305980) + Shattered Spear (385600) | `dinput8.dll` proxy |
| `reload` | Reload (330370) | `dinput8.dll` proxy |
| `martian-panic` | Martian Panic (2343850) | BepInEx 5 (Mono) |

## How delivery works

Each mod's built zip is **committed** at `mods/<id>/dist/<id>-v<version>.zip`. The manager
reads `mods.json`, downloads the named zip from raw GitHub content, verifies its SHA-256,
and extracts it into the game's install directory — recording a receipt so it can be removed
cleanly later. No GitHub API, no rate limits.

## Status

The `manager/` tool is complete. The `mods/<id>/` folders are scaffolded and awaiting their
source + built zips (see each folder's `README.md` and the rollout runbook). Until the zips
are committed and their real hashes fill the `TOFILL` placeholders in `mods.json`, the
manager will report a hash mismatch on install by design.

## License

See [`LICENSE`](LICENSE).

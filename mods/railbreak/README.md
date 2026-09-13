# railbreak — Railbreak VRLF Mod

- **Game:** Railbreak (Steam app **2587170**, Unreal Engine 5)
- **Type:** UE4SS Lua mod, with UE4SS bundled in the zip (`dwmapi.dll` + `ue4ss/` under
  `Railbreak/Binaries/Win64/`). Includes custom UE4SS signatures, without which UE4SS cannot
  attach to this game build.
- **Registry version:** `1.0.0`
- **Distribution zip:** `dist/railbreak-v1.0.0.zip` (pulled from release `v1.0.0` of the private
  `dyzt/railbreak-vrlf-mod`)
- **What it does:** Player 2's crosshair follows P2's VR gun 1:1, read from the VRLF aim bus file
  (`%TEMP%\VRLF_AimBus.bin`). Player 1 needs no mod.
- **Requires:** VRLF 0.1.37 or newer, the "Railbreak" profile, and "2 player" set in the main menu.
- **Co-op:** P2 is a ViGEmBus virtual pad, so `requiresVigembusForCoop: true`.
- **Config:** `DriveP2` (default on) and `VerboseLog` in
  `Railbreak/Binaries/Win64/ue4ss/Mods/RailbreakVrlf/railbreak_vrlf.cfg`.
- **Caveat:** installing replaces an existing UE4SS (`UE4SS.dll`, `mods.txt`, `mods.json`).

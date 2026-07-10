# blue-estate — Blue Estate VRLF Mod

- **Game:** Blue Estate (Steam app **305380**, Unreal Engine 3)
- **Type:** `dinput8.dll` proxy (Win32 + Win64) — Player 2 co-op absolute lightgun aim from
  the VRLF aim bus. Plus a **reversible native crosshair patch** for `BEGame.upk`.
- **Registry version:** `1.0.0`
- **Distribution zip:** `dist/blue-estate-v1.0.0.zip` (pulled from release `v1.0.0`)
- **Co-op:** P2 drives a ViGEmBus virtual pad → `requiresVigembusForCoop: true`.

## Crosshair removal — native, reversible

The mod manager hides the crosshair by patching `BEGame/COOKEDPCCONSOLE/BEGame.upk`
**natively** (a C# port of the community LZO1X + 29-byte patch — see
`manager/src/patches/BlueEstateCrosshairPatch.cs`), MD5-gated to the Steam build. It keeps a
`.vrlf-backup`; toggling the option off (or Steam → Verify integrity of game files) restores
the original. No Python is required. The release zip's `tools/apply_nocrosshair.py` is the
original standalone script, kept for reference only.

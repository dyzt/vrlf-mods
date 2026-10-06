Hides both players' in-game crosshairs in The House of the Dead EX (the 2008
SEGA Lindbergh arcade game, run through TeknoParrot), for VR play with VRLF,
where you aim with the gun itself. The "P1" / "P2" label under the aim point
goes too. The crossed-out gun icon that shows when your gun can't fire is left
as it is.

The crosshairs are two small textures inside the game's fs\test\*.bnk files.
Rather than ship those files, this script makes the two textures transparent
in your own copy. It checks each file first and changes nothing if your game
version differs.

Install
1. Close the game and TeknoParrot.
2. Copy hide_crosshair.ps1, Hide Crosshair.bat and Restore Crosshair.bat into
   your House of the Dead EX game folder (the folder that holds elf and fs).
3. Double-click Hide Crosshair.bat.

The script saves the original textures to fs\test\vrlf-crosshair-backup
before it changes anything.

Undo
Close the game and double-click Restore Crosshair.bat.

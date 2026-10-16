Hides the in-game crosshairs in Let's Go Island - Lost on the Island of Tropics
(2010) and Let's Go Island 3D (2011), the SEGA arcade games run through
TeknoParrot, for VR play with VRLF, where you aim with the gun itself. The
ring sights and the "can't shoot" X both go. The boss weak-point markers and
the name-entry target are left as they are.

The crosshairs are sprites inside the game's
data\acroarts\2D\I_CMN_Target_Sight.afb (and, in Let's Go Island 3D, also
I_CMN_Target_Sight_3D.afb). Rather than ship those files, this script makes
the sprites transparent in your own copy. It checks each file first and
changes nothing if your game version differs.

Tested on Let's Go Island. Let's Go Island 3D is supported but not yet tested
in game.

Install
1. Close the game and TeknoParrot.
2. Copy hide_crosshair.ps1, Hide Crosshair.bat and Restore Crosshair.bat into
   your Let's Go Island game folder (the folder that holds data).
3. Double-click Hide Crosshair.bat.

The script saves the original files to data\acroarts\2D\vrlf-crosshair-backup
before it changes anything.

Undo
Close the game and double-click Restore Crosshair.bat.

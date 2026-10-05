Hides both players' in-game crosshairs in Target Bravo: Operation G.H.O.S.T.
(the 2016 SEGA arcade game), for VR play with VRLF, where you aim with the gun
itself. Mode select, stage select, team order, result and name entry pointers
are left as they are.

The game runs its Lua scripts straight from the root folder. This adds one
check to root\lua\Direction\MouseCursor.lua; nothing else changes.

Install
1. Close the game.
2. Back up your game's root\lua\Direction\MouseCursor.lua.
3. Copy the root folder from this zip into your Target Bravo game folder
   (the folder that holds gs2.exe and root) and overwrite when asked.

Undo
Put your backed-up MouseCursor.lua back.

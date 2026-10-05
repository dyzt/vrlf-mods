------------------------------- UTF-8로 저장하기 위한 코멘트/UTF-8でセーブするためのコメント
NONE_CURSOR		= 0
SNIPE_CURSOR	= 1
HOLD_CURSOR		= 2
READY_CURSOR	= 3

local function MouseCursorFuncObj()	-- horii: 이 lua에서만 참조되는 함수에는 local 지정해주세요! 전역 변수를 줄일 수 있습니다.
	local	hudPath = AppSetting.ConvertPath("img/UI/UI_Demo_Common/UI_@/UI_Demo_Common.ANIM")
	local	aniCommon		= LwAnimation(hudPath)
	hudPath = AppSetting.ConvertPath( "img/UI/UI_hud_common/UI_@/UI_GMode.anim" )
	local	hudCommon2	= LwAnimation(hudPath)
	
	local	nUpdateTraceIdx	= 1
	
	local FullID = {0, 1}
	local BurstID = {2, 3}
	local SemiID = {4, 5}
	local GunModeLen = hudCommon2:GetAnimationLength(BurstID[1])
	local GunModeFrame = {0, 0}
	
	local currentGunMode = {2, 3}
	local beforeGunMode = {2, 3}
	local beforeFrame = {9, 9}
	
	local beforenShootType = {3, 3}
	local bChange = {false, false}

	local bShowAimMaker = true
	
	local	CursorTable		= {}
	CursorTable[NONE_CURSOR]		= {}
	CursorTable[NONE_CURSOR][1]		= 10
	CursorTable[NONE_CURSOR][2]		= 10
	
	CursorTable[SNIPE_CURSOR]		= {}
	CursorTable[SNIPE_CURSOR][1]	= 10
	CursorTable[SNIPE_CURSOR][2]	= 24
	
	CursorTable[HOLD_CURSOR]		= {}
	CursorTable[HOLD_CURSOR][1]		= 9
	CursorTable[HOLD_CURSOR][2]		= 19
	
	CursorTable[READY_CURSOR]		= {}
	CursorTable[READY_CURSOR][1]	= 8
	CursorTable[READY_CURSOR][2]	= 8
	
	local	MouseTrace		= {}
	MouseTrace[1]			= {}
	MouseTrace[2]			= {}
	
	MouseTrace[1]	= POINT(0,0)
	MouseTrace[2]	= POINT(0,0)
	
	
	local	tbl	= {
		bSelect = false,
		eCursorType		= SNIPE_CURSOR,
		bVisiable		= {true, true},
		bIsReload = {false, false},
	
		Main	= function(self)
			if g_State.bShootable then
				if self.eCursorType ~= SNIPE_CURSOR then
					self.eCursorType = SNIPE_CURSOR
				end
			else
				if self.bSelect then
					self.eCursorType = SNIPE_CURSOR
				else
					if self.eCursorType ~= HOLD_CURSOR then
						self.eCursorType	= HOLD_CURSOR
					end
				end
			end
			
			for i, v in pairs( Player.g_Players ) do
				if self.eCursorType == SNIPE_CURSOR then
					if GunModeFrame[i] < GunModeLen - 1 then
						GunModeFrame[i] = GunModeFrame[i] + 1
					end
					
					if bChange[i] == true then
						if beforeFrame[i] > 0 then
							beforeFrame[i] = beforeFrame[i] - 1
						else
							bChange[i] = false
						end
					end
					
					local	nShootType	= Player.g_Players[i]:GetAutomaticFireCount()
					if beforenShootType[i] ~= nShootType and bChange[i] == false then
						beforeGunMode[i] = currentGunMode[i]
						if nShootType == 0 then
							currentGunMode[i] = FullID[i]
						elseif nShootType == 1 then
							currentGunMode[i] = SemiID[i]
						elseif nShootType == 3 then
							currentGunMode[i] = BurstID[i]
						else
							currentGunMode[i] = SemiID[i]
						end
						beforenShootType[i] = nShootType
						beforeFrame[i] = 9
						GunModeFrame[i] = 0
						bChange[i] = true
					end
				else
					GunModeFrame[i] = 0
				end
			end
		end,
	
		Draw	= function(self)
			if g_hud:GetNotShowAll() == true then
				return
			end
			
			if lwMisc:GetAutoPlay() then
				return
			end
			if not bShowAimMaker then
				return
			end
			-- VRLF: no gameplay reticle. Select screens and name entry keep it.
			if not self.bSelect and Seq_NameEntry == nil then
				return
			end
			
			for i, v in pairs( Player.g_Players ) do
				if lwVersion.HwasooLee then
					MouseTrace[i]	= Player.g_Controllers[i]:GetGunPos(i)
					lwMisc:DebugLog(string.format("ch %d:%d,%d\n", i, MouseTrace[i].x, MouseTrace[i].y))
				end

				local bDraw = true
				if Player.g_Players[i].nLife <= 0 or self.bVisiable[i] ~= true or GameControl.GetPlayerState(i - 1) == GameControl.PlayerRetry then
					bDraw = false
				end

				if bDraw then
					MouseTrace[i] = Player.g_Controllers[i]:GetGunPos(i)

					if lwMisc:GetDemoPlay() then
						-- 데모용
						hudCommon2:DrawAnimation2(MouseTrace[i].x, MouseTrace[i].y, 35, currentGunMode[i], GunModeFrame[i])
					elseif lwGun[ i - 1 ]:GetFrameOutCnt() < 1 then
						-- 일반
						
						if (self.eCursorType == SNIPE_CURSOR) or (self.eCursorType == NONE_CURSOR) then
							if g_hud:GetMouseCursorHold(i) then
								aniCommon:DrawCell2(MouseTrace[i].x, MouseTrace[i].y, 35, CursorTable[HOLD_CURSOR][i])
							else
								if (g_hud:GetReload(i) == false) or (self.bSelect == true) then
									if Player.g_Players[i]:GetGunType() == "snipe" then
										aniCommon:DrawCell2(MouseTrace[i].x, MouseTrace[i].y, 35, CursorTable[self.eCursorType][i])
									elseif Player.g_Players[i]:GetGunType() ~= "stinger" then
										hudCommon2:DrawAnimation2(MouseTrace[i].x, MouseTrace[i].y, 35, currentGunMode[i], GunModeFrame[i])
									end
								end
							end
						else
							if MouseTrace[i].x ~= 0 and MouseTrace[i].y ~= 0 then
								aniCommon:DrawCell2(MouseTrace[i].x, MouseTrace[i].y, 35, CursorTable[self.eCursorType][i])
							end
						end
					end
				end
			end
		end,

		SetShowAimMaker = function(self, bShow)
			bShowAimMaker = bShow
		end,
	}
	
	return tbl
end

g_mouseCursor	= MouseCursorFuncObj()

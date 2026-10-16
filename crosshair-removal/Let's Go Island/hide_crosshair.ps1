# Hides the in-game crosshairs in Let's Go Island (2010) and Let's Go Island 3D
# (2011), the SEGA RingWide arcade games run through TeknoParrot, for VR play
# with VRLF, where you aim with the gun itself.
#
# Makes every sprite in data\acroarts\2D\I_CMN_Target_Sight*.afb fully
# transparent, in place: the 1P / 2P ring sights, the Multi Arms sights, the
# 3D sights (Let's Go Island 3D) and the "can't shoot" X. Only alpha bytes
# change; the file keeps its length. The boss weak-point markers and the
# name-entry target are separate files and stay as they are.
#
# Each file is matched by a SHA-256 of its exact bytes, so nothing is written
# to a file that is neither the original nor already hidden. The original file
# is copied to data\acroarts\2D\vrlf-crosshair-backup before the first change.
#
#   hide_crosshair.ps1            hide the crosshairs
#   hide_crosshair.ps1 -Restore   put them back
#   -GameDir <path>               the game folder (holds data); default: the
#                                 folder this script is in
param(
    [switch]$Restore,
    [string]$GameDir = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'

# Every known copy: which file, its game, the hashes before and after, and each
# image's pixel data as @(offset, pixel count), 32-bit BGRA (alpha = byte 3).
$Known = @(
    @{
        File     = 'I_CMN_Target_Sight.afb'
        Game     = "Let's Go Island"
        Original = '13539631c48eed4273270dd574979ca973ca148198cce587ba8e6ed37169ed73'
        Hidden   = '658add741b38a1857972f4c16c1f1e89facdf9b8a41a5d8c60ed807e0777ad90'
        Size     = 300768
        Images   = @(
            @(5056, 16384),    # Target_Sight_2P
            @(70720, 4096),    # target2P_Cannot_Aim
            @(87232, 16384),   # Target_Sight_Multi_Arms_2P
            @(152896, 16384),  # Target_Sight_1P
            @(218560, 4096),   # target1P_Cannot_Aim
            @(235072, 16384)   # Target_Sight_Multi_Arms_1P
        )
    },
    @{
        File     = 'I_CMN_Target_Sight.afb'
        Game     = "Let's Go Island 3D"
        Original = '44e20315b7d61508d8050fb09d7f4cb4f5d44ef3cf5e587c280fae5673a43e28'
        Hidden   = '8af7d9f43a273514fa926bf12d157c240bf0d51ef1e98dc140c204908e51b0d3'
        Size     = 300736
        Images   = @(
            @(5024, 16384),    # Target_Sight_2P
            @(70688, 4096),    # target2P_Cannot_Aim
            @(87200, 16384),   # Target_Sight_Multi_Arms_2P
            @(152864, 16384),  # Target_Sight_1P
            @(218528, 4096),   # target1P_Cannot_Aim
            @(235040, 16384)   # Target_Sight_Multi_Arms_1P
        )
    },
    @{
        File     = 'I_CMN_Target_Sight_3D.afb'
        Game     = "Let's Go Island 3D"
        Original = '9427ee08eb3cc5d8b62fce4269de53c6f539c75fd209ba24bb5c46ac2463754b'
        Hidden   = '723a2354c0f0de59d10cfc06b3ae4fdf7370a97156de5e6c08d81872b3fd8293'
        Size     = 432320
        Images   = @(
            @(5280, 16384),    # Target_Sight_2P_3D
            @(70944, 4096),    # target2P_Cannot_Aim
            @(87456, 16384),   # Target_Sight_Multi_Arms_2P
            @(153120, 16384),  # Target_Sight_2P
            @(218784, 16384),  # Target_Sight_1P_3D
            @(284448, 4096),   # target1P_Cannot_Aim
            @(300960, 16384),  # Target_Sight_Multi_Arms_1P
            @(366624, 16384)   # Target_Sight_1P
        )
    }
)

$dir = Join-Path $GameDir 'data\acroarts\2D'
if (-not (Test-Path -LiteralPath (Join-Path $dir 'I_CMN_Target_Sight.afb'))) {
    throw "No data\acroarts\2D\I_CMN_Target_Sight.afb under '$GameDir'. Put this script in the Let's Go Island game folder (the one that holds data), or pass -GameDir."
}
$backup = Join-Path $dir 'vrlf-crosshair-backup'
$sha = [System.Security.Cryptography.SHA256]::Create()

function Get-Hex([byte[]]$bytes) {
    (($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') }) -join '')
}

# Pass 1 checks every file and plans the writes; any problem stops the script
# before a single byte changes.
$plan = @()
foreach ($file in ($Known | ForEach-Object { $_.File } | Select-Object -Unique)) {
    $path = Join-Path $dir $file
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $hex = Get-Hex $bytes
    $entry = $Known | Where-Object { $_.File -eq $file -and ($_.Original -eq $hex -or $_.Hidden -eq $hex) } | Select-Object -First 1
    if ($null -eq $entry) { throw "${file}: not a version this script knows; nothing was changed." }
    $saved = Join-Path $backup $file
    $step = @{ Path = $path; Saved = $saved; Bytes = $bytes; Label = "$($entry.Game): $file" }

    if ($Restore) {
        if ($hex -eq $entry.Original) { Write-Host "$($step.Label): already original"; continue }
        if (-not (Test-Path -LiteralPath $saved)) { throw "$($step.Label): no backup at $saved; nothing was changed." }
        $orig = [System.IO.File]::ReadAllBytes($saved)
        if ((Get-Hex $orig) -ne $entry.Original) { throw "$saved is damaged; nothing was changed." }
        $step.New = $orig
        $step.Done = 'restored'
    } else {
        if ($hex -eq $entry.Hidden) { Write-Host "$($step.Label): already hidden"; continue }
        $hidden = [byte[]]$bytes.Clone()
        foreach ($img in $entry.Images) {
            $start = [int]$img[0]
            $end = $start + 4 * [int]$img[1]
            for ($i = $start + 3; $i -lt $end; $i += 4) { $hidden[$i] = 0 }
        }
        if ((Get-Hex $hidden) -ne $entry.Hidden) { throw "internal check failed for $($step.Label); nothing was changed." }
        $step.New = $hidden
        $step.Done = 'hidden'
    }
    $plan += $step
}

# Pass 2: save the originals, then write.
if (-not $Restore -and $plan.Count -gt 0) {
    New-Item -ItemType Directory -Force -Path $backup | Out-Null
    foreach ($step in $plan) {
        if (-not (Test-Path -LiteralPath $step.Saved)) { [System.IO.File]::WriteAllBytes($step.Saved, $step.Bytes) }
    }
}
foreach ($step in $plan) {
    try {
        [System.IO.File]::WriteAllBytes($step.Path, $step.New)
    } catch {
        throw "Can't write $($step.Path). Close the game and TeknoParrot, then try again."
    }
    Write-Host "$($step.Label): $($step.Done)"
}
Write-Host ("Done: {0} change(s)." -f $plan.Count)

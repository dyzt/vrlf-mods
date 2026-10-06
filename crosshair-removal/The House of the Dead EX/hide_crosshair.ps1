# Hides the in-game crosshairs in The House of the Dead EX (Sega Lindbergh,
# hodexRI.elf), for VR play with VRLF, where you aim with the gun itself.
#
# Makes two 128x128 DXT5 textures fully transparent, in place:
#   crosshair  the red P1 / blue P2 crosses and the two dashed circles
#   label      the "P1" / "P2" text under the aim point
# The crossed-out gun icon (your gun can't fire yet) is left as it is.
#
# Each texture is matched by a SHA-256 of its exact bytes, so nothing is written
# to a file whose copy is neither the original nor already hidden. The original
# bytes are saved to fs\test\vrlf-crosshair-backup before the first change.
#
#   hide_crosshair.ps1            hide the crosshairs
#   hide_crosshair.ps1 -Restore   put them back
#   -GameDir <path>               the game folder (holds elf and fs); default:
#                                 the folder this script is in
param(
    [switch]$Restore,
    [string]$GameDir = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'

$Size = 16384  # 128 x 128 DXT5: 1024 blocks of 16 bytes
$Textures = @{
    crosshair = @{
        Original = '0b6225e4de2b0bfdbca79cfa2f12e10ff3eb712c4d244c8170029fb55d501b97'
        Hidden   = '965b38caf460b14106b391f6ccb09b7fcf112594615c54dc8741737acf1347d2'
        Files    = [ordered]@{
            'minigameui.bnk'      = 0x44692a0
            'ENG_minigameui.bnk'  = 0x44b4bd0
            'CHN_minigameui.bnk'  = 0x448b950
            'intro_gun08.bnk'     = 0xbbc468
            'ENG_intro_gun08.bnk' = 0xbbd558
            'CHN_intro_gun08.bnk' = 0xbbada8
        }
    }
    label = @{
        Original = '452cdd6d8038bddced3ce5424f5d196d471c08e2dd119be66886e38a614d8a15'
        Hidden   = '0c00c0aeffcd09690372861d8c3b5c3f043d2d08042356e3a318fb51156d6b47'
        Files    = [ordered]@{
            'minigameui.bnk'     = 0x446d330
            'ENG_minigameui.bnk' = 0x44b8c60
            'CHN_minigameui.bnk' = 0x448f9e0
        }
    }
}

$test = Join-Path $GameDir 'fs\test'
if (-not (Test-Path -LiteralPath (Join-Path $test 'minigameui.bnk'))) {
    throw "No fs\test\minigameui.bnk under '$GameDir'. Put this script in the House of the Dead EX game folder (the one that holds elf and fs), or pass -GameDir."
}
$backup = Join-Path $test 'vrlf-crosshair-backup'
$sha = [System.Security.Cryptography.SHA256]::Create()

function Get-Hex([byte[]]$bytes) {
    (($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Read-At([string]$path, [long]$offset, [int]$count) {
    $fs = [System.IO.File]::Open($path, 'Open', 'Read', 'ReadWrite')
    try {
        $buf = New-Object byte[] $count
        $fs.Position = $offset
        $read = 0
        while ($read -lt $count) {
            $n = $fs.Read($buf, $read, $count - $read)
            if ($n -le 0) { throw "$path ends early" }
            $read += $n
        }
        return , $buf
    } finally { $fs.Dispose() }
}

function Write-At([string]$path, [long]$offset, [byte[]]$bytes) {
    try {
        $fs = [System.IO.File]::Open($path, 'Open', 'ReadWrite', 'None')
    } catch {
        throw "Can't write $path. Close the game and TeknoParrot, then try again."
    }
    try { $fs.Position = $offset; $fs.Write($bytes, 0, $bytes.Length) } finally { $fs.Dispose() }
}

# Pass 1 checks every file and plans the writes; any problem stops the script
# before a single byte changes.
$plan = @()
foreach ($name in 'crosshair', 'label') {
    $t = $Textures[$name]
    foreach ($file in $t.Files.Keys) {
        $path = Join-Path $test $file
        $header = [long]$t.Files[$file]
        $head = Read-At $path $header 128
        $tag = [System.Text.Encoding]::ASCII.GetString($head, 0, 4) + '/' +
               [System.Text.Encoding]::ASCII.GetString($head, 84, 4)
        if ($tag -ne 'DDS /DXT5') { throw "$file is not the version this script knows; nothing was changed." }
        $body = Read-At $path ($header + 128) $Size
        $hex = Get-Hex $body
        $saved = Join-Path $backup ("{0}.{1}.bin" -f $file, $name)
        $step = @{ Path = $path; Offset = $header + 128; Saved = $saved; Body = $body; Label = "$file ${name}" }

        if ($Restore) {
            if ($hex -eq $t.Original) { Write-Host "$($step.Label): already original"; continue }
            if ($hex -ne $t.Hidden) { throw "$($step.Label): not the texture this script hides; nothing was changed." }
            if (-not (Test-Path -LiteralPath $saved)) { throw "$($step.Label): no backup at $saved; nothing was changed." }
            $orig = [System.IO.File]::ReadAllBytes($saved)
            if ((Get-Hex $orig) -ne $t.Original) { throw "$saved is damaged; nothing was changed." }
            $step.New = $orig
            $step.Done = 'restored'
        } else {
            if ($hex -eq $t.Hidden) { Write-Host "$($step.Label): already hidden"; continue }
            if ($hex -ne $t.Original) { throw "$($step.Label): not the version this script knows; nothing was changed." }
            $hidden = [byte[]]$body.Clone()
            for ($i = 0; $i -lt $Size; $i += 16) {
                [Array]::Clear($hidden, $i, 8)  # alpha endpoints and indices: fully transparent
            }
            if ((Get-Hex $hidden) -ne $t.Hidden) { throw "internal check failed for $($step.Label); nothing was changed." }
            $step.New = $hidden
            $step.Done = 'hidden'
        }
        $plan += $step
    }
}

# Pass 2: save the originals, then write.
if (-not $Restore -and $plan.Count -gt 0) {
    New-Item -ItemType Directory -Force -Path $backup | Out-Null
    foreach ($step in $plan) {
        if (-not (Test-Path -LiteralPath $step.Saved)) { [System.IO.File]::WriteAllBytes($step.Saved, $step.Body) }
    }
}
foreach ($step in $plan) {
    Write-At $step.Path $step.Offset $step.New
    Write-Host "$($step.Label): $($step.Done)"
}
Write-Host ("Done: {0} change(s)." -f $plan.Count)

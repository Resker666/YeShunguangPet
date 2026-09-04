$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$SpritePath = Join-Path $ProjectRoot "Assets\spritesheet.png"
$ExpectedSpriteHash = "42FBB6129741A7468526AD0CAD27DF82EE3939BE140C768D3F431CC0C9A45C2D"

if (-not (Test-Path -LiteralPath $SpritePath)) {
    throw "Sprite sheet was not found: $SpritePath"
}

$ActualSpriteHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $SpritePath).Hash
if ($ActualSpriteHash -ne $ExpectedSpriteHash) {
    throw "Sprite sheet SHA256 mismatch. Expected $ExpectedSpriteHash, got $ActualSpriteHash."
}

Write-Host "Sprite sheet verified: $ActualSpriteHash"

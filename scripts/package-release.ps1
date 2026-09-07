param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$DotnetPath = "dotnet"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ProjectFile = Join-Path $ProjectRoot "YeShunguangPet.Wpf.csproj"

[xml]$ProjectXml = Get-Content -LiteralPath $ProjectFile
$VersionPropertyGroup = $ProjectXml.Project.PropertyGroup |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_.Version) } |
    Select-Object -First 1
$Version = [string]$VersionPropertyGroup.Version

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Project version is missing from YeShunguangPet.Wpf.csproj."
}

& (Join-Path $PSScriptRoot "publish-self-contained.ps1") `
    -Configuration $Configuration `
    -Runtime $Runtime `
    -DotnetPath $DotnetPath

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$PublishDir = Join-Path $ProjectRoot "bin\$Configuration\net8.0-windows\$Runtime\publish"
$Executable = Join-Path $PublishDir "YeShunguangPet.exe"
if (-not (Test-Path -LiteralPath $Executable)) {
    throw "Published executable was not found: $Executable"
}

$PublishedPet = Join-Path $PublishDir "Pets\YeShunguang"
$PublishedManifest = Join-Path $PublishedPet "pet.json"
$PublishedSprite = Join-Path $PublishedPet "spritesheet.png"
if (-not (Test-Path -LiteralPath $PublishedManifest) -or -not (Test-Path -LiteralPath $PublishedSprite)) {
    throw "Published default skin is missing. Do not distribute the EXE without Pets."
}
foreach ($Name in @("pet.json", "spritesheet.png")) {
    $SourceHash = (Get-FileHash -LiteralPath (Join-Path $ProjectRoot "Pets\YeShunguang\$Name")).Hash
    $PublishedHash = (Get-FileHash -LiteralPath (Join-Path $PublishedPet $Name)).Hash
    if ($SourceHash -ne $PublishedHash) { throw "Published skin differs from source: $Name" }
}

$ArtifactsDir = Join-Path $ProjectRoot "artifacts"
New-Item -ItemType Directory -Force -Path $ArtifactsDir | Out-Null

$ArchiveName = "YeShunguangPet-v$Version-$Runtime-portable.zip"
$ArchivePath = Join-Path $ArtifactsDir $ArchiveName
$ChecksumPath = Join-Path $ArtifactsDir "$ArchiveName.sha256.txt"
$PackageFiles = @(
    $Executable,
    (Join-Path $PublishDir "Pets"),
    (Join-Path $ProjectRoot "README.md"),
    (Join-Path $ProjectRoot "CHANGELOG.md"),
    (Join-Path $ProjectRoot "LICENSE"),
    (Join-Path $ProjectRoot "ASSET_NOTICE.md"),
    (Join-Path $ProjectRoot "docs")
)

Compress-Archive -LiteralPath $PackageFiles -DestinationPath $ArchivePath -CompressionLevel Optimal -Force

$Hash = Get-FileHash -Algorithm SHA256 -LiteralPath $ArchivePath
"$($Hash.Hash)  $ArchiveName" | Set-Content -LiteralPath $ChecksumPath -Encoding ascii

Write-Host "Release archive: $ArchivePath"
Write-Host "SHA256 file:    $ChecksumPath"

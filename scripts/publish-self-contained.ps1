param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$DotnetPath = "dotnet"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ProjectFile = Join-Path $ProjectRoot "YeShunguangPet.Wpf.csproj"
[xml]$ProjectXml = Get-Content -LiteralPath $ProjectFile
$PropertyGroup = $ProjectXml.Project.PropertyGroup |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_.TargetFramework) } |
    Select-Object -First 1
$TargetFramework = [string]$PropertyGroup.TargetFramework
if ([string]::IsNullOrWhiteSpace($TargetFramework)) { throw "Target framework is missing from YeShunguangPet.Wpf.csproj." }
$PublishDir = Join-Path $ProjectRoot "bin\$Configuration\$TargetFramework\$Runtime\publish"

& (Join-Path $PSScriptRoot "verify-assets.ps1")

& $DotnetPath publish $ProjectFile `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $PublishDir `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Published to: $PublishDir"

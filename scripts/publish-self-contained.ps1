param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$DotnetPath = "dotnet"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ProjectFile = Join-Path $ProjectRoot "YeShunguangPet.Wpf.csproj"

& $DotnetPath publish $ProjectFile `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$PublishDir = Join-Path $ProjectRoot "bin\$Configuration\net8.0-windows\$Runtime\publish"
Write-Host "Published to: $PublishDir"

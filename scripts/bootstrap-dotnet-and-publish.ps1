param(
    [string]$Channel = "8.0",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$DotnetRoot = "$env:LOCALAPPDATA\CodexDotnetSdk\8.0"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$DotnetExe = Join-Path $DotnetRoot "dotnet.exe"
$InstallScript = Join-Path $ProjectRoot ".dotnet-install.ps1"

if (-not (Test-Path -LiteralPath $DotnetExe)) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

    Write-Host "Downloading dotnet-install.ps1..."
    $InstallScriptUris = @(
        "https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.ps1",
        "https://dot.net/v1/dotnet-install.ps1"
    )

    $lastError = $null
    foreach ($uri in $InstallScriptUris) {
        try {
            Invoke-WebRequest $uri -OutFile $InstallScript -UseBasicParsing
            $lastError = $null
            break
        }
        catch {
            $lastError = $_
        }
    }

    if ($lastError -ne $null) {
        throw $lastError
    }

    Write-Host "Installing .NET SDK channel $Channel into $DotnetRoot..."
    & powershell -ExecutionPolicy Bypass -File $InstallScript `
        -Channel $Channel `
        -InstallDir $DotnetRoot `
        -Architecture x64
}

& $DotnetExe --info
& (Join-Path $PSScriptRoot "publish-self-contained.ps1") `
    -Configuration $Configuration `
    -Runtime $Runtime `
    -DotnetPath $DotnetExe

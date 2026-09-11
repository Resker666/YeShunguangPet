param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Path
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $ProjectRoot "tests\PetTests.csproj"

if (-not (Test-Path -LiteralPath $Path)) {
    throw "皮肤路径不存在：$Path"
}

dotnet run --project $Project --configuration Release -- --validate-skin (Resolve-Path -LiteralPath $Path).Path
exit $LASTEXITCODE

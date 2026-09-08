param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [int]$Seconds = 30,
    [int]$IntervalSeconds = 3,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
if ($Seconds -lt 1 -or $IntervalSeconds -lt 1) { throw 'Durations must be positive.' }
$process = Get-Process -Id $ProcessId
$version = $process.MainModule.FileVersionInfo.FileVersion
$path = $process.Path
$startCpu = $process.TotalProcessorTime.TotalSeconds
$watch = [Diagnostics.Stopwatch]::StartNew()
$samples = [Collections.Generic.List[object]]::new()
while ($watch.Elapsed.TotalSeconds -lt $Seconds) {
    $process.Refresh()
    if ($process.HasExited) { throw 'Observed process exited before measurement completed.' }
    $samples.Add([PSCustomObject]@{
        ElapsedSeconds = [math]::Round($watch.Elapsed.TotalSeconds, 3)
        CpuSeconds = $process.TotalProcessorTime.TotalSeconds
        WorkingSetMiB = [math]::Round($process.WorkingSet64 / 1MB, 2)
        PrivateMiB = [math]::Round($process.PrivateMemorySize64 / 1MB, 2)
        HandleCount = $process.HandleCount
    })
    Start-Sleep -Seconds $IntervalSeconds
}
$process.Refresh()
$elapsed = $watch.Elapsed.TotalSeconds
$result = [PSCustomObject]@{
    Version = $version
    Executable = $path
    ProcessId = $ProcessId
    CapturedAt = [DateTimeOffset]::Now.ToString('o')
    DurationSeconds = [math]::Round($elapsed, 3)
    CpuPercentOneCore = [math]::Round(100 * ($process.TotalProcessorTime.TotalSeconds - $startCpu) / $elapsed, 3)
    Note = 'Passive observation of the current user session; UI state is not controlled.'
    Samples = $samples.ToArray()
}
$fullPath = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($fullPath)) -Force | Out-Null
$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $fullPath -Encoding utf8
$result | Select-Object Version,DurationSeconds,CpuPercentOneCore,Note | Format-List

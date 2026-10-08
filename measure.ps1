#requires -Version 7.2
param([string]$DataDirectory = 'artifacts/performance-data')
$ErrorActionPreference = 'Stop'
$measurementRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $DataDirectory))
if (-not (Test-Path -LiteralPath (Join-Path $measurementRoot 'settings.json'))) { throw 'Use an initialized, disposable test data directory.' }
$measurementSettings = Get-Content -LiteralPath (Join-Path $measurementRoot 'settings.json') -Raw | ConvertFrom-Json
if (-not $measurementSettings.Initialized) { throw 'Complete onboarding for the disposable test directory first.' }
$readyPath = Join-Path $measurementRoot 'measurement-ready.json'
if (Test-Path -LiteralPath $readyPath) { Remove-Item -LiteralPath $readyPath }
$extractPath = Join-Path $PSScriptRoot ('artifacts/bundle-extract-' + [Guid]::NewGuid().ToString('N'))
[xml]$props=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Directory.Build.props') -Raw
$version=[string]$props.Project.PropertyGroup.Version
$info = [Diagnostics.ProcessStartInfo]::new((Join-Path $PSScriptRoot "dist/PromptFloat-$version-win-x64/PromptFloat.exe"))
$info.UseShellExecute = $false
$info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$info.WorkingDirectory = $PSScriptRoot
$info.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = $extractPath
$info.ArgumentList.Add('--measure')
$info.ArgumentList.Add('--data-dir')
$info.ArgumentList.Add($measurementRoot)
$startupClock = [Diagnostics.Stopwatch]::StartNew()
$measurementProcess = [Diagnostics.Process]::Start($info)
try {
    while (-not (Test-Path -LiteralPath $readyPath)) {
        if ($measurementProcess.HasExited -or $startupClock.Elapsed.TotalSeconds -gt 30) { throw 'No readiness signal received.' }
        Start-Sleep -Milliseconds 10
    }
    $startupClock.Stop()
    $ready = Get-Content -LiteralPath $readyPath -Raw | ConvertFrom-Json
    if ($ready.processId -ne $measurementProcess.Id) { throw 'Readiness signal belongs to another process.' }
    [ordered]@{ fullStartupMs=$startupClock.Elapsed.TotalMilliseconds; managedStartMs=$ready.managedStartMs; freshBundleExtraction=$true; osFileCacheCleared=$false } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'artifacts/startup-metrics.json') -Encoding utf8
    if (-not $measurementProcess.WaitForExit(90000)) { throw 'Idle measurement timed out.' }
    if ($measurementProcess.ExitCode -ne 0) { throw 'Idle measurement failed.' }
    Get-Content -LiteralPath (Join-Path $PSScriptRoot 'artifacts/startup-metrics.json') -Raw
    Get-Content -LiteralPath (Join-Path $PSScriptRoot 'artifacts/idle-metrics.json') -Raw
} finally {
    if (-not $measurementProcess.HasExited) { $measurementProcess.Kill($true) }
    $measurementProcess.Dispose()
}

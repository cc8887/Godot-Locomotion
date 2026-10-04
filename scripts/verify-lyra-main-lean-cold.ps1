[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineRoot,
      [Parameter(Mandatory=$true)][string]$UnrealProject,
      [ValidatePattern('^MainLeanCold_[A-Za-z0-9_]{1,48}$')][string]$TestName = 'MainLeanCold_20261001A')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$editor = Join-Path $EngineRoot 'Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
$plugin = Join-Path $root 'artifacts\unreal\gasp58-lyra-masks\package\AlsV4AssetExporter\AlsV4AssetExporter.uplugin'
$oldOutput = [Environment]::GetEnvironmentVariable('LYRA_OUTPUT_ROOT', 'Process')
$oldTestName = [Environment]::GetEnvironmentVariable('LYRA_MAIN_LEAN_COLD_NAME', 'Process')
try {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', (Join-Path $root 'assets\generated\lyra_als'), 'Process')
    [Environment]::SetEnvironmentVariable('LYRA_MAIN_LEAN_COLD_NAME', $TestName, 'Process')
    $script = Join-Path $root 'tools\unreal\verify_lyra_main_lean_cold.py'
    $result = & $editor $UnrealProject '-run=pythonscript' "-Script=$script" "-PLUGIN=$plugin" `
        '-DisablePlugins=AnimationData,ModelContextProtocol,Mocara' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1
    $exitCode = $LASTEXITCODE
    $result | Set-Content (Join-Path $root 'artifacts\lyra-analysis\main-lean-cold-ue-full.log') -Encoding utf8
    $markers = @($result | Where-Object { "$_" -match 'LYRA_MAIN_LEAN_COLD_OK new_targets=3 samples=24 position=0 quaternion=0 scale=0' })
    $nativeErrors = @($result | Where-Object { "$_" -match 'LogOutputDevice: Error:|LogPython: Error:|Ensure condition failed|Assertion failed' })
    if ($exitCode -ne 0 -or $markers.Count -ne 1 -or $nativeErrors.Count -ne 0) { throw "Main Lean cold verification failed ($exitCode).`n$($result | Select-Object -Last 45 | Out-String)" }
    $markers
} finally {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $oldOutput, 'Process')
    [Environment]::SetEnvironmentVariable('LYRA_MAIN_LEAN_COLD_NAME', $oldTestName, 'Process')
}

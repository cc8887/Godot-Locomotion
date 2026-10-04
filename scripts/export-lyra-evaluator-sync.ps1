[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineRoot,
      [Parameter(Mandatory=$true)][string]$UnrealProject,
      [switch]$NonLoop)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$editor = Join-Path $EngineRoot 'Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
$plugin = Join-Path $root 'artifacts\unreal\gasp58-lyra-masks\package\AlsV4AssetExporter\AlsV4AssetExporter.uplugin'
$script = Join-Path $root 'tools\unreal\export_lyra_evaluator_sync.py'
$oldOutput = [Environment]::GetEnvironmentVariable('LYRA_OUTPUT_ROOT', 'Process')
$oldNonloop = [Environment]::GetEnvironmentVariable('LYRA_EVALUATOR_NONLOOP', 'Process')
try {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', (Join-Path $root 'assets\generated\lyra_als'), 'Process')
    [Environment]::SetEnvironmentVariable('LYRA_EVALUATOR_NONLOOP', $(if ($NonLoop) { '1' } else { $null }), 'Process')
    $result = & $editor $UnrealProject '-run=pythonscript' "-Script=$script" "-PLUGIN=$plugin" `
        '-DisablePlugins=AnimationData,ModelContextProtocol,Mocara' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1
    $exitCode = $LASTEXITCODE
    $logName = if ($NonLoop) { 'evaluator-nonloop-ue-full.log' } else { 'evaluator-sync-ue-full.log' }
    $result | Set-Content (Join-Path $root "artifacts\lyra-analysis\$logName") -Encoding utf8
    $markers = @($result | Where-Object { "$_" -match 'LYRA_EVALUATOR_SYNC_NATIVE_OK traces=27 frames=4725 packages=489 assets_saved=0' })
    if ($exitCode -ne 0 -or $markers.Count -ne 1) { throw "Evaluator Sync export failed ($exitCode).`n$($result | Select-Object -Last 40 | Out-String)" }
    $markers
} finally {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $oldOutput, 'Process')
    [Environment]::SetEnvironmentVariable('LYRA_EVALUATOR_NONLOOP', $oldNonloop, 'Process')
}

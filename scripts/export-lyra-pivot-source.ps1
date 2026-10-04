[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineRoot,
      [Parameter(Mandatory=$true)][string]$UnrealProject,
      [switch]$Machine,
      [switch]$Reentry)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$editor=Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe'
$plugin=Join-Path $root 'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter/AlsV4AssetExporter.uplugin'
$script=Join-Path $root 'tools/unreal/export_lyra_pivot_source.py'
if ($Reentry -and -not $Machine) { throw 'Reentry capture requires -Machine' }
$captureName=if ($Reentry) { 'pivot-machine-reentry' } elseif ($Machine) { 'pivot-machine' } else { 'pivot-source' }
$markerName=if ($Reentry) { 'LYRA_PIVOT_MACHINE_REENTRY_NATIVE_OK' } elseif ($Machine) { 'LYRA_PIVOT_MACHINE_NATIVE_OK' } else { 'LYRA_PIVOT_SOURCE_NATIVE_OK' }
$log=Join-Path $root "artifacts/lyra-analysis/$captureName-ue-export.log"
$previous=[Environment]::GetEnvironmentVariable('LYRA_OUTPUT_ROOT','Process')
$previousMode=[Environment]::GetEnvironmentVariable('LYRA_PIVOT_MACHINE','Process')
$previousReentry=[Environment]::GetEnvironmentVariable('LYRA_PIVOT_REENTRY','Process')
try {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT',(Join-Path $root 'assets/generated/lyra_als'),'Process')
    [Environment]::SetEnvironmentVariable('LYRA_PIVOT_MACHINE',$(if ($Machine) { '1' } else { $null }),'Process')
    [Environment]::SetEnvironmentVariable('LYRA_PIVOT_REENTRY',$(if ($Reentry) { '1' } else { $null }),'Process')
    & $editor $UnrealProject '-run=pythonscript' "-Script=$script" "-PLUGIN=$plugin" `
        '-DisablePlugins=AnimationData,ModelContextProtocol,Mocara' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput *> $log
    $exitCode=$LASTEXITCODE
    $markers=@(Get-Content -LiteralPath $log | Where-Object { $_ -match "$markerName traces=9 frames=3780 packages=\d+ assets=36 assets_saved=0" })
    if ($exitCode -ne 0 -or $markers.Count -ne 1) { throw "Pivot source export failed ($exitCode). See $log" }
    Add-Content -LiteralPath $log -Value "LYRA_EXPORT_PROCESS_EXIT_OK mode=$captureName code=$exitCode"
    $markers
} finally {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT',$previous,'Process')
    [Environment]::SetEnvironmentVariable('LYRA_PIVOT_MACHINE',$previousMode,'Process')
    [Environment]::SetEnvironmentVariable('LYRA_PIVOT_REENTRY',$previousReentry,'Process')
}

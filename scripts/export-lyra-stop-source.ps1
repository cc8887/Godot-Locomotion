[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineRoot,
      [Parameter(Mandatory=$true)][string]$UnrealProject)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$editor=Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe'
$plugin=Join-Path $root 'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter/AlsV4AssetExporter.uplugin'
$script=Join-Path $root 'tools/unreal/export_lyra_stop_source.py'
$log=Join-Path $root 'artifacts/lyra-analysis/stop-source-ue-export.log'
$previous=[Environment]::GetEnvironmentVariable('LYRA_OUTPUT_ROOT','Process')
try {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT',(Join-Path $root 'assets/generated/lyra_als'),'Process')
    & $editor $UnrealProject '-run=pythonscript' "-Script=$script" "-PLUGIN=$plugin" `
        '-DisablePlugins=AnimationData,ModelContextProtocol,Mocara' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput *> $log
    $exitCode=$LASTEXITCODE
    $lines=Get-Content -LiteralPath $log
    $markers=@($lines | Where-Object { $_ -match 'LYRA_STOP_SOURCE_NATIVE_OK traces=9 frames=3780 packages=489 assets=36 assets_saved=0' })
    if ($exitCode -ne 0 -or $markers.Count -ne 1) { throw "Stop source export failed ($exitCode). See $log" }
    $markers
} finally { [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT',$previous,'Process') }

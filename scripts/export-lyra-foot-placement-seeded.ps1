param([Parameter(Mandatory=$true)][string]$EngineRoot,[Parameter(Mandatory=$true)][string]$UnrealProject,
      [ValidatePattern('^[a-zA-Z0-9_-]+\.log$')][string]$LogName='foot-placement-seeded-ue-export.log')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$editor=Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe'
$plugin=Join-Path $root 'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter/AlsV4AssetExporter.uplugin'
$script=Join-Path $root 'tools/unreal/export_lyra_foot_placement_seeded.py'
$log=Join-Path $root "artifacts/lyra-analysis/$LogName"
$previous=[Environment]::GetEnvironmentVariable('LYRA_OUTPUT_ROOT','Process')
try {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT',(Join-Path $root 'assets/generated/lyra_als'),'Process')
    & $editor $UnrealProject '-run=pythonscript' "-Script=$script" "-PLUGIN=$plugin" `
        '-DisablePlugins=AnimationData,ModelContextProtocol,Mocara' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput *> $log
    $exitCode=$LASTEXITCODE
    $markers=@(Get-Content -LiteralPath $log | Where-Object { $_ -match 'LYRA_FOOT_PLACEMENT_NATIVE_OK frames=3780 poses=\d+ previous=\d+ assets_saved=0' })
    if($exitCode -ne 0 -or $markers.Count -ne 1) { throw "FootPlacement export failed ($exitCode). See $log" }
    Add-Content -LiteralPath $log -Value "LYRA_EXPORT_PROCESS_EXIT_OK mode=foot-placement code=$exitCode"
    $markers
} finally { [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT',$previous,'Process') }

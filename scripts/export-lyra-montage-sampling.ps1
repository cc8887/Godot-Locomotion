param([Parameter(Mandatory=$true)][string]$EngineRoot,[Parameter(Mandatory=$true)][string]$UnrealProject,
      [ValidatePattern('^[a-zA-Z0-9_-]+\.log$')][string]$LogName='lyra-montage-sampling-ue.log')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$editor=Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe'
$plugin=Join-Path $root 'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter/AlsV4AssetExporter.uplugin'
$script=Join-Path $root 'tools/unreal/export_lyra_montage_sampling.py'
$log=Join-Path $root "artifacts/lyra-analysis/$LogName"
$previous=[Environment]::GetEnvironmentVariable('LYRA_OUTPUT_ROOT','Process')
try {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT',(Join-Path $root 'assets/generated/lyra_als'),'Process')
    & $editor $UnrealProject '-run=pythonscript' "-Script=$script" "-PLUGIN=$plugin" `
        '-DisablePlugins=ModelContextProtocol,Mocara' -AlsRawTrackDataModel -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput *> $log
    $taskExit=$LASTEXITCODE
    $markers=@(Get-Content -LiteralPath $log | Where-Object { $_ -match 'LYRA_MONTAGE_SAMPLING_NATIVE_OK frames=\d+ samples=\d+ roots=55 assets_saved=0' })
    if($taskExit -ne 0 -or $markers.Count -ne 1){throw "Montage sampling export failed ($taskExit). See $log"}
    Add-Content -LiteralPath $log -Value "LYRA_EXPORT_PROCESS_EXIT_OK mode=montage-sampling code=$taskExit"
    $markers
} finally {[Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT',$previous,'Process')}

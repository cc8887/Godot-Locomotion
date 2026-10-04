[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineRoot,
      [Parameter(Mandatory=$true)][string]$UnrealProject,
      [ValidatePattern('^[a-zA-Z0-9_-]+\.log$')][string]$LogName='notify-source-modes-ue-first.log')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$editor=Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe'
$plugin=Join-Path $root 'artifacts/unreal/gasp58-lyra-rig-target/package/AlsV4AssetExporter.uplugin'
$script=Join-Path $root 'tools/unreal/export_lyra_notify_source_modes.py'
$log=Join-Path $root "artifacts/lyra-analysis/$LogName"
if(Test-Path -LiteralPath $log){throw "Preserve export evidence: $log"}
$previous=[Environment]::GetEnvironmentVariable('LYRA_OUTPUT_ROOT','Process')
$projectHash=(Get-FileHash -LiteralPath $UnrealProject -Algorithm SHA256).Hash
try {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT',(Join-Path $root 'assets/generated/lyra_als'),'Process')
    & $editor $UnrealProject '-run=pythonscript' "-Script=$script" "-PLUGIN=$plugin" `
        '-DisablePlugins=ModelContextProtocol,Mocara' -AlsRawTrackDataModel -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput *> $log
    $taskExit=$LASTEXITCODE
    if((Get-FileHash -LiteralPath $UnrealProject -Algorithm SHA256).Hash -ne $projectHash){throw 'Project descriptor changed'}
    $markers=@(Get-Content -LiteralPath $log | Where-Object { $_ -match 'LYRA_NOTIFY_SOURCE_MODES_OK spaces=4 assets_saved=0' })
    if($taskExit -ne 0 -or $markers.Count -ne 1){throw "Notify source policy export failed ($taskExit). See $log"}
    Add-Content -LiteralPath $log -Value "LYRA_EXPORT_PROCESS_EXIT_OK mode=notify-source-modes code=$taskExit"
    $markers
} finally {[Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT',$previous,'Process')}

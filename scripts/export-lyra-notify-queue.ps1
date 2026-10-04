[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineRoot,
      [Parameter(Mandatory=$true)][string]$UnrealProject,
      [ValidatePattern('^[a-zA-Z0-9_-]+\.log$')][string]$LogName='notify-queue-ue-first.log')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$queueRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$queueEditor=Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe'
$queuePlugin=Join-Path $queueRoot 'artifacts/unreal/lyra-notify-oracle/package-sixth/LyraNotifyOracle.uplugin'
$queueScript=Join-Path $queueRoot 'tools/unreal/export_lyra_notify_queue.py'
$queueLog=Join-Path $queueRoot "artifacts/lyra-analysis/$LogName"
if(Test-Path -LiteralPath $queueLog){throw 'Preserve native queue evidence.'}
$queuePrevious=[Environment]::GetEnvironmentVariable('LYRA_OUTPUT_ROOT','Process')
$queuePreviousSource=[Environment]::GetEnvironmentVariable('LYRA_NOTIFY_PLUGIN_SOURCE','Process')
$queueProjectHash=(Get-FileHash -LiteralPath $UnrealProject -Algorithm SHA256).Hash
try {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT',(Join-Path $queueRoot 'assets/generated/lyra_als'),'Process')
    [Environment]::SetEnvironmentVariable('LYRA_NOTIFY_PLUGIN_SOURCE',(Join-Path $queueRoot 'tools/unreal/LyraNotifyOracle'),'Process')
    & $queueEditor $UnrealProject '-run=pythonscript' "-Script=$queueScript" "-PLUGIN=$queuePlugin" `
        '-DisablePlugins=ModelContextProtocol,Mocara' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput *> $queueLog
    $queueExit=$LASTEXITCODE
    if((Get-FileHash -LiteralPath $UnrealProject -Algorithm SHA256).Hash -ne $queueProjectHash){throw 'Project descriptor changed.'}
    $queueMarkers=@(Get-Content -LiteralPath $queueLog | Where-Object {$_ -match 'LYRA_NOTIFY_QUEUE_NATIVE_OK traces=10 .* linkedQueue=0 packages=676 previous=818 assets_saved=0'})
    if($queueExit -ne 0 -or $queueMarkers.Count -ne 1){throw "Native queue export failed ($queueExit). See $queueLog"}
    Add-Content -LiteralPath $queueLog -Value "LYRA_EXPORT_PROCESS_EXIT_OK mode=notify-queue code=$queueExit"
    $queueMarkers
} finally {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT',$queuePrevious,'Process')
    [Environment]::SetEnvironmentVariable('LYRA_NOTIFY_PLUGIN_SOURCE',$queuePreviousSource,'Process')
}

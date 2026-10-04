[CmdletBinding()]
param([string]$EngineRoot='../UE_5.8',
      [string]$UnrealProject='../GASP58/GASP58.uproject',
      [ValidatePattern('^[a-zA-Z0-9_-]+\.log$')][string]$LogName='notify-termination-windows-v1-native.log',
      [ValidatePattern('^[a-zA-Z0-9-]+$')][string]$Capture='notify-termination-windows-v1')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$montageRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$montageEditor=Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe'
$montagePlugin=Join-Path $montageRoot 'artifacts/unreal/lyra-montage-notify-oracle/package-fourth/LyraMontageNotifyOracle.uplugin'
$montageScript=Join-Path $montageRoot 'tools/unreal/capture_lyra_notify_termination_windows.py'
$montageLog=Join-Path $montageRoot "artifacts/lyra-analysis/$LogName"
if(Test-Path -LiteralPath $montageLog){throw 'Preserve native Montage queue evidence.'}
$montagePrevious=[Environment]::GetEnvironmentVariable('LYRA_OUTPUT_ROOT','Process')
$montagePreviousSource=[Environment]::GetEnvironmentVariable('LYRA_MONTAGE_NOTIFY_PLUGIN_SOURCE','Process')
$montagePreviousVersion=[Environment]::GetEnvironmentVariable('LYRA_MONTAGE_NOTIFY_FIXTURE_VERSION','Process')
$montagePreviousCapture=[Environment]::GetEnvironmentVariable('LYRA_NOTIFY_WINDOWS_CAPTURE','Process')
$montageProjectHash=(Get-FileHash -LiteralPath $UnrealProject -Algorithm SHA256).Hash
try {
    [Environment]::SetEnvironmentVariable('LYRA_NOTIFY_WINDOWS_CAPTURE',$Capture,'Process')
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT',(Join-Path $montageRoot 'assets/generated/lyra_als'),'Process')
    [Environment]::SetEnvironmentVariable('LYRA_MONTAGE_NOTIFY_PLUGIN_SOURCE',(Join-Path $montageRoot 'tools/unreal/LyraMontageNotifyOracle'),'Process')
    [Environment]::SetEnvironmentVariable('LYRA_MONTAGE_NOTIFY_FIXTURE_VERSION','v2','Process')
    & $montageEditor $UnrealProject '-run=pythonscript' "-Script=$montageScript" "-PLUGIN=$montagePlugin" `
        '-DisablePlugins=ModelContextProtocol,Mocara' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput *> $montageLog
    $montageExit=$LASTEXITCODE
    if((Get-FileHash -LiteralPath $UnrealProject -Algorithm SHA256).Hash -ne $montageProjectHash){throw 'Project descriptor changed.'}
    $montageMarkers=@(Get-Content -LiteralPath $montageLog | Where-Object {$_ -match 'LYRA_MONTAGE_NOTIFY_NATIVE_OK traces=15 .* assets=45 tracks=60 packages=676 previous=818 assets_saved=0'})
    if($montageExit -ne 0 -or $montageMarkers.Count -ne 1){throw "Native Montage queue export failed ($montageExit). See $montageLog"}
    Add-Content -LiteralPath $montageLog -Value "LYRA_EXPORT_PROCESS_EXIT_OK mode=montage-notify-queue code=$montageExit"
    $montageMarkers
} finally {
    [Environment]::SetEnvironmentVariable('LYRA_NOTIFY_WINDOWS_CAPTURE',$montagePreviousCapture,'Process')
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT',$montagePrevious,'Process')
    [Environment]::SetEnvironmentVariable('LYRA_MONTAGE_NOTIFY_PLUGIN_SOURCE',$montagePreviousSource,'Process')
    [Environment]::SetEnvironmentVariable('LYRA_MONTAGE_NOTIFY_FIXTURE_VERSION',$montagePreviousVersion,'Process')
}

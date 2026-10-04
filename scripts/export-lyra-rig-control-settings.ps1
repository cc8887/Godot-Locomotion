param([Parameter(Mandatory=$true)][string]$EngineRoot,
      [Parameter(Mandatory=$true)][string]$UnrealProject,
      [ValidatePattern('^[a-zA-Z0-9_-]+\.log$')][string]$LogName='lyra-rig-control-settings-ue.log')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$rigRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$rigEditor=Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe'
$rigPlugin=Join-Path $rigRoot 'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter/AlsV4AssetExporter.uplugin'
$rigScript=Join-Path $rigRoot 'tools/unreal/export_lyra_rig_control_settings.py'
$rigLog=Join-Path $rigRoot "artifacts/lyra-analysis/$LogName"
$rigPrevious=[Environment]::GetEnvironmentVariable('LYRA_OUTPUT_ROOT','Process')
try{
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT',(Join-Path $rigRoot 'assets/generated/lyra_als'),'Process')
    & $rigEditor $UnrealProject '-run=pythonscript' "-Script=$rigScript" "-PLUGIN=$rigPlugin" '-DisablePlugins=ModelContextProtocol,Mocara' -AlsRawTrackDataModel -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput *> $rigLog
    $rigExit=$LASTEXITCODE
    $rigMarkers=@(Get-Content -LiteralPath $rigLog | Where-Object {$_ -match 'LYRA_RIG_CONTROL_SETTINGS_NATIVE_OK controls=7 cdoMatch=true assets_saved=0'})
    if($rigExit -ne 0 -or $rigMarkers.Count -ne 1){throw "Control settings export failed ($rigExit). See $rigLog"}
    Add-Content -LiteralPath $rigLog -Value "LYRA_EXPORT_PROCESS_EXIT_OK mode=rig-control-settings code=$rigExit"
    $rigMarkers
}finally{[Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT',$rigPrevious,'Process')}

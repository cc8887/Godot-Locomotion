param([string]$EngineRoot=$env:UE_ENGINE_ROOT,
      [string]$UnrealProject=$env:LYRA_UE_PROJECT_FILE,
      [ValidatePattern('^[a-zA-Z0-9-]+$')][string]$RunTag='default-layer-runtime-v1')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'common/LocomotionPaths.ps1')
$EngineRoot = Resolve-LocomotionPath -Path $EngineRoot -EnvironmentVariable 'UE_ENGINE_ROOT' -Fallback '../UE_5.8'
$UnrealProject = Resolve-LocomotionPath -Path $UnrealProject -EnvironmentVariable 'LYRA_UE_PROJECT_FILE' -Fallback '../GASP58/GASP58.uproject'

$defaultRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$defaultLog=Join-Path $defaultRoot "artifacts/lyra-analysis/$RunTag-export.log"
$defaultPlugin=Join-Path $defaultRoot 'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter/AlsV4AssetExporter.uplugin'
if(Test-Path -LiteralPath $defaultLog){throw 'Preserve the export log.'}
if(@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'UnrealEditor*' -and $_.CommandLine -and $_.CommandLine.Contains('GASP58')}).Count){throw 'Project Editor is running.'}
$defaultPriorRoot=[Environment]::GetEnvironmentVariable('LYRA_OUTPUT_ROOT','Process')
$defaultPriorTag=[Environment]::GetEnvironmentVariable('LYRA_DEFAULT_GRAPH_TAG','Process')
try{
 [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT',(Join-Path $defaultRoot 'assets/generated/lyra_als'),'Process')
 [Environment]::SetEnvironmentVariable('LYRA_DEFAULT_GRAPH_TAG',$RunTag,'Process')
 & (Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe') $UnrealProject -run=pythonscript "-script=$(Join-Path $defaultRoot 'tools/unreal/export_lyra_default_layer_graphs.py')" "-PLUGIN=$defaultPlugin" '-DisablePlugins=AnimationData,ModelContextProtocol,Mocara' -nullrhi -unattended -nop4 -nosplash -NoSound -stdout -FullStdOutLogOutput *> $defaultLog
 $defaultExit=$LASTEXITCODE
 Add-Content -LiteralPath $defaultLog "LYRA_DEFAULT_GRAPH_EXPORT_EXIT=$defaultExit"
 $defaultText=Get-Content -LiteralPath $defaultLog -Raw
 if($defaultExit -ne 0 -or !$defaultText.Contains('LYRA_DEFAULT_LAYER_GRAPHS_OK roots=14') -or $defaultText -match 'Error:|Fatal error:|Ensure condition failed'){throw 'Default graph export failed.'}
 Write-Output "Default graph export passed: $RunTag"
}finally{
 [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT',$defaultPriorRoot,'Process')
 [Environment]::SetEnvironmentVariable('LYRA_DEFAULT_GRAPH_TAG',$defaultPriorTag,'Process')
}

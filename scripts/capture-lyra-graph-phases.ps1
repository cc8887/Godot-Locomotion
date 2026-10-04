param([string]$EngineRoot='../UE_5.8',
      [string]$UnrealProject='../GASP58/GASP58.uproject',
      [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$PackageName='package-graph-phases-v1',
      [ValidatePattern('^[a-zA-Z0-9-]+$')][string]$RunTag='graph-phases-v1')
$ErrorActionPreference='Stop'
$phaseRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$phaseLog=Join-Path $phaseRoot "artifacts/lyra-analysis/$RunTag-native.log"
$phasePlugin=Join-Path $phaseRoot "artifacts/unreal/lyra-whole-main-oracle/$PackageName/LyraWholeMainOracle.uplugin"
if(Test-Path -LiteralPath $phaseLog){throw 'Preserve graph phase log.'}
if(@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'UnrealEditor*' -and $_.CommandLine -and $_.CommandLine.Contains('GASP58')}).Count){throw 'Project Editor is running.'}
$phasePrior=[Environment]::GetEnvironmentVariable('LYRA_GRAPH_PHASE_TAG','Process')
try{
 [Environment]::SetEnvironmentVariable('LYRA_GRAPH_PHASE_TAG',$RunTag,'Process')
 & (Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe') $UnrealProject -run=pythonscript "-script=$(Join-Path $phaseRoot 'tools/unreal/capture_lyra_graph_phases.py')" "-PLUGIN=$phasePlugin" '-DisablePlugins=ModelContextProtocol,Mocara' -nullrhi -unattended -nop4 -nosplash -NoSound -stdout -FullStdOutLogOutput *> $phaseLog
 $phaseExit=$LASTEXITCODE
 Add-Content -LiteralPath $phaseLog "LYRA_GRAPH_PHASE_PROCESS_EXIT=$phaseExit"
 $phaseText=Get-Content -LiteralPath $phaseLog -Raw
 if($phaseExit -ne 0 -or !$phaseText.Contains('LYRA_GRAPH_PHASE_NATIVE_OK') -or $phaseText -match 'Error:|Fatal error:|Ensure condition failed|LYRA_GRAPH_PHASE_FAILED'){throw 'Native graph phase capture failed.'}
 Write-Output "Graph phases native capture passed: $RunTag"
}finally{[Environment]::SetEnvironmentVariable('LYRA_GRAPH_PHASE_TAG',$phasePrior,'Process')}

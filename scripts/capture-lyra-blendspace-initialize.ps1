param([string]$EngineRoot='../UE_5.8',
      [string]$UnrealProject='../GASP58/GASP58.uproject',
      [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$PackageName='package-blendspace-initialize-v1',
      [ValidatePattern('^[a-zA-Z0-9-]+$')][string]$RunTag='blendspace-initialize-v1')
$ErrorActionPreference='Stop'
$phaseRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$phaseLog=Join-Path $phaseRoot "artifacts/lyra-analysis/$RunTag-native.log"
$phasePlugin=Join-Path $phaseRoot "artifacts/unreal/lyra-whole-main-oracle/$PackageName/LyraWholeMainOracle.uplugin"
if(Test-Path -LiteralPath $phaseLog){throw 'Preserve graph phase log.'}
if(@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'UnrealEditor*' -and $_.CommandLine -and $_.CommandLine.Contains('GASP58')}).Count){throw 'Project Editor is running.'}
$phasePrior=[Environment]::GetEnvironmentVariable('LYRA_BLENDSPACE_INITIALIZE_TAG','Process')
try{
 [Environment]::SetEnvironmentVariable('LYRA_BLENDSPACE_INITIALIZE_TAG',$RunTag,'Process')
 & (Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe') $UnrealProject -run=pythonscript "-script=$(Join-Path $phaseRoot 'tools/unreal/capture_lyra_blendspace_initialize.py')" "-PLUGIN=$phasePlugin" '-DisablePlugins=ModelContextProtocol,Mocara' -nullrhi -unattended -nop4 -nosplash -NoSound -stdout -FullStdOutLogOutput *> $phaseLog
 $phaseExit=$LASTEXITCODE
 Add-Content -LiteralPath $phaseLog "LYRA_BLENDSPACE_INITIALIZE_PROCESS_EXIT=$phaseExit"
 $phaseText=Get-Content -LiteralPath $phaseLog -Raw
 if($phaseExit -ne 0 -or !$phaseText.Contains('LYRA_BLENDSPACE_INITIALIZE_NATIVE_OK') -or $phaseText -match 'Error:|Fatal error:|Ensure condition failed|LYRA_BLENDSPACE_INITIALIZE_FAILED'){throw 'Native graph phase capture failed.'}
 Write-Output "Graph phases native capture passed: $RunTag"
}finally{[Environment]::SetEnvironmentVariable('LYRA_BLENDSPACE_INITIALIZE_TAG',$phasePrior,'Process')}

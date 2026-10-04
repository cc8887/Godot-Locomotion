param([string]$EngineRoot=$env:UE_ENGINE_ROOT,
      [string]$UnrealProject=$env:LYRA_UE_PROJECT_FILE,
      [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$PackageName='package-default-main-v1',
      [ValidatePattern('^[a-zA-Z0-9-]+$')][string]$RunTag='default-main-v1')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'common/LocomotionPaths.ps1')
$EngineRoot = Resolve-LocomotionPath -Path $EngineRoot -EnvironmentVariable 'UE_ENGINE_ROOT' -Fallback '../UE_5.8'
$UnrealProject = Resolve-LocomotionPath -Path $UnrealProject -EnvironmentVariable 'LYRA_UE_PROJECT_FILE' -Fallback '../GASP58/GASP58.uproject'

$defaultRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$defaultLog=Join-Path $defaultRoot "artifacts/lyra-analysis/$RunTag-native.log"
$defaultPlugin=Join-Path $defaultRoot "artifacts/unreal/lyra-whole-main-oracle/$PackageName/LyraWholeMainOracle.uplugin"
if(Test-Path -LiteralPath $defaultLog){throw 'Preserve native log.'}
if(@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'UnrealEditor*' -and $_.CommandLine -and $_.CommandLine.Contains('GASP58')}).Count){throw 'Project Editor is running.'}
$defaultPrior=[Environment]::GetEnvironmentVariable('LYRA_DEFAULT_MAIN_TAG','Process')
try{
 [Environment]::SetEnvironmentVariable('LYRA_DEFAULT_MAIN_TAG',$RunTag,'Process')
 & (Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe') $UnrealProject -run=pythonscript "-script=$(Join-Path $defaultRoot 'tools/unreal/capture_lyra_default_main.py')" "-PLUGIN=$defaultPlugin" '-DisablePlugins=ModelContextProtocol,Mocara' -nullrhi -unattended -nop4 -nosplash -NoSound -stdout -FullStdOutLogOutput *> $defaultLog
 $defaultExit=$LASTEXITCODE
 Add-Content -LiteralPath $defaultLog "LYRA_DEFAULT_MAIN_PROCESS_EXIT=$defaultExit"
 $defaultText=Get-Content -LiteralPath $defaultLog -Raw
 if($defaultExit -ne 0 -or !$defaultText.Contains('LYRA_DEFAULT_MAIN_NATIVE_OK') -or $defaultText -match 'Error:|Fatal error:|Ensure condition failed|LYRA_DEFAULT_MAIN_FAILED'){throw 'Native default Main capture failed.'}
 Write-Output "Default Main native capture passed: $RunTag"
}finally{[Environment]::SetEnvironmentVariable('LYRA_DEFAULT_MAIN_TAG',$defaultPrior,'Process')}

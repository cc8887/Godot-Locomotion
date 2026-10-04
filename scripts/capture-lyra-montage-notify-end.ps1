param([string]$EngineRoot=$env:UE_ENGINE_ROOT,
      [string]$UnrealProject=$env:LYRA_UE_PROJECT_FILE,
      [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$PackageName='package-notify-termination-v4',
      [ValidatePattern('^[a-zA-Z0-9-]+$')][string]$RunTag='notify-termination-v3')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'common/LocomotionPaths.ps1')
$EngineRoot = Resolve-LocomotionPath -Path $EngineRoot -EnvironmentVariable 'UE_ENGINE_ROOT' -Fallback '../UE_5.8'
$UnrealProject = Resolve-LocomotionPath -Path $UnrealProject -EnvironmentVariable 'LYRA_UE_PROJECT_FILE' -Fallback '../GASP58/GASP58.uproject'

$delegateRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$delegateLog=Join-Path $delegateRoot "artifacts/lyra-analysis/$RunTag-native.log"
$delegatePlugin=Join-Path $delegateRoot "artifacts/unreal/lyra-whole-main-oracle/$PackageName/LyraWholeMainOracle.uplugin"
if(Test-Path -LiteralPath $delegateLog){throw 'Preserve native log.'}
if(@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'UnrealEditor*' -and $_.CommandLine -and $_.CommandLine.Contains('GASP58')}).Count){throw 'Project Editor is running.'}
$delegatePrior=[Environment]::GetEnvironmentVariable('LYRA_NOTIFY_TERMINATION_TAG','Process')
try{
 [Environment]::SetEnvironmentVariable('LYRA_NOTIFY_TERMINATION_TAG',$RunTag,'Process')
 & (Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe') $UnrealProject -run=pythonscript "-script=$(Join-Path $delegateRoot 'tools/unreal/capture_lyra_montage_notify_end.py')" "-PLUGIN=$delegatePlugin" '-DisablePlugins=ModelContextProtocol,Mocara' -nullrhi -unattended -nop4 -nosplash -NoSound -stdout -FullStdOutLogOutput *> $delegateLog
 $delegateExit=$LASTEXITCODE
 Add-Content -LiteralPath $delegateLog "MONTAGE_DELEGATES_PROCESS_EXIT=$delegateExit"
 $delegateText=Get-Content -LiteralPath $delegateLog -Raw
 if($delegateExit -ne 0 -or !$delegateText.Contains('LYRA_NOTIFY_TERMINATION_NATIVE_OK') -or $delegateText -match 'Error:|Fatal error:|Ensure condition failed|LYRA_NOTIFY_END_FAILED'){throw 'Native delegate capture failed.'}
 Write-Output "Montage delegate native capture passed: $RunTag"
}finally{[Environment]::SetEnvironmentVariable('LYRA_NOTIFY_TERMINATION_TAG',$delegatePrior,'Process')}

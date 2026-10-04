param([string]$EngineRoot='../UE_5.8',
      [string]$UnrealProject='../GASP58/GASP58.uproject',
      [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$PackageName='package-layer-fallback-v4',
      [ValidatePattern('^[a-zA-Z0-9-]+$')][string]$RunTag='layer-fallback-v4')
$ErrorActionPreference='Stop'
$delegateRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$delegateLog=Join-Path $delegateRoot "artifacts/lyra-analysis/$RunTag-native.log"
$delegatePlugin=Join-Path $delegateRoot "artifacts/unreal/lyra-whole-main-oracle/$PackageName/LyraWholeMainOracle.uplugin"
if(Test-Path -LiteralPath $delegateLog){throw 'Preserve native log.'}
if(@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'UnrealEditor*' -and $_.CommandLine -and $_.CommandLine.Contains('GASP58')}).Count){throw 'Project Editor is running.'}
$delegatePrior=[Environment]::GetEnvironmentVariable('LYRA_LAYER_FALLBACK_TAG','Process')
try{
 [Environment]::SetEnvironmentVariable('LYRA_LAYER_FALLBACK_TAG',$RunTag,'Process')
 & (Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe') $UnrealProject -run=pythonscript "-script=$(Join-Path $delegateRoot 'tools/unreal/capture_lyra_layer_fallback.py')" "-PLUGIN=$delegatePlugin" '-DisablePlugins=ModelContextProtocol,Mocara' -nullrhi -unattended -nop4 -nosplash -NoSound -stdout -FullStdOutLogOutput *> $delegateLog
 $delegateExit=$LASTEXITCODE
 Add-Content -LiteralPath $delegateLog "LYRA_LAYER_FALLBACK_PROCESS_EXIT=$delegateExit"
 $delegateText=Get-Content -LiteralPath $delegateLog -Raw
 if($delegateExit -ne 0 -or !$delegateText.Contains('LYRA_LAYER_FALLBACK_NATIVE_OK') -or $delegateText -match 'Error:|Fatal error:|Ensure condition failed|LYRA_LAYER_FALLBACK_FAILED'){throw 'Native layer fallback capture failed.'}
 Write-Output "Layer fallback native capture passed: $RunTag"
}finally{[Environment]::SetEnvironmentVariable('LYRA_LAYER_FALLBACK_TAG',$delegatePrior,'Process')}

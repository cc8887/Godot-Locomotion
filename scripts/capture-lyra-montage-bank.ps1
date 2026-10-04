param([string]$EngineRoot='../UE_5.8',
      [string]$UnrealProject='../GASP58/GASP58.uproject',
      [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$PackageName='package-montage-bank-callbacks-v1',
      [ValidatePattern('^[a-zA-Z0-9-]+$')][string]$RunTag='montage-bank-callbacks-v1')
$ErrorActionPreference='Stop'
$delegateRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$delegateLog=Join-Path $delegateRoot "artifacts/lyra-analysis/$RunTag-native.log"
$delegatePlugin=Join-Path $delegateRoot "artifacts/unreal/lyra-whole-main-oracle/$PackageName/LyraWholeMainOracle.uplugin"
if(Test-Path -LiteralPath $delegateLog){throw 'Preserve native log.'}
if(@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'UnrealEditor*' -and $_.CommandLine -and $_.CommandLine.Contains('GASP58')}).Count){throw 'Project Editor is running.'}
$delegatePrior=[Environment]::GetEnvironmentVariable('LYRA_MONTAGE_BANK_TAG','Process')
try{
 [Environment]::SetEnvironmentVariable('LYRA_MONTAGE_BANK_TAG',$RunTag,'Process')
 & (Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe') $UnrealProject -run=pythonscript "-script=$(Join-Path $delegateRoot 'tools/unreal/capture_lyra_montage_bank.py')" "-PLUGIN=$delegatePlugin" '-DisablePlugins=ModelContextProtocol,Mocara' -nullrhi -unattended -nop4 -nosplash -NoSound -stdout -FullStdOutLogOutput *> $delegateLog
 $delegateExit=$LASTEXITCODE
 Add-Content -LiteralPath $delegateLog "MONTAGE_DELEGATES_PROCESS_EXIT=$delegateExit"
 $delegateText=Get-Content -LiteralPath $delegateLog -Raw
 if($delegateExit -ne 0 -or !$delegateText.Contains('LYRA_MONTAGE_BANK_NATIVE_OK') -or $delegateText -match 'Error:|Fatal error:|Ensure condition failed|LYRA_MONTAGE_BANK_FAILED'){throw 'Native delegate capture failed.'}
 Write-Output "Montage delegate native capture passed: $RunTag"
}finally{[Environment]::SetEnvironmentVariable('LYRA_MONTAGE_BANK_TAG',$delegatePrior,'Process')}

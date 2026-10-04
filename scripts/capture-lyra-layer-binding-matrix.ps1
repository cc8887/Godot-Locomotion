param([Parameter(Mandatory=$true)][ValidatePattern('^[a-zA-Z0-9-]+$')][string]$RunTag,
      [Parameter(Mandatory=$true)][ValidatePattern('^[a-zA-Z0-9-]+$')][string]$PackageName)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$layerRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$layerPackage=Join-Path $layerRoot "artifacts/unreal/lyra-whole-main-oracle/$PackageName"
$layerProject='../GASP58/GASP58.uproject'
$layerLog=Join-Path $layerRoot "artifacts/lyra-analysis/$RunTag-ue.log"
if(Test-Path -LiteralPath $layerLog){throw 'Preserve previous capture log.'}
$layerLive=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'UnrealEditor*' -and $_.CommandLine -and $_.CommandLine.Replace('\','/').Contains('../GASP58')})
if($layerLive.Count){throw 'Project Editor is already running.'}
$layerOldTag=[Environment]::GetEnvironmentVariable('LYRA_LAYER_BINDING_TAG','Process')
$layerOldPackage=[Environment]::GetEnvironmentVariable('LYRA_LAYER_BINDING_PACKAGE','Process')
try{
    [Environment]::SetEnvironmentVariable('LYRA_LAYER_BINDING_TAG',$RunTag,'Process')
    [Environment]::SetEnvironmentVariable('LYRA_LAYER_BINDING_PACKAGE',$layerPackage,'Process')
    & '../UE_5.8/Engine/Binaries/Win64/UnrealEditor-Cmd.exe' $layerProject -run=pythonscript `
        "-Script=$(Join-Path $layerRoot 'tools/unreal/capture_lyra_layer_binding_matrix.py')" "-PLUGIN=$(Join-Path $layerPackage 'LyraWholeMainOracle.uplugin')" `
        '-DisablePlugins=ModelContextProtocol,Mocara' -ModelContextProtocolPort=58081 -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput *> $layerLog
    $layerExit=$LASTEXITCODE
    Add-Content -LiteralPath $layerLog -Value "LAYER_BINDING_PROCESS_EXIT=$layerExit" -Encoding utf8
    if($layerExit -ne 0){throw 'Native binding matrix capture failed.'}
    if(!(Get-Content -LiteralPath $layerLog -Raw).Contains('LYRA_LAYER_BINDING_MATRIX_NATIVE_OK')){throw 'Native binding capture omitted success.'}
}finally{
    [Environment]::SetEnvironmentVariable('LYRA_LAYER_BINDING_TAG',$layerOldTag,'Process')
    [Environment]::SetEnvironmentVariable('LYRA_LAYER_BINDING_PACKAGE',$layerOldPackage,'Process')
}

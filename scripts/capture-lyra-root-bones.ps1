param([Parameter(Mandatory=$true)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$RunTag,
      [Parameter(Mandatory=$true)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$PackageName,
      [string]$EngineRoot=$env:UE_ENGINE_ROOT,
      [string]$UnrealProject=$env:LYRA_UE_PROJECT_FILE)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'common/LocomotionPaths.ps1')
$EngineRoot = Resolve-LocomotionPath -Path $EngineRoot -EnvironmentVariable 'UE_ENGINE_ROOT' -Fallback '../UE_5.8'
$UnrealProject = Resolve-LocomotionPath -Path $UnrealProject -EnvironmentVariable 'LYRA_UE_PROJECT_FILE' -Fallback '../GASP58/GASP58.uproject'

Set-StrictMode -Version Latest
$startupRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$startupProject=[IO.Path]::GetFullPath($UnrealProject)
$startupPackage=Join-Path $startupRoot "artifacts/unreal/lyra-root-bone-oracle/$PackageName"
$startupProbe=Join-Path $startupPackage 'LyraRootBoneOracle.uplugin'
$startupLog=Join-Path $startupRoot "artifacts/lyra-analysis/$RunTag-native.log"
foreach($startupPath in @($startupLog,(Join-Path $startupRoot "artifacts/lyra-analysis/$RunTag-requests.json"))){if(Test-Path -LiteralPath $startupPath){throw 'Preserve existing startup capture.'}}
if(!(Test-Path -LiteralPath $startupProbe)){throw 'Missing built startup probe.'}
if(@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'UnrealEditor*' -and $_.CommandLine -and $_.CommandLine.Contains((Split-Path $startupProject -Parent))}).Count){throw 'Project Editor is already running.'}
$startupSource=Join-Path $startupRoot 'tools/unreal/LyraRootBoneOracle'
foreach($startupFile in Get-ChildItem -LiteralPath $startupSource -Recurse -File){
 $startupRelative=[IO.Path]::GetRelativePath($startupSource,$startupFile.FullName)
 if((Get-FileHash -LiteralPath $startupFile.FullName -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath (Join-Path $startupPackage $startupRelative) -Algorithm SHA256).Hash){throw 'Probe package source differs.'}
}
$startupPrevious=[Environment]::GetEnvironmentVariable('LYRA_ROOT_BONES_TAG','Process')
try{
 [Environment]::SetEnvironmentVariable('LYRA_ROOT_BONES_TAG',$RunTag,'Process')
 & (Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe') $startupProject -run=pythonscript "-Script=$(Join-Path $startupRoot 'tools/unreal/capture_lyra_root_bones.py')" "-PLUGIN=$startupProbe" '-DisablePlugins=ModelContextProtocol,Mocara' -ModelContextProtocolPort=58081 -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput *> $startupLog
 $startupExit=$LASTEXITCODE
 Add-Content -LiteralPath $startupLog -Value "LYRA_ROOT_BONES_PROCESS_EXIT=$startupExit" -Encoding utf8
 if($startupExit -ne 0){throw 'Native startup process failed.'}
 $startupText=Get-Content -LiteralPath $startupLog -Raw -Encoding utf8
 if(!$startupText.Contains('LYRA_ROOT_BONES_NATIVE_OK') -or $startupText -match 'Error:|Fatal error:|Ensure condition failed'){throw 'Native startup capture failed validation.'}
}finally{[Environment]::SetEnvironmentVariable('LYRA_ROOT_BONES_TAG',$startupPrevious,'Process')}

param([Parameter(Mandatory=$true)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$RunTag,
      [Parameter(Mandatory=$true)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$PackageName,
      [ValidateSet(30,60,120)][int]$Hz=60,
      [ValidateSet('movement','turn','actions','rebind','physics','multi-layer')][string]$Case='actions',
      [ValidateRange(0,100000)][int]$FrameLimit=0,
      [string]$EngineRoot=$env:UE_ENGINE_ROOT,
      [string]$UnrealProject=$env:LYRA_UE_PROJECT_FILE)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'common/LocomotionPaths.ps1')
$EngineRoot = Resolve-LocomotionPath -Path $EngineRoot -EnvironmentVariable 'UE_ENGINE_ROOT' -Fallback '../UE_5.8'
$UnrealProject = Resolve-LocomotionPath -Path $UnrealProject -EnvironmentVariable 'LYRA_UE_PROJECT_FILE' -Fallback '../GASP58/GASP58.uproject'

Set-StrictMode -Version Latest
$captureRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$captureProject=[IO.Path]::GetFullPath($UnrealProject)
$capturePackage=Join-Path $captureRoot "artifacts/unreal/lyra-whole-main-oracle/$PackageName"
$captureProbe=Join-Path $capturePackage 'LyraWholeMainOracle.uplugin'
$captureExporter=Join-Path $captureRoot 'artifacts/unreal/gasp58-lyra-rig-reference/package-ready/AlsV4AssetExporter/AlsV4AssetExporter.uplugin'
$captureLog=Join-Path $captureRoot "artifacts/lyra-analysis/whole-main-native-$RunTag.log"
$captureRequest=Join-Path $captureRoot "artifacts/lyra-analysis/whole-main-$RunTag-request.json"
$captureLaunch=Join-Path $captureRoot "artifacts/lyra-analysis/whole-main-$RunTag-launch.json"
foreach($capturePath in @($captureLog,$captureRequest,$captureLaunch)){
    if(Test-Path -LiteralPath $capturePath){throw 'Preserve existing capture evidence.'}
}
foreach($capturePath in @($captureProbe,$captureExporter)){
    if(-not (Test-Path -LiteralPath $capturePath)){throw 'Missing capture plugin package.'}
}
$captureLive=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'UnrealEditor*' -and $_.CommandLine -and $_.CommandLine.Contains((Split-Path $captureProject -Parent))})
if($captureLive.Count){throw 'Project Editor is already running.'}
$captureSource=Join-Path $captureRoot 'tools/unreal/LyraWholeMainOracle/Source'
$captureHashes=@{}
foreach($captureFile in Get-ChildItem -LiteralPath $captureSource -File -Recurse){
    $captureRelative=[IO.Path]::GetRelativePath($captureSource,$captureFile.FullName)
    $captureMirror=Join-Path (Join-Path $capturePackage 'Source') $captureRelative
    $captureHash=(Get-FileHash -LiteralPath $captureFile.FullName -Algorithm SHA256).Hash
    if((Get-FileHash -LiteralPath $captureMirror -Algorithm SHA256).Hash -ne $captureHash){throw 'Probe package source differs; build a new package.'}
    $captureHashes[$captureRelative]=$captureHash
}
@{runTag=$RunTag;hz=$Hz;case=$Case;frameLimit=$FrameLimit;project=$captureProject;package=$capturePackage;sourceSha256=$captureHashes;animationDataEnabled=$true} |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $captureLaunch -Encoding utf8
$captureVariables=@{LYRA_WHOLE_RUN_TAG=$RunTag;LYRA_WHOLE_HZ=[string]$Hz;LYRA_WHOLE_FRAME_LIMIT=[string]$FrameLimit;LYRA_WHOLE_CASE=$Case}
$capturePrevious=@{}
try{
    foreach($captureKey in $captureVariables.Keys){$capturePrevious[$captureKey]=[Environment]::GetEnvironmentVariable($captureKey,'Process');[Environment]::SetEnvironmentVariable($captureKey,$captureVariables[$captureKey],'Process')}
    # AnimationData is required by the original Sequencer-backed Emote assets.
    # The exporter prefers raw models only for newly constructed transient data.
    & (Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe') $captureProject -run=pythonscript `
        "-Script=$(Join-Path $captureRoot 'tools/unreal/capture_lyra_linked_private_v2.py')" "-PLUGIN=$captureExporter" "-PLUGIN=$captureProbe" `
        '-DisablePlugins=ModelContextProtocol,Mocara' -ModelContextProtocolPort=58081 -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput *> $captureLog
    $captureExit=$LASTEXITCODE
    Add-Content -LiteralPath $captureLog -Value "WHOLE_MAIN_PROCESS_EXIT=$captureExit" -Encoding utf8
    if($captureExit -ne 0){throw "Native capture failed; preserve $captureLog"}
    $captureText=Get-Content -LiteralPath $captureLog -Raw -Encoding utf8
    if(-not $captureText.Contains('LYRA_WHOLE_MAIN_CAPTURE_OK')){throw 'Native process omitted capture success.'}
}finally{
    foreach($captureKey in $capturePrevious.Keys){[Environment]::SetEnvironmentVariable($captureKey,$capturePrevious[$captureKey],'Process')}
}
$captureLog

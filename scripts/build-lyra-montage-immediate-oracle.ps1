param([Parameter(Mandatory=$true)][string]$EngineRoot,
      [Parameter(Mandatory=$true)][string]$UnrealProject,
      [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$PackageName='package')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$weaponRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$weaponProject=[IO.Path]::GetFullPath($UnrealProject)
$weaponProjectRoot=Split-Path $weaponProject -Parent
$weaponPlugins=[IO.Path]::GetFullPath((Join-Path $weaponProjectRoot 'Plugins'))
$weaponStage=[IO.Path]::GetFullPath((Join-Path $weaponPlugins 'LyraWholeMainOracle'))
$weaponDestination=[IO.Path]::GetFullPath((Join-Path $weaponRoot "artifacts/unreal/lyra-whole-main-oracle/$PackageName"))
if(-not $weaponStage.StartsWith($weaponPlugins+[IO.Path]::DirectorySeparatorChar) -or
    -not $weaponDestination.StartsWith($weaponRoot+[IO.Path]::DirectorySeparatorChar)){throw 'Unexpected staging boundaries.'}
if((Test-Path -LiteralPath $weaponStage) -or (Test-Path -LiteralPath $weaponDestination)){throw 'Preserve existing probe/package directories.'}
$weaponLive=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'UnrealEditor*' -and $_.CommandLine -and $_.CommandLine.Contains($weaponProjectRoot)})
if($weaponLive.Count){throw 'Close the project Editor before building its optional probe.'}
$weaponHash=(Get-FileHash -LiteralPath $weaponProject -Algorithm SHA256).Hash
$weaponLog=Join-Path $weaponRoot "artifacts/lyra-analysis/whole-main-build-$PackageName.log"
if(Test-Path -LiteralPath $weaponLog){throw 'Preserve previous build log.'}
New-Item -ItemType Directory -Path $weaponStage | Out-Null
try {
    Copy-Item -LiteralPath (Join-Path $weaponRoot 'tools/unreal/LyraMontageImmediateOracle/Source') -Destination $weaponStage -Recurse
    Copy-Item -LiteralPath (Join-Path $weaponRoot 'tools/unreal/LyraMontageImmediateOracle/LyraWholeMainOracle.uplugin') -Destination $weaponStage
    & (Join-Path $EngineRoot 'Engine/Build/BatchFiles/Build.bat') GASP58Editor Win64 Development "-Project=$weaponProject" `
        "-Plugin=$(Join-Path $weaponStage 'LyraWholeMainOracle.uplugin')" '-Module=LyraWholeMainOracle' `
        -BuildPluginAsLocal -NoHotReload -NoUBTMakefiles -ForceRulesCompile -gather *> $weaponLog
    if($LASTEXITCODE -ne 0){throw "Probe build failed; preserve $weaponLog"}
    # -Module builds the real DLL but does not emit a module manifest. Match the
    # project's existing host BuildId; never change the project's own manifest.
    $weaponHost=Get-Content -LiteralPath (Join-Path $weaponProjectRoot 'Binaries/Win64/UnrealEditor.modules') -Raw | ConvertFrom-Json
    @{BuildId=$weaponHost.BuildId;Modules=@{LyraWholeMainOracle='UnrealEditor-LyraWholeMainOracle.dll'}} |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $weaponStage 'Binaries/Win64/UnrealEditor.modules') -Encoding utf8
} finally {
    New-Item -ItemType Directory -Path (Split-Path $weaponDestination -Parent) -Force | Out-Null
    # Move only this newly owned directory, including failed-build evidence.
    Move-Item -LiteralPath $weaponStage -Destination $weaponDestination
    if((Get-FileHash -LiteralPath $weaponProject -Algorithm SHA256).Hash -ne $weaponHash){throw 'Project descriptor changed.'}
}
$weaponDestination

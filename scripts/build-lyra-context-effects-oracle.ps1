param([Parameter(Mandatory=$true)][string]$EngineRoot,
      [Parameter(Mandatory=$true)][string]$UnrealProject,
      [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$PackageName='package-rebuilt')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$contextRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$contextProject=[IO.Path]::GetFullPath($UnrealProject)
$contextProjectRoot=Split-Path $contextProject -Parent
$contextPlugins=[IO.Path]::GetFullPath((Join-Path $contextProjectRoot 'Plugins'))
$contextStage=[IO.Path]::GetFullPath((Join-Path $contextPlugins 'LyraContextEffectsOracle'))
$contextDestination=[IO.Path]::GetFullPath((Join-Path $contextRoot "artifacts/unreal/lyra-context-effects-oracle/$PackageName"))
if(-not $contextStage.StartsWith($contextPlugins+[IO.Path]::DirectorySeparatorChar) -or
    -not $contextDestination.StartsWith($contextRoot+[IO.Path]::DirectorySeparatorChar)){throw 'Unexpected staging boundaries.'}
if((Test-Path -LiteralPath $contextStage) -or (Test-Path -LiteralPath $contextDestination)){throw 'Preserve existing probe/package directories.'}
$contextLive=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'UnrealEditor*' -and $_.CommandLine -and $_.CommandLine.Contains($contextProjectRoot)})
if($contextLive.Count){throw 'Close the project Editor before building its optional probe.'}
$contextHash=(Get-FileHash -LiteralPath $contextProject -Algorithm SHA256).Hash
$contextLog=Join-Path $contextRoot "artifacts/lyra-analysis/notify-context-build-$PackageName.log"
if(Test-Path -LiteralPath $contextLog){throw 'Preserve previous build log.'}
New-Item -ItemType Directory -Path $contextStage | Out-Null
try {
    Copy-Item -LiteralPath (Join-Path $contextRoot 'tools/unreal/LyraContextEffectsOracle/Source') -Destination $contextStage -Recurse
    Copy-Item -LiteralPath (Join-Path $contextRoot 'tools/unreal/LyraContextEffectsOracle/LyraContextEffectsOracle.uplugin') -Destination $contextStage
    & (Join-Path $EngineRoot 'Engine/Build/BatchFiles/Build.bat') GASP58Editor Win64 Development "-Project=$contextProject" `
        "-Plugin=$(Join-Path $contextStage 'LyraContextEffectsOracle.uplugin')" '-Module=LyraContextEffectsOracle' `
        -BuildPluginAsLocal -NoHotReload -NoUBTMakefiles -ForceRulesCompile -gather *> $contextLog
    if($LASTEXITCODE -ne 0){throw "Probe build failed; preserve $contextLog"}
    # -Module builds the real DLL but does not emit a module manifest. Match the
    # project's existing host BuildId; never change the project's own manifest.
    $contextHost=Get-Content -LiteralPath (Join-Path $contextProjectRoot 'Binaries/Win64/UnrealEditor.modules') -Raw | ConvertFrom-Json
    @{BuildId=$contextHost.BuildId;Modules=@{LyraContextEffectsOracle='UnrealEditor-LyraContextEffectsOracle.dll'}} |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $contextStage 'Binaries/Win64/UnrealEditor.modules') -Encoding utf8
} finally {
    New-Item -ItemType Directory -Path (Split-Path $contextDestination -Parent) -Force | Out-Null
    # Move only this newly owned directory, including failed-build evidence.
    Move-Item -LiteralPath $contextStage -Destination $contextDestination
    if((Get-FileHash -LiteralPath $contextProject -Algorithm SHA256).Hash -ne $contextHash){throw 'Project descriptor changed.'}
}
$contextDestination

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EngineRoot,

    [Parameter(Mandatory = $true)]
    [string]$UnrealProject,

    [switch]$ExternalOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-RequiredPath([string]$Path, [string]$Label) {
    if (-not [IO.Path]::IsPathFullyQualified($Path) -or -not (Test-Path -LiteralPath $Path)) {
        throw "$Label must be an existing absolute path: $Path"
    }
    return (Resolve-Path -LiteralPath $Path).Path
}

function Assert-UnlinkedPath([string]$Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if ((Test-Path -LiteralPath $cursor) -and
            ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Refusing a managed path through a reparse point: $cursor"
        }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if ([string]::IsNullOrEmpty($parent) -or $parent -eq $cursor) { break }
        $cursor = $parent
    }
}

function Reset-ManagedDirectory([string]$Path, [string]$ExpectedParent, [switch]$ParentManaged) {
    $resolvedPath = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $resolvedParent = [IO.Path]::GetFullPath($ExpectedParent).TrimEnd('\')
    if (-not [IO.Path]::GetDirectoryName($resolvedPath).Equals($resolvedParent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Managed directory is outside its expected parent: $resolvedPath"
    }
    Assert-UnlinkedPath $resolvedPath
    if (Test-Path -LiteralPath $Path) {
        $leafManaged = Test-Path -LiteralPath (Join-Path $Path '.godotals-managed') -PathType Leaf
        $parentMarker = Join-Path $ExpectedParent '.godotals-managed'
        if (-not $leafManaged -and -not ($ParentManaged -and
            (Test-Path -LiteralPath $parentMarker -PathType Leaf))) {
            throw "Refusing to replace an unmanaged directory: $Path"
        }
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    New-Item -ItemType File -Path (Join-Path $Path '.godotals-managed') -Force | Out-Null
}

$repositoryRoot = Resolve-RequiredPath (Split-Path -Parent $PSScriptRoot) 'Repository root'
$engineRootPath = Resolve-RequiredPath $EngineRoot 'Engine root'
$unrealProjectPath = Resolve-RequiredPath $UnrealProject 'Unreal project'
$buildVersion = Get-Content -LiteralPath (Join-Path $engineRootPath 'Engine\Build\Build.version') -Raw | ConvertFrom-Json
if ($buildVersion.MajorVersion -ne 5 -or $buildVersion.MinorVersion -ne 8) {
    throw 'The GASP58 asset exporter requires Unreal Engine 5.8.'
}

$projectRoot = Split-Path -Parent $unrealProjectPath
[void](Resolve-RequiredPath (Join-Path $projectRoot 'Content\AdvancedLocomotionV4\CharacterAssets\MannequinSkeleton\ALS_AnimBP.uasset') 'ALS V4 assets')
$runUat = Resolve-RequiredPath (Join-Path $engineRootPath 'Engine\Build\BatchFiles\RunUAT.bat') 'RunUAT'
$editorCommand = Resolve-RequiredPath (Join-Path $engineRootPath 'Engine\Binaries\Win64\UnrealEditor-Cmd.exe') 'Unreal Editor commandlet'
$scaffold = Resolve-RequiredPath (Join-Path $repositoryRoot 'tools\unreal\AlsV4AssetExporter') 'Asset exporter scaffold'
$sharedSource = Resolve-RequiredPath (Join-Path $repositoryRoot 'tools\unreal\AlsGodotExporter\Source\AlsGodotExporter\Private') 'Shared asset exporter source'
$artifactsRoot = Join-Path $repositoryRoot $(if ($ExternalOnly) { 'artifacts\unreal\gasp58-lyra-masks' } else { 'artifacts\unreal\gasp58-exporter' })
$stagedPlugin = Join-Path $artifactsRoot 'source\AlsV4AssetExporter'
$packagePath = Join-Path $artifactsRoot 'package\AlsV4AssetExporter'
$targetPath = Join-Path $projectRoot 'Plugins\AlsV4AssetExporter'

New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
$sourceParent = Split-Path -Parent $stagedPlugin
$packageParent = Split-Path -Parent $packagePath
New-Item -ItemType Directory -Path $sourceParent -Force | Out-Null
Assert-UnlinkedPath $packageParent
if (-not (Test-Path -LiteralPath $packageParent)) {
    New-Item -ItemType Directory -Path $packageParent | Out-Null
    New-Item -ItemType File -Path (Join-Path $packageParent '.godotals-managed') | Out-Null
} elseif (-not (Test-Path -LiteralPath (Join-Path $packageParent '.godotals-managed') -PathType Leaf)) {
    throw "Refusing to use an unmanaged package directory: $packageParent"
}
Reset-ManagedDirectory $stagedPlugin $sourceParent
Copy-Item -LiteralPath (Join-Path $scaffold 'AlsV4AssetExporter.uplugin') -Destination $stagedPlugin
if ($ExternalOnly) {
    $descriptorPath = Join-Path $stagedPlugin 'AlsV4AssetExporter.uplugin'
    $descriptor = Get-Content -LiteralPath $descriptorPath -Raw | ConvertFrom-Json
    $descriptor.Version = 2
    $descriptor.VersionName = '1.0.1-lyra-masks'
    $descriptor | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $descriptorPath -Encoding utf8
}
Copy-Item -LiteralPath (Join-Path $scaffold 'Source') -Destination $stagedPlugin -Recurse
$privateSource = Join-Path $stagedPlugin 'Source\AlsV4AssetExporter\Private'
foreach ($name in @(
    'AlsAnimationMetadataReader', 'AlsAssetDiscovery', 'AlsCompositeAssetReader',
    'AlsExportPlanner', 'AlsFbxExporter', 'AlsFbxNormalizer', 'AlsManifestWriter',
    'AlsMaterialMetadataReader', 'AlsNotifyClassRegistry', 'AlsOutputAuditor',
    'AlsRigMetadataReader', 'AlsStableAssetId', 'AlsTextureExporter'
)) {
    foreach ($extension in @('.cpp', '.h')) {
        $sourcePath = Join-Path $sharedSource ($name + $extension)
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Required asset exporter source is missing: $sourcePath"
        }
        Copy-Item -LiteralPath $sourcePath -Destination $privateSource
    }
}
Copy-Item -LiteralPath (Join-Path $sharedSource 'AlsExportTypes.h') -Destination $privateSource
foreach ($name in @('AlsSourcePoseKeysLibrary.cpp', 'AlsSourceAnimationLibrary.cpp',
    'AlsSourceSyncMetadata.cpp', 'AlsBlendSpacePoseLibrary.cpp', 'AlsBlendSpaceTriangulation.cpp')) {
    $sourcePath = Join-Path $sharedSource $name
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Required animation source reader is missing: $sourcePath"
    }
    Copy-Item -LiteralPath $sourcePath -Destination $privateSource
}

Reset-ManagedDirectory $packagePath $packageParent -ParentManaged
& $runUat BuildPlugin "-Plugin=$(Join-Path $stagedPlugin 'AlsV4AssetExporter.uplugin')" "-Package=$packagePath" -TargetPlatforms=Win64
if ($LASTEXITCODE -ne 0) {
    throw "RunUAT BuildPlugin failed with exit code $LASTEXITCODE."
}
[void](Resolve-RequiredPath (Join-Path $packagePath 'Binaries\Win64') 'Packaged Win64 binaries')
if ($ExternalOnly) {
    Write-Output "GODOT_ALS_EXTERNAL_EXPORTER_OK plugin=$(Join-Path $packagePath 'AlsV4AssetExporter.uplugin')"
    return
}
Reset-ManagedDirectory $targetPath (Join-Path $projectRoot 'Plugins')
Get-ChildItem -LiteralPath $packagePath -Force |
    Where-Object Name -ne '.godotals-managed' |
    Copy-Item -Destination $targetPath -Recurse -Force

$projectHashBefore = (Get-FileHash -LiteralPath $unrealProjectPath -Algorithm SHA256).Hash
$readyOutput = & $editorCommand $unrealProjectPath -run=AlsV4AssetExport -ReadyCheck `
    '-ini:EditorPerProjectUserSettings:[/Script/ModelContextProtocolEngine.ModelContextProtocolSettings]:bAutoStartServer=false' `
    -unattended -nop4 -nosplash -nullrhi 2>&1
$readyOutput | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) {
    throw "Unreal ready check failed with exit code $LASTEXITCODE."
}
if ($projectHashBefore -ne (Get-FileHash -LiteralPath $unrealProjectPath -Algorithm SHA256).Hash) {
    throw 'The ready check modified the Unreal project descriptor.'
}
$marker = 'GODOT_ALS_EXPORTER_READY'
if (-not (($readyOutput | Out-String).Contains($marker, [StringComparison]::Ordinal))) {
    throw "Ready marker was not found: $marker"
}
Write-Output $marker

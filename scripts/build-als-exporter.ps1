[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EngineRoot,

    [Parameter(Mandatory = $true)]
    [string]$UnrealProject
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-RequiredPath([string]$Path, [string]$Label) {
    if (-not [IO.Path]::IsPathFullyQualified($Path)) {
        throw "$Label must be an absolute path: $Path"
    }
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Label does not exist: $Path"
    }
    return (Resolve-Path -LiteralPath $Path).Path
}

$repositoryRoot = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
$engineRootPath = Resolve-RequiredPath $EngineRoot 'EngineRoot'
$unrealProjectPath = Resolve-RequiredPath $UnrealProject 'UnrealProject'
$runUat = Resolve-RequiredPath (Join-Path $engineRootPath 'Engine\Build\BatchFiles\RunUAT.bat') 'RunUAT'
$editorCommand = Resolve-RequiredPath (Join-Path $engineRootPath 'Engine\Binaries\Win64\UnrealEditor-Cmd.exe') 'UnrealEditor-Cmd'
$pluginSource = Resolve-RequiredPath (Join-Path $repositoryRoot 'tools\unreal\AlsGodotExporter\AlsGodotExporter.uplugin') 'Plugin source'
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\unreal'))
$packagePath = [IO.Path]::GetFullPath((Join-Path $artifactsRoot 'AlsGodotExporter'))
$projectRoot = Split-Path -Parent $unrealProjectPath
$pluginsRoot = Join-Path $projectRoot 'Plugins'
$targetPath = [IO.Path]::GetFullPath((Join-Path $pluginsRoot 'AlsGodotExporter'))
$managedSentinel = Join-Path $targetPath '.godotals-managed'

if (-not ($packagePath.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))) {
    throw "Refusing to clean package path outside artifacts/unreal: $packagePath"
}

if (Test-Path -LiteralPath $packagePath) {
    Remove-Item -LiteralPath $packagePath -Recurse -Force
}
New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null

& $runUat BuildPlugin "-Plugin=$pluginSource" "-Package=$packagePath" -TargetPlatforms=Win64
if ($LASTEXITCODE -ne 0) {
    throw "RunUAT BuildPlugin failed with exit code $LASTEXITCODE."
}

$editorBinaries = Join-Path $packagePath 'Binaries\Win64'
if (-not (Test-Path -LiteralPath $editorBinaries -PathType Container)) {
    throw "Packaged plugin does not contain Win64 editor binaries: $editorBinaries"
}

if (Test-Path -LiteralPath $targetPath) {
    if (-not (Test-Path -LiteralPath $managedSentinel -PathType Leaf)) {
        throw "Refusing to replace unmanaged plugin directory: $targetPath"
    }
    Remove-Item -LiteralPath $targetPath -Recurse -Force
}

New-Item -ItemType Directory -Path $pluginsRoot -Force | Out-Null
Copy-Item -LiteralPath $packagePath -Destination $targetPath -Recurse
New-Item -ItemType File -Path $managedSentinel -Force | Out-Null

$readyOutput = & $editorCommand $unrealProjectPath -run=AlsGodotExport -ReadyCheck -unattended -nop4 -nosplash -nullrhi 2>&1
$readyOutput | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) {
    throw "Unreal ready check failed with exit code $LASTEXITCODE."
}

$curveSelfTestMarker = 'GODOT_ALS_CURVE_EXPORT_SELF_TEST_OK cases=16'
if (-not (($readyOutput | Out-String).Contains($curveSelfTestMarker, [StringComparison]::Ordinal))) {
    throw "Native curve export self-test marker was not found: $curveSelfTestMarker"
}

$timelineSelfTestMarker = 'GODOT_ALS_TIMELINE_EXPORT_SELF_TEST_OK cases=26'
if (-not (($readyOutput | Out-String).Contains($timelineSelfTestMarker, [StringComparison]::Ordinal))) {
    throw "Native timeline export self-test marker was not found: $timelineSelfTestMarker"
}

$marker = 'GODOT_ALS_EXPORTER_READY engine=5.9.0 plugin=2.0.0'
if (-not (($readyOutput | Out-String).Contains($marker, [StringComparison]::Ordinal))) {
    throw "Ready marker was not found: $marker"
}

Write-Output $marker

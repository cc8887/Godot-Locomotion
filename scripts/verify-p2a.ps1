[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EngineRoot,

    [Parameter(Mandatory = $true)]
    [string]$UnrealProject,

    [string]$Output = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$buildScript = Join-Path $PSScriptRoot 'build-als-exporter.ps1'
if (-not (Test-Path -LiteralPath $buildScript -PathType Leaf)) {
    throw "Build script does not exist: $buildScript"
}

$outputLines = & $buildScript -EngineRoot $EngineRoot -UnrealProject $UnrealProject 2>&1
$outputLines | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) {
    throw "P2A build gate failed with exit code $LASTEXITCODE."
}

$marker = 'GODOT_ALS_EXPORTER_READY engine=5.9.0 plugin=1.0.0'
if (-not (($outputLines | Out-String).Contains($marker, [StringComparison]::Ordinal))) {
    throw "P2A ready marker was not found: $marker"
}

Write-Host 'GODOT_ALS_P2A_READY'

$repositoryRoot = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $repositoryRoot 'assets\generated\als_v4'
}
$outputPath = [IO.Path]::GetFullPath($Output)
$editorCommand = Join-Path ([IO.Path]::GetFullPath($EngineRoot)) 'Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
$projectHashBefore = (Get-FileHash -LiteralPath $UnrealProject -Algorithm SHA256).Hash

$dryRunOutput = & $editorCommand $UnrealProject -run=AlsGodotExport -DryRun "-Output=$outputPath" -unattended -nop4 -nosplash -nullrhi -nosound 2>&1
$dryRunOutput | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) {
    throw "P2A dry-run failed with exit code $LASTEXITCODE."
}

$projectHashAfter = (Get-FileHash -LiteralPath $UnrealProject -Algorithm SHA256).Hash
if ($projectHashBefore -ne $projectHashAfter) {
    throw 'The exporter modified the source .uproject file.'
}

$planPath = Join-Path $outputPath 'export_plan.json'
if (-not (Test-Path -LiteralPath $planPath -PathType Leaf)) {
    throw "Dry-run export plan does not exist: $planPath"
}

$plan = Get-Content -LiteralPath $planPath -Raw | ConvertFrom-Json
$requiredKinds = @('Skeleton', 'SkeletalMesh', 'StaticMesh', 'AnimationSequence', 'AnimMontage', 'PhysicsAsset', 'Texture', 'Blueprint')
$actualKinds = @($plan.assets | ForEach-Object { $_.kind } | Sort-Object -Unique)
foreach ($kind in $requiredKinds) {
    if ($kind -notin $actualKinds) {
        throw "Dry-run plan does not contain required asset kind: $kind"
    }
}
if (-not (@('BlendSpace', 'AimOffset') | Where-Object { $_ -in $actualKinds })) {
    throw 'Dry-run plan contains neither BlendSpace nor AimOffset.'
}
if (-not (@('Material', 'MaterialInstance') | Where-Object { $_ -in $actualKinds })) {
    throw 'Dry-run plan contains neither Material nor MaterialInstance.'
}

$excludedPattern = '/(Audio|Environment|Levels|UI|AI|GameModes)/'
$excludedAssets = @($plan.assets | Where-Object { $_.objectPath -match $excludedPattern })
if ($excludedAssets.Count -ne 0) {
    throw "Dry-run plan contains $($excludedAssets.Count) excluded assets."
}

$ids = @($plan.assets | ForEach-Object { $_.id })
$sortedIds = @($ids | Sort-Object -CaseSensitive)
if (($ids -join "`n") -cne ($sortedIds -join "`n")) {
    throw 'Dry-run plan assets are not sorted by stable ID.'
}

$planMarker = 'GODOT_ALS_P2A_PLAN_OK assets='
if (-not (($dryRunOutput | Out-String).Contains($planMarker, [StringComparison]::Ordinal))) {
    throw "P2A plan marker was not found: $planMarker"
}

Write-Host "GODOT_ALS_P2A_DRY_RUN_OK assets=$($plan.summary.assetCount) exportable=$($plan.summary.exportableCount)"

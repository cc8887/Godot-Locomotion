[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EngineRoot,

    [Parameter(Mandatory = $true)]
    [string]$UnrealProject,

    [string]$Output = '',

    [switch]$UpdateAssetLock
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'asset-lock-functions.ps1')

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
$artifactsPath = Join-Path $repositoryRoot 'artifacts'
[void](New-Item -ItemType Directory -Path $artifactsPath -Force)
$godotIgnorePath = Join-Path $artifactsPath '.gdignore'
if (-not (Test-Path -LiteralPath $godotIgnorePath -PathType Leaf)) {
    [IO.File]::WriteAllText(
        $godotIgnorePath,
        "# Generated build and determinism artifacts are not Godot project resources.`n",
        [Text.UTF8Encoding]::new($false))
}
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

$partialManifestPath = Join-Path $outputPath 'partial\als_manifest.partial.json'
if (-not (Test-Path -LiteralPath $partialManifestPath -PathType Leaf)) {
    throw "Dry-run partial manifest does not exist: $partialManifestPath"
}
$manifest = Get-Content -LiteralPath $partialManifestPath -Raw | ConvertFrom-Json
$allBoneNames = @($manifest.skeletons | ForEach-Object { $_.metadata.bones } | ForEach-Object { $_.name })
foreach ($boneName in @('root', 'pelvis', 'foot_l', 'foot_r')) {
    if ($boneName -notin $allBoneNames) {
        throw "Partial manifest does not contain required bone: $boneName"
    }
}
foreach ($skeleton in @($manifest.skeletons)) {
    if ($skeleton.metadata.restPoseHash -notmatch '^[0-9a-f]{40}$') {
        throw "Skeleton metadata has no valid rest pose hash: $($skeleton.objectPath)"
    }
    if ($null -eq $skeleton.metadata.PSObject.Properties['sockets']) {
        throw "Skeleton metadata has no sockets array: $($skeleton.objectPath)"
    }
    if (@($skeleton.metadata.bones).Count -ne $skeleton.metadata.boneCount) {
        throw "Skeleton bone count does not match metadata: $($skeleton.objectPath)"
    }
}
$animationsWithSemantics = @($manifest.animations | Where-Object {
    @($_.metadata.curves).Count -gt 0 -or @($_.metadata.notifies).Count -gt 0
})
if ($animationsWithSemantics.Count -eq 0) {
    throw 'Partial manifest contains no animation with curves or notifies.'
}
foreach ($animation in @($manifest.animations)) {
    foreach ($field in @('loop', 'interpolation', 'forceRootLock', 'useNormalizedRootMotionScale',
        'additiveBasePoseType', 'additiveBasePoseFrame', 'additiveBasePoseId', 'additiveBasePoseObjectPath')) {
        if ($null -eq $animation.metadata.PSObject.Properties[$field]) {
            throw "Animation metadata is missing '$field': $($animation.objectPath)"
        }
    }
}
if (@($manifest.montages | Where-Object { @($_.metadata.sections).Count -gt 0 }).Count -eq 0) {
    throw 'Partial manifest contains no montage sections.'
}
foreach ($montage in @($manifest.montages)) {
    foreach ($field in @('blendInTime', 'blendInOption', 'blendOutTime', 'blendOutOption',
        'blendOutTriggerTime', 'enableAutoBlendOut')) {
        if ($null -eq $montage.metadata.PSObject.Properties[$field]) {
            throw "Montage metadata is missing '$field': $($montage.objectPath)"
        }
    }
}
if (@($manifest.blendSpaces + $manifest.aimOffsets | Where-Object { @($_.metadata.samples).Count -gt 0 }).Count -eq 0) {
    throw 'Partial manifest contains no blend space or aim offset samples.'
}
if (@($manifest.physicsAssets | Where-Object { @($_.metadata.bodies).Count -gt 0 }).Count -eq 0) {
    throw 'Partial manifest contains no physics bodies.'
}
foreach ($physicsAsset in @($manifest.physicsAssets)) {
    if ($null -eq $physicsAsset.metadata.PSObject.Properties['constraints'] -or
        @($physicsAsset.metadata.constraints).Count -ne $physicsAsset.metadata.constraintCount) {
        throw "Physics asset constraints do not match metadata: $($physicsAsset.objectPath)"
    }
}
if (@($manifest.animations | Where-Object { $_.objectPath -match '/Overlay/' }).Count -eq 0) {
    throw 'Partial manifest contains no overlay animation.'
}
if (@($manifest.staticMeshes + $manifest.skeletalMeshes | Where-Object { $_.objectPath -match '/Props/' }).Count -eq 0) {
    throw 'Partial manifest contains no prop mesh.'
}
$materialInstances = @($manifest.materials | Where-Object { $_.classPath -match 'MaterialInstance' })
if ($materialInstances.Count -eq 0) {
    throw 'Partial manifest contains no material instance.'
}
$materialOverrideCount = 0
foreach ($materialInstance in $materialInstances) {
    foreach ($field in @('scalarParameterOverrides', 'vectorParameterOverrides', 'textureParameterOverrides')) {
        if ($null -eq $materialInstance.metadata.PSObject.Properties[$field]) {
            throw "Material instance metadata is missing '$field': $($materialInstance.objectPath)"
        }
        $materialOverrideCount += @($materialInstance.metadata.$field).Count
    }
}
if ($materialOverrideCount -eq 0) {
    throw 'Partial manifest contains no material parameter overrides.'
}
if ($manifest.auditSummary.status -cne 'planned') {
    throw "Unexpected dry-run audit status: $($manifest.auditSummary.status)"
}
Write-Host 'GODOT_ALS_P2A_METADATA_OK'

$exportOutput = & $editorCommand $UnrealProject -run=AlsGodotExport -Export "-Output=$outputPath" -unattended -nop4 -nosplash -nosound -AllowCommandletRendering -RenderOffscreen 2>&1
$exportOutput | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) {
    throw "P2A full export failed with exit code $LASTEXITCODE."
}
$exportMarker = 'GODOT_ALS_P2A_EXPORT_OK assets='
if (-not (($exportOutput | Out-String).Contains($exportMarker, [StringComparison]::Ordinal))) {
    throw "P2A full export marker was not found: $exportMarker"
}

$formalManifestPath = Join-Path $outputPath 'als_manifest.json'
if (-not (Test-Path -LiteralPath $formalManifestPath -PathType Leaf)) {
    throw "Formal manifest does not exist: $formalManifestPath"
}
$formalManifest = Get-Content -LiteralPath $formalManifestPath -Raw | ConvertFrom-Json
if ($formalManifest.auditSummary.status -cne 'complete' -or $formalManifest.auditSummary.errorCount -ne 0) {
    throw "Formal manifest audit is not complete: $($formalManifest.auditSummary.status)"
}
foreach ($file in $formalManifest.files) {
    $filePath = Join-Path $outputPath $file.relativePath
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf) -or (Get-Item -LiteralPath $filePath).Length -le 0) {
        throw "Manifest output file is missing or empty: $($file.relativePath)"
    }
    if ($file.sha256 -cnotmatch '^[0-9a-f]{64}$') {
        throw "Manifest output SHA-256 is invalid: $($file.relativePath)"
    }
    $actualHash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -cne $file.sha256) {
        throw "Manifest output SHA-256 mismatch: $($file.relativePath)"
    }
}
$fbxFiles = Get-ChildItem -LiteralPath $outputPath -Recurse -File -Filter '*.fbx'
$externalTextureObjects = @($fbxFiles | Select-String -Pattern '^\s*(Texture|Video):\s+\d+,' -CaseSensitive)
if ($externalTextureObjects.Count -ne 0) {
    $firstMatch = $externalTextureObjects[0]
    throw "Normalized FBX still contains an external texture object: $($firstMatch.Path):$($firstMatch.LineNumber)"
}
if (@($formalManifest.files).Count -ne $plan.summary.exportableCount) {
    throw "Formal manifest file count does not match export plan: $(@($formalManifest.files).Count)"
}
Write-Host "GODOT_ALS_P2A_FULL_EXPORT_OK files=$(@($formalManifest.files).Count)"

$determinismPath = Join-Path $repositoryRoot 'artifacts\p2a-determinism\als_v4'
$determinismOutput = & $editorCommand $UnrealProject -run=AlsGodotExport -Export "-Output=$determinismPath" -unattended -nop4 -nosplash -nosound -AllowCommandletRendering -RenderOffscreen 2>&1
$determinismOutput | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) {
    throw "P2A determinism export failed with exit code $LASTEXITCODE."
}

$compareScript = Join-Path $PSScriptRoot 'compare-p2a-exports.ps1'
& $compareScript -ReferenceRoot $outputPath -CandidateRoot $determinismPath
if ($LASTEXITCODE -ne 0) {
    throw "P2A determinism comparison failed with exit code $LASTEXITCODE."
}
if ($UpdateAssetLock) {
    $assetLockPath = Join-Path $repositoryRoot 'reference\als-v4-export.lock.json'
    Publish-AlsExportLock -ManifestPath $formalManifestPath -LockPath $assetLockPath
    Write-Host 'GODOT_ALS_P2A_ASSET_LOCK_OK'
}
Write-Host 'P2A_VERIFICATION_OK'

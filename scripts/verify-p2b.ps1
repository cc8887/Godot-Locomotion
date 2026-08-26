param(
    [Parameter(Mandatory)]
    [string]$GodotExecutable,
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [switch]$CleanImport
)

$ErrorActionPreference = 'Stop'
$markerPattern = 'P2B_IMPORT_OK assets=(\d+) files=(\d+) skeletal=(\d+) static=(\d+) animations=(\d+) textures=(\d+)'
$expectedManifestSha256 = '369AF84ABA028AFBDF6EEA7F1A4F1161DFD4B5E9BEA736E9460BFE368CE14327'
. (Join-Path $PSScriptRoot 'p2b-verification-functions.ps1')

if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf)) {
    throw "Godot executable not found: $GodotExecutable"
}

$projectRootPath = (Resolve-Path -LiteralPath $ProjectRoot).Path
$assetRoot = Join-Path $projectRootPath 'assets\generated\als_v4'
$manifestPath = Join-Path $assetRoot 'als_manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Formal ALS manifest not found: $manifestPath"
}

Assert-P2bManifestHash -ManifestPath $manifestPath -ExpectedSha256 $expectedManifestSha256
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
if ($manifest.auditSummary.status -ne 'complete') {
    throw "ALS manifest audit is not complete: $($manifest.auditSummary.status)"
}

if ($CleanImport) {
    $importCache = Join-Path $projectRootPath '.godot\imported'
    if (Test-Path -LiteralPath $importCache) {
        $resolvedCache = (Resolve-Path -LiteralPath $importCache).Path
        if (-not $resolvedCache.StartsWith($projectRootPath, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clear import cache outside project root: $resolvedCache"
        }

        Remove-Item -LiteralPath $resolvedCache -Recurse -Force
    }

    Get-ChildItem -LiteralPath $assetRoot -Recurse -File -Filter '*.import' |
        Remove-Item -Force
    $compiledRoot = Join-Path $assetRoot 'compiled'
    if (Test-Path -LiteralPath $compiledRoot) {
        $resolvedCompiled = (Resolve-Path -LiteralPath $compiledRoot).Path
        if (-not $resolvedCompiled.StartsWith($assetRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clear compiled resources outside asset root: $resolvedCompiled"
        }

        Remove-Item -LiteralPath $resolvedCompiled -Recurse -Force
    }
}

dotnet restore (Join-Path $projectRootPath 'GodotALS.sln')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build (Join-Path $projectRootPath 'GodotALS.sln') --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$importOutput = & $GodotExecutable --headless --editor --path $projectRootPath --import --quit 2>&1
$importExitCode = $LASTEXITCODE
$importOutput | ForEach-Object { Write-Host $_ }
$joinedImportOutput = $importOutput -join [Environment]::NewLine
$importErrorLines = @($importOutput | Where-Object { "$_" -match '^(SCRIPT ERROR|ERROR):' })
if ($importExitCode -ne 0 -or $importErrorLines.Count -ne 0) {
    $details = $importErrorLines -join [Environment]::NewLine
    throw "Godot asset import failed with exit code $importExitCode.$([Environment]::NewLine)$details"
}

$smokeOutput = & $GodotExecutable --headless --path $projectRootPath `
    'res://scenes/tests/p2b_import.tscn' 2>&1
$smokeExitCode = $LASTEXITCODE
$smokeOutput | ForEach-Object { Write-Host $_ }
$joinedSmokeOutput = $smokeOutput -join [Environment]::NewLine
$smokeErrorLines = @($smokeOutput | Where-Object { "$_" -match '^(SCRIPT ERROR|ERROR):' })
if ($smokeExitCode -ne 0 -or $smokeErrorLines.Count -ne 0) {
    $details = $smokeErrorLines -join [Environment]::NewLine
    throw "Godot P2B import smoke failed with exit code $smokeExitCode.$([Environment]::NewLine)$details"
}

$match = [regex]::Match($joinedSmokeOutput, $markerPattern)
if (-not $match.Success) {
    throw 'Godot P2B import marker was not emitted.'
}

$expected = @(267, 141, 7, 4, 126, 4)
for ($index = 0; $index -lt $expected.Length; $index++) {
    if ([int]$match.Groups[$index + 1].Value -ne $expected[$index]) {
        throw "Godot P2B import marker reported unexpected counts: $($match.Value)"
    }
}

$assetSmokeOutput = & $GodotExecutable --headless --path $projectRootPath `
    'res://scenes/tests/p2b_asset_smoke.tscn' 2>&1
$assetSmokeExitCode = $LASTEXITCODE
$assetSmokeOutput | ForEach-Object { Write-Host $_ }
$joinedAssetSmokeOutput = $assetSmokeOutput -join [Environment]::NewLine
$assetSmokeErrorLines = @($assetSmokeOutput | Where-Object { "$_" -match '^(SCRIPT ERROR|ERROR):' })
if ($assetSmokeExitCode -ne 0 -or $assetSmokeErrorLines.Count -ne 0) {
    $details = $assetSmokeErrorLines -join [Environment]::NewLine
    throw "Godot P2B representative asset smoke failed with exit code $assetSmokeExitCode.$([Environment]::NewLine)$details"
}

$assetSmokePattern = 'P2B_ASSET_SMOKE_OK mannequinBones=(\d+) clips=(\d+) overlay=(\d+) props=(\d+)'
$assetSmokeMatch = [regex]::Match($joinedAssetSmokeOutput, $assetSmokePattern)
if (-not $assetSmokeMatch.Success) {
    throw 'Godot P2B representative asset marker was not emitted.'
}

$expectedAssetSmoke = @(68, 6, 2, 1)
for ($index = 0; $index -lt $expectedAssetSmoke.Length; $index++) {
    if ([int]$assetSmokeMatch.Groups[$index + 1].Value -ne $expectedAssetSmoke[$index]) {
        throw "Godot P2B representative asset marker reported unexpected counts: $($assetSmokeMatch.Value)"
    }
}

Write-Output 'P2B_ASSET_VERIFICATION_OK'

$rigMarkerPattern = 'GODOT_ALS_P2B_RIG_OK mode=(single|parallel) characters=(\d+) frames=(\d+) digest=([0-9A-F]{16}) missing=(\d+) stale=(\d+) off_main=(\d+) replacements=(\d+) events=(\d+)'
function Invoke-P2bRigHarness {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('single', 'parallel')]
        [string]$Mode,
        [Parameter(Mandatory)]
        [ValidateSet(1, 10)]
        [int]$CharacterCount
    )

    $rigOutput = & $GodotExecutable --headless --path $projectRootPath `
        'res://scenes/tests/p2b_real_rig_harness.tscn' -- `
        "--als-mode=$Mode" "--als-characters=$CharacterCount" '--als-frames=120' 2>&1
    $rigExitCode = $LASTEXITCODE
    $rigOutput | ForEach-Object { Write-Host $_ }
    $rigErrorLines = @($rigOutput | Where-Object { "$_" -match '^(SCRIPT ERROR|ERROR):' })
    if ($rigExitCode -ne 0 -or $rigErrorLines.Count -ne 0) {
        $details = $rigErrorLines -join [Environment]::NewLine
        throw "Godot P2B real-rig harness failed for mode=$Mode characters=$CharacterCount with exit code $rigExitCode.$([Environment]::NewLine)$details"
    }

    $match = [regex]::Match(($rigOutput -join [Environment]::NewLine), $rigMarkerPattern)
    if (-not $match.Success) {
        throw "Godot P2B real-rig marker was not emitted for mode=$Mode characters=$CharacterCount."
    }

    return [pscustomobject]@{
        Mode = $match.Groups[1].Value
        Characters = [int]$match.Groups[2].Value
        Frames = [int]$match.Groups[3].Value
        Digest = $match.Groups[4].Value
        Missing = [long]$match.Groups[5].Value
        Stale = [long]$match.Groups[6].Value
        OffMain = [int]$match.Groups[7].Value
        Replacements = [int]$match.Groups[8].Value
        Events = [long]$match.Groups[9].Value
    }
}

foreach ($characterCount in @(1, 10)) {
    $single = Invoke-P2bRigHarness -Mode single -CharacterCount $characterCount
    $parallel = Invoke-P2bRigHarness -Mode parallel -CharacterCount $characterCount
    if ($single.Digest -cne $parallel.Digest) {
        throw "P2B real-rig digest mismatch for characters=${characterCount}: single=$($single.Digest), parallel=$($parallel.Digest)."
    }
    foreach ($result in @($single, $parallel)) {
        if ($result.Characters -ne $characterCount -or $result.Frames -ne 120 -or
            $result.Missing -ne 0 -or $result.Stale -ne 0 -or
            $result.Replacements -ne 1 -or $result.Events -ne ($characterCount * 4 + 4)) {
            throw "P2B real-rig harness reported invalid semantics for mode=$($result.Mode) characters=$characterCount."
        }
    }
    if ($single.OffMain -ne 0 -or $parallel.OffMain -ne $characterCount) {
        throw "P2B real-rig harness reported invalid worker affinity for characters=$characterCount."
    }
}

Write-Output 'P2B_REAL_RIG_VERIFICATION_OK'
Write-Output 'P2B_VERIFICATION_OK'
exit 0

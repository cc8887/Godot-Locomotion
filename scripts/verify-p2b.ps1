param(
    [Parameter(Mandatory)]
    [string]$GodotExecutable,
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [switch]$CleanImport
)

$ErrorActionPreference = 'Stop'
$markerPattern = 'P2B_IMPORT_OK assets=(\d+) files=(\d+) skeletal=(\d+) static=(\d+) animations=(\d+) textures=(\d+)'

if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf)) {
    throw "Godot executable not found: $GodotExecutable"
}

$projectRootPath = (Resolve-Path -LiteralPath $ProjectRoot).Path
$assetRoot = Join-Path $projectRootPath 'assets\generated\als_v4'
$manifestPath = Join-Path $assetRoot 'als_manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Formal ALS manifest not found: $manifestPath"
}

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

Write-Output 'P2B_VERIFICATION_OK'
exit 0

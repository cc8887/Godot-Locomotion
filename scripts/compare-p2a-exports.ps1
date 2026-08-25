[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ReferenceRoot,

    [Parameter(Mandatory = $true)]
    [string]$CandidateRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-ExportRoot([string]$Path, [string]$Label) {
    if (-not [IO.Path]::IsPathFullyQualified($Path)) {
        throw "$Label must be an absolute path: $Path"
    }
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Label does not exist: $Path"
    }
    return (Resolve-Path -LiteralPath $Path).Path
}

function Get-ComparableFiles([string]$Root) {
    $manifestPath = Join-Path $Root 'als_manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Formal manifest is missing: $manifestPath"
    }
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    $producerMetadata = @(
        'als_manifest.json',
        'export_plan.json',
        'partial/als_manifest.partial.json',
        'audit/export_report.json',
        'audit/export_report.txt'
    )
    $files = @($manifest.files | ForEach-Object { $_.relativePath }) + $producerMetadata
    return @($files | Sort-Object -Unique)
}

$referencePath = Resolve-ExportRoot $ReferenceRoot 'ReferenceRoot'
$candidatePath = Resolve-ExportRoot $CandidateRoot 'CandidateRoot'
$referenceFiles = @(Get-ComparableFiles $referencePath)
$candidateFiles = @(Get-ComparableFiles $candidatePath)

$setDifference = Compare-Object -ReferenceObject $referenceFiles -DifferenceObject $candidateFiles
if ($setDifference) {
    $first = $setDifference | Select-Object -First 1
    throw "Export file set differs at '$($first.InputObject)' (side=$($first.SideIndicator))."
}

foreach ($relativePath in $referenceFiles) {
    $referenceFile = Join-Path $referencePath $relativePath
    $candidateFile = Join-Path $candidatePath $relativePath
    $referenceLength = (Get-Item -LiteralPath $referenceFile).Length
    $candidateLength = (Get-Item -LiteralPath $candidateFile).Length
    $referenceHash = (Get-FileHash -LiteralPath $referenceFile -Algorithm SHA256).Hash.ToLowerInvariant()
    $candidateHash = (Get-FileHash -LiteralPath $candidateFile -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($referenceLength -ne $candidateLength -or $referenceHash -cne $candidateHash) {
        throw "Export differs at '$relativePath': reference(length=$referenceLength sha256=$referenceHash), candidate(length=$candidateLength sha256=$candidateHash)."
    }
}

$manifestRelativePath = 'als_manifest.json'
$referenceManifest = [IO.File]::ReadAllBytes((Join-Path $referencePath $manifestRelativePath))
$candidateManifest = [IO.File]::ReadAllBytes((Join-Path $candidatePath $manifestRelativePath))
if (-not [System.Linq.Enumerable]::SequenceEqual[byte]($referenceManifest, $candidateManifest)) {
    throw 'Formal manifests are not byte-identical.'
}

Write-Host "P2A_DETERMINISM_OK files=$($referenceFiles.Count)"

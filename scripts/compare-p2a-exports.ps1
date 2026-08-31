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
    $cursor = [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    while (-not [string]::IsNullOrEmpty($cursor)) {
        $item = Get-Item -LiteralPath $cursor -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Label traverses a reparse point and cannot be compared: $cursor"
        }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if ([string]::IsNullOrEmpty($parent) -or $parent -ceq $cursor) { break }
        $cursor = $parent.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    }
    return (Resolve-Path -LiteralPath $Path).Path
}

function Test-ExportRootAncestor([string]$Ancestor, [string]$Descendant) {
    $comparison = if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        [StringComparison]::OrdinalIgnoreCase
    }
    else { [StringComparison]::Ordinal }
    $prefix = $Ancestor.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    return $Descendant.StartsWith($prefix, $comparison)
}

function Get-ComparableFiles([string]$Root) {
    $manifestPath = Join-Path $Root 'als_manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Formal manifest is missing: $manifestPath"
    }
    return @(Get-ChildItem -LiteralPath $Root -File -Recurse | ForEach-Object {
        [IO.Path]::GetRelativePath($Root, $_.FullName).Replace([IO.Path]::DirectorySeparatorChar, '/')
    } | Sort-Object -CaseSensitive)
}

$referencePath = Resolve-ExportRoot $ReferenceRoot 'ReferenceRoot'
$candidatePath = Resolve-ExportRoot $CandidateRoot 'CandidateRoot'
if ($referencePath -ceq $candidatePath) {
    throw 'ReferenceRoot and CandidateRoot must be independent directories.'
}
if ((Test-ExportRootAncestor $referencePath $candidatePath) -or
    (Test-ExportRootAncestor $candidatePath $referencePath)) {
    throw 'ReferenceRoot and CandidateRoot must not be ancestors or descendants of one another.'
}
$referenceFiles = @(Get-ComparableFiles $referencePath)
$candidateFiles = @(Get-ComparableFiles $candidatePath)

$setDifference = Compare-Object -ReferenceObject $referenceFiles -DifferenceObject $candidateFiles -CaseSensitive
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

Write-Host "P2A_DETERMINISM_OK files=$($referenceFiles.Count)"

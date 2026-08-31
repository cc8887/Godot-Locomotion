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
$rootComparison = if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
    [StringComparison]::OrdinalIgnoreCase
}
else { [StringComparison]::Ordinal }
if ([string]::Equals($referencePath, $candidatePath, $rootComparison)) {
    throw 'ReferenceRoot and CandidateRoot must be independent directories.'
}
if ((Test-ExportRootAncestor $referencePath $candidatePath) -or
    (Test-ExportRootAncestor $candidatePath $referencePath)) {
    throw 'ReferenceRoot and CandidateRoot must not be ancestors or descendants of one another.'
}
$referenceFiles = @(Get-ComparableFiles $referencePath)
$candidateFiles = @(Get-ComparableFiles $candidatePath)

$referenceSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$candidateSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($relativePath in $referenceFiles) { [void]$referenceSet.Add($relativePath) }
foreach ($relativePath in $candidateFiles) { [void]$candidateSet.Add($relativePath) }
if (-not $referenceSet.SetEquals($candidateSet)) {
    $referenceOnly = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    $candidateOnly = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    foreach ($relativePath in $referenceSet) {
        if (-not $candidateSet.Contains($relativePath)) { [void]$referenceOnly.Add($relativePath) }
    }
    foreach ($relativePath in $candidateSet) {
        if (-not $referenceSet.Contains($relativePath)) { [void]$candidateOnly.Add($relativePath) }
    }
    $first = if ($referenceOnly.Count -eq 0) {
        [pscustomobject]@{ Path = $candidateOnly.Min; Side = '=>' }
    }
    elseif ($candidateOnly.Count -eq 0 -or
        [StringComparer]::Ordinal.Compare($referenceOnly.Min, $candidateOnly.Min) -le 0) {
        [pscustomobject]@{ Path = $referenceOnly.Min; Side = '<=' }
    }
    else { [pscustomobject]@{ Path = $candidateOnly.Min; Side = '=>' } }
    throw "Export file set differs at '$($first.Path)' (side=$($first.Side))."
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

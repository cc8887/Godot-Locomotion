function Get-P3aExpectedDigest
{
    param(
        [Parameter(Mandatory)]
        [ValidateSet(1, 10)]
        [int]$CharacterCount
    )

    if ($CharacterCount -eq 1)
    {
        return '4E567B2CB05AF7DF'
    }

    return '1623F6F27E89C051'
}

function ConvertFrom-P3aHarnessOutput
{
    param(
        [Parameter(Mandatory)]
        [object[]]$OutputLines,
        [Parameter(Mandatory)]
        [ValidateSet('single', 'parallel')]
        [string]$ExpectedMode,
        [Parameter(Mandatory)]
        [ValidateSet(1, 10)]
        [int]$ExpectedCharacterCount
    )

    $markerLines = @($OutputLines | ForEach-Object { "$_" } | Where-Object {
        $_.StartsWith('GODOT_ALS_P3A_', [StringComparison]::Ordinal)
    })
    if ($markerLines.Count -ne 1)
    {
        throw "Expected exactly one P3A marker line; observed $($markerLines.Count)."
    }

    $pattern = '\AGODOT_ALS_P3A_OK mode=(single|parallel) characters=(1|10) warmup=120 frames=600 digest=([0-9A-F]{16}) missing=(\d+) stale=(\d+) generation=(\d+) off_main=(\d+) lag=(\d+) allocations=(\d+)\z'
    $match = [regex]::Match($markerLines[0], $pattern)
    if (-not $match.Success)
    {
        throw "Malformed or unsuccessful P3A marker: $($markerLines[0])"
    }

    $mode = $match.Groups[1].Value
    $characters = [int]$match.Groups[2].Value
    if ($mode -cne $ExpectedMode -or $characters -ne $ExpectedCharacterCount)
    {
        throw "Unexpected P3A identity: expected mode=$ExpectedMode characters=$ExpectedCharacterCount, observed mode=$mode characters=$characters."
    }

    $digest = $match.Groups[3].Value
    $expectedDigest = Get-P3aExpectedDigest -CharacterCount $ExpectedCharacterCount
    if ($digest -cne $expectedDigest)
    {
        throw "P3A digest baseline mismatch for characters=${ExpectedCharacterCount}: expected=$expectedDigest, observed=$digest."
    }

    [pscustomobject]@{
        Mode = $mode
        Characters = $characters
        Digest = $digest
        Missing = [long]$match.Groups[4].Value
        Stale = [long]$match.Groups[5].Value
        Generation = [long]$match.Groups[6].Value
        OffMain = [long]$match.Groups[7].Value
        Lag = [long]$match.Groups[8].Value
        Allocations = [long]$match.Groups[9].Value
    }
}

function Assert-P3aResultPair
{
    param(
        [Parameter(Mandatory)]
        [psobject]$Single,
        [Parameter(Mandatory)]
        [psobject]$Parallel,
        [Parameter(Mandatory)]
        [ValidateSet(1, 10)]
        [int]$CharacterCount
    )

    if ($Single.Mode -cne 'single' -or $Parallel.Mode -cne 'parallel' -or
        $Single.Characters -ne $CharacterCount -or $Parallel.Characters -ne $CharacterCount)
    {
        throw "Godot P3A harness reported unexpected mode or character count for characters=$CharacterCount."
    }
    if ($Single.Digest -cne $Parallel.Digest)
    {
        throw "P3A digest mismatch for characters=${CharacterCount}: single=$($Single.Digest), parallel=$($Parallel.Digest)."
    }

    $expectedDigest = Get-P3aExpectedDigest -CharacterCount $CharacterCount
    if ($Single.Digest -cne $expectedDigest)
    {
        throw "P3A digest baseline mismatch for characters=${CharacterCount}: expected=$expectedDigest, observed=$($Single.Digest)."
    }
}

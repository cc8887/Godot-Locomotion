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

    [pscustomobject]@{
        Mode = $mode
        Characters = $characters
        Digest = $match.Groups[3].Value
        Missing = [long]$match.Groups[4].Value
        Stale = [long]$match.Groups[5].Value
        Generation = [long]$match.Groups[6].Value
        OffMain = [long]$match.Groups[7].Value
        Lag = [long]$match.Groups[8].Value
        Allocations = [long]$match.Groups[9].Value
    }
}

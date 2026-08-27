function ConvertFrom-P3bHarnessOutput
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

    $lines = @($OutputLines | ForEach-Object { "$_" })
    $errorLines = @($lines | Where-Object { $_ -match 'SCRIPT ERROR|ERROR:' })
    if ($errorLines.Count -ne 0)
    {
        throw "Godot emitted an error line:$([Environment]::NewLine)$($errorLines -join [Environment]::NewLine)"
    }

    $markerLines = @($lines | Where-Object {
        $_.StartsWith('GODOT_ALS_P3B_OK', [StringComparison]::Ordinal) -or
        $_.StartsWith('GODOT_ALS_P3B_FAIL', [StringComparison]::Ordinal)
    })
    if ($markerLines.Count -ne 1)
    {
        throw "Expected exactly one P3B result marker; observed $($markerLines.Count)."
    }

    $markerPattern = '\AGODOT_ALS_P3B_OK mode=(single|parallel) characters=(1|10) warmup=120 frames=600 digest=([0-9A-F]{16}) pose=([0-9A-F]{16}) missing=(\d+) stale=(\d+) generation=(\d+) off_main=(\d+) lag=(\d+) allocations=(\d+) p95_us=(\d+) p99_us=(\d+)\z'
    $marker = [regex]::Match($markerLines[0], $markerPattern)
    if (-not $marker.Success)
    {
        throw "Malformed or unsuccessful P3B marker: $($markerLines[0])"
    }

    $mode = $marker.Groups[1].Value
    $characters = [int]$marker.Groups[2].Value
    if ($mode -cne $ExpectedMode -or $characters -ne $ExpectedCharacterCount)
    {
        throw "Unexpected P3B identity: expected mode=$ExpectedMode characters=$ExpectedCharacterCount, observed mode=$mode characters=$characters."
    }

    $missing = [long]$marker.Groups[5].Value
    $stale = [long]$marker.Groups[6].Value
    $generation = [long]$marker.Groups[7].Value
    $offMain = [long]$marker.Groups[8].Value
    $lag = [long]$marker.Groups[9].Value
    $allocations = [long]$marker.Groups[10].Value
    if ($missing -ne 0 -or $stale -ne 0 -or $generation -ne 0 -or
        $lag -ne 0 -or $allocations -ne 0)
    {
        throw "P3B counters must be zero: missing=$missing stale=$stale generation=$generation lag=$lag allocations=$allocations."
    }

    $expectedOffMain = if ($mode -ceq 'parallel') { $characters } else { 0 }
    if ($offMain -ne $expectedOffMain)
    {
        throw "P3B worker affinity mismatch: expected off_main=$expectedOffMain, observed=$offMain."
    }

    $p95 = [long]$marker.Groups[11].Value
    $p99 = [long]$marker.Groups[12].Value
    if ($p95 -gt $p99)
    {
        throw "P3B timing percentiles are invalid: p95_us=$p95 p99_us=$p99."
    }

    $allocationLines = @($lines | Where-Object {
        $_.StartsWith('GODOT_ALS_P3B_ALLOC', [StringComparison]::Ordinal)
    })
    if ($allocationLines.Count -ne 1)
    {
        throw "Expected exactly one P3B allocation line; observed $($allocationLines.Count)."
    }
    $allocationPattern = '\AGODOT_ALS_P3B_ALLOC model=(\d+) controller=(\d+) skeleton=(\d+) exchange=(\d+) commit=(\d+)\z'
    $allocationMatch = [regex]::Match($allocationLines[0], $allocationPattern)
    if (-not $allocationMatch.Success)
    {
        throw "Malformed P3B allocation evidence: $($allocationLines[0])"
    }
    $allocationBuckets = @(1..5 | ForEach-Object { [long]$allocationMatch.Groups[$_].Value })
    if (@($allocationBuckets | Where-Object { $_ -ne 0 }).Count -ne 0)
    {
        throw "P3B allocation buckets must all be zero: $($allocationLines[0])"
    }

    $advanceLines = @($lines | Where-Object {
        $_.StartsWith('GODOT_ALS_P3B_ADVANCE', [StringComparison]::Ordinal)
    })
    if ($advanceLines.Count -ne $characters)
    {
        throw "Expected $characters P3B advance lines; observed $($advanceLines.Count)."
    }
    $advances = @{}
    foreach ($line in $advanceLines)
    {
        $advanceMatch = [regex]::Match(
            $line,
            '\AGODOT_ALS_P3B_ADVANCE character=(\d+) frames=(\d+)\z')
        if (-not $advanceMatch.Success)
        {
            throw "Malformed P3B advance evidence: $line"
        }
        $character = [int]$advanceMatch.Groups[1].Value
        $frames = [long]$advanceMatch.Groups[2].Value
        if ($character -lt 0 -or $character -ge $characters -or
            $advances.ContainsKey($character) -or $frames -lt 600)
        {
            throw "Invalid P3B advance evidence: $line"
        }
        $advances[$character] = $frames
    }

    $poseLines = @($lines | Where-Object {
        $_.StartsWith('GODOT_ALS_P3B_POSE', [StringComparison]::Ordinal)
    })
    if ($poseLines.Count -ne $characters)
    {
        throw "Expected $characters P3B pose lines; observed $($poseLines.Count)."
    }
    $poseChanges = @{}
    foreach ($line in $poseLines)
    {
        $poseMatch = [regex]::Match(
            $line,
            '\AGODOT_ALS_P3B_POSE character=(\d+) changes=(\d+)\z')
        if (-not $poseMatch.Success)
        {
            throw "Malformed P3B pose evidence: $line"
        }
        $character = [int]$poseMatch.Groups[1].Value
        $changes = [long]$poseMatch.Groups[2].Value
        if ($character -lt 0 -or $character -ge $characters -or
            $poseChanges.ContainsKey($character) -or $changes -le 0)
        {
            throw "Invalid P3B pose evidence: $line"
        }
        $poseChanges[$character] = $changes
    }

    $replacementLines = @($lines | Where-Object {
        $_.StartsWith('GODOT_ALS_P3B_REPLACEMENT', [StringComparison]::Ordinal)
    })
    if ($replacementLines.Count -ne 1 -or
        $replacementLines[0] -cne 'GODOT_ALS_P3B_REPLACEMENT character=0 old_generation_rejected=1')
    {
        throw 'P3B production replacement evidence is missing or malformed.'
    }

    [pscustomobject]@{
        Mode = $mode
        Characters = $characters
        Digest = $marker.Groups[3].Value
        Pose = $marker.Groups[4].Value
        Missing = $missing
        Stale = $stale
        Generation = $generation
        OffMain = $offMain
        Lag = $lag
        Allocations = $allocations
        P95Microseconds = $p95
        P99Microseconds = $p99
        Advances = $advances
        PoseChanges = $poseChanges
        ModelAllocations = $allocationBuckets[0]
        ControllerAllocations = $allocationBuckets[1]
        SkeletonAllocations = $allocationBuckets[2]
        ExchangeAllocations = $allocationBuckets[3]
        CommitAllocations = $allocationBuckets[4]
    }
}

function Assert-P3bResultPair
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
        throw "P3B result pair identity mismatch for characters=$CharacterCount."
    }
    if ($Single.Digest -cne $Parallel.Digest)
    {
        throw "P3B digest mismatch for characters=${CharacterCount}: single=$($Single.Digest), parallel=$($Parallel.Digest)."
    }
    if ($Single.Pose -cne $Parallel.Pose)
    {
        throw "P3B pose mismatch for characters=${CharacterCount}: single=$($Single.Pose), parallel=$($Parallel.Pose)."
    }
}

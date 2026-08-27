function Test-P3bFailureMarkerLine
{
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string]$Line
    )

    return [regex]::IsMatch(
        $Line,
        '\AGODOT_ALS_P3B_FAIL(?:\z|\s)',
        [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
}

function Test-P3bSceneFailureMarkerLine
{
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string]$Line
    )

    return [regex]::IsMatch(
        $Line,
        '(?:\A|\s)GODOT_ALS_[A-Z0-9_]*FAIL(?:\z|\s)',
        [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
}

function Get-P3bExpectedGraphDirectionDigest
{
    return 'DD72BD02BE20DCC3'
}

function Get-P3bExpectedGraphMarkerPattern
{
    $directionDigest = Get-P3bExpectedGraphDirectionDigest
    return "\AGODOT_ALS_P3B_GRAPH_OK transitions=5 direction_poses=4 rotation_modes=3 direction_digest=($directionDigest) digest=([0-9A-F]{16})\z"
}

function Get-P3bExpectedMarkerName
{
    param(
        [Parameter(Mandatory)]
        [string]$Expectation,
        [Parameter(Mandatory)]
        [bool]$IsRegex
    )

    if (-not $IsRegex)
    {
        return $Expectation.Split(' ', 2)[0]
    }

    $nameMatch = [regex]::Match(
        $Expectation,
        '\A\\A(?<name>[A-Z0-9_]+)',
        [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $nameMatch.Success -or
        -not $Expectation.EndsWith('\z', [StringComparison]::Ordinal))
    {
        throw "Scene-gate regex markers must use anchored uppercase marker names: $Expectation"
    }
    return $nameMatch.Groups['name'].Value
}

function Invoke-P3bSceneGate
{
    param(
        [Parameter(Mandatory)]
        [string]$PhaseName,
        [Parameter(Mandatory)]
        [string]$GodotExecutable,
        [Parameter(Mandatory)]
        [string]$ProjectRoot,
        [Parameter(Mandatory)]
        [string]$ScenePath,
        [AllowEmptyCollection()]
        [string[]]$SceneArguments = @(),
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$ExpectedExactMarkers,
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$ExpectedRegexMarkers
    )

    if (@($ExpectedExactMarkers | Select-Object -Unique).Count -ne $ExpectedExactMarkers.Count -or
        @($ExpectedRegexMarkers | Select-Object -Unique).Count -ne $ExpectedRegexMarkers.Count)
    {
        throw "$PhaseName scene gate contains duplicate marker expectations."
    }

    $resolvedRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
    $godotArguments = @('--headless', '--path', $resolvedRoot, $ScenePath)
    if ($SceneArguments.Count -ne 0)
    {
        $godotArguments += '--'
        $godotArguments += $SceneArguments
    }

    $sceneOutput = @(& $GodotExecutable @godotArguments *>&1)
    $sceneExitCode = $LASTEXITCODE
    $sceneOutput | ForEach-Object { Write-Host $_ }
    $lines = @($sceneOutput | ForEach-Object { "$_" })
    if ($sceneExitCode -ne 0)
    {
        throw "$PhaseName scene gate exited with code $sceneExitCode."
    }

    $errorLines = @($lines | Where-Object { $_ -match 'SCRIPT ERROR:|ERROR:' })
    if ($errorLines.Count -ne 0)
    {
        throw "$PhaseName scene gate emitted an error line:$([Environment]::NewLine)$($errorLines -join [Environment]::NewLine)"
    }

    $failureLines = @($lines | Where-Object {
        Test-P3bSceneFailureMarkerLine -Line $_
    })
    if ($failureLines.Count -ne 0)
    {
        throw "$PhaseName scene gate emitted an ALS failure marker:$([Environment]::NewLine)$($failureLines -join [Environment]::NewLine)"
    }

    $expectedMarkerNames = [System.Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    foreach ($expectedMarker in $ExpectedExactMarkers)
    {
        $markerName = Get-P3bExpectedMarkerName -Expectation $expectedMarker -IsRegex $false
        if (-not $expectedMarkerNames.Add($markerName))
        {
            throw "$PhaseName scene gate contains duplicate marker name '$markerName'."
        }
        $candidateLines = @($lines | Where-Object {
            $_ -ceq $markerName -or
            $_.StartsWith("$markerName ", [StringComparison]::Ordinal)
        })
        $matches = @($lines | Where-Object { $_ -ceq $expectedMarker })
        if ($candidateLines.Count -ne 1 -or $matches.Count -ne 1)
        {
            throw "$PhaseName scene gate expected exactly one well-formed '$expectedMarker' marker; observed candidates=$($candidateLines.Count) matches=$($matches.Count)."
        }
    }

    foreach ($expectedPattern in $ExpectedRegexMarkers)
    {
        $markerName = Get-P3bExpectedMarkerName -Expectation $expectedPattern -IsRegex $true
        if (-not $expectedMarkerNames.Add($markerName))
        {
            throw "$PhaseName scene gate contains duplicate marker name '$markerName'."
        }
        $candidateLines = @($lines | Where-Object {
            $_ -ceq $markerName -or
            $_.StartsWith("$markerName ", [StringComparison]::Ordinal)
        })
        $matches = @($lines | Where-Object {
            [regex]::IsMatch(
                $_,
                $expectedPattern,
                [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
        })
        if ($candidateLines.Count -ne 1 -or $matches.Count -ne 1)
        {
            throw "$PhaseName scene gate expected exactly one well-formed '$expectedPattern' regex marker; observed candidates=$($candidateLines.Count) matches=$($matches.Count)."
        }
    }

    return $lines
}

function ConvertFrom-P3bGraphOutput
{
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]]$OutputLines
    )

    $lines = @($OutputLines | ForEach-Object { "$_" })
    $errorLines = @($lines | Where-Object { $_ -match 'SCRIPT ERROR:|ERROR:' })
    if ($errorLines.Count -ne 0)
    {
        throw "Godot emitted a graph error line:$([Environment]::NewLine)$($errorLines -join [Environment]::NewLine)"
    }
    $failureLines = @($lines | Where-Object {
        Test-P3bSceneFailureMarkerLine -Line $_
    })
    if ($failureLines.Count -ne 0)
    {
        throw "Godot emitted a graph failure marker:$([Environment]::NewLine)$($failureLines -join [Environment]::NewLine)"
    }

    $markerLines = @($lines | Where-Object {
        $_.StartsWith('GODOT_ALS_P3B_GRAPH_OK', [StringComparison]::Ordinal)
    })
    if ($markerLines.Count -ne 1)
    {
        throw "Expected exactly one P3B graph result marker; observed $($markerLines.Count)."
    }

    $marker = [regex]::Match(
        $markerLines[0],
        (Get-P3bExpectedGraphMarkerPattern),
        [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $marker.Success)
    {
        throw "Malformed or unsuccessful P3B graph marker: $($markerLines[0])"
    }

    return [pscustomobject]@{
        DirectionDigest = $marker.Groups[1].Value
        Digest = $marker.Groups[2].Value
    }
}

function Assert-P3bGraphPair
{
    param(
        [Parameter(Mandatory)]
        [AllowNull()]
        [object]$First,
        [Parameter(Mandatory)]
        [AllowNull()]
        [object]$Second
    )

    $firstValues = @{}
    $secondValues = @{}
    foreach ($propertyName in @('DirectionDigest', 'Digest'))
    {
        $firstValue = (Get-P3bRequiredProperty $First $propertyName 'First P3B graph').Value
        $secondValue = (Get-P3bRequiredProperty $Second $propertyName 'Second P3B graph').Value
        if ($firstValue -isnot [string] -or $firstValue -cnotmatch '\A[0-9A-F]{16}\z' -or
            $secondValue -isnot [string] -or $secondValue -cnotmatch '\A[0-9A-F]{16}\z')
        {
            throw "P3B graph property '$propertyName' must be an uppercase 16-hex string."
        }
        $firstValues[$propertyName] = $firstValue
        $secondValues[$propertyName] = $secondValue
    }

    $expectedDirectionDigest = Get-P3bExpectedGraphDirectionDigest
    if ($firstValues.DirectionDigest -cne $expectedDirectionDigest -or
        $secondValues.DirectionDigest -cne $expectedDirectionDigest)
    {
        throw "P3B graph direction digest must match the formal asset golden '$expectedDirectionDigest': first=$($firstValues.DirectionDigest), second=$($secondValues.DirectionDigest)."
    }

    if ($firstValues.DirectionDigest -cne $secondValues.DirectionDigest)
    {
        throw "P3B graph direction digest mismatch: first=$($firstValues.DirectionDigest), second=$($secondValues.DirectionDigest)."
    }
    if ($firstValues.Digest -cne $secondValues.Digest)
    {
        throw "P3B graph digest mismatch: first=$($firstValues.Digest), second=$($secondValues.Digest)."
    }
}

function Get-P3bRequiredProperty
{
    param(
        [AllowNull()]
        [object]$InputObject,
        [Parameter(Mandatory)]
        [string]$PropertyName,
        [Parameter(Mandatory)]
        [string]$Context
    )

    if ($null -eq $InputObject)
    {
        throw "$Context result is null."
    }

    $property = $InputObject.PSObject.Properties[$PropertyName]
    if ($null -eq $property)
    {
        throw "$Context result is missing required property '$PropertyName'."
    }

    return $property
}

function Get-P3bValidatedSummaries
{
    param(
        [AllowNull()]
        [object]$InputObject,
        [Parameter(Mandatory)]
        [string]$Context
    )

    $values = @{}
    foreach ($propertyName in @('Digest', 'Pose', 'FullPose', 'Root'))
    {
        $property = Get-P3bRequiredProperty `
            -InputObject $InputObject `
            -PropertyName $propertyName `
            -Context $Context
        $value = $property.Value
        if ($value -isnot [string] -or $value -cnotmatch '\A[0-9A-F]{16}\z')
        {
            throw "$Context property '$propertyName' must be an uppercase 16-hex string."
        }
        $values[$propertyName] = $value
    }

    return [pscustomobject]$values
}

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
    $errorLines = @($lines | Where-Object { $_ -match 'SCRIPT ERROR:|ERROR:' })
    if ($errorLines.Count -ne 0)
    {
        throw "Godot emitted an error line:$([Environment]::NewLine)$($errorLines -join [Environment]::NewLine)"
    }

    $markerLines = @($lines | Where-Object {
        $_.StartsWith('GODOT_ALS_P3B_OK', [StringComparison]::Ordinal) -or
        (Test-P3bFailureMarkerLine -Line $_)
    })
    if ($markerLines.Count -ne 1)
    {
        throw "Expected exactly one P3B result marker; observed $($markerLines.Count)."
    }

    $markerPattern = '\AGODOT_ALS_P3B_OK mode=(single|parallel) characters=(1|10) warmup=120 frames=600 digest=([0-9A-F]{16}) pose=([0-9A-F]{16}) full_pose=([0-9A-F]{16}) root=([0-9A-F]{16}) missing=(\d+) stale=(\d+) generation=(\d+) off_main=(\d+) lag=(\d+) allocations=(\d+) p95_us=(\d+) p99_us=(\d+)\z'
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

    $missing = [long]$marker.Groups[7].Value
    $stale = [long]$marker.Groups[8].Value
    $generation = [long]$marker.Groups[9].Value
    $offMain = [long]$marker.Groups[10].Value
    $lag = [long]$marker.Groups[11].Value
    $allocations = [long]$marker.Groups[12].Value
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

    $p95 = [long]$marker.Groups[13].Value
    $p99 = [long]$marker.Groups[14].Value
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
        FullPose = $marker.Groups[5].Value
        Root = $marker.Groups[6].Value
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
        [AllowNull()]
        [object]$Single,
        [Parameter(Mandatory)]
        [AllowNull()]
        [object]$Parallel,
        [Parameter(Mandatory)]
        [ValidateSet(1, 10)]
        [int]$CharacterCount
    )

    $singleMode = (Get-P3bRequiredProperty $Single Mode 'Single P3B').Value
    $parallelMode = (Get-P3bRequiredProperty $Parallel Mode 'Parallel P3B').Value
    $singleCharacters = (Get-P3bRequiredProperty $Single Characters 'Single P3B').Value
    $parallelCharacters = (Get-P3bRequiredProperty $Parallel Characters 'Parallel P3B').Value
    if ($singleMode -isnot [string] -or $singleMode -cne 'single' -or
        $parallelMode -isnot [string] -or $parallelMode -cne 'parallel' -or
        $singleCharacters -isnot [int] -or $singleCharacters -ne $CharacterCount -or
        $parallelCharacters -isnot [int] -or $parallelCharacters -ne $CharacterCount)
    {
        throw "P3B result pair identity mismatch for characters=$CharacterCount."
    }

    $singleSummaries = Get-P3bValidatedSummaries $Single 'Single P3B'
    $parallelSummaries = Get-P3bValidatedSummaries $Parallel 'Parallel P3B'
    if ($singleSummaries.Digest -cne $parallelSummaries.Digest)
    {
        throw "P3B digest mismatch for characters=${CharacterCount}: single=$($singleSummaries.Digest), parallel=$($parallelSummaries.Digest)."
    }
    if ($singleSummaries.Pose -cne $parallelSummaries.Pose)
    {
        throw "P3B pose mismatch for characters=${CharacterCount}: single=$($singleSummaries.Pose), parallel=$($parallelSummaries.Pose)."
    }
    if ($singleSummaries.FullPose -cne $parallelSummaries.FullPose)
    {
        throw "P3B full-pose mismatch for characters=${CharacterCount}: single=$($singleSummaries.FullPose), parallel=$($parallelSummaries.FullPose)."
    }
    if ($singleSummaries.Root -cne $parallelSummaries.Root)
    {
        throw "P3B root mismatch for characters=${CharacterCount}: single=$($singleSummaries.Root), parallel=$($parallelSummaries.Root)."
    }
}

function ConvertFrom-P3bFrameOrderOutput
{
    param(
        [Parameter(Mandatory)]
        [object[]]$OutputLines,
        [Parameter(Mandatory)]
        [ValidateSet('single', 'parallel')]
        [string]$ExpectedMode
    )

    $lines = @($OutputLines | ForEach-Object { "$_" })
    $errorLines = @($lines | Where-Object { $_ -match 'SCRIPT ERROR:|ERROR:' })
    if ($errorLines.Count -ne 0)
    {
        throw "Godot emitted a frame-order error line:$([Environment]::NewLine)$($errorLines -join [Environment]::NewLine)"
    }

    $markerLines = @($lines | Where-Object {
        $_.StartsWith('GODOT_ALS_P3B_FRAME_ORDER_OK', [StringComparison]::Ordinal) -or
        (Test-P3bFailureMarkerLine -Line $_)
    })
    if ($markerLines.Count -ne 1)
    {
        throw "Expected exactly one P3B frame-order result marker; observed $($markerLines.Count)."
    }

    $markerPattern = '\AGODOT_ALS_P3B_FRAME_ORDER_OK mode=(single|parallel) frames=180 digest=([0-9A-F]{16}) pose=([0-9A-F]{16}) full_pose=([0-9A-F]{16}) root=([0-9A-F]{16}) lag=0 stale=0 generation=1 old_generation_rejected=1 retired_released=1 max_visible=1 real_rig_visibility=1 recovery_zero_visible=1\z'
    $marker = [regex]::Match($markerLines[0], $markerPattern)
    if (-not $marker.Success)
    {
        throw "Malformed or unsuccessful P3B frame-order marker: $($markerLines[0])"
    }

    $mode = $marker.Groups[1].Value
    if ($mode -cne $ExpectedMode)
    {
        throw "Unexpected P3B frame-order mode: expected $ExpectedMode, observed $mode."
    }

    [pscustomobject]@{
        Mode = $mode
        Frames = 180
        Digest = $marker.Groups[2].Value
        Pose = $marker.Groups[3].Value
        FullPose = $marker.Groups[4].Value
        Root = $marker.Groups[5].Value
    }
}

function Assert-P3bFrameOrderPair
{
    param(
        [Parameter(Mandatory)]
        [AllowNull()]
        [object]$Single,
        [Parameter(Mandatory)]
        [AllowNull()]
        [object]$Parallel
    )

    $singleMode = (Get-P3bRequiredProperty $Single Mode 'Single P3B frame-order').Value
    $parallelMode = (Get-P3bRequiredProperty $Parallel Mode 'Parallel P3B frame-order').Value
    $singleFrames = (Get-P3bRequiredProperty $Single Frames 'Single P3B frame-order').Value
    $parallelFrames = (Get-P3bRequiredProperty $Parallel Frames 'Parallel P3B frame-order').Value
    if ($singleMode -isnot [string] -or $singleMode -cne 'single' -or
        $parallelMode -isnot [string] -or $parallelMode -cne 'parallel' -or
        $singleFrames -isnot [int] -or $singleFrames -ne 180 -or
        $parallelFrames -isnot [int] -or $parallelFrames -ne 180)
    {
        throw 'P3B frame-order result pair identity mismatch.'
    }

    $singleSummaries = Get-P3bValidatedSummaries $Single 'Single P3B frame-order'
    $parallelSummaries = Get-P3bValidatedSummaries $Parallel 'Parallel P3B frame-order'
    if ($singleSummaries.Digest -cne $parallelSummaries.Digest)
    {
        throw "P3B frame-order digest mismatch: single=$($singleSummaries.Digest), parallel=$($parallelSummaries.Digest)."
    }
    if ($singleSummaries.Pose -cne $parallelSummaries.Pose)
    {
        throw "P3B frame-order pose mismatch: single=$($singleSummaries.Pose), parallel=$($parallelSummaries.Pose)."
    }
    if ($singleSummaries.FullPose -cne $parallelSummaries.FullPose)
    {
        throw "P3B frame-order full-pose mismatch: single=$($singleSummaries.FullPose), parallel=$($parallelSummaries.FullPose)."
    }
    if ($singleSummaries.Root -cne $parallelSummaries.Root)
    {
        throw "P3B frame-order root mismatch: single=$($singleSummaries.Root), parallel=$($parallelSummaries.Root)."
    }
}

function Assert-P3bChildGateOutput
{
    param(
        [Parameter(Mandatory)]
        [string]$PhaseName,
        [Parameter(Mandatory)]
        [object[]]$OutputLines,
        [Parameter(Mandatory)]
        [int]$ExitCode,
        [Parameter(Mandatory)]
        [string]$ExpectedMarker
    )

    $lines = @($OutputLines | ForEach-Object { "$_" })
    if ($ExitCode -ne 0)
    {
        throw "$PhaseName gate exited with code $ExitCode."
    }

    $errorLines = @($lines | Where-Object { $_ -match 'SCRIPT ERROR:|ERROR:' })
    if ($errorLines.Count -ne 0)
    {
        throw "$PhaseName gate emitted an error line:$([Environment]::NewLine)$($errorLines -join [Environment]::NewLine)"
    }

    $failureLines = @($lines | Where-Object {
        Test-P3bSceneFailureMarkerLine -Line $_
    })
    if ($failureLines.Count -ne 0)
    {
        throw "$PhaseName gate emitted an ALS failure marker:$([Environment]::NewLine)$($failureLines -join [Environment]::NewLine)"
    }

    $markerLines = @($lines | Where-Object { $_ -ceq $ExpectedMarker })
    if ($markerLines.Count -ne 1)
    {
        throw "$PhaseName gate expected exactly one '$ExpectedMarker' marker; observed $($markerLines.Count)."
    }
}

function Get-P3bCompletionMarker
{
    param(
        [Parameter(Mandatory)]
        [bool]$RegressionSkipped
    )

    if ($RegressionSkipped)
    {
        return 'P3B_FOCUSED_VERIFICATION_OK regression=skipped'
    }

    return 'P3B_VERIFICATION_OK'
}

function Assert-P3bCleanWorktree
{
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    $resolvedRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
    $statusOutput = @(& git -C $resolvedRoot status --porcelain --untracked-files=all *>&1)
    $statusExitCode = $LASTEXITCODE
    if ($statusExitCode -ne 0)
    {
        throw "Could not inspect P3B repository status (exit $statusExitCode):$([Environment]::NewLine)$($statusOutput -join [Environment]::NewLine)"
    }

    if ($statusOutput.Count -ne 0)
    {
        throw "P3B full verification requires a clean worktree:$([Environment]::NewLine)$($statusOutput -join [Environment]::NewLine)"
    }
}

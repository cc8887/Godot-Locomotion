function Get-P3aExpectedDigest
{
    param(
        [Parameter(Mandatory)]
        [ValidateSet(1, 10)]
        [int]$CharacterCount
    )

    if ($CharacterCount -eq 1)
    {
        return 'B79EDC1516A133F9'
    }

    return 'D81D16BAA88519DC'
}

function Get-P3aCompletionMarker
{
    param(
        [Parameter(Mandatory)]
        [bool]$RegressionSkipped
    )

    if ($RegressionSkipped)
    {
        return 'P3A_FOCUSED_VERIFICATION_OK regression=skipped'
    }

    return 'P3A_VERIFICATION_OK'
}

function Assert-P3aRepositoryClosure
{
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryRoot,
        [Parameter(Mandatory)]
        [string]$BaseCommit
    )

    $resolvedRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
    $resolvedBaseOutput = @(& git -C $resolvedRoot rev-parse --verify "${BaseCommit}^{commit}" 2>&1)
    if ($LASTEXITCODE -ne 0 -or
        $resolvedBaseOutput.Count -ne 1 -or
        "$($resolvedBaseOutput[0])" -notmatch '\A[0-9a-fA-F]{40}\z')
    {
        throw "P3A base commit does not exist: $BaseCommit"
    }
    $resolvedBaseCommit = "$($resolvedBaseOutput[0])"

    $ancestorOutput = @(& git -C $resolvedRoot merge-base --is-ancestor $resolvedBaseCommit HEAD 2>&1)
    $ancestorExitCode = $LASTEXITCODE
    if ($ancestorExitCode -eq 1)
    {
        throw "P3A base commit is not an ancestor of HEAD: $resolvedBaseCommit"
    }
    if ($ancestorExitCode -ne 0)
    {
        throw "Could not verify the P3A base ancestry:$([Environment]::NewLine)$($ancestorOutput -join [Environment]::NewLine)"
    }

    $committedDiffOutput = @(& git -C $resolvedRoot diff "${resolvedBaseCommit}..HEAD" --check -- 2>&1)
    if ($LASTEXITCODE -ne 0)
    {
        throw "Committed P3A range whitespace check failed:$([Environment]::NewLine)$($committedDiffOutput -join [Environment]::NewLine)"
    }

    $indexDiffOutput = @(& git -C $resolvedRoot diff --cached --check -- 2>&1)
    if ($LASTEXITCODE -ne 0)
    {
        throw "P3A index whitespace check failed:$([Environment]::NewLine)$($indexDiffOutput -join [Environment]::NewLine)"
    }

    $worktreeDiffOutput = @(& git -C $resolvedRoot diff --check -- 2>&1)
    if ($LASTEXITCODE -ne 0)
    {
        throw "P3A worktree whitespace check failed:$([Environment]::NewLine)$($worktreeDiffOutput -join [Environment]::NewLine)"
    }

    $trackedFiles = @(& git -C $resolvedRoot ls-files 2>&1)
    if ($LASTEXITCODE -ne 0)
    {
        throw "Could not enumerate tracked repository files:$([Environment]::NewLine)$($trackedFiles -join [Environment]::NewLine)"
    }

    $forbiddenPattern = '(?i)(^|/)(\.godot|\.mono|bin|obj|Binaries|Intermediate|Saved|DerivedDataCache|StagedBuilds|Cooked)(/|$)|^(assets/generated|artifacts/(?!\.gdignore$)|benchmark-results/(?!\.gdignore$))|\.(dll|pdb|modules|target|ubulk|uexp|pak|ucas|utoc|sav|log)$'
    $forbiddenFiles = @($trackedFiles | ForEach-Object { "$_".Replace('\', '/') } |
        Where-Object { $_ -match $forbiddenPattern })
    if ($forbiddenFiles.Count -ne 0)
    {
        throw "Tracked generated/build output is forbidden:$([Environment]::NewLine)$($forbiddenFiles -join [Environment]::NewLine)"
    }
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

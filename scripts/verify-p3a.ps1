param(
    [Parameter(Mandatory)]
    [string]$GodotExecutable,
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [switch]$SkipRegression
)

$ErrorActionPreference = 'Stop'
$projectRootPath = (Resolve-Path -LiteralPath $ProjectRoot).Path
$solutionPath = Join-Path $projectRootPath 'GodotALS.sln'
$scenePath = 'res://scenes/tests/p3a_locomotion_harness.tscn'
$markerPattern = '(?m)^GODOT_ALS_P3A_OK mode=(single|parallel) characters=(1|10) warmup=120 frames=600 digest=([0-9A-F]{16}) missing=(\d+) stale=(\d+) generation=(\d+) off_main=(\d+) lag=(\d+) allocations=(\d+)$'

# Tier promotion can charge runtime bookkeeping to an otherwise allocation-free measured frame.
$env:DOTNET_TieredCompilation = '0'

if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf)) {
    throw "Godot executable not found: $GodotExecutable"
}

dotnet restore $solutionPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $solutionPath --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $SkipRegression) {
    dotnet test $solutionPath --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $motorOutput = & $GodotExecutable --headless --path $projectRootPath `
        'res://scenes/tests/p3a_motor_smoke.tscn' 2>&1
    $motorExitCode = $LASTEXITCODE
    $motorOutput | ForEach-Object { Write-Host $_ }
    $motorErrors = @($motorOutput | Where-Object { "$_" -match 'SCRIPT ERROR|ERROR:' })
    if ($motorExitCode -ne 0 -or $motorErrors.Count -ne 0) {
        throw "Godot P3A motor smoke failed with exit code $motorExitCode."
    }
    if (-not (($motorOutput -join [Environment]::NewLine) -match '(?m)^GODOT_ALS_P3A_MOTOR_OK cases=7$')) {
        throw 'Godot P3A motor smoke marker was not emitted.'
    }
}

function Invoke-P3aHarness {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('single', 'parallel')]
        [string]$Mode,
        [Parameter(Mandatory)]
        [ValidateSet(1, 10)]
        [int]$CharacterCount
    )

    $godotOutput = & $GodotExecutable --headless --path $projectRootPath $scenePath -- `
        "--als-mode=$Mode" "--als-characters=$CharacterCount" 2>&1
    $godotExitCode = $LASTEXITCODE
    $godotOutput | ForEach-Object { Write-Host $_ }
    $joinedOutput = $godotOutput -join [Environment]::NewLine
    $errorLines = @($godotOutput | Where-Object { "$_" -match 'SCRIPT ERROR|ERROR:' })
    $failLines = @($godotOutput | Where-Object { "$_" -match '^GODOT_ALS_P3A_FAIL(?: |$)' })

    if ($godotExitCode -ne 0 -or $errorLines.Count -ne 0 -or $failLines.Count -ne 0) {
        $details = $errorLines -join [Environment]::NewLine
        throw "Godot P3A harness failed for mode=$Mode characters=$CharacterCount with exit code $godotExitCode.$([Environment]::NewLine)$details"
    }

    $matches = [regex]::Matches($joinedOutput, $markerPattern)
    if ($matches.Count -ne 1) {
        throw "Expected exactly one Godot P3A marker for mode=$Mode characters=$CharacterCount; observed $($matches.Count)."
    }

    $match = $matches[0]
    return [pscustomobject]@{
        Mode = $match.Groups[1].Value
        Characters = [int]$match.Groups[2].Value
        Digest = $match.Groups[3].Value
        Missing = [long]$match.Groups[4].Value
        Stale = [long]$match.Groups[5].Value
        Generation = [long]$match.Groups[6].Value
        OffMain = [long]$match.Groups[7].Value
        Lag = [long]$match.Groups[8].Value
        Allocations = [long]$match.Groups[9].Value
    }
}

foreach ($characterCount in @(1, 10)) {
    $single = Invoke-P3aHarness -Mode single -CharacterCount $characterCount
    $parallel = Invoke-P3aHarness -Mode parallel -CharacterCount $characterCount

    if ($single.Mode -ne 'single' -or $parallel.Mode -ne 'parallel' -or
        $single.Characters -ne $characterCount -or $parallel.Characters -ne $characterCount) {
        throw "Godot P3A harness reported unexpected mode or character count for characters=$characterCount."
    }
    if ($single.Digest -cne $parallel.Digest) {
        throw "P3A digest mismatch for characters=${characterCount}: single=$($single.Digest), parallel=$($parallel.Digest)."
    }

    foreach ($result in @($single, $parallel)) {
        if ($result.Missing -ne 0 -or $result.Stale -ne 0 -or $result.Generation -ne 0 -or
            $result.Lag -ne 0 -or $result.Allocations -ne 0) {
            throw "P3A semantics failed for mode=$($result.Mode) characters=${characterCount}: missing=$($result.Missing) stale=$($result.Stale) generation=$($result.Generation) lag=$($result.Lag) allocations=$($result.Allocations)."
        }
    }
    if ($single.OffMain -ne 0 -or $parallel.OffMain -ne $characterCount) {
        throw "P3A worker affinity failed for characters=${characterCount}: single=$($single.OffMain) parallel=$($parallel.OffMain)."
    }
}

Write-Output 'P3A_VERIFICATION_OK'
exit 0

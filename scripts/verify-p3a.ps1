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
. (Join-Path $PSScriptRoot 'p3a-verification-functions.ps1')

# Tier promotion can charge runtime bookkeeping to an otherwise allocation-free measured frame.
$env:DOTNET_TieredCompilation = '0'
$env:COMPlus_TieredCompilation = '0'

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
    $errorLines = @($godotOutput | Where-Object { "$_" -match 'SCRIPT ERROR|ERROR:' })

    if ($godotExitCode -ne 0 -or $errorLines.Count -ne 0) {
        $details = $errorLines -join [Environment]::NewLine
        throw "Godot P3A harness failed for mode=$Mode characters=$CharacterCount with exit code $godotExitCode.$([Environment]::NewLine)$details"
    }

    return ConvertFrom-P3aHarnessOutput `
        -OutputLines $godotOutput `
        -ExpectedMode $Mode `
        -ExpectedCharacterCount $CharacterCount
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

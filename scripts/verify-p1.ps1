param(
    [Parameter(Mandatory)]
    [string]$GodotExecutable,
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [ValidateRange(30, 600)]
    [int]$Frames = 90,
    [ValidateNotNullOrEmpty()]
    [int[]]$CharacterCounts = @(1, 10, 16, 32)
)

$ErrorActionPreference = 'Stop'
$markerPattern = 'GODOT_ALS_P1_OK mode=(single|parallel) characters=(\d+) frames=(\d+) digest=([0-9A-F]{16}) missing=(\d+) replacements=(\d+) allocations=(\d+) off_main=(\d+)'

if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf)) {
    throw "Godot executable not found: $GodotExecutable"
}

foreach ($characterCount in $CharacterCounts) {
    if ($characterCount -lt 1 -or $characterCount -gt 32) {
        throw "Character count must be between 1 and 32: $characterCount"
    }
}

dotnet restore (Join-Path $ProjectRoot 'GodotALS.sln')
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet build (Join-Path $ProjectRoot 'GodotALS.sln') --no-restore
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet test (Join-Path $ProjectRoot 'GodotALS.sln') --no-build --no-restore
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

function Invoke-P1Harness {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('single', 'parallel')]
        [string]$Mode,
        [Parameter(Mandatory)]
        [int]$CharacterCount
    )

    $godotOutput = & $GodotExecutable --headless --path $ProjectRoot `
        'res://scenes/tests/p1_dispatch_harness.tscn' -- `
        "--als-mode=$Mode" "--als-characters=$CharacterCount" "--als-frames=$Frames" 2>&1
    $godotExitCode = $LASTEXITCODE
    $godotOutput | ForEach-Object { Write-Host $_ }

    if ($godotExitCode -ne 0) {
        throw "Godot P1 harness failed for mode=$Mode characters=$CharacterCount with exit code $godotExitCode."
    }

    $joinedOutput = $godotOutput -join [Environment]::NewLine
    $match = [regex]::Match($joinedOutput, $markerPattern)
    if (-not $match.Success) {
        throw "Godot P1 marker was not emitted for mode=$Mode characters=$CharacterCount."
    }

    return [pscustomobject]@{
        Mode = $match.Groups[1].Value
        Characters = [int]$match.Groups[2].Value
        Frames = [int]$match.Groups[3].Value
        Digest = $match.Groups[4].Value
        Missing = [long]$match.Groups[5].Value
        Replacements = [long]$match.Groups[6].Value
        Allocations = [long]$match.Groups[7].Value
        OffMain = [long]$match.Groups[8].Value
    }
}

foreach ($characterCount in $CharacterCounts) {
    $single = Invoke-P1Harness -Mode single -CharacterCount $characterCount
    $parallel = Invoke-P1Harness -Mode parallel -CharacterCount $characterCount

    if ($single.Characters -ne $characterCount -or $parallel.Characters -ne $characterCount) {
        throw "Harness reported an unexpected character count for requested count $characterCount."
    }

    if ($single.Frames -ne $Frames -or $parallel.Frames -ne $Frames) {
        throw "Harness reported an unexpected frame count for characters=$characterCount."
    }

    if ($single.Digest -ne $parallel.Digest) {
        throw "Digest mismatch for characters=${characterCount}: single=$($single.Digest), parallel=$($parallel.Digest)."
    }

    foreach ($result in @($single, $parallel)) {
        if ($result.Missing -ne 0) {
            throw "Missing results in mode=$($result.Mode) characters=${characterCount}: $($result.Missing)."
        }

        if ($result.Allocations -ne 0) {
            throw "Steady-state allocation in mode=$($result.Mode) characters=${characterCount}: $($result.Allocations) bytes."
        }

        $expectedReplacements = [Math]::Floor(($Frames - 1) / 30)
        if ($result.Replacements -ne $expectedReplacements) {
            throw "Unexpected replacements in mode=$($result.Mode) characters=${characterCount}: expected=$expectedReplacements actual=$($result.Replacements)."
        }
    }

    if ($single.OffMain -ne 0) {
        throw "Single mode observed off-main workers for characters=${characterCount}: $($single.OffMain)."
    }

    if ($parallel.OffMain -le 0) {
        throw "Parallel mode did not observe an off-main worker for characters=$characterCount."
    }
}

Write-Output 'P1_VERIFICATION_OK'
exit 0

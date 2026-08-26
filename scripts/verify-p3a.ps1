param(
    [Parameter(Mandatory)]
    [string]$GodotExecutable,
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [switch]$SkipRegression
)

$ErrorActionPreference = 'Stop'
$projectRootPath = (Resolve-Path -LiteralPath $ProjectRoot).Path
$solutionPath = Join-Path $projectRootPath 'GodotALS.sln'
$godotProjectPath = Join-Path $projectRootPath 'GodotALS.csproj'
$scenePath = 'res://scenes/tests/p3a_locomotion_harness.tscn'
$p3aBaseCommit = 'e69f18bb3410d77ef50df38b073535b5e9f20635'
. (Join-Path $PSScriptRoot 'p3a-verification-functions.ps1')

# Tier promotion can charge runtime bookkeeping to an otherwise allocation-free measured frame.
$env:DOTNET_TieredCompilation = '0'
$env:COMPlus_TieredCompilation = '0'

if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf)) {
    throw "Godot executable not found: $GodotExecutable"
}

dotnet restore $solutionPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $solutionPath -c Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $SkipRegression) {
    $pesterResult = Invoke-Pester (Join-Path $projectRootPath 'tests\*.Tests.ps1') -PassThru
    if ($pesterResult.FailedCount -ne 0) {
        throw "P3A PowerShell regression suite failed: $($pesterResult.FailedCount) failing test(s)."
    }
}

# Godot's headless editor loads Debug/TOOLS assemblies. Optimize that host build explicitly;
# this runtime evidence is not an execution of the ExportRelease artifact built above.
dotnet build $godotProjectPath -c Debug -p:Optimize=true --no-restore --no-incremental
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $SkipRegression) {
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

    Assert-P3aResultPair -Single $single -Parallel $parallel -CharacterCount $characterCount

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

if (-not $SkipRegression) {
    $p2bScript = Join-Path $PSScriptRoot 'verify-p2b.ps1'
    & $p2bScript -GodotExecutable $GodotExecutable -ProjectRoot $projectRootPath -CleanImport
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $phaseScripts = @(
        'verify-p1.ps1',
        'verify-p0.ps1'
    )
    foreach ($phaseScriptName in $phaseScripts) {
        $phaseScript = Join-Path $PSScriptRoot $phaseScriptName
        & $phaseScript -GodotExecutable $GodotExecutable -ProjectRoot $projectRootPath
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    dotnet test $solutionPath -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Assert-P3aRepositoryClosure -RepositoryRoot $projectRootPath -BaseCommit $p3aBaseCommit
}

$completionMarker = Get-P3aCompletionMarker -RegressionSkipped ([bool]$SkipRegression)
Write-Output $completionMarker
exit 0

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
$scenePath = 'res://scenes/tests/p3b_animation_harness.tscn'
$p3aBaseCommit = 'e69f18bb3410d77ef50df38b073535b5e9f20635'
. (Join-Path $PSScriptRoot 'p3b-verification-functions.ps1')
. (Join-Path $PSScriptRoot 'p3a-verification-functions.ps1')

$env:DOTNET_TieredCompilation = '0'
$env:COMPlus_TieredCompilation = '0'

if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf))
{
    throw "Godot executable not found: $GodotExecutable"
}

dotnet restore $godotProjectPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $godotProjectPath -c Debug -p:Optimize=true --no-restore --no-incremental
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

function Invoke-P3bHarness
{
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
    if ($godotExitCode -ne 0)
    {
        throw "Godot P3B harness failed for mode=$Mode characters=$CharacterCount with exit code $godotExitCode."
    }

    return ConvertFrom-P3bHarnessOutput `
        -OutputLines $godotOutput `
        -ExpectedMode $Mode `
        -ExpectedCharacterCount $CharacterCount
}

foreach ($characterCount in @(1, 10))
{
    $single = Invoke-P3bHarness -Mode single -CharacterCount $characterCount
    $parallel = Invoke-P3bHarness -Mode parallel -CharacterCount $characterCount
    Assert-P3bResultPair -Single $single -Parallel $parallel -CharacterCount $characterCount
}

if (-not $SkipRegression)
{
    $p3aScript = Join-Path $PSScriptRoot 'verify-p3a.ps1'
    $p3aOutput = @(& $p3aScript -GodotExecutable $GodotExecutable -ProjectRoot $projectRootPath *>&1)
    $p3aExitCode = $LASTEXITCODE
    $p3aOutput | ForEach-Object { Write-Host $_ }
    Assert-P3bChildGateOutput `
        -PhaseName 'P3A' `
        -OutputLines $p3aOutput `
        -ExitCode $p3aExitCode `
        -ExpectedMarker 'P3A_VERIFICATION_OK'
    Assert-P3bChildGateOutput `
        -PhaseName 'P2B through P3A' `
        -OutputLines $p3aOutput `
        -ExitCode $p3aExitCode `
        -ExpectedMarker 'P2B_VERIFICATION_OK'
    Assert-P3bChildGateOutput `
        -PhaseName 'P1 through P3A' `
        -OutputLines $p3aOutput `
        -ExitCode $p3aExitCode `
        -ExpectedMarker 'P1_VERIFICATION_OK'
    Assert-P3bChildGateOutput `
        -PhaseName 'P0 through P3A' `
        -OutputLines $p3aOutput `
        -ExitCode $p3aExitCode `
        -ExpectedMarker 'P0_VERIFICATION_OK'

    $releaseTestOutput = @(dotnet test $solutionPath -c Release --no-restore *>&1)
    $releaseTestExitCode = $LASTEXITCODE
    $releaseTestOutput | ForEach-Object { Write-Host $_ }
    if ($releaseTestExitCode -ne 0)
    {
        throw "P3B Release tests exited with code $releaseTestExitCode."
    }
    $releaseTestErrors = @($releaseTestOutput | Where-Object { "$_" -match 'SCRIPT ERROR:|ERROR:' })
    if ($releaseTestErrors.Count -ne 0)
    {
        throw "P3B Release tests emitted an error line:$([Environment]::NewLine)$($releaseTestErrors -join [Environment]::NewLine)"
    }
    Write-Output 'P3B_RELEASE_TESTS_OK'

    Assert-P3aRepositoryClosure -RepositoryRoot $projectRootPath -BaseCommit $p3aBaseCommit
    Assert-P3bCleanWorktree -RepositoryRoot $projectRootPath
    Write-Output "P3B_REPOSITORY_CLOSURE_OK p3a_base=$p3aBaseCommit"
}

$completionMarker = Get-P3bCompletionMarker -RegressionSkipped ([bool]$SkipRegression)
Write-Output $completionMarker
exit 0

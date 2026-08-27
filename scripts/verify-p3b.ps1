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

$inputOutput = @(Invoke-P3bSceneGate `
    -PhaseName 'P3 demo input' `
    -GodotExecutable $GodotExecutable `
    -ProjectRoot $projectRootPath `
    -ScenePath 'res://scenes/tests/p3_demo_input_smoke.tscn' `
    -ExpectedExactMarkers @(
        'GODOT_ALS_P3_DEMO_INPUT_OK actions=11 directions=12 camera_basis=1 pitch=1 aiming=1 cleared=1 hud=1') `
    -ExpectedRegexMarkers @())

$libraryOutput = @(Invoke-P3bSceneGate `
    -PhaseName 'P3B animation library' `
    -GodotExecutable $GodotExecutable `
    -ProjectRoot $projectRootPath `
    -ScenePath 'res://scenes/tests/p3b_animation_library_smoke.tscn' `
    -ExpectedExactMarkers @(
        'GODOT_ALS_P3B_LIBRARY_OK bones=68 clips=28 skeletons=1',
        'GODOT_ALS_P3B_LIBRARY_LIFECYCLE_OK double_dispose=1 parent_free=1 partial=1 rebuild=1') `
    -ExpectedRegexMarkers @())

$presentationOutput = @(Invoke-P3bSceneGate `
    -PhaseName 'P3 presentation' `
    -GodotExecutable $GodotExecutable `
    -ProjectRoot $projectRootPath `
    -ScenePath 'res://scenes/tests/p3_presentation_smoke.tscn' `
    -ExpectedExactMarkers @() `
    -ExpectedRegexMarkers @(
        '\AGODOT_ALS_P3_PRESENTATION_OK yaws=3 identity=1 root=([0-9A-F]{16})\z'))

$initialRollbackOutput = @(Invoke-P3bSceneGate `
    -PhaseName 'P3 presentation initial rollback' `
    -GodotExecutable $GodotExecutable `
    -ProjectRoot $projectRootPath `
    -ScenePath 'res://scenes/tests/p3_presentation_smoke.tscn' `
    -SceneArguments @('--als-failure-policy=initial') `
    -ExpectedExactMarkers @() `
    -ExpectedRegexMarkers @(
        '\AGODOT_ALS_P3B_INITIAL_ROLLBACK_OK mode=parallel corrected=1 visual_ready=0 visible=0 full_pose=([0-9A-F]{16}) root=([0-9A-F]{16})\z'))

$graphOutputFirst = @(Invoke-P3bSceneGate `
    -PhaseName 'P3B animation graph first' `
    -GodotExecutable $GodotExecutable `
    -ProjectRoot $projectRootPath `
    -ScenePath 'res://scenes/tests/p3b_animation_graph_smoke.tscn' `
    -ExpectedExactMarkers @(
        'GODOT_ALS_P3B_GRAPH_LIFECYCLE_OK double_dispose=1 parent_free=1 partial=1 rebuild=1 borrowed=1') `
    -ExpectedRegexMarkers @(
        '\AGODOT_ALS_P3B_GRAPH_OK transitions=5 direction_poses=4 rotation_modes=3 direction_digest=([0-9A-F]{16}) digest=([0-9A-F]{16})\z'))
$graphFirst = ConvertFrom-P3bGraphOutput -OutputLines $graphOutputFirst

$graphOutputSecond = @(Invoke-P3bSceneGate `
    -PhaseName 'P3B animation graph second' `
    -GodotExecutable $GodotExecutable `
    -ProjectRoot $projectRootPath `
    -ScenePath 'res://scenes/tests/p3b_animation_graph_smoke.tscn' `
    -ExpectedExactMarkers @(
        'GODOT_ALS_P3B_GRAPH_LIFECYCLE_OK double_dispose=1 parent_free=1 partial=1 rebuild=1 borrowed=1') `
    -ExpectedRegexMarkers @(
        '\AGODOT_ALS_P3B_GRAPH_OK transitions=5 direction_poses=4 rotation_modes=3 direction_digest=([0-9A-F]{16}) digest=([0-9A-F]{16})\z'))
$graphSecond = ConvertFrom-P3bGraphOutput -OutputLines $graphOutputSecond
Assert-P3bGraphPair -First $graphFirst -Second $graphSecond

$frameOrderSingleOutput = @(Invoke-P3bSceneGate `
    -PhaseName 'P3B frame order single' `
    -GodotExecutable $GodotExecutable `
    -ProjectRoot $projectRootPath `
    -ScenePath 'res://scenes/tests/p3b_frame_order_smoke.tscn' `
    -SceneArguments @('--als-mode=single') `
    -ExpectedExactMarkers @() `
    -ExpectedRegexMarkers @(
        '\AGODOT_ALS_P3B_FRAME_ORDER_OK mode=(single|parallel) frames=180 digest=([0-9A-F]{16}) pose=([0-9A-F]{16}) full_pose=([0-9A-F]{16}) root=([0-9A-F]{16}) lag=0 stale=0 generation=1 old_generation_rejected=1 retired_released=1 max_visible=1 recovery_zero_visible=1\z'))
$frameOrderSingle = ConvertFrom-P3bFrameOrderOutput `
    -OutputLines $frameOrderSingleOutput `
    -ExpectedMode single

$frameOrderParallelOutput = @(Invoke-P3bSceneGate `
    -PhaseName 'P3B frame order parallel' `
    -GodotExecutable $GodotExecutable `
    -ProjectRoot $projectRootPath `
    -ScenePath 'res://scenes/tests/p3b_frame_order_smoke.tscn' `
    -SceneArguments @('--als-mode=parallel') `
    -ExpectedExactMarkers @() `
    -ExpectedRegexMarkers @(
        '\AGODOT_ALS_P3B_FRAME_ORDER_OK mode=(single|parallel) frames=180 digest=([0-9A-F]{16}) pose=([0-9A-F]{16}) full_pose=([0-9A-F]{16}) root=([0-9A-F]{16}) lag=0 stale=0 generation=1 old_generation_rejected=1 retired_released=1 max_visible=1 recovery_zero_visible=1\z'))
$frameOrderParallel = ConvertFrom-P3bFrameOrderOutput `
    -OutputLines $frameOrderParallelOutput `
    -ExpectedMode parallel
Assert-P3bFrameOrderPair -Single $frameOrderSingle -Parallel $frameOrderParallel

$demoOutput = @(Invoke-P3bSceneGate `
    -PhaseName 'P3 locomotion demo' `
    -GodotExecutable $GodotExecutable `
    -ProjectRoot $projectRootPath `
    -ScenePath 'res://scenes/demo/p3_locomotion_demo.tscn' `
    -SceneArguments @('--als-smoke-frames=300') `
    -ExpectedExactMarkers @(
        'GODOT_ALS_P3_DEMO_OK frames=300 errors=0 ready=1 visible=1 max_visible=1') `
    -ExpectedRegexMarkers @())

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

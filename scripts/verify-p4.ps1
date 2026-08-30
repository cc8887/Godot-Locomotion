param(
    [Parameter(Mandatory)]
    [string]$GodotExecutable,
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [switch]$Focused,
    [ValidateRange(1, 3600)][int]$BuildTimeoutSeconds = 300,
    [ValidateRange(1, 3600)][int]$TestTimeoutSeconds = 600,
    [ValidateRange(1, 3600)][int]$SceneTimeoutSeconds = 180,
    [ValidateRange(1, 3600)][int]$DemoTimeoutSeconds = 300,
    [ValidateRange(1, 3600)][int]$MatrixTimeoutSeconds = 1800,
    [ValidateRange(1, 3600)][int]$PesterTimeoutSeconds = 1800,
    [ValidateRange(1, 3600)][int]$RegressionTimeoutSeconds = 3600,
    [ValidateRange(1, 3600)][int]$ReleaseTimeoutSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$p4BaseCommit = '1d941ee0611ca2f6af710deab7a6d63f07e2105c'
$functionsPath = Join-Path $PSScriptRoot 'p4-verification-functions.ps1'
. $functionsPath

function Write-P4ValidatedOutput
{
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Lines)
    $Lines | ForEach-Object { Write-Output $_ }
}

function Invoke-P4CheckedCommand
{
    param(
        [Parameter(Mandatory)][string]$PhaseName,
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Arguments,
        [Parameter(Mandatory)][int]$TimeoutSeconds
    )

    $process = Invoke-P4VerificationProcess `
        -FilePath $FilePath `
        -Arguments $Arguments `
        -TimeoutSeconds $TimeoutSeconds `
        -Stage $PhaseName
    $validated = @(Assert-P4ChildGateOutput `
        -PhaseName $PhaseName `
        -OutputLines $process.OutputLines `
        -ExitCode $process.ExitCode `
        -ExpectedMarkers @())
    Write-P4ValidatedOutput -Lines $validated
}

function Invoke-P4DotnetTestGate
{
    param(
        [Parameter(Mandatory)][string]$PhaseName,
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][string]$Configuration,
        [AllowEmptyString()][string]$Filter,
        [Parameter(Mandatory)][int]$TimeoutSeconds,
        [AllowEmptyCollection()][string[]]$ExpectedTestClasses = @(),
        [switch]$NoBuild
    )

    $resultRoot = Join-Path ([IO.Path]::GetTempPath()) (
        'GodotALS-P4-TRX-' + [Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($resultRoot)
    $trxName = 'results.trx'
    $trxPath = Join-Path $resultRoot $trxName
    try
    {
        $arguments = @(
            'test', $ProjectPath, '-c', $Configuration, '--no-restore',
            '--logger', "trx;LogFileName=$trxName",
            '--results-directory', $resultRoot)
        if (-not [string]::IsNullOrEmpty($Filter))
        {
            $arguments += @('--filter', $Filter)
        }
        if ($NoBuild)
        {
            $arguments += '--no-build'
        }
        $process = Invoke-P4VerificationProcess `
            -FilePath 'dotnet' `
            -Arguments $arguments `
            -TimeoutSeconds $TimeoutSeconds `
            -Stage $PhaseName
        $validated = @(Assert-P4ChildGateOutput `
            -PhaseName $PhaseName `
            -OutputLines $process.OutputLines `
            -ExitCode $process.ExitCode `
            -ExpectedMarkers @())
        $executed = Assert-P4TrxTestRun `
            -Path $trxPath `
            -PhaseName $PhaseName `
            -ExpectedTestClasses $ExpectedTestClasses
        Write-P4ValidatedOutput -Lines $validated
        Write-Output "P4_DOTNET_TESTS_OK phase=$($PhaseName.Replace(' ', '_')) executed=$executed"
    }
    finally
    {
        if (Test-Path -LiteralPath $resultRoot)
        {
            Remove-Item -LiteralPath $resultRoot -Recurse -Force
        }
    }
}

function Invoke-P4PesterGate
{
    param(
        [Parameter(Mandatory)][string]$PhaseName,
        [Parameter(Mandatory)][string[]]$Paths,
        [Parameter(Mandatory)][string]$ExpectedMarker,
        [Parameter(Mandatory)][int]$TimeoutSeconds
    )

    $pathLiterals = @($Paths | ForEach-Object {
        "'{0}'" -f $_.Replace("'", "''")
    }) -join ', '
    $markerLiteral = $ExpectedMarker.Replace("'", "''")
    $command = @"
`$ErrorActionPreference = 'Stop'
Import-Module Pester -ErrorAction Stop
`$result = Invoke-Pester -Script @($pathLiterals) -PassThru
if (`$null -eq `$result -or `$null -eq `$result.PSObject.Properties['FailedCount'] -or
    `$null -eq `$result.PSObject.Properties['TotalCount'] -or `$result.TotalCount -le 0 -or
    `$result.FailedCount -ne 0) { exit 1 }
Write-Output '$markerLiteral'
"@
    $process = Invoke-P4VerificationProcess `
        -FilePath 'pwsh' `
        -Arguments @('-NoProfile', '-NonInteractive', '-Command', $command) `
        -TimeoutSeconds $TimeoutSeconds `
        -Stage $PhaseName
    $validated = @(Assert-P4ChildGateOutput `
        -PhaseName $PhaseName `
        -OutputLines $process.OutputLines `
        -ExitCode $process.ExitCode `
        -ExpectedMarkers @($ExpectedMarker))
    Write-P4ValidatedOutput -Lines $validated
}

function Invoke-P4RestoreAndOptimizedBuild
{
    param(
        [Parameter(Mandatory)][string]$ProjectRootPath,
        [Parameter(Mandatory)][int]$TimeoutSeconds
    )

    Invoke-P4CheckedCommand `
        -PhaseName 'P4 solution restore' `
        -FilePath 'dotnet' `
        -Arguments @('restore', (Join-Path $ProjectRootPath 'GodotALS.sln')) `
        -TimeoutSeconds $TimeoutSeconds
    Invoke-P4CheckedCommand `
        -PhaseName 'P4 optimized Debug build' `
        -FilePath 'dotnet' `
        -Arguments @(
            'build', (Join-Path $ProjectRootPath 'GodotALS.csproj'), '-c', 'Debug',
            '-p:Optimize=true', '--no-restore', '--no-incremental') `
        -TimeoutSeconds $TimeoutSeconds
}

function Invoke-P4FocusedImportTests
{
    param(
        [Parameter(Mandatory)][string]$ProjectRootPath,
        [Parameter(Mandatory)][int]$TestTimeout,
        [Parameter(Mandatory)][int]$PesterTimeout
    )

    $testClasses = @(
        'AlsAnimationSetCompilerTests',
        'AlsCurveExporterSourceContractTests',
        'AlsManifestSerializerTests',
        'AlsPoseProfileCompilerTests',
        'AlsRotationYawCurveTests')
    $filter = @($testClasses | ForEach-Object { "FullyQualifiedName~$_" }) -join '|'
    Invoke-P4DotnetTestGate `
        -PhaseName 'P4 focused Import tests' `
        -ProjectPath (Join-Path $ProjectRootPath 'tests\Als.Import.Tests\Als.Import.Tests.csproj') `
        -Configuration Debug `
        -Filter $filter `
        -TimeoutSeconds $TestTimeout `
        -ExpectedTestClasses $testClasses
    Invoke-P4PesterGate `
        -PhaseName 'P4 focused profile scripts' `
        -Paths @(
            (Join-Path $ProjectRootPath 'tests\GenerateP4Profile.Tests.ps1'),
            (Join-Path $ProjectRootPath 'tests\P4RuntimeDefaults.Tests.ps1')) `
        -ExpectedMarker 'P4_FOCUSED_IMPORT_PESTER_OK' `
        -TimeoutSeconds $PesterTimeout
}

function Invoke-P4FocusedCoreTests
{
    param(
        [Parameter(Mandatory)][string]$ProjectRootPath,
        [Parameter(Mandatory)][int]$TestTimeout,
        [Parameter(Mandatory)][int]$PesterTimeout
    )

    $testClasses = @(
        'AlsFootPlacementModelTests',
        'AlsLocomotionCommandResolverTests',
        'AlsLocomotionRotationTests',
        'AlsPoseGoldenTests',
        'AlsResultDigestTests',
        'AlsTurnRotateModelTests',
        'AlsViewPoseModelTests',
        'ContractLayoutTests',
        'HotPathAllocationTests')
    $filter = @($testClasses | ForEach-Object { "FullyQualifiedName~$_" }) -join '|'
    Invoke-P4DotnetTestGate `
        -PhaseName 'P4 focused Core tests' `
        -ProjectPath (Join-Path $ProjectRootPath 'tests\Als.Core.Tests\Als.Core.Tests.csproj') `
        -Configuration Debug `
        -Filter $filter `
        -TimeoutSeconds $TestTimeout `
        -ExpectedTestClasses $testClasses
    Invoke-P4PesterGate `
        -PhaseName 'P4 focused golden scripts' `
        -Paths @((Join-Path $ProjectRootPath 'tests\GenerateP4Golden.Tests.ps1')) `
        -ExpectedMarker 'P4_FOCUSED_CORE_PESTER_OK' `
        -TimeoutSeconds $PesterTimeout
}

function Invoke-P4PoseCertificate
{
    param(
        [Parameter(Mandatory)][string]$GodotPath,
        [Parameter(Mandatory)][string]$ProjectRootPath,
        [Parameter(Mandatory)][int]$TimeoutSeconds
    )

    $graph = Invoke-P4VerificationProcess `
        -FilePath $GodotPath `
        -Arguments @(
            '--headless', '--path', $ProjectRootPath,
            'res://scenes/tests/p4_animation_graph_smoke.tscn') `
        -TimeoutSeconds $TimeoutSeconds `
        -Stage 'P4 animation graph'
    $graphLines = @(Assert-P4SceneGateOutput `
        -PhaseName 'P4 animation graph' `
        -OutputLines $graph.OutputLines `
        -ExitCode $graph.ExitCode `
        -ExpectedRegexMarkers @(
            '\AP4_ANIMATION_GRAPH_OK frames=240 advances=240 digest=(?!0000000000000000)[0-9A-F]{16}\z'))
    Write-P4ValidatedOutput -Lines $graphLines

    $pose = Invoke-P4VerificationProcess `
        -FilePath $GodotPath `
        -Arguments @(
            '--headless', '--path', $ProjectRootPath,
            'res://scenes/tests/p4_pose_smoke.tscn') `
        -TimeoutSeconds $TimeoutSeconds `
        -Stage 'P4 component pose'
    $poseLines = @(Assert-P4SceneGateOutput `
        -PhaseName 'P4 component pose' `
        -OutputLines $pose.OutputLines `
        -ExitCode $pose.ExitCode `
        -ExpectedRegexMarkers @(
            '\AP4_POSE_TOPOLOGY_PERF iterations=10000 elapsed_ms=[0-9]+(?:\.[0-9]+)? bones=68 alloc=0B\z',
            '\AP4_POSE_ACTIVE_PERF iterations=10000 elapsed_ms=[0-9]+(?:\.[0-9]+)? bones=68 affected=[1-9][0-9]* alloc=0B writes=1\z',
            '\AP4_POSE_OK aim=(?!0000000000000000)[0-9A-F]{16} turn=(?!0000000000000000)[0-9A-F]{16} rotate=(?!0000000000000000)[0-9A-F]{16} rollback=3 alloc=0B allocation_mode=controlled\z'))
    if (@($poseLines | Where-Object {
        $_.StartsWith('P4_POSE_ALLOCATION_UNCONTROLLED', [StringComparison]::Ordinal)
    }).Count -ne 0)
    {
        throw 'Controlled P4 Pose gate emitted an uncontrolled-allocation marker.'
    }
    Write-P4ValidatedOutput -Lines $poseLines

    foreach ($mode in @('single', 'parallel'))
    {
        $lateTransaction = Invoke-P4VerificationProcess `
            -FilePath $GodotPath `
            -Arguments @(
                '--headless', '--path', $ProjectRootPath,
                'res://scenes/tests/p3b_frame_order_smoke.tscn', '--',
                "--als-mode=$mode", '--als-failure-policy=late_transaction') `
            -TimeoutSeconds $TimeoutSeconds `
            -Stage "P4 late transaction rollback ($mode)"
        $lateLines = @(Assert-P4SceneGateOutput `
            -PhaseName "P4 late transaction rollback ($mode)" `
            -OutputLines $lateTransaction.OutputLines `
            -ExitCode $lateTransaction.ExitCode `
            -ExpectedRegexMarkers @(
                "\AGODOT_ALS_P3B_LATE_TRANSACTION_ROLLBACK_OK mode=$mode exchange=0 runtime=1 result=1 controller=1 pose=1 p4_banks=1\z"))
        Write-P4ValidatedOutput -Lines $lateLines
    }
}

function Invoke-P4FootRuntimeCertificates
{
    param(
        [Parameter(Mandatory)][string]$GodotPath,
        [Parameter(Mandatory)][string]$ProjectRootPath,
        [Parameter(Mandatory)][int]$TimeoutSeconds
    )

    $footGather = Invoke-P4VerificationProcess `
        -FilePath $GodotPath `
        -Arguments @(
            '--headless', '--path', $ProjectRootPath,
            'res://scenes/tests/p4_foot_gather_smoke.tscn') `
        -TimeoutSeconds $TimeoutSeconds `
        -Stage 'P4 foot gather'
    $footGatherLines = @(Assert-P4SceneGateOutput `
        -PhaseName 'P4 foot gather' `
        -OutputLines $footGather.OutputLines `
        -ExitCode $footGather.ExitCode `
        -ExpectedExactMarkers @(
            'P4_FOOT_GATHER_OK latency=1 removal_identity=1 step_off=1') `
        -ExpectedRegexMarkers @(
            '\AP4_FOOT_GATHER_SLIDE_ALLOC bytes=[1-9][0-9]*\z',
            '\AP4_FOOT_GATHER_MANY_CONTACT_OK slide_count=[1-9][0-9]* actual_moving_index=[0-9]+ limited_probe_count=4 limited_platform_index=-1 probe_count=[5-9][0-9]* probe_platform_index=[4-9][0-9]*\z'))
    Write-P4ValidatedOutput -Lines $footGatherLines

    $lifecycle = Invoke-P4VerificationProcess `
        -FilePath $GodotPath `
        -Arguments @(
            '--headless', '--path', $ProjectRootPath,
            'res://scenes/tests/p4_lifecycle_smoke.tscn') `
        -TimeoutSeconds $TimeoutSeconds `
        -Stage 'P4 lifecycle'
    $lifecycleLines = @(Assert-P4SceneGateOutput `
        -PhaseName 'P4 lifecycle' `
        -OutputLines $lifecycle.OutputLines `
        -ExitCode $lifecycle.ExitCode `
        -ExpectedExactMarkers @(
            'P4_LIFECYCLE_OK order=1 yaw=1 deactivate=1 replace=1 stale_counter=3 injected_stale=1 initial_stale=0 generation=1 failure=1 recovery=1 platform_recovery=1 initial_failure=1 platform_motion=1 platform_removal=1 probe=1'))
    Write-P4ValidatedOutput -Lines $lifecycleLines

    foreach ($mode in @('single', 'parallel'))
    {
        $footPlacement = Invoke-P4VerificationProcess `
            -FilePath $GodotPath `
            -Arguments @(
                '--headless', '--path', $ProjectRootPath,
                'res://scenes/tests/p4_foot_placement_smoke.tscn', '--',
                "--als-mode=$mode") `
            -TimeoutSeconds $TimeoutSeconds `
            -Stage "P4 foot placement ($mode)"
        $placementLines = @(Assert-P4SceneGateOutput `
            -PhaseName "P4 foot placement ($mode)" `
            -OutputLines $footPlacement.OutputLines `
            -ExitCode $footPlacement.ExitCode `
            -ExpectedRegexMarkers @(
                "\AP4_FOOT_PLACEMENT_OK mode=$mode flat=1 slope=1 stairs=1 translate=1 rotate=1 jump=1 base=1 teleport=1 rollback=2\z"))
        Write-P4ValidatedOutput -Lines $placementLines
    }
}

function Invoke-P4DemoCertificate
{
    param(
        [Parameter(Mandatory)][string]$GodotPath,
        [Parameter(Mandatory)][string]$ProjectRootPath,
        [Parameter(Mandatory)][int]$BuildTimeoutSeconds,
        [Parameter(Mandatory)][int]$P3InputTimeoutSeconds,
        [Parameter(Mandatory)][int]$P4DemoTimeoutSeconds
    )

    $outerTimeoutSeconds = $BuildTimeoutSeconds + $P3InputTimeoutSeconds +
        $P4DemoTimeoutSeconds + 30
    $process = Invoke-P4VerificationProcess `
        -FilePath 'pwsh' `
        -Arguments @(
            '-NoProfile', '-NonInteractive', '-File',
            (Join-Path $PSScriptRoot 'verify-p4-demo.ps1'),
            '-GodotExecutable', $GodotPath,
            '-ProjectRoot', $ProjectRootPath,
            '-BuildTimeoutSeconds', "$BuildTimeoutSeconds",
            '-P3InputTimeoutSeconds', "$P3InputTimeoutSeconds",
            '-P4DemoTimeoutSeconds', "$P4DemoTimeoutSeconds") `
        -TimeoutSeconds $outerTimeoutSeconds `
        -Stage 'P4 demo certificate'
    $validated = @(Assert-P4ChildGateOutput `
        -PhaseName 'P4 demo certificate' `
        -OutputLines $process.OutputLines `
        -ExitCode $process.ExitCode `
        -ExpectedMarkers @(
            'GODOT_ALS_P3_DEMO_INPUT_OK actions=11 directions=12 camera_basis=1 pitch=1 aiming=1 cleared=1 hud=1',
            'P4_DEMO_OK frames=300 rigs=1',
            'P4_DEMO_VERIFICATION_OK frames=300 rigs=1'))
    Write-P4ValidatedOutput -Lines $validated
}

function Invoke-P4MatrixCertificate
{
    param(
        [Parameter(Mandatory)][string]$GodotPath,
        [Parameter(Mandatory)][string]$ProjectRootPath,
        [Parameter(Mandatory)][int]$TimeoutSeconds
    )

    $process = Invoke-P4VerificationProcess `
        -FilePath 'pwsh' `
        -Arguments @(
            '-NoProfile', '-NonInteractive', '-File',
            (Join-Path $PSScriptRoot 'verify-p4-matrix.ps1'),
            '-GodotExecutable', $GodotPath,
            '-ProjectRoot', $ProjectRootPath) `
        -TimeoutSeconds $TimeoutSeconds `
        -Stage 'P4 matrix certificate'
    $certificate = Assert-P4MatrixCertificateOutput `
        -OutputLines $process.OutputLines `
        -ExitCode $process.ExitCode
    Write-P4ValidatedOutput -Lines @($certificate.OutputLines)
}

function Invoke-P4RepositoryPester
{
    param(
        [Parameter(Mandatory)][string]$ProjectRootPath,
        [Parameter(Mandatory)][int]$TimeoutSeconds
    )

    Invoke-P4PesterGate `
        -PhaseName 'P4 repository Pester' `
        -Paths @((Join-Path $ProjectRootPath 'tests\*.Tests.ps1')) `
        -ExpectedMarker 'P4_REPOSITORY_PESTER_OK' `
        -TimeoutSeconds $TimeoutSeconds
}

function Invoke-P4P3bRegression
{
    param(
        [Parameter(Mandatory)][string]$GodotPath,
        [Parameter(Mandatory)][string]$ProjectRootPath,
        [Parameter(Mandatory)][int]$TimeoutSeconds
    )

    $process = Invoke-P4VerificationProcess `
        -FilePath 'pwsh' `
        -Arguments @(
            '-NoProfile', '-NonInteractive', '-File',
            (Join-Path $PSScriptRoot 'verify-p3b.ps1'),
            '-GodotExecutable', $GodotPath,
            '-ProjectRoot', $ProjectRootPath) `
        -TimeoutSeconds $TimeoutSeconds `
        -Stage 'P3B full regression'
    $validated = @(Assert-P4ChildGateOutput `
        -PhaseName 'P3B full regression' `
        -OutputLines $process.OutputLines `
        -ExitCode $process.ExitCode `
        -ExpectedMarkers @(
            'P3B_VERIFICATION_OK',
            'P3A_VERIFICATION_OK',
            'P2B_VERIFICATION_OK',
            'P1_VERIFICATION_OK',
            'P0_VERIFICATION_OK'))
    Write-P4ValidatedOutput -Lines $validated
}

function Invoke-P4ReleaseTests
{
    param(
        [Parameter(Mandatory)][string]$ProjectRootPath,
        [Parameter(Mandatory)][int]$TimeoutSeconds
    )

    Invoke-P4CheckedCommand `
        -PhaseName 'P4 Release solution tests' `
        -FilePath 'dotnet' `
        -Arguments @(
            'test', (Join-Path $ProjectRootPath 'GodotALS.sln'),
            '-c', 'Release', '--no-restore') `
        -TimeoutSeconds $TimeoutSeconds
    Invoke-P4DotnetTestGate `
        -PhaseName 'P4 Release Core TRX' `
        -ProjectPath (Join-Path $ProjectRootPath 'tests\Als.Core.Tests\Als.Core.Tests.csproj') `
        -Configuration Release `
        -Filter '' `
        -TimeoutSeconds $TimeoutSeconds `
        -NoBuild
    Invoke-P4DotnetTestGate `
        -PhaseName 'P4 Release Import TRX' `
        -ProjectPath (Join-Path $ProjectRootPath 'tests\Als.Import.Tests\Als.Import.Tests.csproj') `
        -Configuration Release `
        -Filter '' `
        -TimeoutSeconds $TimeoutSeconds `
        -NoBuild
}

function Invoke-P4VerificationStages
{
    param(
        [Parameter(Mandatory)][string]$GodotPath,
        [Parameter(Mandatory)][string]$ProjectRootPath,
        [Parameter(Mandatory)][bool]$FocusedRun,
        [Parameter(Mandatory)][hashtable]$Timeouts
    )

    Invoke-P4RestoreAndOptimizedBuild `
        -ProjectRootPath $ProjectRootPath `
        -TimeoutSeconds $Timeouts.Build
    Invoke-P4FocusedImportTests `
        -ProjectRootPath $ProjectRootPath `
        -TestTimeout $Timeouts.Test `
        -PesterTimeout $Timeouts.Pester
    Invoke-P4FocusedCoreTests `
        -ProjectRootPath $ProjectRootPath `
        -TestTimeout $Timeouts.Test `
        -PesterTimeout $Timeouts.Pester
    Invoke-P4PoseCertificate `
        -GodotPath $GodotPath `
        -ProjectRootPath $ProjectRootPath `
        -TimeoutSeconds $Timeouts.Scene
    Invoke-P4FootRuntimeCertificates `
        -GodotPath $GodotPath `
        -ProjectRootPath $ProjectRootPath `
        -TimeoutSeconds $Timeouts.Scene
    Write-Output 'P4_POSE_VERIFICATION_OK graph=1 pose=1 foot_placement=2 late_transaction=2 zero_alloc=0B active_alloc=0B'
    Invoke-P4DemoCertificate `
        -GodotPath $GodotPath `
        -ProjectRootPath $ProjectRootPath `
        -BuildTimeoutSeconds $Timeouts.Build `
        -P3InputTimeoutSeconds $Timeouts.Scene `
        -P4DemoTimeoutSeconds $Timeouts.Demo
    Invoke-P4MatrixCertificate `
        -GodotPath $GodotPath `
        -ProjectRootPath $ProjectRootPath `
        -TimeoutSeconds $Timeouts.Matrix

    if ($FocusedRun)
    {
        Write-Output 'P4_FOCUSED_VERIFICATION_OK regression=skipped'
        return
    }

    Invoke-P4RepositoryPester `
        -ProjectRootPath $ProjectRootPath `
        -TimeoutSeconds $Timeouts.Pester
    Invoke-P4P3bRegression `
        -GodotPath $GodotPath `
        -ProjectRootPath $ProjectRootPath `
        -TimeoutSeconds $Timeouts.Regression
    Invoke-P4ReleaseTests `
        -ProjectRootPath $ProjectRootPath `
        -TimeoutSeconds $Timeouts.Release
    $resolvedBase = Assert-P4RepositoryClosure `
        -RepositoryRoot $ProjectRootPath `
        -BaseCommit $p4BaseCommit
    Assert-P4CleanWorktree -RepositoryRoot $ProjectRootPath
    Write-Output "P4_REPOSITORY_CLOSURE_OK p4_base=$resolvedBase"
    Write-Output 'P4_VERIFICATION_OK'
}

$hadDotnetTiering = Test-Path Env:DOTNET_TieredCompilation
$hadComPlusTiering = Test-Path Env:COMPlus_TieredCompilation
$previousDotnetTiering = [Environment]::GetEnvironmentVariable(
    'DOTNET_TieredCompilation',
    [EnvironmentVariableTarget]::Process)
$previousComPlusTiering = [Environment]::GetEnvironmentVariable(
    'COMPlus_TieredCompilation',
    [EnvironmentVariableTarget]::Process)

try
{
    $env:DOTNET_TieredCompilation = '0'
    $env:COMPlus_TieredCompilation = '0'
    $projectRootPath = (Resolve-Path -LiteralPath $ProjectRoot).Path
    if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf))
    {
        throw "Godot executable not found: $GodotExecutable"
    }
    $godotPath = (Resolve-Path -LiteralPath $GodotExecutable).Path
    $timeouts = @{
        Build = $BuildTimeoutSeconds
        Test = $TestTimeoutSeconds
        Scene = $SceneTimeoutSeconds
        Demo = $DemoTimeoutSeconds
        Matrix = $MatrixTimeoutSeconds
        Pester = $PesterTimeoutSeconds
        Regression = $RegressionTimeoutSeconds
        Release = $ReleaseTimeoutSeconds
    }
    Invoke-P4VerificationStages `
        -GodotPath $godotPath `
        -ProjectRootPath $projectRootPath `
        -FocusedRun ([bool]$Focused) `
        -Timeouts $timeouts
}
finally
{
    if ($hadDotnetTiering)
    {
        [Environment]::SetEnvironmentVariable(
            'DOTNET_TieredCompilation',
            $previousDotnetTiering,
            [EnvironmentVariableTarget]::Process)
    }
    else
    {
        Remove-Item Env:DOTNET_TieredCompilation -ErrorAction SilentlyContinue
    }
    if ($hadComPlusTiering)
    {
        [Environment]::SetEnvironmentVariable(
            'COMPlus_TieredCompilation',
            $previousComPlusTiering,
            [EnvironmentVariableTarget]::Process)
    }
    else
    {
        Remove-Item Env:COMPlus_TieredCompilation -ErrorAction SilentlyContinue
    }
}

$script:RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:VerifierPath = Join-Path $script:RepositoryRoot 'scripts\verify-p4.ps1'
$script:FunctionsPath = Join-Path $script:RepositoryRoot 'scripts\p4-verification-functions.ps1'
$script:LockedBase = '1d941ee0611ca2f6af710deab7a6d63f07e2105c'

if (Test-Path -LiteralPath $script:FunctionsPath -PathType Leaf)
{
    . $script:FunctionsPath
}

$script:VerifierSource = if (Test-Path -LiteralPath $script:VerifierPath -PathType Leaf)
{
    [IO.File]::ReadAllText($script:VerifierPath)
}
else
{
    ''
}

function Test-P4ChildOutputRejects
{
    param(
        [object[]]$OutputLines,
        [int]$ExitCode = 0,
        [string[]]$ExpectedMarkers = @('P4_CHILD_OK')
    )

    try
    {
        Assert-P4ChildGateOutput `
            -PhaseName 'synthetic child' `
            -OutputLines $OutputLines `
            -ExitCode $ExitCode `
            -ExpectedMarkers $ExpectedMarkers | Out-Null
        return $false
    }
    catch
    {
        return $true
    }
}

function New-P4MatrixMarker
{
    param(
        [ValidateSet('single', 'parallel')]
        [string]$Mode,
        [ValidateSet(1, 10)]
        [int]$Characters,
        [string]$ResultDigest
    )

    $total = $Characters * 600
    $prefix = if ($Characters -eq 1) { '1' } else { 'A' }
    if ([string]::IsNullOrEmpty($ResultDigest)) { $ResultDigest = $prefix * 16 }
    return "P4_MATRIX_OK mode=$Mode characters=$Characters warmup=120 frames=600 " +
        "result=$ResultDigest pose=$($prefix * 15)2 full_pose=$($prefix * 15)3 " +
        "root=$($prefix * 15)4 aim=$($prefix * 15)5 turn_rotate=$($prefix * 15)6 " +
        "feet=$($prefix * 15)7 missing=0 stale=0 generation=0 lag=0 thread=0 " +
        "model=0 curve=0 controller=0 modifier=0 skeleton=0 exchange=0 commit=0 " +
        "foot_gather=4096 advances=$total modifiers=$total commits=$total " +
        "per_character_advances=600 per_character_modifiers=600 per_character_commits=600 " +
        "replacement=1 old_generation_rejected=1 lanes=$Characters " +
        'gather_commit_p95_us=100 worker_p95_us=200 total_p99_us=300'
}

function Get-P4MatrixCertificateOutput
{
    return @(
        (New-P4MatrixMarker -Mode single -Characters 1),
        (New-P4MatrixMarker -Mode parallel -Characters 1),
        (New-P4MatrixMarker -Mode single -Characters 10),
        (New-P4MatrixMarker -Mode parallel -Characters 10),
        'P4_MATRIX_VERIFICATION_OK cells=4 pairs=2'
    )
}

function Test-P4MatrixCertificateRejects
{
    param([object[]]$OutputLines, [int]$ExitCode = 0)

    try
    {
        Assert-P4MatrixCertificateOutput `
            -OutputLines $OutputLines `
            -ExitCode $ExitCode | Out-Null
        return $false
    }
    catch
    {
        return $true
    }
}

function New-P4ClosureRepository
{
    $root = Join-Path $TestDrive ([Guid]::NewGuid().ToString('N'))
    [void](New-Item -ItemType Directory -Path $root)
    & git -C $root init --quiet
    & git -C $root config user.email 'p4-tests@example.invalid'
    & git -C $root config user.name 'P4 Tests'
    [IO.File]::WriteAllText((Join-Path $root 'tracked.txt'), "clean`n")
    & git -C $root add tracked.txt
    & git -C $root commit --quiet -m baseline
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize P4 closure fixture.' }
    $base = (& git -C $root rev-parse HEAD).Trim()
    return [pscustomobject]@{ Root = $root; Base = $base }
}

function Test-P4RepositoryClosureRejects
{
    param([string]$RepositoryRoot, [string]$BaseCommit)
    try
    {
        Assert-P4RepositoryClosure `
            -RepositoryRoot $RepositoryRoot `
            -BaseCommit $BaseCommit | Out-Null
        return $false
    }
    catch
    {
        return $true
    }
}

function Test-P4CleanWorktreeRejects
{
    param([string]$RepositoryRoot)
    try
    {
        Assert-P4CleanWorktree -RepositoryRoot $RepositoryRoot
        return $false
    }
    catch
    {
        return $true
    }
}

Describe 'P4 full verifier source contract' {
    It 'exists and parses without PowerShell syntax errors' {
        Test-Path -LiteralPath $script:VerifierPath -PathType Leaf | Should Be $true
        if (-not (Test-Path -LiteralPath $script:VerifierPath -PathType Leaf)) { return }

        $tokens = $null
        $errors = $null
        [void][Management.Automation.Language.Parser]::ParseFile(
            $script:VerifierPath,
            [ref]$tokens,
            [ref]$errors)
        @($errors).Count | Should Be 0
    }

    It 'accepts only used project Godot focused and watchdog parameters' {
        if ($script:VerifierSource.Length -eq 0) { $false | Should Be $true; return }
        $tokens = $null
        $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile(
            $script:VerifierPath,
            [ref]$tokens,
            [ref]$errors)
        $names = @($ast.ParamBlock.Parameters | ForEach-Object {
            $_.Name.VariablePath.UserPath
        })
        ($names -contains 'GodotExecutable') | Should Be $true
        ($names -contains 'ProjectRoot') | Should Be $true
        ($names -contains 'Focused') | Should Be $true
        ($names -contains 'UnrealEditorCmd') | Should Be $false
        ($names -contains 'UnrealProject') | Should Be $false
        ($names -contains 'ReferenceRoot') | Should Be $false
    }

    It 'freezes the agreed P4 base rather than the P3A base' {
        $script:VerifierSource | Should Match ([regex]::Escape($script:LockedBase))
        $script:VerifierSource | Should Not Match 'e69f18bb3410d77ef50df38b073535b5e9f20635'
    }

    It 'runs the fixed twelve phases in order and keeps full work after focused work' {
        if ($script:VerifierSource.Length -eq 0) { $false | Should Be $true; return }
        $tokens = $null
        $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile(
            $script:VerifierPath,
            [ref]$tokens,
            [ref]$errors)
        $stages = @($ast.FindAll({
            param($node)
            $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                $node.Name -ceq 'Invoke-P4VerificationStages'
        }, $true))
        $stages.Count | Should Be 1
        if ($stages.Count -ne 1) { return }
        $body = $stages[0].Extent.Text
        $needles = @(
            'Invoke-P4RestoreAndOptimizedBuild',
            'Invoke-P4FocusedImportTests',
            'Invoke-P4FocusedCoreTests',
            'Invoke-P4PoseCertificate',
            'Invoke-P4FootRuntimeCertificates',
            "Write-Output 'P4_POSE_VERIFICATION_OK graph=1 pose=1 foot_placement=2 late_transaction=2 zero_alloc=0B active_alloc=0B'",
            'Invoke-P4DemoCertificate',
            'Invoke-P4MatrixCertificate',
            "Write-Output 'P4_FOCUSED_VERIFICATION_OK regression=skipped'",
            'Invoke-P4RepositoryPester',
            'Invoke-P4P3bRegression',
            'Invoke-P4ReleaseTests',
            'Assert-P4RepositoryClosure',
            'Assert-P4CleanWorktree',
            "Write-Output 'P4_VERIFICATION_OK'"
        )
        $previous = -1
        foreach ($needle in $needles)
        {
            $current = $body.IndexOf($needle, [StringComparison]::Ordinal)
            $current | Should BeGreaterThan $previous
            $previous = $current
        }
    }

    It 'uses the formal Task 16 and Task 15 runners and a non-skip P3B child' {
        $script:VerifierSource | Should Match "'verify-p4-demo\.ps1'"
        $script:VerifierSource | Should Match "'verify-p4-matrix\.ps1'"
        $script:VerifierSource | Should Match "'verify-p3b\.ps1'"
        $script:VerifierSource | Should Not Match 'verify-p3b\.ps1[^\r\n]*SkipRegression'
        $script:VerifierSource | Should Match 'Assert-P4MatrixCertificateOutput'
        $script:VerifierSource | Should Match 'P3B_VERIFICATION_OK'
        $script:VerifierSource | Should Match 'P3A_VERIFICATION_OK'
        $script:VerifierSource | Should Match 'P2B_VERIFICATION_OK'
        $script:VerifierSource | Should Match 'P1_VERIFICATION_OK'
        $script:VerifierSource | Should Match 'P0_VERIFICATION_OK'
    }

    It 'covers all three formal Demo child budgets with the outer watchdog' {
        $tokens = $null
        $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile(
            $script:VerifierPath,
            [ref]$tokens,
            [ref]$errors)
        $demo = @($ast.FindAll({
            param($node)
            $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                $node.Name -ceq 'Invoke-P4DemoCertificate'
        }, $true))[0].Extent.Text
        $stages = @($ast.FindAll({
            param($node)
            $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                $node.Name -ceq 'Invoke-P4VerificationStages'
        }, $true))[0].Extent.Text

        $demo | Should Match '\$outerTimeoutSeconds\s*=\s*\$BuildTimeoutSeconds\s*\+\s*\$P3InputTimeoutSeconds\s*\+\s*\$P4DemoTimeoutSeconds\s*\+\s*30'
        $demo | Should Match '''-BuildTimeoutSeconds'', "\$BuildTimeoutSeconds"'
        $demo | Should Match '''-P3InputTimeoutSeconds'', "\$P3InputTimeoutSeconds"'
        $demo | Should Match '''-P4DemoTimeoutSeconds'', "\$P4DemoTimeoutSeconds"'
        $demo | Should Match '-TimeoutSeconds\s+\$outerTimeoutSeconds'
        $stages | Should Match '(?s)Invoke-P4DemoCertificate.*-BuildTimeoutSeconds\s+\$Timeouts\.Build.*-P3InputTimeoutSeconds\s+\$Timeouts\.Scene.*-P4DemoTimeoutSeconds\s+\$Timeouts\.Demo'
        [IO.File]::ReadAllText($script:FunctionsPath) |
            Should Match '(?s)function Invoke-P4VerificationProcess.*ValidateRange\(1,\s*10830\)'
    }

    It 'runs each pose and foot scene under its own watchdog in the frozen order' {
        $script:VerifierSource | Should Not Match "'verify-p4-pose\.ps1'"
        $tokens = $null
        $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile(
            $script:VerifierPath,
            [ref]$tokens,
            [ref]$errors)
        $pose = @($ast.FindAll({
            param($node)
            $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                $node.Name -ceq 'Invoke-P4PoseCertificate'
        }, $true))[0].Extent.Text
        $poseOrder = @(
            'p4_animation_graph_smoke.tscn',
            'p4_pose_smoke.tscn',
            "foreach (`$mode in @('single', 'parallel'))",
            'p3b_frame_order_smoke.tscn')
        $previous = -1
        foreach ($needle in $poseOrder)
        {
            $current = $pose.IndexOf($needle, [StringComparison]::Ordinal)
            $current | Should BeGreaterThan $previous
            $previous = $current
        }
        ([regex]::Matches($pose, 'Invoke-P4VerificationProcess')).Count | Should Be 3
        $pose | Should Match 'Assert-P4SceneGateOutput'

        $feet = @($ast.FindAll({
            param($node)
            $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                $node.Name -ceq 'Invoke-P4FootRuntimeCertificates'
        }, $true))[0].Extent.Text
        $feetOrder = @(
            'p4_foot_gather_smoke.tscn',
            'p4_lifecycle_smoke.tscn',
            "foreach (`$mode in @('single', 'parallel'))",
            'p4_foot_placement_smoke.tscn')
        $previous = -1
        foreach ($needle in $feetOrder)
        {
            $current = $feet.IndexOf($needle, [StringComparison]::Ordinal)
            $current | Should BeGreaterThan $previous
            $previous = $current
        }
        ([regex]::Matches($feet, 'Invoke-P4VerificationProcess')).Count | Should Be 3
        $feet | Should Match 'Assert-P4SceneGateOutput'
    }

    It 'builds optimized non-incremental Debug before runtime and runs Release last' {
        $script:VerifierSource | Should Match "(?s)'build'.*'Debug'.*'-p:Optimize=true'.*'--no-restore'.*'--no-incremental'"
        $script:VerifierSource | Should Match "(?s)function Invoke-P4DotnetTestGate.*'test'.*'--no-restore'"
        $script:VerifierSource | Should Match "(?s)function Invoke-P4ReleaseTests.*'GodotALS\.sln'.*Configuration Release"
    }

    It 'keeps the exact Release solution run and validates independent Core and Import TRX files' {
        $tokens = $null
        $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile(
            $script:VerifierPath,
            [ref]$tokens,
            [ref]$errors)
        $release = @($ast.FindAll({
            param($node)
            $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                $node.Name -ceq 'Invoke-P4ReleaseTests'
        }, $true))[0].Extent.Text
        $solution = $release.IndexOf("'GodotALS.sln'", [StringComparison]::Ordinal)
        $core = $release.IndexOf("'tests\Als.Core.Tests\Als.Core.Tests.csproj'", [StringComparison]::Ordinal)
        $import = $release.IndexOf("'tests\Als.Import.Tests\Als.Import.Tests.csproj'", [StringComparison]::Ordinal)

        $release | Should Match '(?s)Invoke-P4CheckedCommand.*-FilePath ''dotnet''.*''test''.*''GodotALS\.sln''.*''Release''.*''--no-restore'''
        ([regex]::Matches($release, 'Invoke-P4DotnetTestGate')).Count | Should Be 2
        $core | Should BeGreaterThan $solution
        $import | Should BeGreaterThan $core
        ([regex]::Matches($release, '(?m)^\s*-NoBuild\s*$')).Count | Should Be 2
        $release | Should Not Match '(?s)Invoke-P4DotnetTestGate.*GodotALS\.sln'
        $script:VerifierSource | Should Match '(?s)function Invoke-P4DotnetTestGate.*\[switch\]\$NoBuild.*if \(\$NoBuild\).*''--no-build'''
    }

    It 'uses explicit P4 Import and Core filters and validates structured executed counts' {
        foreach ($testClass in @(
            'AlsPoseProfileCompilerTests',
            'AlsAnimationSetCompilerTests',
            'AlsRotationYawCurveTests',
            'AlsCurveExporterSourceContractTests',
            'AlsManifestSerializerTests',
            'AlsPoseGoldenTests',
            'AlsViewPoseModelTests',
            'AlsTurnRotateModelTests',
            'AlsFootPlacementModelTests',
            'AlsLocomotionCommandResolverTests',
            'AlsLocomotionRotationTests',
            'AlsResultDigestTests',
            'ContractLayoutTests',
            'HotPathAllocationTests'))
        {
            $script:VerifierSource | Should Match ([regex]::Escape($testClass))
        }
        $script:VerifierSource | Should Match 'Invoke-P4DotnetTestGate'
        $script:VerifierSource | Should Match 'Assert-P4TrxTestRun'
    }

    It 'requires every frozen focused class as structured TRX evidence' {
        $tokens = $null
        $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile(
            $script:VerifierPath,
            [ref]$tokens,
            [ref]$errors)
        foreach ($contract in @(
            @{ Function = 'Invoke-P4FocusedImportTests'; Count = 5 },
            @{ Function = 'Invoke-P4FocusedCoreTests'; Count = 9 }))
        {
            $body = @($ast.FindAll({
                param($node)
                $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                    $node.Name -ceq $contract.Function
            }, $true))[0].Extent.Text
            $body | Should Match '\$testClasses\s*=\s*@\('
            ([regex]::Matches($body, 'FullyQualifiedName~')).Count | Should Be 1
            $body | Should Match '\$testClasses\s*\|\s*ForEach-Object'
            $body | Should Match '-ExpectedTestClasses\s+\$testClasses'
            ([regex]::Matches(
                $body,
                "'(?:Als[A-Za-z0-9]+Tests|ContractLayoutTests|HotPathAllocationTests)'" )).Count |
                Should Be $contract.Count
        }
    }

    It 'has distinct focused output and exactly one direct full success output' {
        ([regex]::Matches(
            $script:VerifierSource,
            "(?m)^\s*Write-Output 'P4_FOCUSED_VERIFICATION_OK regression=skipped'\s*$")).Count |
            Should Be 1
        ([regex]::Matches(
            $script:VerifierSource,
            "(?m)^\s*Write-Output 'P4_VERIFICATION_OK'\s*$")).Count |
            Should Be 1
    }

    It 'restores present and absent tiering variables when validation throws' {
        $hadDotnet = Test-Path Env:DOTNET_TieredCompilation
        $hadComPlus = Test-Path Env:COMPlus_TieredCompilation
        $oldDotnet = $env:DOTNET_TieredCompilation
        $oldComPlus = $env:COMPlus_TieredCompilation
        try
        {
            $env:DOTNET_TieredCompilation = 'caller-dotnet'
            $env:COMPlus_TieredCompilation = 'caller-complus'
            $message = ''
            try
            {
                & $script:VerifierPath `
                    -GodotExecutable (Join-Path $TestDrive 'missing-godot.exe') `
                    -Focused | Out-Null
            }
            catch { $message = $_.Exception.Message }
            $message | Should Match 'Godot executable not found'
            $env:DOTNET_TieredCompilation | Should Be 'caller-dotnet'
            $env:COMPlus_TieredCompilation | Should Be 'caller-complus'

            Remove-Item Env:DOTNET_TieredCompilation -ErrorAction SilentlyContinue
            Remove-Item Env:COMPlus_TieredCompilation -ErrorAction SilentlyContinue
            try
            {
                & $script:VerifierPath `
                    -GodotExecutable (Join-Path $TestDrive 'missing-godot.exe') `
                    -Focused | Out-Null
            }
            catch { }
            (Test-Path Env:DOTNET_TieredCompilation) | Should Be $false
            (Test-Path Env:COMPlus_TieredCompilation) | Should Be $false
        }
        finally
        {
            if ($hadDotnet) { $env:DOTNET_TieredCompilation = $oldDotnet }
            else { Remove-Item Env:DOTNET_TieredCompilation -ErrorAction SilentlyContinue }
            if ($hadComPlus) { $env:COMPlus_TieredCompilation = $oldComPlus }
            else { Remove-Item Env:COMPlus_TieredCompilation -ErrorAction SilentlyContinue }
        }
    }

    It 'parses each formal child before forwarding any captured output' {
        if ($script:VerifierSource.Length -eq 0) { $false | Should Be $true; return }
        $tokens = $null
        $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile(
            $script:VerifierPath,
            [ref]$tokens,
            [ref]$errors)
        foreach ($contract in @(
            @{ Function = 'Invoke-P4PoseCertificate'; Assert = 'Assert-P4SceneGateOutput' },
            @{ Function = 'Invoke-P4DemoCertificate'; Assert = 'Assert-P4ChildGateOutput' },
            @{ Function = 'Invoke-P4MatrixCertificate'; Assert = 'Assert-P4MatrixCertificateOutput' },
            @{ Function = 'Invoke-P4P3bRegression'; Assert = 'Assert-P4ChildGateOutput' }))
        {
            $definition = @($ast.FindAll({
                param($node)
                $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                    $node.Name -ceq $contract.Function
            }, $true))
            $definition.Count | Should Be 1
            if ($definition.Count -ne 1) { continue }
            $body = $definition[0].Extent.Text
            $invoke = $body.IndexOf('Invoke-P4VerificationProcess', [StringComparison]::Ordinal)
            $assert = $body.IndexOf($contract.Assert, [StringComparison]::Ordinal)
            $forward = $body.IndexOf('Write-P4ValidatedOutput', [StringComparison]::Ordinal)
            $invoke | Should BeGreaterThan -1
            $assert | Should BeGreaterThan $invoke
            $forward | Should BeGreaterThan $assert
        }
    }
}

Describe 'P4 structured dotnet test evidence' {
    It 'accepts a TRX result only when at least one test executed and none failed' {
        $trx = Join-Path $TestDrive 'passing.trx'
        [IO.File]::WriteAllText(
            $trx,
            '<TestRun><ResultSummary><Counters total="3" executed="3" passed="3" failed="0" /></ResultSummary></TestRun>')
        Assert-P4TrxTestRun -Path $trx -PhaseName 'focused tests' | Should Be 3
    }

    It 'rejects zero executed failed and malformed TRX evidence' {
        foreach ($fixture in @(
            '<TestRun><ResultSummary><Counters total="0" executed="0" passed="0" failed="0" /></ResultSummary></TestRun>',
            '<TestRun><ResultSummary><Counters total="2" executed="2" passed="1" failed="1" /></ResultSummary></TestRun>',
            '<TestRun><ResultSummary><Counters total="2" executed="2" passed="1" failed="0" /></ResultSummary></TestRun>',
            '<TestRun><ResultSummary /></TestRun>'))
        {
            $trx = Join-Path $TestDrive (([Guid]::NewGuid().ToString('N')) + '.trx')
            [IO.File]::WriteAllText($trx, $fixture)
            $rejected = $false
            try { Assert-P4TrxTestRun -Path $trx -PhaseName 'focused tests' | Out-Null }
            catch { $rejected = $true }
            $rejected | Should Be $true
        }
    }

    It 'requires every expected test class to have a passed TRX result identity' {
        $trx = Join-Path $TestDrive 'classes.trx'
        $alphaId = '11111111-1111-1111-1111-111111111111'
        $betaId = '22222222-2222-2222-2222-222222222222'
        [IO.File]::WriteAllText(
            $trx,
            '<TestRun><Results>' +
                ('<UnitTestResult testId="{0}" outcome="Passed" />' -f $alphaId) +
                ('<UnitTestResult testId="{0}" outcome="Passed" />' -f $betaId) +
                '</Results><TestDefinitions>' +
                ('<UnitTest id="{0}"><TestMethod className="Example.AlphaTests" name="A" /></UnitTest>' -f $alphaId) +
                ('<UnitTest id="{0}"><TestMethod className="Example.BetaTests" name="B" /></UnitTest>' -f $betaId) +
                '</TestDefinitions><ResultSummary><Counters total="2" executed="2" passed="2" failed="0" />' +
                '</ResultSummary></TestRun>')

        Assert-P4TrxTestRun `
            -Path $trx `
            -PhaseName 'focused classes' `
            -ExpectedTestClasses @('AlphaTests', 'BetaTests') | Should Be 2

        foreach ($expected in @(
            @('AlphaTests', 'MissingTests'),
            @('AlphaTests', 'AlphaTests')))
        {
            $rejected = $false
            try
            {
                Assert-P4TrxTestRun `
                    -Path $trx `
                    -PhaseName 'focused classes' `
                    -ExpectedTestClasses $expected | Out-Null
            }
            catch { $rejected = $true }
            $rejected | Should Be $true
        }

        [IO.File]::WriteAllText(
            $trx,
            '<TestRun><Results>' +
                ('<UnitTestResult testId="{0}" outcome="Passed" />' -f $alphaId) +
                '</Results><TestDefinitions>' +
                ('<UnitTest id="{0}"><TestMethod className="Example.AlphaTests" name="A" /></UnitTest>' -f $alphaId) +
                ('<UnitTest id="{0}"><TestMethod className="Example.BetaTests" name="B" /></UnitTest>' -f $betaId) +
                '</TestDefinitions><ResultSummary><Counters total="1" executed="1" passed="1" failed="0" />' +
                '</ResultSummary></TestRun>')
        $rejected = $false
        try
        {
            Assert-P4TrxTestRun `
                -Path $trx `
                -PhaseName 'focused classes' `
                -ExpectedTestClasses @('AlphaTests', 'BetaTests') | Out-Null
        }
        catch { $rejected = $true }
        $rejected | Should Be $true
    }
}

Describe 'P4 child gate boundary' {
    It 'accepts every exact marker once' {
        $result = Assert-P4ChildGateOutput `
            -PhaseName 'synthetic child' `
            -OutputLines @('diagnostic', 'P4_FIRST_OK', 'P4_SECOND_OK') `
            -ExitCode 0 `
            -ExpectedMarkers @('P4_FIRST_OK', 'P4_SECOND_OK')
        @($result).Count | Should Be 3
    }

    It 'rejects missing duplicate nonzero engine-error and ALS-failure results' {
        $cases = @(
            @{ Lines = @('diagnostic'); ExitCode = 0 },
            @{ Lines = @('P4_CHILD_OK', 'P4_CHILD_OK'); ExitCode = 0 },
            @{ Lines = @('P4_CHILD_OK'); ExitCode = 9 },
            @{ Lines = @('P4_CHILD_OK', 'SCRIPT ERROR: synthetic'); ExitCode = 0 },
            @{ Lines = @('P4_CHILD_OK', 'Godot: ERROR: synthetic'); ExitCode = 0 },
            @{ Lines = @('P4_CHILD_OK', 'GODOT_ALS_P4_FOOT_GATHER_FAIL code=test'); ExitCode = 0 },
            @{ Lines = @('P4_CHILD_OK', 'P4_MATRIX_FAIL code=test'); ExitCode = 0 }
        )
        foreach ($case in $cases)
        {
            Test-P4ChildOutputRejects `
                -OutputLines $case.Lines `
                -ExitCode $case.ExitCode | Should Be $true
        }
    }

    It 'rejects malformed duplicate and duplicate expectations with the same marker name' {
        Test-P4ChildOutputRejects `
            -OutputLines @('P4_CHILD_OK value=1', 'P4_CHILD_OK value=wrong') `
            -ExpectedMarkers @('P4_CHILD_OK value=1') | Should Be $true
        Test-P4ChildOutputRejects `
            -OutputLines @('P4_CHILD_OK value=1', 'P4_CHILD_OK value=2') `
            -ExpectedMarkers @('P4_CHILD_OK value=1', 'P4_CHILD_OK value=2') |
            Should Be $true
    }

    It 'does not mistake explanatory text for emitted errors or failure markers' {
        Assert-P4ChildGateOutput `
            -PhaseName 'Pester' `
            -OutputLines @(
                '[+] rejects SCRIPT ERROR: and ERROR: tokens',
                '[+] rejects GODOT_ALS_P4_FAIL markers',
                'P4_CHILD_OK') `
            -ExitCode 0 `
            -ExpectedMarkers @('P4_CHILD_OK') | Out-Null
    }

    It 'rejects reserved top-level sentinels emitted by any child' {
        foreach ($sentinel in @(
            'P4_VERIFICATION_OK',
            'P4_FOCUSED_VERIFICATION_OK regression=skipped'))
        {
            Test-P4ChildOutputRejects `
                -OutputLines @('P4_CHILD_OK', $sentinel) | Should Be $true
        }

        $message = ''
        try
        {
            Assert-P4ChildGateOutput `
                -PhaseName 'sentinel child' `
                -OutputLines @('P4_CHILD_OK', 'P4_VERIFICATION_OK') `
                -ExitCode 0 `
                -ExpectedMarkers @('P4_CHILD_OK') | Out-Null
        }
        catch { $message = $_.Exception.Message }
        $message | Should Not Match '(?m)^\s*P4_VERIFICATION_OK\s*$'
    }

    It 'rejects wrapped engine errors and ALS failure markers at token boundaries' {
        foreach ($line in @(
            'wrapper ERROR: late failure',
            'wrapper SCRIPT ERROR: late failure',
            'wrapper GODOT_ALS_P4_FAIL code=x',
            'Godot: GODOT_ALS_P4_FAIL code=x'))
        {
            Test-P4ChildOutputRejects `
                -OutputLines @('P4_CHILD_OK', $line) | Should Be $true
        }
    }

    It 'includes only a bounded captured-output tail when child validation fails' {
        $lines = @(1..50 | ForEach-Object { 'captured-line-{0:d2}' -f $_ })
        $message = ''
        try
        {
            Assert-P4ChildGateOutput `
                -PhaseName 'bounded child' `
                -OutputLines $lines `
                -ExitCode 17 `
                -ExpectedMarkers @('P4_CHILD_OK') | Out-Null
        }
        catch { $message = $_.Exception.Message }

        $message | Should Match 'bounded child exited with code 17'
        $message | Should Match 'captured output tail: 40/50 lines'
        $message | Should Match 'captured-line-11'
        $message | Should Match 'captured-line-50'
        $message | Should Not Match 'captured-line-10(?:\r?\n|$)'

        $message = ''
        try
        {
            Assert-P4ChildGateOutput `
                -PhaseName 'missing marker child' `
                -OutputLines @('diagnostic root cause') `
                -ExitCode 0 `
                -ExpectedMarkers @('P4_CHILD_OK') | Out-Null
        }
        catch { $message = $_.Exception.Message }
        $message | Should Match 'diagnostic root cause'

        $message = ''
        try
        {
            Assert-P4ChildGateOutput `
                -PhaseName 'empty child' `
                -OutputLines @() `
                -ExitCode 3 `
                -ExpectedMarkers @() | Out-Null
        }
        catch { $message = $_.Exception.Message }
        $message | Should Match 'captured output tail: 0/0 lines\r?\n<no output>'
        $message | Should Not Match 'lines`n<no output>'
    }

    It 'rejects a malformed duplicate of an otherwise valid regex scene marker' {
        $rejected = $false
        try
        {
            Assert-P4SceneGateOutput `
                -PhaseName 'scene' `
                -OutputLines @('P4_SCENE_OK value=1', 'P4_SCENE_OK value=bad') `
                -ExitCode 0 `
                -ExpectedRegexMarkers @('\AP4_SCENE_OK value=1\z') | Out-Null
        }
        catch { $rejected = $true }
        $rejected | Should Be $true
    }

    It 'captures stdout information warning and stderr from a real child process' {
        $probe = Join-Path $TestDrive 'streams.ps1'
        [IO.File]::WriteAllText(
            $probe,
            "Write-Output 'STREAM_OUTPUT'`nWrite-Host 'STREAM_INFORMATION'`n" +
                "Write-Warning 'STREAM_WARNING'`n[Console]::Error.WriteLine('STREAM_STDERR')`nexit 0`n")
        $result = Invoke-P4VerificationProcess `
            -FilePath 'pwsh' `
            -Arguments @('-NoProfile', '-File', $probe) `
            -TimeoutSeconds 10 `
            -Stage 'stream probe'
        $result.ExitCode | Should Be 0
        $text = @($result.OutputLines) -join "`n"
        @($result.OutputLines).Count | Should Be 4
        $text | Should Match 'STREAM_OUTPUT'
        $text | Should Match 'STREAM_INFORMATION'
        $text | Should Match 'STREAM_WARNING'
        $text | Should Match 'STREAM_STDERR'
        $text | Should Not Match 'MethodInvocationException|InvalidOperation'
    }

    It 'times out a stalled child through the process-tree watchdog' {
        $probe = Join-Path $TestDrive 'stall.ps1'
        [IO.File]::WriteAllText($probe, "Start-Sleep -Seconds 30`n")
        $message = ''
        try
        {
            Invoke-P4VerificationProcess `
                -FilePath 'pwsh' `
                -Arguments @('-NoProfile', '-File', $probe) `
                -TimeoutSeconds 1 `
                -Stage 'stall probe' | Out-Null
        }
        catch { $message = $_.Exception.Message }
        $message | Should Match 'stall probe timed out after 1 second'
    }

    It 'does not wait for an escaped descendant that keeps redirected pipes open' {
        $descendant = Join-Path $TestDrive 'pipe-descendant.ps1'
        $descendantIdPath = Join-Path $TestDrive 'pipe-descendant.pid'
        $probe = Join-Path $TestDrive 'pipe-parent.ps1'
        [IO.File]::WriteAllText($descendant, "Start-Sleep -Seconds 6`n")
        [IO.File]::WriteAllText(
            $probe,
            "`$descendant = Start-Process -FilePath 'pwsh' -ArgumentList @(" +
                "'-NoProfile','-NonInteractive','-File','$($descendant.Replace("'", "''"))') " +
                "-NoNewWindow -PassThru`n" +
                "[IO.File]::WriteAllText('$($descendantIdPath.Replace("'", "''"))', " +
                "[string]`$descendant.Id)`nexit 0`n")

        $message = ''
        $watch = [Diagnostics.Stopwatch]::StartNew()
        try
        {
            Invoke-P4VerificationProcess `
                -FilePath 'pwsh' `
                -Arguments @('-NoProfile', '-NonInteractive', '-File', $probe) `
                -TimeoutSeconds 1 `
                -Stage 'inherited pipe probe' | Out-Null
        }
        catch { $message = $_.Exception.Message }
        finally
        {
            $watch.Stop()
            if (Test-Path -LiteralPath $descendantIdPath)
            {
                $descendantId = [int][IO.File]::ReadAllText($descendantIdPath)
                Stop-Process -Id $descendantId -Force -ErrorAction SilentlyContinue
            }
        }

        $message | Should Match 'inherited pipe probe timed out after 1 second'
        $watch.ElapsedMilliseconds | Should BeLessThan 3000
    }

    It 'terminates a native-parent descendant after the intermediate parent exits' {
        $descendant = Join-Path $TestDrive 'native-descendant.ps1'
        $descendantIdPath = Join-Path $TestDrive 'native-descendant.pid'
        $parent = Join-Path $TestDrive 'native-parent.cmd'
        [IO.File]::WriteAllText(
            $descendant,
            "[IO.File]::WriteAllText('$($descendantIdPath.Replace("'", "''"))', " +
                "[string]`$PID)`nStart-Sleep -Seconds 15`n")
        [IO.File]::WriteAllText(
            $parent,
            "@echo off`r`nstart `"`" /b pwsh -NoProfile -NonInteractive -File " +
                "`"$descendant`"`r`nexit /b 0`r`n")

        $message = ''
        $descendantId = 0
        try
        {
            Invoke-P4VerificationProcess `
                -FilePath 'cmd.exe' `
                -Arguments @('/d', '/s', '/c', $parent) `
                -TimeoutSeconds 5 `
                -Stage 'native parent probe' | Out-Null
        }
        catch { $message = $_.Exception.Message }
        finally
        {
            $deadline = [DateTime]::UtcNow.AddSeconds(1)
            while (-not (Test-Path -LiteralPath $descendantIdPath) -and
                   [DateTime]::UtcNow -lt $deadline)
            {
                Start-Sleep -Milliseconds 20
            }
            if (Test-Path -LiteralPath $descendantIdPath)
            {
                $descendantId = [int][IO.File]::ReadAllText($descendantIdPath)
            }
        }

        try
        {
            $message | Should Match 'native parent probe timed out after 5 seconds'
            $descendantId | Should BeGreaterThan 0
            (Get-Process -Id $descendantId -ErrorAction SilentlyContinue) |
                Should BeNullOrEmpty
        }
        finally
        {
            if ($descendantId -gt 0)
            {
                Stop-Process -Id $descendantId -Force -ErrorAction SilentlyContinue
            }
        }
    }

    It 'keeps process exit and redirected stream cleanup under bounded waits' {
        $tokens = $null
        $errors = $null
        $ast = [Management.Automation.Language.Parser]::ParseFile(
            $script:FunctionsPath, [ref]$tokens, [ref]$errors)
        $errors.Count | Should Be 0
        $definition = @($ast.FindAll({
            param($node)
            $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                $node.Name -ceq 'Invoke-P4VerificationProcess'
        }, $true))
        $definition.Count | Should Be 1
        $body = $definition[0].Extent.Text

        $body | Should Not Match '\.WaitForExit\(\)'
        $body | Should Match 'CancellationTokenSource'
        $body | Should Match '\$streamTasks\.Wait\(\$remainingMilliseconds\)'
        $body | Should Match 'WaitForExit\(1000\)'
        $body | Should Match '\$wrapperCommand\s*=\s*''\$ErrorActionPreference = ''''Stop'''';'
        ([regex]::Matches($body, '\$startGate\.Dispose\(\)')).Count | Should Be 1
    }
}

Describe 'P4 formal matrix certificate parser' {
    It 'parses exactly four ordered cells and validates both digest pairs' {
        $result = Assert-P4MatrixCertificateOutput `
            -OutputLines (Get-P4MatrixCertificateOutput) `
            -ExitCode 0
        @($result.Cells).Count | Should Be 4
        $result.Cells[0].Mode | Should Be 'single'
        $result.Cells[3].Characters | Should Be 10
    }

    It 'rejects a missing or reordered cell and duplicate certificate marker' {
        $valid = @(Get-P4MatrixCertificateOutput)
        Test-P4MatrixCertificateRejects -OutputLines @($valid[0..2] + $valid[4]) |
            Should Be $true
        Test-P4MatrixCertificateRejects -OutputLines @(
            $valid[1], $valid[0], $valid[2], $valid[3], $valid[4]) | Should Be $true
        Test-P4MatrixCertificateRejects -OutputLines @($valid + $valid[4]) | Should Be $true
    }

    It 'rejects any mismatch among the seven single-parallel digests' {
        $digestFields = @('result', 'pose', 'full_pose', 'root', 'aim', 'turn_rotate', 'feet')
        foreach ($field in $digestFields)
        {
            $lines = @(Get-P4MatrixCertificateOutput)
            $lines[3] = [regex]::Replace(
                $lines[3],
                "(?<=\b$field=)[0-9A-F]{16}",
                'FEDCBA9876543210')
            Test-P4MatrixCertificateRejects -OutputLines $lines | Should Be $true
        }
    }

    It 'rejects raw success output when a later error or nonzero exit exists' {
        $valid = @(Get-P4MatrixCertificateOutput)
        Test-P4MatrixCertificateRejects `
            -OutputLines @($valid + 'ERROR: late failure') | Should Be $true
        Test-P4MatrixCertificateRejects -OutputLines $valid -ExitCode 17 | Should Be $true
    }
}

Describe 'P4 locked repository closure' {
    It 'accepts a clean repository at a real ancestor base' {
        $fixture = New-P4ClosureRepository
        Assert-P4RepositoryClosure `
            -RepositoryRoot $fixture.Root `
            -BaseCommit $fixture.Base | Out-Null
        Assert-P4CleanWorktree -RepositoryRoot $fixture.Root
    }

    It 'rejects a missing base and a commit that is not a HEAD ancestor' {
        $fixture = New-P4ClosureRepository
        Test-P4RepositoryClosureRejects `
            -RepositoryRoot $fixture.Root `
            -BaseCommit ('f' * 40) | Should Be $true

        [IO.File]::WriteAllText((Join-Path $fixture.Root 'later.txt'), "later`n")
        & git -C $fixture.Root add later.txt
        & git -C $fixture.Root commit --quiet -m later
        $later = (& git -C $fixture.Root rev-parse HEAD).Trim()
        & git -C $fixture.Root checkout --quiet $fixture.Base
        Test-P4RepositoryClosureRejects `
            -RepositoryRoot $fixture.Root `
            -BaseCommit $later | Should Be $true
    }

    It 'rejects committed staged and worktree whitespace errors' {
        $committed = New-P4ClosureRepository
        [IO.File]::WriteAllText((Join-Path $committed.Root 'bad.txt'), "bad  `n")
        & git -C $committed.Root add bad.txt
        & git -C $committed.Root commit --quiet -m bad
        Test-P4RepositoryClosureRejects $committed.Root $committed.Base | Should Be $true

        $staged = New-P4ClosureRepository
        [IO.File]::WriteAllText((Join-Path $staged.Root 'bad.txt'), "bad  `n")
        & git -C $staged.Root add bad.txt
        Test-P4RepositoryClosureRejects $staged.Root $staged.Base | Should Be $true

        $worktree = New-P4ClosureRepository
        [IO.File]::WriteAllText((Join-Path $worktree.Root 'tracked.txt'), "bad  `n")
        Test-P4RepositoryClosureRejects $worktree.Root $worktree.Base | Should Be $true
    }

    It 'rejects tracked generated or build output' {
        $fixture = New-P4ClosureRepository
        [void](New-Item -ItemType Directory -Path (Join-Path $fixture.Root 'bin'))
        [IO.File]::WriteAllText((Join-Path $fixture.Root 'bin\bad.dll'), 'binary')
        & git -C $fixture.Root add -f bin/bad.dll
        & git -C $fixture.Root commit --quiet -m forbidden
        Test-P4RepositoryClosureRejects $fixture.Root $fixture.Base | Should Be $true
    }

    It 'rejects modified staged and untracked status entries' {
        $modified = New-P4ClosureRepository
        [IO.File]::WriteAllText((Join-Path $modified.Root 'tracked.txt'), "modified`n")
        Test-P4CleanWorktreeRejects $modified.Root | Should Be $true

        $staged = New-P4ClosureRepository
        [IO.File]::WriteAllText((Join-Path $staged.Root 'staged.txt'), "staged`n")
        & git -C $staged.Root add staged.txt
        Test-P4CleanWorktreeRejects $staged.Root | Should Be $true

        $untracked = New-P4ClosureRepository
        [IO.File]::WriteAllText((Join-Path $untracked.Root 'untracked.txt'), "untracked`n")
        Test-P4CleanWorktreeRejects $untracked.Root | Should Be $true
    }
}

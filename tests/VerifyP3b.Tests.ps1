$script:RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:FunctionsPath = Join-Path $script:RepositoryRoot 'scripts\p3b-verification-functions.ps1'
$script:WorkerPath = Join-Path $script:RepositoryRoot 'src\Als.Godot\Locomotion\AlsP3WorkerRoot.cs'
$script:RuntimePath = Join-Path $script:RepositoryRoot 'src\Als.Godot\Locomotion\AlsP3RuntimeContext.cs'
$script:HarnessPath = Join-Path $script:RepositoryRoot 'src\Als.Godot\Locomotion\P3bAnimationHarness.cs'
$script:VerifierPath = Join-Path $script:RepositoryRoot 'scripts\verify-p3b.ps1'
if (Test-Path -LiteralPath $script:FunctionsPath)
{
    . $script:FunctionsPath
}

$script:ValidMarker = 'GODOT_ALS_P3B_OK mode=single characters=1 warmup=120 frames=600 digest=0123456789ABCDEF pose=FEDCBA9876543210 missing=0 stale=0 generation=0 off_main=0 lag=0 allocations=0 p95_us=100 p99_us=200'
$script:ValidAllocation = 'GODOT_ALS_P3B_ALLOC model=0 controller=0 skeleton=0 exchange=0 commit=0'
$script:ValidReplacement = 'GODOT_ALS_P3B_REPLACEMENT character=0 old_generation_rejected=1'
$script:ValidPose = 'GODOT_ALS_P3B_POSE character=0 changes=42'
$script:VerifierSource = [System.IO.File]::ReadAllText($script:VerifierPath)

function Get-P3bOutput
{
    param(
        [string]$Marker = $script:ValidMarker,
        [string]$Allocation = $script:ValidAllocation,
        [string[]]$Advances = @('GODOT_ALS_P3B_ADVANCE character=0 frames=600'),
        [string[]]$Poses = @($script:ValidPose),
        [string]$Replacement = $script:ValidReplacement
    )

    return @('Godot Engine test', $Allocation) + $Advances + $Poses + @($Replacement, $Marker)
}

function Test-P3bParserRejects
{
    param(
        [object[]]$OutputLines,
        [string]$ExpectedMode = 'single',
        [int]$ExpectedCharacterCount = 1
    )

    try
    {
        ConvertFrom-P3bHarnessOutput `
            -OutputLines $OutputLines `
            -ExpectedMode $ExpectedMode `
            -ExpectedCharacterCount $ExpectedCharacterCount | Out-Null
        return $false
    }
    catch
    {
        return $true
    }
}

function Test-P3bChildGateRejects
{
    param(
        [object[]]$OutputLines,
        [int]$ExitCode,
        [string]$ExpectedMarker
    )

    try
    {
        Assert-P3bChildGateOutput `
            -PhaseName 'test' `
            -OutputLines $OutputLines `
            -ExitCode $ExitCode `
            -ExpectedMarker $ExpectedMarker
        return $false
    }
    catch
    {
        return $true
    }
}

function Test-P3bCleanWorktreeRejects
{
    param([string]$RepositoryRoot)

    try
    {
        Assert-P3bCleanWorktree -RepositoryRoot $RepositoryRoot
        return $false
    }
    catch
    {
        return $true
    }
}

Describe 'P3B verifier contracts' {
    It 'provides the Task 6 parser and pair validator' {
        Get-Command ConvertFrom-P3bHarnessOutput -ErrorAction SilentlyContinue |
            Should Not BeNullOrEmpty
        Get-Command Assert-P3bResultPair -ErrorAction SilentlyContinue |
            Should Not BeNullOrEmpty
    }

    It 'accepts one complete marker and all required evidence' {
        if ($null -eq (Get-Command ConvertFrom-P3bHarnessOutput -ErrorAction SilentlyContinue))
        {
            return
        }

        $result = ConvertFrom-P3bHarnessOutput `
            -OutputLines (Get-P3bOutput) `
            -ExpectedMode single `
            -ExpectedCharacterCount 1

        $result.Digest | Should Be '0123456789ABCDEF'
        $result.Pose | Should Be 'FEDCBA9876543210'
        $result.P95Microseconds | Should Be 100
        $result.P99Microseconds | Should Be 200
    }

    It 'rejects malformed duplicate or unsuccessful markers' {
        if ($null -eq (Get-Command ConvertFrom-P3bHarnessOutput -ErrorAction SilentlyContinue))
        {
            return
        }

        Test-P3bParserRejects ((Get-P3bOutput) + 'GODOT_ALS_P3B_OK malformed') |
            Should Be $true
        Test-P3bParserRejects ((Get-P3bOutput) + $script:ValidMarker) |
            Should Be $true
        Test-P3bParserRejects @('GODOT_ALS_P3B_FAIL code=runtime') |
            Should Be $true
    }

    It 'rejects wrong fixed fields identities and trailing fields' {
        if ($null -eq (Get-Command ConvertFrom-P3bHarnessOutput -ErrorAction SilentlyContinue))
        {
            return
        }

        Test-P3bParserRejects (Get-P3bOutput -Marker $script:ValidMarker.Replace('warmup=120', 'warmup=119')) |
            Should Be $true
        Test-P3bParserRejects (Get-P3bOutput -Marker ($script:ValidMarker + ' extra=1')) |
            Should Be $true
        Test-P3bParserRejects (Get-P3bOutput) -ExpectedMode parallel | Should Be $true
        Test-P3bParserRejects (Get-P3bOutput) -ExpectedCharacterCount 10 | Should Be $true
    }

    It 'rejects every nonzero error and aggregate allocation counter' {
        if ($null -eq (Get-Command ConvertFrom-P3bHarnessOutput -ErrorAction SilentlyContinue))
        {
            return
        }

        foreach ($field in @('missing', 'stale', 'generation', 'lag', 'allocations'))
        {
            Test-P3bParserRejects (Get-P3bOutput -Marker $script:ValidMarker.Replace("$field=0", "$field=1")) |
                Should Be $true
        }
    }

    It 'rejects nonzero allocation buckets' {
        if ($null -eq (Get-Command ConvertFrom-P3bHarnessOutput -ErrorAction SilentlyContinue))
        {
            return
        }

        foreach ($field in @('model', 'controller', 'skeleton', 'exchange', 'commit'))
        {
            Test-P3bParserRejects (Get-P3bOutput -Allocation $script:ValidAllocation.Replace("$field=0", "$field=1")) |
                Should Be $true
        }
    }

    It 'requires exactly one 600-frame advance line for every character' {
        if ($null -eq (Get-Command ConvertFrom-P3bHarnessOutput -ErrorAction SilentlyContinue))
        {
            return
        }

        Test-P3bParserRejects (Get-P3bOutput -Advances @()) | Should Be $true
        Test-P3bParserRejects (Get-P3bOutput -Advances @('GODOT_ALS_P3B_ADVANCE character=0 frames=599')) |
            Should Be $true
        Test-P3bParserRejects (Get-P3bOutput -Advances @(
            'GODOT_ALS_P3B_ADVANCE character=0 frames=600',
            'GODOT_ALS_P3B_ADVANCE character=0 frames=600')) | Should Be $true

        $tenMarker = $script:ValidMarker.Replace('characters=1', 'characters=10')
        $tenAdvances = @(0..9 | ForEach-Object {
            "GODOT_ALS_P3B_ADVANCE character=$_ frames=600"
        })
        $tenPoses = @(0..9 | ForEach-Object {
            "GODOT_ALS_P3B_POSE character=$_ changes=42"
        })
        $result = ConvertFrom-P3bHarnessOutput `
            -OutputLines (Get-P3bOutput -Marker $tenMarker -Advances $tenAdvances -Poses $tenPoses) `
            -ExpectedMode single `
            -ExpectedCharacterCount 10
        $result.Advances.Count | Should Be 10
    }

    It 'requires production old-generation rejection evidence exactly once' {
        if ($null -eq (Get-Command ConvertFrom-P3bHarnessOutput -ErrorAction SilentlyContinue))
        {
            return
        }

        Test-P3bParserRejects (Get-P3bOutput -Replacement '') | Should Be $true
        Test-P3bParserRejects (Get-P3bOutput -Replacement 'GODOT_ALS_P3B_REPLACEMENT character=0 old_generation_rejected=0') |
            Should Be $true
    }

    It 'requires nonzero raw skeleton pose changes for every character' {
        Test-P3bParserRejects (Get-P3bOutput -Poses @()) | Should Be $true
        Test-P3bParserRejects (Get-P3bOutput -Poses @(
            'GODOT_ALS_P3B_POSE character=0 changes=0')) | Should Be $true
        Test-P3bParserRejects (Get-P3bOutput -Poses @(
            'GODOT_ALS_P3B_POSE character=0 changes=1',
            'GODOT_ALS_P3B_POSE character=0 changes=2')) | Should Be $true
    }

    It 'rejects wrong worker affinity and invalid percentiles' {
        if ($null -eq (Get-Command ConvertFrom-P3bHarnessOutput -ErrorAction SilentlyContinue))
        {
            return
        }

        Test-P3bParserRejects (Get-P3bOutput -Marker $script:ValidMarker.Replace('off_main=0', 'off_main=1')) |
            Should Be $true
        $parallelMarker = $script:ValidMarker.Replace('mode=single', 'mode=parallel').Replace('off_main=0', 'off_main=1')
        ConvertFrom-P3bHarnessOutput `
            -OutputLines (Get-P3bOutput -Marker $parallelMarker) `
            -ExpectedMode parallel `
            -ExpectedCharacterCount 1 | Out-Null
        Test-P3bParserRejects (Get-P3bOutput -Marker $script:ValidMarker.Replace('p95_us=100 p99_us=200', 'p95_us=201 p99_us=200')) |
            Should Be $true
    }

    It 'rejects Godot script and engine errors even with a valid marker' {
        if ($null -eq (Get-Command ConvertFrom-P3bHarnessOutput -ErrorAction SilentlyContinue))
        {
            return
        }

        Test-P3bParserRejects ((Get-P3bOutput) + 'SCRIPT ERROR: failed') | Should Be $true
        Test-P3bParserRejects ((Get-P3bOutput) + 'ERROR: failed') | Should Be $true
        Test-P3bParserRejects ((Get-P3bOutput) + '  SCRIPT ERROR: indented failure') |
            Should Be $true
        Test-P3bParserRejects ((Get-P3bOutput) + 'Godot: ERROR: prefixed failure') |
            Should Be $true
    }

    It 'rejects digest or pose mismatch between execution modes' {
        if ($null -eq (Get-Command Assert-P3bResultPair -ErrorAction SilentlyContinue))
        {
            return
        }

        $single = [pscustomobject]@{
            Mode = 'single'; Characters = 1; Digest = '0123456789ABCDEF'; Pose = 'FEDCBA9876543210'
        }
        $parallel = [pscustomobject]@{
            Mode = 'parallel'; Characters = 1; Digest = '0123456789ABCDEE'; Pose = 'FEDCBA9876543210'
        }
        $digestRejected = $false
        try
        {
            Assert-P3bResultPair -Single $single -Parallel $parallel -CharacterCount 1
        }
        catch
        {
            $digestRejected = $true
        }
        $digestRejected | Should Be $true
        $parallel.Digest = $single.Digest
        $parallel.Pose = 'FEDCBA9876543211'
        $poseRejected = $false
        try
        {
            Assert-P3bResultPair -Single $single -Parallel $parallel -CharacterCount 1
        }
        catch
        {
            $poseRejected = $true
        }
        $poseRejected | Should Be $true
    }
}

Describe 'P3B raw-pose and timing instrumentation' {
    It 'publishes the frame-independent full skeleton digest to the harness' {
        $runtimeSource = [System.IO.File]::ReadAllText($script:RuntimePath)
        $harnessSource = [System.IO.File]::ReadAllText($script:HarnessPath)

        $runtimeSource | Should Match 'ulong FullPoseDigest'
        $harnessSource | Should Match 'diagnostics\.FullPoseDigest'
        $harnessSource | Should Not Match 'Append\(ref _poseDigest, diagnostics\.PoseDigest\)'
    }

    It 'sums only bounded production segments into worker timing' {
        $workerSource = [System.IO.File]::ReadAllText($script:WorkerPath)

        $workerSource | Should Not Match 'workerStartedAt'
        $workerSource | Should Match 'RecordWorkerAdvance\(\s*measurementIndex,\s*productionElapsedTicks\)'
        ([regex]::Matches(
            $workerSource,
            'productionSegmentStartedAt = measure\s*\? Stopwatch\.GetTimestamp\(\)\s*:\s*0L;')).Count |
            Should Be 6
        ([regex]::Matches(
            $workerSource,
            'productionElapsedTicks \+= Stopwatch\.GetTimestamp\(\) - productionSegmentStartedAt;')).Count |
            Should Be 6

        $firstProbe = $workerSource.IndexOf('GC.GetAllocatedBytesForCurrentThread()')
        $firstSegment = $workerSource.IndexOf('productionSegmentStartedAt')
        $firstRecord = $workerSource.IndexOf('RecordWorkerAdvance')
        $lastAllocationRecord = $workerSource.LastIndexOf('AddExchangeAllocations')
        $firstProbe | Should BeGreaterThan -1
        $firstSegment | Should BeGreaterThan $firstProbe
        $firstRecord | Should BeGreaterThan $lastAllocationRecord
    }
}

Describe 'P3B final regression closure' {
    BeforeEach {
        $script:ClosureRepository = Join-Path $TestDrive ([Guid]::NewGuid().ToString('N'))
        [void](New-Item -ItemType Directory -Path $script:ClosureRepository)
        & git -C $script:ClosureRepository init --quiet
        & git -C $script:ClosureRepository config user.email 'p3b-tests@example.invalid'
        & git -C $script:ClosureRepository config user.name 'P3B Tests'
        [System.IO.File]::WriteAllText((Join-Path $script:ClosureRepository 'tracked.txt'), "clean`n")
        & git -C $script:ClosureRepository add tracked.txt
        & git -C $script:ClosureRepository commit --quiet -m baseline
        if ($LASTEXITCODE -ne 0) { throw 'Could not initialize P3B closure test repository.' }
    }

    It 'accepts every unique full P3A closure marker without errors' {
        $output = @(
            'P2B_VERIFICATION_OK',
            'P1_VERIFICATION_OK',
            'P0_VERIFICATION_OK',
            'P3A_VERIFICATION_OK'
        )
        foreach ($marker in $output)
        {
            Assert-P3bChildGateOutput `
                -PhaseName $marker `
                -OutputLines $output `
                -ExitCode 0 `
                -ExpectedMarker $marker
        }
    }

    It 'rejects missing duplicate and successful-looking error output' {
        foreach ($output in @(
            ,@('build output'),
            ,@('P2B_VERIFICATION_OK', 'P2B_VERIFICATION_OK'),
            ,@('P2B_VERIFICATION_OK', 'SCRIPT ERROR: failed'),
            ,@('P2B_VERIFICATION_OK', 'Godot: ERROR: failed')
        ))
        {
            Test-P3bChildGateRejects `
                -OutputLines $output `
                -ExitCode 0 `
                -ExpectedMarker 'P2B_VERIFICATION_OK' | Should Be $true
        }
    }

    It 'rejects a child gate nonzero exit even with the expected marker' {
        Test-P3bChildGateRejects `
            -OutputLines @('P1_VERIFICATION_OK') `
            -ExitCode 7 `
            -ExpectedMarker 'P1_VERIFICATION_OK' | Should Be $true
    }

    It 'captures and rejects information-stream errors from a real child script' {
        $probe = Join-Path $TestDrive 'information-error.ps1'
        [System.IO.File]::WriteAllText(
            $probe,
            "Write-Host 'Godot: ERROR: information stream failure'`nWrite-Output 'P3A_VERIFICATION_OK'`nexit 0`n")
        $output = @(& $probe *>&1)
        $exitCode = $LASTEXITCODE

        Test-P3bChildGateRejects `
            -OutputLines $output `
            -ExitCode $exitCode `
            -ExpectedMarker 'P3A_VERIFICATION_OK' | Should Be $true
    }

    It 'accepts Pester descriptions that name error tokens without emitting an error line' {
        Assert-P3bChildGateOutput `
            -PhaseName 'P3A' `
            -OutputLines @(
                '[+] rejects SCRIPT ERROR and ERROR lines even with exit zero',
                'P3A_VERIFICATION_OK') `
            -ExitCode 0 `
            -ExpectedMarker 'P3A_VERIFICATION_OK'
    }

    It 'runs full P3A and final Release tests after the P3B matrix' {
        $matrixIndex = $script:VerifierSource.IndexOf('foreach ($characterCount in @(1, 10))')
        $p3aIndex = $script:VerifierSource.IndexOf("'verify-p3a.ps1'", $matrixIndex)
        $testsIndex = $script:VerifierSource.IndexOf(
            'dotnet test $solutionPath -c Release --no-restore',
            $matrixIndex)
        $successIndex = $script:VerifierSource.LastIndexOf('Write-Output $completionMarker')

        $matrixIndex | Should BeGreaterThan -1
        $p3aIndex | Should BeGreaterThan $matrixIndex
        $testsIndex | Should BeGreaterThan $p3aIndex
        $successIndex | Should BeGreaterThan $testsIndex
    }

    It 'captures all full P3A streams and validates its four real completion markers' {
        $script:VerifierSource | Should Match ([regex]::Escape(
            '& $p3aScript -GodotExecutable $GodotExecutable -ProjectRoot $projectRootPath *>&1'))
        $script:VerifierSource | Should Not Match ([regex]::Escape(
            '& $p3aScript -GodotExecutable $GodotExecutable -ProjectRoot $projectRootPath -SkipRegression'))
        $script:VerifierSource | Should Match 'Assert-P3bChildGateOutput'
        foreach ($marker in @(
            'P3A_VERIFICATION_OK',
            'P2B_VERIFICATION_OK',
            'P1_VERIFICATION_OK',
            'P0_VERIFICATION_OK'))
        {
            $script:VerifierSource | Should Match ([regex]::Escape("ExpectedMarker '$marker'"))
        }
        $script:VerifierSource | Should Not Match "Write-Output 'P3A_VERIFICATION_OK"
        $script:VerifierSource | Should Not Match "Write-Output 'P2B_VERIFICATION_OK"
        $script:VerifierSource | Should Not Match "Write-Output 'P1_VERIFICATION_OK"
        $script:VerifierSource | Should Not Match "Write-Output 'P0_VERIFICATION_OK"
    }

    It 'uses a focused marker that cannot impersonate full verification' {
        Get-P3bCompletionMarker -RegressionSkipped $true |
            Should Be 'P3B_FOCUSED_VERIFICATION_OK regression=skipped'
        Get-P3bCompletionMarker -RegressionSkipped $false |
            Should Be 'P3B_VERIFICATION_OK'
        $script:VerifierSource | Should Match 'Get-P3bCompletionMarker -RegressionSkipped \(\[bool\]\$SkipRegression\)'
    }

    It 'runs the locked P3A closure and clean-worktree closure before full success' {
        $p3aClosureIndex = $script:VerifierSource.IndexOf(
            'Assert-P3aRepositoryClosure -RepositoryRoot $projectRootPath -BaseCommit $p3aBaseCommit')
        $cleanClosureIndex = $script:VerifierSource.IndexOf(
            'Assert-P3bCleanWorktree -RepositoryRoot $projectRootPath')
        $successIndex = $script:VerifierSource.LastIndexOf('Write-Output $completionMarker')

        $p3aClosureIndex | Should BeGreaterThan -1
        $cleanClosureIndex | Should BeGreaterThan $p3aClosureIndex
        $successIndex | Should BeGreaterThan $cleanClosureIndex
    }

    It 'accepts a clean repository' {
        $failure = ''
        try { Assert-P3bCleanWorktree -RepositoryRoot $script:ClosureRepository }
        catch { $failure = $_.Exception.Message }
        $failure | Should BeNullOrEmpty
    }

    It 'rejects a dirty tracked worktree' {
        [System.IO.File]::WriteAllText(
            (Join-Path $script:ClosureRepository 'tracked.txt'),
            "modified`n")
        Test-P3bCleanWorktreeRejects -RepositoryRoot $script:ClosureRepository |
            Should Be $true
    }

    It 'rejects staged changes' {
        [System.IO.File]::WriteAllText(
            (Join-Path $script:ClosureRepository 'staged.txt'),
            "staged`n")
        & git -C $script:ClosureRepository add staged.txt
        Test-P3bCleanWorktreeRejects -RepositoryRoot $script:ClosureRepository |
            Should Be $true
    }

    It 'rejects untracked files' {
        [System.IO.File]::WriteAllText(
            (Join-Path $script:ClosureRepository 'untracked.txt'),
            "untracked`n")
        Test-P3bCleanWorktreeRejects -RepositoryRoot $script:ClosureRepository |
            Should Be $true
    }
}

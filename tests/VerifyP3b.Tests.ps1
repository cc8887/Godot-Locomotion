$script:RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:FunctionsPath = Join-Path $script:RepositoryRoot 'scripts\p3b-verification-functions.ps1'
if (Test-Path -LiteralPath $script:FunctionsPath)
{
    . $script:FunctionsPath
}

$script:ValidMarker = 'GODOT_ALS_P3B_OK mode=single characters=1 warmup=120 frames=600 digest=0123456789ABCDEF pose=FEDCBA9876543210 missing=0 stale=0 generation=0 off_main=0 lag=0 allocations=0 p95_us=100 p99_us=200'
$script:ValidAllocation = 'GODOT_ALS_P3B_ALLOC model=0 controller=0 skeleton=0 exchange=0 commit=0'
$script:ValidReplacement = 'GODOT_ALS_P3B_REPLACEMENT character=0 old_generation_rejected=1'

function Get-P3bOutput
{
    param(
        [string]$Marker = $script:ValidMarker,
        [string]$Allocation = $script:ValidAllocation,
        [string[]]$Advances = @('GODOT_ALS_P3B_ADVANCE character=0 frames=600'),
        [string]$Replacement = $script:ValidReplacement
    )

    return @('Godot Engine test', $Allocation) + $Advances + @($Replacement, $Marker)
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
        $result = ConvertFrom-P3bHarnessOutput `
            -OutputLines (Get-P3bOutput -Marker $tenMarker -Advances $tenAdvances) `
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

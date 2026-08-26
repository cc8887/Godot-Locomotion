$script:RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:FunctionsPath = Join-Path $script:RepositoryRoot 'scripts\p3a-verification-functions.ps1'
$script:VerifierPath = Join-Path $script:RepositoryRoot 'scripts\verify-p3a.ps1'
$script:SolutionPath = Join-Path $script:RepositoryRoot 'GodotALS.sln'
$script:CommandResolverPath = Join-Path $script:RepositoryRoot 'src\Als.Core\Locomotion\AlsLocomotionCommandResolver.cs'
$script:LocomotionModelPath = Join-Path $script:RepositoryRoot 'src\Als.Core\Locomotion\AlsLocomotionModel.cs'
if (Test-Path -LiteralPath $script:FunctionsPath)
{
    . $script:FunctionsPath
}

$script:ValidMarker = 'GODOT_ALS_P3A_OK mode=single characters=1 warmup=120 frames=600 digest=302AA679C5C51AC1 missing=0 stale=0 generation=0 off_main=0 lag=0 allocations=0'
$script:VerifierSource = [System.IO.File]::ReadAllText($script:VerifierPath)
$script:SolutionSource = [System.IO.File]::ReadAllText($script:SolutionPath)

function Test-P3aParserRejects
{
    param(
        [object[]]$OutputLines,
        [string]$ExpectedMode = 'single',
        [int]$ExpectedCharacterCount = 1
    )

    try
    {
        ConvertFrom-P3aHarnessOutput `
            -OutputLines $OutputLines `
            -ExpectedMode $ExpectedMode `
            -ExpectedCharacterCount $ExpectedCharacterCount | Out-Null
        $false
    }
    catch
    {
        $true
    }
}

Describe 'P3A verifier marker parsing' {
    It 'accepts exactly one complete marker among non-marker output' {
        $result = ConvertFrom-P3aHarnessOutput `
            -OutputLines @('Godot Engine test', $script:ValidMarker) `
            -ExpectedMode 'single' `
            -ExpectedCharacterCount 1

        $result.Digest | Should Be '302AA679C5C51AC1'
    }

    It 'rejects a valid marker plus a malformed marker line' {
        Test-P3aParserRejects @($script:ValidMarker, 'GODOT_ALS_P3A_OK malformed') | Should Be $true
    }

    It 'rejects duplicate valid markers' {
        Test-P3aParserRejects @($script:ValidMarker, $script:ValidMarker) | Should Be $true
    }

    It 'rejects a FAIL marker' {
        Test-P3aParserRejects @('GODOT_ALS_P3A_FAIL mode=single characters=1') | Should Be $true
    }

    It 'rejects a marker with a wrong fixed field' {
        Test-P3aParserRejects @($script:ValidMarker.Replace('warmup=120', 'warmup=119')) | Should Be $true
    }

    It 'rejects a marker with an extra field' {
        Test-P3aParserRejects @($script:ValidMarker + ' extra=1') | Should Be $true
    }

    It 'rejects a valid marker with an unexpected mode' {
        Test-P3aParserRejects @($script:ValidMarker) -ExpectedMode 'parallel' | Should Be $true
    }

    It 'rejects a valid marker with an unexpected character count' {
        Test-P3aParserRejects @($script:ValidMarker) -ExpectedCharacterCount 10 | Should Be $true
    }

    It 'rejects a well-formed marker whose digest drifted from the character baseline' {
        $drifted = $script:ValidMarker.Replace('302AA679C5C51AC1', '302AA679C5C51AC0')

        Test-P3aParserRejects @($drifted) | Should Be $true
    }

    It 'rejects equal single and parallel digests when both drifted from the baseline' {
        $pairValidator = Get-Command Assert-P3aResultPair -ErrorAction SilentlyContinue
        $pairValidator | Should Not BeNullOrEmpty
        if ($null -eq $pairValidator)
        {
            return
        }

        $single = [pscustomobject]@{
            Mode = 'single'; Characters = 1; Digest = '302AA679C5C51AC0'
        }
        $parallel = [pscustomobject]@{
            Mode = 'parallel'; Characters = 1; Digest = '302AA679C5C51AC0'
        }
        $rejected = $false
        try
        {
            Assert-P3aResultPair -Single $single -Parallel $parallel -CharacterCount 1
        }
        catch
        {
            $rejected = $true
        }

        $rejected | Should Be $true
    }
}

Describe 'P3A verifier build configuration' {
    It 'maps the Godot solution project Release configuration to ExportRelease' {
        $project = [regex]::Escape('{70FF2579-9440-4397-98AD-D77F94992698}')
        $script:SolutionSource | Should Match "${project}\.Release\|Any CPU\.ActiveCfg = ExportRelease\|Any CPU"
        $script:SolutionSource | Should Match "${project}\.Release\|Any CPU\.Build\.0 = ExportRelease\|Any CPU"
    }

    It 'builds Release and the optimized Debug editor host before the final Release tests' {
        $script:VerifierSource | Should Match 'dotnet build \$solutionPath -c Release --no-restore'
        $script:VerifierSource | Should Match 'dotnet build \$godotProjectPath -c Debug -p:Optimize=true --no-restore --no-incremental'
        $script:VerifierSource | Should Match 'dotnet test \$solutionPath -c Release --no-restore'
    }

    It 'runs the repository Pester suite during non-skipped regression verification' {
        $script:VerifierSource | Should Match 'Invoke-Pester'
        $script:VerifierSource | Should Match "tests\\\*\.Tests\.ps1"
    }
}

Describe 'P3A verifier regression closure' {
    It 'runs P2B P1 P0 and the final Release tests in order after the P3A matrix' {
        $matrixIndex = $script:VerifierSource.IndexOf('foreach ($characterCount in @(1, 10))')
        $p2bIndex = $script:VerifierSource.IndexOf("'verify-p2b.ps1'", $matrixIndex)
        $p1Index = $script:VerifierSource.IndexOf("'verify-p1.ps1'", $matrixIndex)
        $p0Index = $script:VerifierSource.IndexOf("'verify-p0.ps1'", $matrixIndex)
        $finalTestsIndex = $script:VerifierSource.IndexOf(
            'dotnet test $solutionPath -c Release --no-restore',
            $matrixIndex)

        $matrixIndex | Should BeGreaterThan -1
        $p2bIndex | Should BeGreaterThan $matrixIndex
        $p1Index | Should BeGreaterThan $p2bIndex
        $p0Index | Should BeGreaterThan $p1Index
        $finalTestsIndex | Should BeGreaterThan $p0Index
    }

    It 'passes the selected Godot executable to every phase verifier' {
        foreach ($phase in @('p2b', 'p1', 'p0')) {
            $script:VerifierSource | Should Match ([regex]::Escape("'verify-$phase.ps1'"))
        }
        $script:VerifierSource | Should Match ([regex]::Escape(
            '& $phaseScript -GodotExecutable $GodotExecutable -ProjectRoot $projectRootPath'))
    }

    It 'fails immediately after each non-zero regression command' {
        $regressionIndex = $script:VerifierSource.IndexOf("'verify-p2b.ps1'")
        $regressionIndex | Should BeGreaterThan -1
        if ($regressionIndex -lt 0) { return }
        $regressionSource = $script:VerifierSource.Substring($regressionIndex)
        $guardPattern = '(?ms)& \$phaseScript -GodotExecutable \$GodotExecutable -ProjectRoot \$projectRootPath\s+if \(\$LASTEXITCODE -ne 0\) \{ exit \$LASTEXITCODE \}'
        ([regex]::Matches($regressionSource, $guardPattern)).Count | Should Be 1
        $regressionSource | Should Match '(?ms)dotnet test \$solutionPath -c Release --no-restore\s+if \(\$LASTEXITCODE -ne 0\) \{ exit \$LASTEXITCODE \}'
    }

    It 'keeps the regression closure enabled by default and skippable only as one block' {
        $script:VerifierSource | Should Match '(?ms)if \(-not \$SkipRegression\) \{\s+\$phaseScripts = @\('
        $script:VerifierSource | Should Not Match '\[switch\]\$RunRegression'
    }
}

Describe 'P3A hot-path enum validation' {
    It 'does not use reflection-backed Enum.IsDefined in locomotion hot paths' {
        [System.IO.File]::ReadAllText($script:CommandResolverPath) | Should Not Match 'Enum\.IsDefined'
        [System.IO.File]::ReadAllText($script:LocomotionModelPath) | Should Not Match 'Enum\.IsDefined'
    }
}

$script:RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:FunctionsPath = Join-Path $script:RepositoryRoot 'scripts\p3a-verification-functions.ps1'
$script:VerifierPath = Join-Path $script:RepositoryRoot 'scripts\verify-p3a.ps1'
$script:SolutionPath = Join-Path $script:RepositoryRoot 'GodotALS.sln'
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

    It 'builds and tests Release before rebuilding the optimized Debug editor host' {
        $script:VerifierSource | Should Match 'dotnet build \$solutionPath -c Release --no-restore'
        $script:VerifierSource | Should Match 'dotnet test \$solutionPath -c Release --no-build --no-restore'
        $script:VerifierSource | Should Match 'dotnet build \$godotProjectPath -c Debug -p:Optimize=true --no-restore --no-incremental'
    }

    It 'runs the repository Pester suite during non-skipped regression verification' {
        $script:VerifierSource | Should Match 'Invoke-Pester'
        $script:VerifierSource | Should Match "tests\\\*\.Tests\.ps1"
    }
}

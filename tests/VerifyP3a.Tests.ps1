$script:RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:FunctionsPath = Join-Path $script:RepositoryRoot 'scripts\p3a-verification-functions.ps1'
if (Test-Path -LiteralPath $script:FunctionsPath)
{
    . $script:FunctionsPath
}

$script:ValidMarker = 'GODOT_ALS_P3A_OK mode=single characters=1 warmup=120 frames=600 digest=0123456789ABCDEF missing=0 stale=0 generation=0 off_main=0 lag=0 allocations=0'

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

        $result.Digest | Should Be '0123456789ABCDEF'
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
}

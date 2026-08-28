$script:RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:P2bVerifierPath = Join-Path $script:RepositoryRoot 'scripts\verify-p2b.ps1'
$script:P2bFunctionsPath = Join-Path $script:RepositoryRoot 'scripts\p2b-verification-functions.ps1'
$script:P1VerifierPath = Join-Path $script:RepositoryRoot 'scripts\verify-p1.ps1'
$script:P0VerifierPath = Join-Path $script:RepositoryRoot 'scripts\verify-p0.ps1'
$script:GodotOutputFunctionsPath = Join-Path $script:RepositoryRoot 'scripts\godot-output-functions.ps1'

if (Test-Path -LiteralPath $script:P2bFunctionsPath) {
    . $script:P2bFunctionsPath
}
if (Test-Path -LiteralPath $script:GodotOutputFunctionsPath) {
    . $script:GodotOutputFunctionsPath
}

Describe 'P2B formal manifest lock' {
    It 'locks the regenerated formal manifest SHA before parsing JSON' {
        $source = [System.IO.File]::ReadAllText($script:P2bVerifierPath)
        $expectedHash = '369AF84ABA028AFBDF6EEA7F1A4F1161DFD4B5E9BEA736E9460BFE368CE14327'
        $hashIndex = $source.IndexOf('Assert-P2bManifestHash')
        $jsonIndex = $source.IndexOf('ConvertFrom-Json')

        $source | Should Match $expectedHash
        $hashIndex | Should BeGreaterThan -1
        $jsonIndex | Should BeGreaterThan $hashIndex
    }

    It 'rejects a wrong manifest hash without touching the formal asset' {
        (Get-Command Assert-P2bManifestHash -ErrorAction SilentlyContinue) |
            Should Not BeNullOrEmpty
        if (-not (Get-Command Assert-P2bManifestHash -ErrorAction SilentlyContinue)) {
            return
        }

        $fixture = Join-Path $TestDrive 'als_manifest.json'
        [System.IO.File]::WriteAllText($fixture, '{"status":"wrong"}')

        $rejected = $false
        try {
            Assert-P2bManifestHash `
                -ManifestPath $fixture `
                -ExpectedSha256 '369AF84ABA028AFBDF6EEA7F1A4F1161DFD4B5E9BEA736E9460BFE368CE14327'
        }
        catch {
            $rejected = $true
        }

        $rejected | Should Be $true
    }
}

Describe 'Godot verifier error-line handling' {
    It 'rejects SCRIPT ERROR and ERROR lines even with exit zero and a success marker' {
        (Get-Command Assert-GodotInvocationSucceeded -ErrorAction SilentlyContinue) |
            Should Not BeNullOrEmpty
        if (-not (Get-Command Assert-GodotInvocationSucceeded -ErrorAction SilentlyContinue)) {
            return
        }

        foreach ($line in @(
            'SCRIPT ERROR: injected failure',
            'ERROR: injected failure'
        )) {
            $rejected = $false
            try {
                Assert-GodotInvocationSucceeded `
                -OutputLines @($line, 'GODOT_ALS_P0_OK frame=0 generation=1') `
                -ExitCode 0 `
                -Context 'test invocation'
            }
            catch {
                $rejected = $true
            }

            $rejected | Should Be $true
        }
    }

    It 'accepts clean output with exit zero' {
        (Get-Command Assert-GodotInvocationSucceeded -ErrorAction SilentlyContinue) |
            Should Not BeNullOrEmpty
        if (-not (Get-Command Assert-GodotInvocationSucceeded -ErrorAction SilentlyContinue)) {
            return
        }

        Assert-GodotInvocationSucceeded `
            -OutputLines @('Godot Engine test', 'GODOT_ALS_P0_OK frame=0 generation=1') `
            -ExitCode 0 `
            -Context 'test invocation'
    }

    It 'routes both P1 and P0 output through the strict parser before success markers' {
        foreach ($verifierPath in @($script:P1VerifierPath, $script:P0VerifierPath)) {
            $source = [System.IO.File]::ReadAllText($verifierPath)
            $assertIndex = $source.IndexOf('Assert-GodotInvocationSucceeded')
            $successIndex = $source.LastIndexOf('_VERIFICATION_OK')

            $assertIndex | Should BeGreaterThan -1
            $successIndex | Should BeGreaterThan $assertIndex
        }
    }
}

Describe 'Native canonical rotation yaw ready gate' {
    It 'requires the expanded native curve self-test marker before publishing readiness' {
        $buildScript = [System.IO.File]::ReadAllText((Join-Path $script:RepositoryRoot 'scripts\build-als-exporter.ps1'))
        $selfTestMarker = 'GODOT_ALS_CURVE_EXPORT_SELF_TEST_OK cases=8'
        $selfTestIndex = $buildScript.IndexOf($selfTestMarker, [StringComparison]::Ordinal)
        $readyMarkerIndex = $buildScript.IndexOf('GODOT_ALS_EXPORTER_READY', [StringComparison]::Ordinal)

        $selfTestIndex | Should BeGreaterThan -1
        $readyMarkerIndex | Should BeGreaterThan $selfTestIndex
        $buildScript | Should Match 'Native curve export self-test marker was not found'
    }
}

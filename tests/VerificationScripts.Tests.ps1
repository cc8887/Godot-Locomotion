$script:RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:P2bVerifierPath = Join-Path $script:RepositoryRoot 'scripts\verify-p2b.ps1'
$script:P2bFunctionsPath = Join-Path $script:RepositoryRoot 'scripts\p2b-verification-functions.ps1'
$script:P1VerifierPath = Join-Path $script:RepositoryRoot 'scripts\verify-p1.ps1'
$script:P0VerifierPath = Join-Path $script:RepositoryRoot 'scripts\verify-p0.ps1'
$script:GodotOutputFunctionsPath = Join-Path $script:RepositoryRoot 'scripts\godot-output-functions.ps1'
$script:AssetLockFunctionsPath = Join-Path $script:RepositoryRoot 'scripts\asset-lock-functions.ps1'
$script:P2aVerifierPath = Join-Path $script:RepositoryRoot 'scripts\verify-p2a.ps1'
$script:TrackedAssetLockPath = Join-Path $script:RepositoryRoot 'reference\als-v4-export.lock.json'

if (Test-Path -LiteralPath $script:P2bFunctionsPath) {
    . $script:P2bFunctionsPath
}
if (Test-Path -LiteralPath $script:GodotOutputFunctionsPath) {
    . $script:GodotOutputFunctionsPath
}
if (Test-Path -LiteralPath $script:AssetLockFunctionsPath) {
    . $script:AssetLockFunctionsPath
}

Describe 'P2B formal manifest lock' {
    It 'reads the tracked asset lock and validates it before parsing manifest JSON' {
        $source = [System.IO.File]::ReadAllText($script:P2bVerifierPath)
        $lockIndex = $source.IndexOf('Assert-AlsExportLock')
        $jsonIndex = $source.IndexOf('ConvertFrom-Json')

        $source | Should Match 'reference\\als-v4-export\.lock\.json'
        $source | Should Not Match '369AF84ABA028AFBDF6EEA7F1A4F1161DFD4B5E9BEA736E9460BFE368CE14327'
        $lockIndex | Should BeGreaterThan -1
        $jsonIndex | Should BeGreaterThan $lockIndex
    }

    It 'rejects a tampered lock and manifest without changing either file' {
        (Get-Command Assert-AlsExportLock -ErrorAction SilentlyContinue) |
            Should Not BeNullOrEmpty
        if (-not (Get-Command Assert-AlsExportLock -ErrorAction SilentlyContinue)) {
            return
        }
        $assetRoot = Join-Path $script:RepositoryRoot 'assets\generated\als_v4'
        $manifest = Join-Path $assetRoot 'als_manifest.json'
        $tamperedLock = Join-Path $TestDrive 'tampered-lock.json'
        $lock = Get-Content -Raw $script:TrackedAssetLockPath | ConvertFrom-Json
        $lock.manifestSha256 = 'ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff'
        [IO.File]::WriteAllText($tamperedLock, ($lock | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
        $lockHash = (Get-FileHash $tamperedLock -Algorithm SHA256).Hash
        $rejected = $false
        try { Assert-AlsExportLock -ManifestPath $manifest -AssetRoot $assetRoot -LockPath $tamperedLock | Out-Null }
        catch { $rejected = $true }
        $rejected | Should Be $true
        (Get-FileHash $tamperedLock -Algorithm SHA256).Hash | Should Be $lockHash

        $tamperedManifest = Join-Path $TestDrive 'tampered-manifest.json'
        [IO.File]::WriteAllBytes($tamperedManifest, [IO.File]::ReadAllBytes($manifest))
        [IO.File]::AppendAllText($tamperedManifest, ' ')
        $manifestHash = (Get-FileHash $tamperedManifest -Algorithm SHA256).Hash
        $rejected = $false
        try { Assert-AlsExportLock -ManifestPath $tamperedManifest -AssetRoot $assetRoot -LockPath $script:TrackedAssetLockPath | Out-Null }
        catch { $rejected = $true }
        $rejected | Should Be $true
        (Get-FileHash $tamperedManifest -Algorithm SHA256).Hash | Should Be $manifestHash
    }

    It 'publishes exact lock fields durably and preserves old bytes on replacement failure' {
        (Get-Command Publish-AlsExportLock -ErrorAction SilentlyContinue) | Should Not BeNullOrEmpty
        if (-not (Get-Command Publish-AlsExportLock -ErrorAction SilentlyContinue)) { return }
        $manifest = Join-Path $script:RepositoryRoot 'assets\generated\als_v4\als_manifest.json'
        $output = Join-Path $TestDrive 'asset.lock.json'
        Publish-AlsExportLock -ManifestPath $manifest -LockPath $output
        $value = Get-Content -Raw $output | ConvertFrom-Json
        (@($value.PSObject.Properties.Name) -join ',') | Should Be 'schemaVersion,manifestSha256,assetCount,fileCount,animationCount,exporterVersion,sourceProjectId'
        $value.schemaVersion | Should Be 1
        $value.assetCount | Should Be 267
        $value.fileCount | Should Be 141
        $value.animationCount | Should Be 126
        $value.manifestSha256 | Should Match '^[0-9a-f]{64}$'

        $beforeHash = (Get-FileHash $output -Algorithm SHA256).Hash
        $handle = [IO.FileStream]::new($output, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
        $rejected = $false
        try { Publish-AlsExportLock -ManifestPath $manifest -LockPath $output }
        catch { $rejected = $true }
        finally { $handle.Dispose() }
        $rejected | Should Be $true
        (Get-FileHash $output -Algorithm SHA256).Hash | Should Be $beforeHash
        @(Get-ChildItem $TestDrive -Filter '.asset.lock.json.*.tmp').Count | Should Be 0
    }

    It 'updates the asset lock only after byte-identical double export comparison' {
        $source = [IO.File]::ReadAllText($script:P2aVerifierPath)
        $compare = $source.LastIndexOf('compare-p2a-exports.ps1')
        $publish = $source.LastIndexOf('Publish-AlsExportLock')
        $source | Should Match '\[switch\]\$UpdateAssetLock'
        $compare | Should BeGreaterThan -1
        $publish | Should BeGreaterThan $compare
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
        $selfTestMarker = 'GODOT_ALS_CURVE_EXPORT_SELF_TEST_OK cases=16'
        $selfTestIndex = $buildScript.IndexOf($selfTestMarker, [StringComparison]::Ordinal)
        $readyMarkerIndex = $buildScript.IndexOf('GODOT_ALS_EXPORTER_READY', [StringComparison]::Ordinal)

        $selfTestIndex | Should BeGreaterThan -1
        $readyMarkerIndex | Should BeGreaterThan $selfTestIndex
        $buildScript | Should Match 'Native curve export self-test marker was not found'
    }
}

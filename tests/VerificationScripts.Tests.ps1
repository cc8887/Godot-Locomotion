$script:RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:P2bVerifierPath = Join-Path $script:RepositoryRoot 'scripts\verify-p2b.ps1'
$script:P2bFunctionsPath = Join-Path $script:RepositoryRoot 'scripts\p2b-verification-functions.ps1'
$script:P1VerifierPath = Join-Path $script:RepositoryRoot 'scripts\verify-p1.ps1'
$script:P0VerifierPath = Join-Path $script:RepositoryRoot 'scripts\verify-p0.ps1'
$script:GodotOutputFunctionsPath = Join-Path $script:RepositoryRoot 'scripts\godot-output-functions.ps1'
$script:AssetLockFunctionsPath = Join-Path $script:RepositoryRoot 'scripts\asset-lock-functions.ps1'
$script:P2aVerifierPath = Join-Path $script:RepositoryRoot 'scripts\verify-p2a.ps1'
$script:TrackedAssetLockPath = Join-Path $script:RepositoryRoot 'reference\als-v4-export.lock.json'
$script:P4VerificationFunctionsPath = Join-Path $script:RepositoryRoot 'scripts\p4-verification-functions.ps1'
$script:P4MatrixVerifierPath = Join-Path $script:RepositoryRoot 'scripts\verify-p4-matrix.ps1'
$script:P4DemoVerifierPath = Join-Path $script:RepositoryRoot 'scripts\verify-p4-demo.ps1'
$script:P4DemoControllerPath = Join-Path $script:RepositoryRoot 'src\Als.Godot\Locomotion\P4LocomotionDemo.cs'

if (Test-Path -LiteralPath $script:P2bFunctionsPath) {
    . $script:P2bFunctionsPath
}
if (Test-Path -LiteralPath $script:GodotOutputFunctionsPath) {
    . $script:GodotOutputFunctionsPath
}
if (Test-Path -LiteralPath $script:AssetLockFunctionsPath) {
    . $script:AssetLockFunctionsPath
}
if (Test-Path -LiteralPath $script:P4VerificationFunctionsPath) {
    . $script:P4VerificationFunctionsPath
}

function Write-SynchronizedManifestAndLock([object]$Manifest, [string]$ManifestPath, [string]$LockPath) {
    [IO.File]::WriteAllText($ManifestPath, ($Manifest | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
    $lock = Get-Content -Raw $script:TrackedAssetLockPath | ConvertFrom-Json
    $lock.manifestSha256 = (Get-FileHash -LiteralPath $ManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($LockPath, ($lock | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
}

function Get-ExportLockValidationError([string]$ManifestPath, [string]$AssetRoot, [string]$LockPath) {
    try {
        Assert-AlsExportLock -ManifestPath $ManifestPath -AssetRoot $AssetRoot -LockPath $LockPath | Out-Null
        return $null
    }
    catch { return $_.Exception.Message }
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

    It 'rejects a synchronized manifest traversal before reading outside the asset root' {
        $assetRoot = Join-Path $script:RepositoryRoot 'assets\generated\als_v4'
        $manifest = Get-Content -Raw (Join-Path $assetRoot 'als_manifest.json') | ConvertFrom-Json
        $outside = Get-Item -LiteralPath (Join-Path $script:RepositoryRoot 'assets\config\p4_pose_profile.json')
        $manifest.files[0].relativePath = '..\..\config\p4_pose_profile.json'
        $manifest.files[0].size = $outside.Length
        $manifest.files[0].sha256 = (Get-FileHash -LiteralPath $outside.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $manifestPath = Join-Path $TestDrive 'traversal-manifest.json'
        $lockPath = Join-Path $TestDrive 'traversal-lock.json'
        Write-SynchronizedManifestAndLock $manifest $manifestPath $lockPath

        $errorMessage = Get-ExportLockValidationError $manifestPath $assetRoot $lockPath

        $errorMessage | Should Match 'files\[0\]\.relativePath'
        $errorMessage | Should Match 'outside|escape|segment'
    }

    It 'rejects an absolute manifest file path with an indexed diagnostic' {
        $assetRoot = Join-Path $script:RepositoryRoot 'assets\generated\als_v4'
        $manifest = Get-Content -Raw (Join-Path $assetRoot 'als_manifest.json') | ConvertFrom-Json
        $file = Get-Item -LiteralPath (Join-Path $assetRoot $manifest.files[0].relativePath)
        $manifest.files[0].relativePath = $file.FullName
        $manifestPath = Join-Path $TestDrive 'absolute-manifest.json'
        $lockPath = Join-Path $TestDrive 'absolute-lock.json'
        Write-SynchronizedManifestAndLock $manifest $manifestPath $lockPath

        $errorMessage = Get-ExportLockValidationError $manifestPath $assetRoot $lockPath

        $errorMessage | Should Match 'files\[0\]\.relativePath'
        $errorMessage | Should Match 'relative|rooted|absolute'
    }

    It 'rejects a dot path segment even when it resolves to a locked file' {
        $assetRoot = Join-Path $script:RepositoryRoot 'assets\generated\als_v4'
        $manifest = Get-Content -Raw (Join-Path $assetRoot 'als_manifest.json') | ConvertFrom-Json
        $manifest.files[0].relativePath = '.\' + ([string]$manifest.files[0].relativePath).Replace('/', '\')
        $manifestPath = Join-Path $TestDrive 'dot-manifest.json'
        $lockPath = Join-Path $TestDrive 'dot-lock.json'
        Write-SynchronizedManifestAndLock $manifest $manifestPath $lockPath

        $errorMessage = Get-ExportLockValidationError $manifestPath $assetRoot $lockPath

        $errorMessage | Should Match 'files\[0\]\.relativePath'
        $errorMessage | Should Match 'segment'
    }

    It 'rejects a dot-dot segment even when normalization remains inside the asset root' {
        $assetRoot = Join-Path $script:RepositoryRoot 'assets\generated\als_v4'
        $manifest = Get-Content -Raw (Join-Path $assetRoot 'als_manifest.json') | ConvertFrom-Json
        $fileName = [IO.Path]::GetFileName([string]$manifest.files[0].relativePath)
        $manifest.files[0].relativePath = "animations\ignored\..\$fileName"
        $manifestPath = Join-Path $TestDrive 'dot-dot-manifest.json'
        $lockPath = Join-Path $TestDrive 'dot-dot-lock.json'
        Write-SynchronizedManifestAndLock $manifest $manifestPath $lockPath

        $errorMessage = Get-ExportLockValidationError $manifestPath $assetRoot $lockPath

        $errorMessage | Should Match 'files\[0\]\.relativePath'
        $errorMessage | Should Match 'segment'
    }

    It 'rejects duplicate canonical relative paths across separator and case variants' {
        $assetRoot = Join-Path $script:RepositoryRoot 'assets\generated\als_v4'
        $manifest = Get-Content -Raw (Join-Path $assetRoot 'als_manifest.json') | ConvertFrom-Json
        $manifest.files[1].relativePath = ([string]$manifest.files[0].relativePath).ToUpperInvariant().Replace('/', '\')
        $manifest.files[1].size = $manifest.files[0].size
        $manifest.files[1].sha256 = $manifest.files[0].sha256
        $manifestPath = Join-Path $TestDrive 'duplicate-path-manifest.json'
        $lockPath = Join-Path $TestDrive 'duplicate-path-lock.json'
        Write-SynchronizedManifestAndLock $manifest $manifestPath $lockPath

        $errorMessage = Get-ExportLockValidationError $manifestPath $assetRoot $lockPath

        $errorMessage | Should Match 'files\[1\]\.relativePath'
        $errorMessage | Should Match 'duplicate'
    }

    It 'rejects a manifest target that resolves to a directory' {
        $assetRoot = Join-Path $script:RepositoryRoot 'assets\generated\als_v4'
        $manifest = Get-Content -Raw (Join-Path $assetRoot 'als_manifest.json') | ConvertFrom-Json
        $manifest.files[0].relativePath = 'animations'
        $manifestPath = Join-Path $TestDrive 'directory-manifest.json'
        $lockPath = Join-Path $TestDrive 'directory-lock.json'
        Write-SynchronizedManifestAndLock $manifest $manifestPath $lockPath

        $errorMessage = Get-ExportLockValidationError $manifestPath $assetRoot $lockPath

        $errorMessage | Should Match 'files\[0\]\.relativePath'
        $errorMessage | Should Match 'file'
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

    It 'routes verify-p2a through the one tested joint publication after existing export gates' {
        $source = [IO.File]::ReadAllText($script:P2aVerifierPath)
        $cleanupBoundary = $source.IndexOf('Invoke-AlsP2aStagingWorkflow')
        $dryRun = $source.IndexOf('$dryRunOutput = & $editorCommand')
        $fullExportGate = $source.LastIndexOf('GODOT_ALS_P2A_FULL_EXPORT_OK')
        $determinism = $source.IndexOf('$determinismOutput = & $editorCommand')
        $orchestration = $source.LastIndexOf('Invoke-AlsP2aJointPublication')
        $comparisonArgument = $source.LastIndexOf('-ComparisonScriptPath $compareScript')
        $source | Should Match '\[switch\]\$UpdateAssetLock'
        $cleanupBoundary | Should BeGreaterThan -1
        $dryRun | Should BeGreaterThan $cleanupBoundary
        $orchestration | Should BeGreaterThan $fullExportGate
        $determinism | Should BeGreaterThan $fullExportGate
        $orchestration | Should BeGreaterThan $determinism
        $comparisonArgument | Should BeGreaterThan $orchestration
        $source | Should Match 'Assert-AlsP2aCanonicalPublicationRoot'
        $source | Should Match '(?s)Invoke-AlsP2aStagingWorkflow.+?-Action\s*\{.+?\$determinismOutput.+?\r?\n\}\r?\n\$compareScript.+?\r?\nInvoke-AlsP2aJointPublication'
        $source | Should Not Match 'Invoke-AlsP2aCompareAndPublish'
        $source | Should Not Match 'Publish-AlsExportLock -ManifestPath \$formalManifestPath -LockPath \$assetLockPath'
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

Describe 'P4 four-cell matrix output contract' {
    BeforeAll {
        $script:ValidP4MatrixLine = 'P4_MATRIX_OK mode=single characters=10 warmup=120 frames=600 result=0123456789ABCDEF pose=1123456789ABCDEF full_pose=2123456789ABCDEF root=3123456789ABCDEF aim=4123456789ABCDEF turn_rotate=5123456789ABCDEF feet=6123456789ABCDEF missing=0 stale=0 generation=0 lag=0 thread=0 model=0 curve=0 controller=0 modifier=0 skeleton=0 exchange=0 commit=0 foot_gather=408 advances=6000 modifiers=6000 commits=6000 per_character_advances=600 per_character_modifiers=600 per_character_commits=600 replacement=1 old_generation_rejected=1 lanes=10 gather_commit_p95_us=1400 worker_p95_us=2400 total_p99_us=3900'
    }

    It 'requires the strict parser and accepts exactly one complete finite result line' {
        (Get-Command ConvertFrom-P4MatrixOutput -ErrorAction SilentlyContinue) |
            Should Not BeNullOrEmpty
        if (-not (Get-Command ConvertFrom-P4MatrixOutput -ErrorAction SilentlyContinue)) {
            return
        }

        $result = ConvertFrom-P4MatrixOutput -OutputLines @('Godot Engine', $script:ValidP4MatrixLine) `
            -ExpectedMode single -ExpectedCharacterCount 10

        $result.Mode | Should Be 'single'
        $result.Characters | Should Be 10
        $result.GatherCommitP95Microseconds | Should Be 1400
        $result.WorkerP95Microseconds | Should Be 2400
        $result.TotalP99Microseconds | Should Be 3900
        $result.FootGatherAllocations | Should Be 408
    }

    It 'accepts over-budget timing for the 10-character single reference cell' {
        if (-not (Get-Command ConvertFrom-P4MatrixOutput -ErrorAction SilentlyContinue)) {
            return
        }

        $timedOutSingleLine = $script:ValidP4MatrixLine.Replace(
            ' gather_commit_p95_us=1400', ' gather_commit_p95_us=1501').Replace(
            ' worker_p95_us=2400', ' worker_p95_us=2501').Replace(
            ' total_p99_us=3900', ' total_p99_us=4001')

        $result = ConvertFrom-P4MatrixOutput -OutputLines @($timedOutSingleLine) `
            -ExpectedMode single -ExpectedCharacterCount 10

        $result.GatherCommitP95Microseconds | Should Be 1501
        $result.WorkerP95Microseconds | Should Be 2501
        $result.TotalP99Microseconds | Should Be 4001
    }

    It 'rejects over-budget timing for the 10-character parallel cell' {
        if (-not (Get-Command ConvertFrom-P4MatrixOutput -ErrorAction SilentlyContinue)) {
            return
        }

        $parallelLine = $script:ValidP4MatrixLine.Replace('mode=single', 'mode=parallel')
        foreach ($timedOut in @(
            $parallelLine.Replace(' gather_commit_p95_us=1400', ' gather_commit_p95_us=1501'),
            $parallelLine.Replace(' worker_p95_us=2400', ' worker_p95_us=2501'),
            $parallelLine.Replace(' total_p99_us=3900', ' total_p99_us=4001'))) {
            $rejected = $false
            try {
                ConvertFrom-P4MatrixOutput -OutputLines @($timedOut) `
                    -ExpectedMode parallel -ExpectedCharacterCount 10 | Out-Null
            }
            catch { $rejected = $true }
            $rejected | Should Be $true
        }
    }

    It 'requires non-negative finite timing in every matrix cell even when performance is not gated' {
        if (-not (Get-Command ConvertFrom-P4MatrixOutput -ErrorAction SilentlyContinue)) {
            return
        }

        foreach ($mode in @('single', 'parallel')) {
            foreach ($characterCount in @(1, 10)) {
                $line = $script:ValidP4MatrixLine
                if ($mode -eq 'parallel') {
                    $line = $line.Replace('mode=single', 'mode=parallel')
                }
                if ($characterCount -eq 1) {
                    $line = $line.Replace('characters=10', 'characters=1').Replace(
                        'advances=6000', 'advances=600').Replace(
                        'modifiers=6000', 'modifiers=600').Replace(
                        'commits=6000', 'commits=600').Replace(
                        'lanes=10', 'lanes=1')
                }

                ConvertFrom-P4MatrixOutput -OutputLines @($line) `
                    -ExpectedMode $mode -ExpectedCharacterCount $characterCount | Out-Null

                foreach ($field in @('gather_commit_p95_us', 'worker_p95_us', 'total_p99_us')) {
                    $negative = $line -replace "${field}=[0-9]+", "${field}=-1"
                    $rejected = $false
                    try {
                        ConvertFrom-P4MatrixOutput -OutputLines @($negative) `
                            -ExpectedMode $mode -ExpectedCharacterCount $characterCount | Out-Null
                    }
                    catch { $rejected = $true }
                    $rejected | Should Be $true
                }
            }
        }
    }

    It 'rejects duplicate, missing, malformed, non-finite and nonzero fields' {
        if (-not (Get-Command ConvertFrom-P4MatrixOutput -ErrorAction SilentlyContinue)) {
            return
        }

        $cases = @(
            @($script:ValidP4MatrixLine, $script:ValidP4MatrixLine),
            @($script:ValidP4MatrixLine.Replace(' result=', ' result=0123456789ABCDEF result=')),
            @($script:ValidP4MatrixLine + ' unknown=0'),
            @($script:ValidP4MatrixLine.Replace(' feet=6123456789ABCDEF', '')),
            @($script:ValidP4MatrixLine.Replace(' result=0123456789ABCDEF', ' result=not-a-digest')),
            @($script:ValidP4MatrixLine.Replace(' worker_p95_us=2400', ' worker_p95_us=NaN')),
            @($script:ValidP4MatrixLine.Replace(' stale=0', ' stale=1')),
            @($script:ValidP4MatrixLine.Replace(' modifier=0', ' modifier=8')),
            @($script:ValidP4MatrixLine.Replace(' advances=6000', ' advances=5999'))
        )

        foreach ($candidateLines in $cases) {
            $rejected = $false
            try {
                ConvertFrom-P4MatrixOutput -OutputLines $candidateLines `
                    -ExpectedMode single -ExpectedCharacterCount 10 | Out-Null
            }
            catch { $rejected = $true }
            $rejected | Should Be $true
        }
    }

    It 'requires transparent Foot Gather reporting without putting it in the zero-byte gate' {
        if (-not (Get-Command ConvertFrom-P4MatrixOutput -ErrorAction SilentlyContinue)) {
            return
        }

        $result = ConvertFrom-P4MatrixOutput -OutputLines @($script:ValidP4MatrixLine) `
            -ExpectedMode single -ExpectedCharacterCount 10
        $result.FootGatherAllocations | Should Be 408
        (ConvertFrom-P4MatrixOutput `
            -OutputLines @($script:ValidP4MatrixLine.Replace(' foot_gather=408', ' foot_gather=0')) `
            -ExpectedMode single -ExpectedCharacterCount 10).FootGatherAllocations | Should Be 0
        foreach ($invalid in @('-1', 'NaN')) {
            $rejected = $false
            try {
                ConvertFrom-P4MatrixOutput `
                    -OutputLines @($script:ValidP4MatrixLine.Replace(' foot_gather=408', " foot_gather=$invalid")) `
                    -ExpectedMode single -ExpectedCharacterCount 10 | Out-Null
            }
            catch { $rejected = $true }
            $rejected | Should Be $true
        }
    }

    It 'requires exact seven-digest equality for each single and parallel pair' {
        (Get-Command Assert-P4MatrixPair -ErrorAction SilentlyContinue) |
            Should Not BeNullOrEmpty
        if (-not (Get-Command Assert-P4MatrixPair -ErrorAction SilentlyContinue) -or
            -not (Get-Command ConvertFrom-P4MatrixOutput -ErrorAction SilentlyContinue)) {
            return
        }

        $single = ConvertFrom-P4MatrixOutput -OutputLines @($script:ValidP4MatrixLine) `
            -ExpectedMode single -ExpectedCharacterCount 10
        $parallelLine = $script:ValidP4MatrixLine.Replace('mode=single', 'mode=parallel')
        $parallel = ConvertFrom-P4MatrixOutput -OutputLines @($parallelLine) `
            -ExpectedMode parallel -ExpectedCharacterCount 10
        { Assert-P4MatrixPair -Single $single -Parallel $parallel -CharacterCount 10 } |
            Should Not Throw

        foreach ($field in @('result', 'pose', 'full_pose', 'root', 'aim', 'turn_rotate', 'feet')) {
            $mismatch = $parallelLine -replace "${field}=[0-9A-F]{16}", "${field}=FEDCBA9876543210"
            $rejected = $false
            try {
                $candidate = ConvertFrom-P4MatrixOutput -OutputLines @($mismatch) `
                    -ExpectedMode parallel -ExpectedCharacterCount 10
                Assert-P4MatrixPair -Single $single -Parallel $candidate -CharacterCount 10
            }
            catch { $rejected = $true }
            $rejected | Should Be $true
        }
    }

    It 'requires the exact four command-line cells and whole-frame timing instrumentation in source' {
        $harnessPath = Join-Path $script:RepositoryRoot 'src\Als.Godot\Locomotion\P4AnimationHarness.cs'
        $contextPath = Join-Path $script:RepositoryRoot 'src\Als.Godot\Locomotion\AlsP4HarnessContext.cs'
        $scenePath = Join-Path $script:RepositoryRoot 'scenes\tests\p4_animation_harness.tscn'

        Test-Path -LiteralPath $harnessPath -PathType Leaf | Should Be $true
        Test-Path -LiteralPath $contextPath -PathType Leaf | Should Be $true
        Test-Path -LiteralPath $scenePath -PathType Leaf | Should Be $true
        if (-not (Test-Path -LiteralPath $harnessPath -PathType Leaf) -or
            -not (Test-Path -LiteralPath $contextPath -PathType Leaf)) {
            return
        }

        $harness = [IO.File]::ReadAllText($harnessPath)
        $context = [IO.File]::ReadAllText($contextPath)
        $harness | Should Match '--mode='
        $harness | Should Match '--characters='
        $harness | Should Match '--warmup='
        $harness | Should Match '--frames='
        $harness | Should Match 'ProcessThreadGroupOrder\s*=\s*3'
        $harness | Should Match "_mode == AlsHarnessMode.Parallel && _characterCount == 10"
        $context | Should Match 'RecordGatherStart'
        $context | Should Match 'RecordWorker(Start|Window)'
        $context | Should Match 'RecordCommitEnd'
        $context | Should Match 'GatherCommitP95'
        $context | Should Match 'WorkerP95'
        $context | Should Match 'TotalP99'
    }

    It 'canonicalizes process-local platform ids before producing cross-process digests' {
        $harnessPath = Join-Path $script:RepositoryRoot 'src\Als.Godot\Locomotion\P4AnimationHarness.cs'
        $harness = [IO.File]::ReadAllText($harnessPath)

        $harness | Should Match 'NormalizeResultForDigest'
        $harness | Should Match 'CanonicalizePlatformIdForDigest'
        $harness | Should Match 'TryCanonicalizePlatformIdForDigest'
        $harness | Should Match 'ResolveFootColliderIdForDigest'
        $harness | Should Match 'ValidateDigestPlatformCanonicalization'
        $harness | Should Match 'Unknown P4 matrix platform ID'
        $harness | Should Match 'StableTranslatingPlatformId'
        $harness | Should Match 'StableRotatingPlatformId'
        $harness | Should Match '_translatingColliderIds'
        $harness | Should Match '_rotatingColliderIds'
        $harness | Should Match 'accepted mismatched platform provenance'
        $harness | Should Match '(?s)gatherHit\.Valid\s*==\s*1.*gatherHit\.Walkable\s*==\s*1'
        $harness | Should Match '(?s)digestResult\s*=\s*NormalizeResultForDigest\(\s*diagnostics\.Result,\s*diagnostics\.FootPose,\s*index\).*AlsResultDigest\.Append\(ref _resultDigest, digestResult\).*AppendFeet\(ref _feetDigest, digestResult'
        $harness | Should Match '(?s)platformId\s*==\s*_translatingPlatformIds\[characterIndex\].*colliderId\s*==\s*_translatingColliderIds\[characterIndex\]'
        $harness | Should Match '(?s)platformId\s*==\s*_rotatingPlatformIds\[characterIndex\].*colliderId\s*==\s*_rotatingColliderIds\[characterIndex\]'
    }
}

Describe 'P4 controlled matrix runner boundary' {
    BeforeEach {
        $script:OriginalPath = $env:PATH
        $script:HadDotnetTiering = Test-Path Env:DOTNET_TieredCompilation
        $script:HadComPlusTiering = Test-Path Env:COMPlus_TieredCompilation
        $script:OriginalDotnetTiering = $env:DOTNET_TieredCompilation
        $script:OriginalComPlusTiering = $env:COMPlus_TieredCompilation
        $script:FakeToolRoot = Join-Path $TestDrive ([Guid]::NewGuid().ToString('N'))
        [void][IO.Directory]::CreateDirectory($script:FakeToolRoot)
        $script:BuildLog = Join-Path $script:FakeToolRoot 'build.log'
        $script:ChildLog = Join-Path $script:FakeToolRoot 'children.log'
        $script:FakeGodot = Join-Path $script:FakeToolRoot 'fake-godot.cmd'
        $fakeGodotScript = Join-Path $script:FakeToolRoot 'fake-godot.ps1'

        [IO.File]::WriteAllText(
            (Join-Path $script:FakeToolRoot 'dotnet.cmd'),
@'
@echo off
echo %DOTNET_TieredCompilation%^|%COMPlus_TieredCompilation%^|%*>>"%P4_FAKE_BUILD_LOG%"
if "%P4_FAKE_BUILD_FAIL%"=="1" exit /b 23
exit /b 0
'@.Replace("`n", "`r`n"),
            [Text.Encoding]::ASCII)
        [IO.File]::WriteAllText(
            $script:FakeGodot,
(@'
@echo off
set "P4_FAKE_ARGS=%*"
pwsh -NoProfile -File "{0}"
exit /b %ERRORLEVEL%
'@ -f $fakeGodotScript).Replace("`n", "`r`n"),
            [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText(
            $fakeGodotScript,
@'
$arguments = @($env:P4_FAKE_ARGS -split ' ')
$mode = ($arguments | Where-Object { $_ -like '--mode=*' }) -replace '^--mode=', ''
$characters = ($arguments | Where-Object { $_ -like '--characters=*' }) -replace '^--characters=', ''
$cell = "$characters/$mode"
[IO.File]::AppendAllText(
    $env:P4_FAKE_CHILD_LOG,
    "$cell|$env:DOTNET_TieredCompilation|$env:COMPlus_TieredCompilation|$env:P4_FAKE_ARGS`r`n")
if ($env:P4_FAKE_CHILD_FAIL_CELL -eq $cell) { exit 17 }
$count = if ($characters -eq '10') { '6000' } else { '600' }
$lanes = $characters
$prefix = if ($characters -eq '10') { 'A' } else { '1' }
$result = $prefix * 16
if ($env:P4_FAKE_PAIR_MISMATCH -eq $characters -and $mode -eq 'parallel') {
    $result = 'FEDCBA9876543210'
}
Write-Output "P4_MATRIX_OK mode=$mode characters=$characters warmup=120 frames=600 result=$result pose=$($prefix * 15)2 full_pose=$($prefix * 15)3 root=$($prefix * 15)4 aim=$($prefix * 15)5 turn_rotate=$($prefix * 15)6 feet=$($prefix * 15)7 missing=0 stale=0 generation=0 lag=0 thread=0 model=0 curve=0 controller=0 modifier=0 skeleton=0 exchange=0 commit=0 foot_gather=0 advances=$count modifiers=$count commits=$count per_character_advances=600 per_character_modifiers=600 per_character_commits=600 replacement=1 old_generation_rejected=1 lanes=$lanes gather_commit_p95_us=100 worker_p95_us=200 total_p99_us=300"
'@,
            [Text.UTF8Encoding]::new($false))

        $env:PATH = "$($script:FakeToolRoot);$($script:OriginalPath)"
        $env:P4_FAKE_BUILD_LOG = $script:BuildLog
        $env:P4_FAKE_CHILD_LOG = $script:ChildLog
        Remove-Item Env:P4_FAKE_BUILD_FAIL -ErrorAction SilentlyContinue
        Remove-Item Env:P4_FAKE_CHILD_FAIL_CELL -ErrorAction SilentlyContinue
        Remove-Item Env:P4_FAKE_PAIR_MISMATCH -ErrorAction SilentlyContinue
    }

    AfterEach {
        $env:PATH = $script:OriginalPath
        foreach ($name in @(
            'P4_FAKE_BUILD_LOG', 'P4_FAKE_CHILD_LOG', 'P4_FAKE_BUILD_FAIL',
            'P4_FAKE_CHILD_FAIL_CELL', 'P4_FAKE_PAIR_MISMATCH')) {
            Remove-Item "Env:$name" -ErrorAction SilentlyContinue
        }
        if ($script:HadDotnetTiering) {
            $env:DOTNET_TieredCompilation = $script:OriginalDotnetTiering
        }
        else { Remove-Item Env:DOTNET_TieredCompilation -ErrorAction SilentlyContinue }
        if ($script:HadComPlusTiering) {
            $env:COMPlus_TieredCompilation = $script:OriginalComPlusTiering
        }
        else { Remove-Item Env:COMPlus_TieredCompilation -ErrorAction SilentlyContinue }
    }

    It 'builds optimized non-incremental Debug and runs the exact four cells under disabled tiering' {
        $env:DOTNET_TieredCompilation = 'caller-dotnet'
        $env:COMPlus_TieredCompilation = 'caller-complus'

        $output = @(& $script:P4MatrixVerifierPath -GodotExecutable $script:FakeGodot)

        $env:DOTNET_TieredCompilation | Should Be 'caller-dotnet'
        $env:COMPlus_TieredCompilation | Should Be 'caller-complus'
        (Get-Content -Raw $script:BuildLog).Trim() | Should Be (
            "caller-dotnet|caller-complus|build $($script:RepositoryRoot)\GodotALS.csproj -c Debug -p:Optimize=true --no-incremental")
        $cells = @(Get-Content $script:ChildLog)
        @($cells | ForEach-Object { ($_ -split '\|')[0] }) -join ',' |
            Should Be '1/single,1/parallel,10/single,10/parallel'
        @($cells | Where-Object { $_ -notmatch '^[^|]+\|0\|0\|' }).Count | Should Be 0
        foreach ($line in $cells) {
            $line | Should Match ('--headless --path ' + [regex]::Escape($script:RepositoryRoot) +
                ' res://scenes/tests/p4_animation_harness\.tscn -- --mode=(single|parallel) ' +
                '--characters=(1|10) --warmup=120 --frames=600$')
        }
        @($output | Where-Object { $_ -match '^P4_MATRIX_OK ' }).Count | Should Be 4
        @($output | Where-Object { $_ -eq 'P4_MATRIX_VERIFICATION_OK cells=4 pairs=2' }).Count |
            Should Be 1
    }

    It 'restores absent tiering variables after successful execution' {
        Remove-Item Env:DOTNET_TieredCompilation -ErrorAction SilentlyContinue
        Remove-Item Env:COMPlus_TieredCompilation -ErrorAction SilentlyContinue

        & $script:P4MatrixVerifierPath -GodotExecutable $script:FakeGodot | Out-Null

        (Test-Path Env:DOTNET_TieredCompilation) | Should Be $false
        (Test-Path Env:COMPlus_TieredCompilation) | Should Be $false
    }

    It 'restores caller tiering and returns failure when the build fails' {
        $env:DOTNET_TieredCompilation = 'build-dotnet'
        $env:COMPlus_TieredCompilation = 'build-complus'
        $env:P4_FAKE_BUILD_FAIL = '1'
        $failed = $false
        $failureMessage = ''
        try { & $script:P4MatrixVerifierPath -GodotExecutable $script:FakeGodot | Out-Null }
        catch { $failed = $true; $failureMessage = $_.Exception.Message }

        $failed | Should Be $true
        $failureMessage | Should Match 'optimized build failed with code 23'
        $env:DOTNET_TieredCompilation | Should Be 'build-dotnet'
        $env:COMPlus_TieredCompilation | Should Be 'build-complus'
        (Test-Path $script:BuildLog) | Should Be $true
        (Test-Path $script:ChildLog) | Should Be $false

        & pwsh -NoProfile -File $script:P4MatrixVerifierPath `
            -GodotExecutable $script:FakeGodot *>&1 | Out-Null
        $LASTEXITCODE | Should Not Be 0
    }

    It 'restores caller tiering and returns failure when a child fails' {
        $env:DOTNET_TieredCompilation = 'child-dotnet'
        $env:COMPlus_TieredCompilation = 'child-complus'
        $env:P4_FAKE_CHILD_FAIL_CELL = '10/parallel'
        $failed = $false
        $failureMessage = ''
        try { & $script:P4MatrixVerifierPath -GodotExecutable $script:FakeGodot | Out-Null }
        catch { $failed = $true; $failureMessage = $_.Exception.Message }

        $failed | Should Be $true
        $failureMessage | Should Match 'cell 10/parallel exited with code 17'
        $env:DOTNET_TieredCompilation | Should Be 'child-dotnet'
        $env:COMPlus_TieredCompilation | Should Be 'child-complus'
        @(Get-Content $script:ChildLog).Count | Should Be 4

        & pwsh -NoProfile -File $script:P4MatrixVerifierPath `
            -GodotExecutable $script:FakeGodot *>&1 | Out-Null
        $LASTEXITCODE | Should Not Be 0
    }

    It 'uses the strict parser and rejects a digest mismatch in a completed pair' {
        $env:DOTNET_TieredCompilation = 'pair-dotnet'
        $env:COMPlus_TieredCompilation = 'pair-complus'
        $env:P4_FAKE_PAIR_MISMATCH = '10'
        $failed = $false
        $failureMessage = ''
        try { & $script:P4MatrixVerifierPath -GodotExecutable $script:FakeGodot | Out-Null }
        catch { $failed = $true; $failureMessage = $_.Exception.Message }

        $failed | Should Be $true
        $failureMessage | Should Match 'ResultDigest mismatch for characters=10'
        @(Get-Content $script:ChildLog).Count | Should Be 4
        $env:DOTNET_TieredCompilation | Should Be 'pair-dotnet'
        $env:COMPlus_TieredCompilation | Should Be 'pair-complus'
    }
}

Describe 'P4 production demo output contract' {
    BeforeAll {
        $script:ValidP4DemoMarker = 'P4_DEMO_OK frames=300 rigs=1'
    }

    It 'accepts exactly one complete production demo marker' {
        (Get-Command ConvertFrom-P4DemoOutput -ErrorAction SilentlyContinue) |
            Should Not BeNullOrEmpty
        if (-not (Get-Command ConvertFrom-P4DemoOutput -ErrorAction SilentlyContinue)) {
            return
        }

        $result = ConvertFrom-P4DemoOutput `
            -OutputLines @('Godot Engine', $script:ValidP4DemoMarker) `
            -ExitCode 0

        $result.Frames | Should Be 300
        $result.Rigs | Should Be 1
    }

    It 'rejects missing duplicate malformed wrong-count engine and failure output' {
        if (-not (Get-Command ConvertFrom-P4DemoOutput -ErrorAction SilentlyContinue)) {
            return
        }

        $cases = @(
            @{ Lines = @('Godot Engine'); ExitCode = 0 },
            @{ Lines = @($script:ValidP4DemoMarker, $script:ValidP4DemoMarker); ExitCode = 0 },
            @{ Lines = @('P4_DEMO_OK frames=300'); ExitCode = 0 },
            @{ Lines = @('P4_DEMO_OK frames=299 rigs=1'); ExitCode = 0 },
            @{ Lines = @('P4_DEMO_OK frames=300 rigs=2'); ExitCode = 0 },
            @{ Lines = @($script:ValidP4DemoMarker + ' extra=1'); ExitCode = 0 },
            @{ Lines = @($script:ValidP4DemoMarker, ' prefix P4_DEMO_OK frames=299 rigs=9'); ExitCode = 0 },
            @{ Lines = @($script:ValidP4DemoMarker, "`tP4_DEMO_OK frames=300 rigs=1"); ExitCode = 0 },
            @{ Lines = @($script:ValidP4DemoMarker, 'diagnostic[P4_DEMO_OK frames=300 rigs=1]'); ExitCode = 0 },
            @{ Lines = @('P4_DEMO_FAIL code=runtime failed'); ExitCode = 0 },
            @{ Lines = @('SCRIPT ERROR: failed', $script:ValidP4DemoMarker); ExitCode = 0 },
            @{ Lines = @('ERROR: failed', $script:ValidP4DemoMarker); ExitCode = 0 },
            @{ Lines = @($script:ValidP4DemoMarker); ExitCode = 17 }
        )

        foreach ($candidate in $cases) {
            $rejected = $false
            try {
                ConvertFrom-P4DemoOutput `
                    -OutputLines $candidate.Lines `
                    -ExitCode $candidate.ExitCode | Out-Null
            }
            catch { $rejected = $true }
            $rejected | Should Be $true
        }
    }

    It 'disposes a configured slot even when initialization fails before runtime readiness' {
        $source = [System.IO.File]::ReadAllText($script:P4DemoControllerPath)
        $disposeStart = $source.IndexOf('internal void DisposeRuntime()')
        $exitTreeStart = $source.IndexOf('public override void _ExitTree()', $disposeStart)
        $disposeSource = $source.Substring($disposeStart, $exitTreeStart - $disposeStart)

        $disposeStart | Should BeGreaterThan -1
        $exitTreeStart | Should BeGreaterThan $disposeStart
        $disposeSource | Should Match '_slot is not null && GodotObject\.IsInstanceValid\(_slot\)'
        $disposeSource | Should Not Match '_runtimeConfigured\s*&&'
        $disposeSource | Should Match '_slot\.DisposeRuntime\(\)'
    }
}

Describe 'P3 demo input output contract for the P4 runner' {
    BeforeAll {
        $script:ValidP3DemoInputMarker =
            'GODOT_ALS_P3_DEMO_INPUT_OK actions=11 directions=12 camera_basis=1 pitch=1 aiming=1 cleared=1 hud=1'
    }

    It 'accepts exactly one complete P3 input marker' {
        (Get-Command ConvertFrom-P3DemoInputOutput -ErrorAction SilentlyContinue) |
            Should Not BeNullOrEmpty
        if (-not (Get-Command ConvertFrom-P3DemoInputOutput -ErrorAction SilentlyContinue)) {
            return
        }

        $result = ConvertFrom-P3DemoInputOutput `
            -OutputLines @('Godot Engine', $script:ValidP3DemoInputMarker) `
            -ExitCode 0

        $result.Marker | Should Be $script:ValidP3DemoInputMarker
    }

    It 'rejects missing duplicate malformed embedded error failure and nonzero output' {
        if (-not (Get-Command ConvertFrom-P3DemoInputOutput -ErrorAction SilentlyContinue)) {
            return
        }

        $cases = @(
            @{ Lines = @('Godot Engine'); ExitCode = 0 },
            @{ Lines = @($script:ValidP3DemoInputMarker, $script:ValidP3DemoInputMarker); ExitCode = 0 },
            @{ Lines = @('GODOT_ALS_P3_DEMO_INPUT_OK actions=10 directions=12 camera_basis=1 pitch=1 aiming=1 cleared=1 hud=1'); ExitCode = 0 },
            @{ Lines = @($script:ValidP3DemoInputMarker, 'prefix GODOT_ALS_P3_DEMO_INPUT_OK actions=11'); ExitCode = 0 },
            @{ Lines = @('GODOT_ALS_P3_DEMO_INPUT_FAIL code=input'); ExitCode = 0 },
            @{ Lines = @('SCRIPT ERROR: failed', $script:ValidP3DemoInputMarker); ExitCode = 0 },
            @{ Lines = @('ERROR: failed', $script:ValidP3DemoInputMarker); ExitCode = 0 },
            @{ Lines = @($script:ValidP3DemoInputMarker); ExitCode = 17 }
        )

        foreach ($candidate in $cases) {
            $rejected = $false
            try {
                ConvertFrom-P3DemoInputOutput `
                    -OutputLines $candidate.Lines `
                    -ExitCode $candidate.ExitCode | Out-Null
            }
            catch { $rejected = $true }
            $rejected | Should Be $true
        }
    }
}

Describe 'P4 production demo runner boundary' {
    BeforeEach {
        $script:OriginalPath = $env:PATH
        $script:FakeToolRoot = Join-Path $TestDrive ([Guid]::NewGuid().ToString('N'))
        [void][IO.Directory]::CreateDirectory($script:FakeToolRoot)
        $script:DemoBuildLog = Join-Path $script:FakeToolRoot 'build.log'
        $script:DemoChildLog = Join-Path $script:FakeToolRoot 'child.log'
        $script:DemoDescendantLog = Join-Path $script:FakeToolRoot 'descendant.log'
        $script:FakeDemoGodot = Join-Path $script:FakeToolRoot 'fake-godot.cmd'
        $fakeGodotScript = Join-Path $script:FakeToolRoot 'fake-godot.ps1'

        [IO.File]::WriteAllText(
            (Join-Path $script:FakeToolRoot 'dotnet.cmd'),
@'
@echo off
echo %*>>"%P4_DEMO_FAKE_BUILD_LOG%"
if "%P4_DEMO_FAKE_BUILD_SLEEP%"=="1" ping 127.0.0.1 -n 6 >nul
if "%P4_DEMO_FAKE_BUILD_FAIL%"=="1" exit /b 23
exit /b 0
'@.Replace("`n", "`r`n"),
            [Text.Encoding]::ASCII)
        [IO.File]::WriteAllText(
            $script:FakeDemoGodot,
(@'
@echo off
set "P4_DEMO_FAKE_ARGS=%*"
echo %*>>"%P4_DEMO_FAKE_CHILD_LOG%"
pwsh -NoProfile -File "{0}"
exit /b %ERRORLEVEL%
'@ -f $fakeGodotScript).Replace("`n", "`r`n"),
            [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText(
            $fakeGodotScript,
@'
$isP3Input = $env:P4_DEMO_FAKE_ARGS.Contains('res://scenes/tests/p3_demo_input_smoke.tscn')
$sleep = if ($isP3Input) { $env:P4_DEMO_FAKE_P3_SLEEP } else { $env:P4_DEMO_FAKE_P4_SLEEP }
if ($sleep -eq '1') {
    $descendant = Start-Process pwsh -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoProfile', '-Command', 'Start-Sleep -Seconds 30')
    if (-not [string]::IsNullOrEmpty($env:P4_DEMO_FAKE_DESCENDANT_LOG)) {
        [IO.File]::WriteAllText($env:P4_DEMO_FAKE_DESCENDANT_LOG, "$($descendant.Id)")
    }
    Start-Sleep -Seconds 30
}
if ($isP3Input -and $env:P4_DEMO_FAKE_P3_FAIL -eq '1') { exit 19 }
if (-not $isP3Input -and $env:P4_DEMO_FAKE_CHILD_FAIL -eq '1') { exit 17 }
$configuredOutput = if ($isP3Input) {
    $env:P4_DEMO_FAKE_P3_OUTPUT
}
else {
    $env:P4_DEMO_FAKE_OUTPUT
}
$defaultOutput = if ($isP3Input) {
    'GODOT_ALS_P3_DEMO_INPUT_OK actions=11 directions=12 camera_basis=1 pitch=1 aiming=1 cleared=1 hud=1'
}
else {
    'P4_DEMO_OK frames=300 rigs=1'
}
$lines = if ([string]::IsNullOrEmpty($configuredOutput)) {
    @($defaultOutput)
}
else {
    @($configuredOutput -split ';;')
}
$lines | ForEach-Object { Write-Output $_ }
'@,
            [Text.UTF8Encoding]::new($false))

        $env:PATH = "$($script:FakeToolRoot);$($script:OriginalPath)"
        $env:P4_DEMO_FAKE_BUILD_LOG = $script:DemoBuildLog
        $env:P4_DEMO_FAKE_CHILD_LOG = $script:DemoChildLog
        $env:P4_DEMO_FAKE_DESCENDANT_LOG = $script:DemoDescendantLog
        Remove-Item Env:P4_DEMO_FAKE_BUILD_FAIL -ErrorAction SilentlyContinue
        Remove-Item Env:P4_DEMO_FAKE_BUILD_SLEEP -ErrorAction SilentlyContinue
        Remove-Item Env:P4_DEMO_FAKE_P3_FAIL -ErrorAction SilentlyContinue
        Remove-Item Env:P4_DEMO_FAKE_P3_SLEEP -ErrorAction SilentlyContinue
        Remove-Item Env:P4_DEMO_FAKE_P3_OUTPUT -ErrorAction SilentlyContinue
        Remove-Item Env:P4_DEMO_FAKE_CHILD_FAIL -ErrorAction SilentlyContinue
        Remove-Item Env:P4_DEMO_FAKE_P4_SLEEP -ErrorAction SilentlyContinue
        Remove-Item Env:P4_DEMO_FAKE_OUTPUT -ErrorAction SilentlyContinue
    }

    AfterEach {
        $env:PATH = $script:OriginalPath
        foreach ($name in @(
            'P4_DEMO_FAKE_BUILD_LOG', 'P4_DEMO_FAKE_CHILD_LOG',
            'P4_DEMO_FAKE_DESCENDANT_LOG', 'P4_DEMO_FAKE_BUILD_FAIL',
            'P4_DEMO_FAKE_BUILD_SLEEP', 'P4_DEMO_FAKE_P3_FAIL',
            'P4_DEMO_FAKE_P3_SLEEP', 'P4_DEMO_FAKE_P3_OUTPUT',
            'P4_DEMO_FAKE_CHILD_FAIL', 'P4_DEMO_FAKE_P4_SLEEP',
            'P4_DEMO_FAKE_OUTPUT')) {
            Remove-Item "Env:$name" -ErrorAction SilentlyContinue
        }
    }

    It 'builds then gates P3 input before invoking the actual P4 scene exactly once' {
        Test-Path -LiteralPath $script:P4DemoVerifierPath -PathType Leaf | Should Be $true
        if (-not (Test-Path -LiteralPath $script:P4DemoVerifierPath -PathType Leaf)) {
            return
        }

        $output = @(& $script:P4DemoVerifierPath -GodotExecutable $script:FakeDemoGodot)

        (Get-Content -Raw $script:DemoBuildLog).Trim() | Should Be (
            "build $($script:RepositoryRoot)\GodotALS.csproj -c Debug -p:Optimize=true --no-restore --no-incremental")
        $calls = @(Get-Content $script:DemoChildLog)
        $calls.Count | Should Be 2
        $calls[0] | Should Be (
            "--headless --path $($script:RepositoryRoot) res://scenes/tests/p3_demo_input_smoke.tscn")
        $calls[1] | Should Be (
            "--headless --path $($script:RepositoryRoot) res://scenes/tests/p4_demo_smoke.tscn -- --als-smoke-frames=300")
        $output -join "`n" | Should Be (
            "GODOT_ALS_P3_DEMO_INPUT_OK actions=11 directions=12 camera_basis=1 pitch=1 aiming=1 cleared=1 hud=1`n" +
            "P4_DEMO_OK frames=300 rigs=1`n" +
            'P4_DEMO_VERIFICATION_OK frames=300 rigs=1')
    }

    It 'does not invoke Godot or publish readiness after a failed build' {
        if (-not (Test-Path -LiteralPath $script:P4DemoVerifierPath -PathType Leaf)) {
            return
        }
        $env:P4_DEMO_FAKE_BUILD_FAIL = '1'
        $failed = $false
        try { & $script:P4DemoVerifierPath -GodotExecutable $script:FakeDemoGodot | Out-Null }
        catch { $failed = $true }

        $failed | Should Be $true
        (Test-Path -LiteralPath $script:DemoChildLog) | Should Be $false
    }

    It 'rejects a nonzero or engine-error child without a readiness marker' {
        if (-not (Test-Path -LiteralPath $script:P4DemoVerifierPath -PathType Leaf)) {
            return
        }

        $env:P4_DEMO_FAKE_CHILD_FAIL = '1'
        $failed = $false
        $output = @()
        try { $output = @(& $script:P4DemoVerifierPath -GodotExecutable $script:FakeDemoGodot) }
        catch { $failed = $true }
        $failed | Should Be $true
        @($output | Where-Object { $_ -eq 'P4_DEMO_OK frames=300 rigs=1' }).Count |
            Should Be 0
        @($output | Where-Object { $_ -eq 'P4_DEMO_VERIFICATION_OK frames=300 rigs=1' }).Count |
            Should Be 0

        Remove-Item Env:P4_DEMO_FAKE_CHILD_FAIL
        $env:P4_DEMO_FAKE_OUTPUT = 'ERROR: failed;;P4_DEMO_OK frames=300 rigs=1'
        $failed = $false
        $output = @()
        try { $output = @(& $script:P4DemoVerifierPath -GodotExecutable $script:FakeDemoGodot) }
        catch { $failed = $true }
        $failed | Should Be $true
        @($output | Where-Object { $_ -eq 'P4_DEMO_OK frames=300 rigs=1' }).Count |
            Should Be 0
        @($output | Where-Object { $_ -eq 'P4_DEMO_VERIFICATION_OK frames=300 rigs=1' }).Count |
            Should Be 0
    }

    It 'fails closed at the P3 input gate and never invokes P4' {
        $env:P4_DEMO_FAKE_P3_OUTPUT =
            'ERROR: p3 input failed;;GODOT_ALS_P3_DEMO_INPUT_OK actions=11 directions=12 camera_basis=1 pitch=1 aiming=1 cleared=1 hud=1'

        $output = @(& pwsh -NoProfile -File $script:P4DemoVerifierPath `
            -GodotExecutable $script:FakeDemoGodot *>&1)

        $LASTEXITCODE | Should Not Be 0
        @(Get-Content $script:DemoChildLog).Count | Should Be 1
        @($output | Where-Object {
            "$_" -ceq 'GODOT_ALS_P3_DEMO_INPUT_OK actions=11 directions=12 camera_basis=1 pitch=1 aiming=1 cleared=1 hud=1'
        }).Count | Should Be 0
        @($output | Where-Object { "$_" -ceq 'P4_DEMO_OK frames=300 rigs=1' }).Count |
            Should Be 0
    }

    It 'does not forward a raw P4 success marker before rejecting later engine errors' {
        $env:P4_DEMO_FAKE_OUTPUT = 'P4_DEMO_OK frames=300 rigs=1;;ERROR: failed after marker'

        $output = @(& pwsh -NoProfile -File $script:P4DemoVerifierPath `
            -GodotExecutable $script:FakeDemoGodot *>&1)

        $LASTEXITCODE | Should Not Be 0
        @($output | Where-Object { "$_" -ceq 'P4_DEMO_OK frames=300 rigs=1' }).Count |
            Should Be 0
        @($output | Where-Object { "$_" -ceq 'P4_DEMO_VERIFICATION_OK frames=300 rigs=1' }).Count |
            Should Be 0
    }

    It 'times out a stalled build and never invokes Godot' {
        $env:P4_DEMO_FAKE_BUILD_SLEEP = '1'
        $failed = $false
        $failureMessage = ''
        try {
            & $script:P4DemoVerifierPath -GodotExecutable $script:FakeDemoGodot `
                -BuildTimeoutSeconds 1 | Out-Null
        }
        catch { $failed = $true; $failureMessage = $_.Exception.Message }

        $failed | Should Be $true
        $failureMessage | Should Match 'build timed out after 1 second'
        (Test-Path -LiteralPath $script:DemoChildLog) | Should Be $false
    }

    It 'times out a stalled P3 input gate before invoking P4' {
        $env:P4_DEMO_FAKE_P3_SLEEP = '1'
        $failed = $false
        $failureMessage = ''
        try {
            & $script:P4DemoVerifierPath -GodotExecutable $script:FakeDemoGodot `
                -P3InputTimeoutSeconds 3 | Out-Null
        }
        catch { $failed = $true; $failureMessage = $_.Exception.Message }

        $failed | Should Be $true
        $failureMessage | Should Match 'P3 input smoke timed out after 3 seconds'
        @(Get-Content $script:DemoChildLog).Count | Should Be 1
    }

    It 'times out a stalled P4 smoke and kills its descendant process tree' {
        $env:P4_DEMO_FAKE_P4_SLEEP = '1'
        $failed = $false
        $failureMessage = ''
        try {
            & $script:P4DemoVerifierPath -GodotExecutable $script:FakeDemoGodot `
                -P4DemoTimeoutSeconds 3 | Out-Null
        }
        catch { $failed = $true; $failureMessage = $_.Exception.Message }

        $failed | Should Be $true
        $failureMessage | Should Match 'P4 demo smoke timed out after 3 seconds'
        @(Get-Content $script:DemoChildLog).Count | Should Be 2
        (Test-Path -LiteralPath $script:DemoDescendantLog -PathType Leaf) | Should Be $true
        $descendantId = [int](Get-Content -Raw $script:DemoDescendantLog)
        (Get-Process -Id $descendantId -ErrorAction SilentlyContinue) |
            Should BeNullOrEmpty
    }
}

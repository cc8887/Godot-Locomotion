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

    It 'never publishes or changes lock bytes when the injected comparison fails' {
        (Get-Command Invoke-AlsP2aCompareAndPublish -ErrorAction SilentlyContinue) | Should Not BeNullOrEmpty
        $output = Join-Path $TestDrive 'orchestration-failure.lock.json'
        [IO.File]::WriteAllText($output, '{"sentinel":true}', [Text.UTF8Encoding]::new($false))
        $before = [Convert]::ToBase64String([IO.File]::ReadAllBytes($output))
        $events = [Collections.Generic.List[string]]::new()
        $manifest = Join-Path $script:RepositoryRoot 'assets\generated\als_v4\als_manifest.json'
        $compare = { [void]$events.Add('compare'); throw 'injected comparison failure' }
        $publish = { [void]$events.Add('publish'); Publish-AlsExportLock -ManifestPath $manifest -LockPath $output }
        $rejected = $false

        try {
            Invoke-AlsP2aCompareAndPublish -GateToken 'P2A_EXPORT_GATES_COMPLETE' `
                -CompareAction $compare -PublishAction $publish -UpdateAssetLock
        }
        catch { $rejected = $true }

        $rejected | Should Be $true
        ($events -join ',') | Should Be 'compare'
        [Convert]::ToBase64String([IO.File]::ReadAllBytes($output)) | Should Be $before
        @(Get-ChildItem $TestDrive -Filter '.orchestration-failure.lock.json.*.tmp').Count | Should Be 0
    }

    It 'publishes exactly once and only after the injected comparison succeeds' {
        (Get-Command Invoke-AlsP2aCompareAndPublish -ErrorAction SilentlyContinue) | Should Not BeNullOrEmpty
        $output = Join-Path $TestDrive 'orchestration-success.lock.json'
        $events = [Collections.Generic.List[string]]::new()
        $manifest = Join-Path $script:RepositoryRoot 'assets\generated\als_v4\als_manifest.json'
        $compare = { [void]$events.Add('compare') }
        $publish = { [void]$events.Add('publish'); Publish-AlsExportLock -ManifestPath $manifest -LockPath $output }

        Invoke-AlsP2aCompareAndPublish -GateToken 'P2A_EXPORT_GATES_COMPLETE' `
            -CompareAction $compare -PublishAction $publish -UpdateAssetLock

        ($events -join ',') | Should Be 'compare,publish'
        (Test-Path -LiteralPath $output -PathType Leaf) | Should Be $true
    }

    It 'requires the completed export gate token before compare or publish' {
        (Get-Command Invoke-AlsP2aCompareAndPublish -ErrorAction SilentlyContinue) | Should Not BeNullOrEmpty
        $events = [Collections.Generic.List[string]]::new()
        $compare = { [void]$events.Add('compare') }
        $publish = { [void]$events.Add('publish') }
        $rejected = $false

        try {
            Invoke-AlsP2aCompareAndPublish -GateToken 'INCOMPLETE' `
                -CompareAction $compare -PublishAction $publish -UpdateAssetLock
        }
        catch { $rejected = $true }

        $rejected | Should Be $true
        $events.Count | Should Be 0
    }

    It 'routes verify-p2a through the tested orchestration after existing export gates' {
        $source = [IO.File]::ReadAllText($script:P2aVerifierPath)
        $fullExportGate = $source.LastIndexOf('GODOT_ALS_P2A_FULL_EXPORT_OK')
        $compareInvocation = $source.LastIndexOf('& $compareScript')
        $orchestration = $source.LastIndexOf('Invoke-AlsP2aCompareAndPublish')
        $source | Should Match '\[switch\]\$UpdateAssetLock'
        $compareInvocation | Should BeGreaterThan $fullExportGate
        $orchestration | Should BeGreaterThan $fullExportGate
        $orchestration | Should BeGreaterThan $compareInvocation
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
}

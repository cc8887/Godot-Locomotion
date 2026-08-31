$script:RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:AssetLockFunctionsPath = Join-Path $script:RepositoryRoot 'scripts\asset-lock-functions.ps1'
$script:CompareExportsPath = Join-Path $script:RepositoryRoot 'scripts\compare-p2a-exports.ps1'

if (Test-Path -LiteralPath $script:AssetLockFunctionsPath -PathType Leaf) {
    . $script:AssetLockFunctionsPath
}

function Get-TestStableId([int]$Value) {
    return ([long]$Value).ToString('x40')
}

function Get-TestSha1([string]$Value) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    return [Convert]::ToHexString([Security.Cryptography.SHA1]::HashData($bytes)).ToLowerInvariant()
}

function New-TestAsset([int]$Index, [string]$Kind) {
    $id = Get-TestStableId ($Index + 1)
    return [pscustomobject][ordered]@{
        id = $id
        objectPath = "/Game/AdvancedLocomotionV4/$Kind/$id.$id"
        packagePath = "/Game/AdvancedLocomotionV4/$Kind/$id"
        assetName = $id
        classPath = "/Script/Engine.$Kind"
        outputPath = $null
        dependencies = @()
        metadata = [pscustomobject]@{}
    }
}

function New-TestP5aManifest {
    $nextId = 0
    $newAssets = {
        param([int]$Count, [string]$Kind)
        $items = @()
        for ($index = 0; $index -lt $Count; $index++) {
            $items += New-TestAsset -Index $script:FixtureAssetId -Kind $Kind
            $script:FixtureAssetId++
        }
        return $items
    }
    $script:FixtureAssetId = 0

    $animations = @(& $newAssets 126 'AnimSequence')
    foreach ($animation in $animations) {
        $animation.classPath = '/Script/Engine.AnimSequence'
        $animation.metadata = [pscustomobject][ordered]@{ timeline = @(); syncMarkers = @() }
    }
    $sequence = $animations[0]
    $sequenceClass0 = '/Script/Engine.AnimNotify'
    $sequenceClass1 = '/Script/Engine.AnimNotifyState'
    $sequence.metadata.timeline = @(
        [pscustomobject][ordered]@{
            stableEventId = Get-TestSha1 "$($sequence.id)|timeline|0|$sequenceClass0"
            kind = 'Generic'; sourceClassPath = $sequenceClass0; displayName = 'Queued event'
            timeSeconds = 0.1; durationSeconds = 0.0; triggerWeightThreshold = 0.0
            tickMode = 'Queued'; sourceIndex = 0; trackIndex = 0; payload = [pscustomobject]@{}
        },
        [pscustomobject][ordered]@{
            stableEventId = Get-TestSha1 "$($sequence.id)|timeline|1|$sequenceClass1"
            kind = 'Generic'; sourceClassPath = $sequenceClass1; displayName = 'Branching event'
            timeSeconds = 0.2; durationSeconds = 0.1; triggerWeightThreshold = 0.0
            tickMode = 'BranchingPoint'; sourceIndex = 1; trackIndex = 0; payload = [pscustomobject]@{}
        }
    )
    $sequence.metadata.syncMarkers = @(
        [pscustomobject][ordered]@{
            stableMarkerId = Get-TestSha1 "$($sequence.id)|marker|0|Left"
            name = 'Left'; timeSeconds = 0.15; sourceIndex = 0; trackIndex = 0
        },
        [pscustomobject][ordered]@{
            stableMarkerId = Get-TestSha1 "$($sequence.id)|marker|1|Right"
            name = 'Right'; timeSeconds = 0.65; sourceIndex = 1; trackIndex = 0
        }
    )

    $montages = @(& $newAssets 18 'AnimMontage')
    foreach ($montage in $montages) {
        $montage.classPath = '/Script/Engine.AnimMontage'
        $montage.metadata = [pscustomobject][ordered]@{ timeline = @(); sections = @() }
    }
    $montage = $montages[0]
    $montageClass = '/Script/Engine.AnimNotify'
    $montage.metadata.timeline = @(
        [pscustomobject][ordered]@{
            stableEventId = Get-TestSha1 "$($montage.id)|timeline|0|$montageClass"
            kind = 'Generic'; sourceClassPath = $montageClass; displayName = 'Montage event'
            timeSeconds = 0.25; durationSeconds = 0.0; triggerWeightThreshold = 0.0
            tickMode = 'Queued'; sourceIndex = 0; trackIndex = 0; payload = [pscustomobject]@{}
        }
    )
    $montage.metadata.sections = @(
        [pscustomobject][ordered]@{ name = 'Default'; nextSection = 'Loop'; startTime = 0.0 },
        [pscustomobject][ordered]@{ name = 'Loop'; nextSection = ''; startTime = 0.5 }
    )

    $files = @()
    for ($index = 0; $index -lt 141; $index++) {
        $relativePath = "animations/$(([long]($index + 1000)).ToString('x40')).fbx"
        $files += [pscustomobject][ordered]@{
            relativePath = $relativePath
            sha256 = ('a' * 64)
            size = 1
        }
    }

    return [pscustomobject][ordered]@{
        schemaVersion = 2
        exporterVersion = '2.0.0'
        sourceEngineVersion = '5.9.0'
        sourceProjectId = 'AdvancedLocomotionSystemV'
        sourceContentRoot = '/Game/AdvancedLocomotionV4'
        skeletons = @(& $newAssets 5 'Skeleton')
        skeletalMeshes = @(& $newAssets 7 'SkeletalMesh')
        staticMeshes = @(& $newAssets 4 'StaticMesh')
        animations = $animations
        montages = $montages
        blendSpaces = @(& $newAssets 8 'BlendSpace')
        aimOffsets = @(& $newAssets 1 'AimOffset')
        materials = @(& $newAssets 15 'Material')
        textures = @(& $newAssets 4 'Texture')
        physicsAssets = @(& $newAssets 2 'PhysicsAsset')
        curves = @(& $newAssets 28 'Curve')
        configAssets = @(& $newAssets 49 'Blueprint')
        files = $files
        auditSummary = [pscustomobject][ordered]@{
            status = 'complete'; assetCount = 267; fileCount = 141; errorCount = 0; warningCount = 0
        }
    }
}

function Copy-TestManifest([object]$Manifest) {
    return ($Manifest | ConvertTo-Json -Depth 20 | ConvertFrom-Json)
}

function Get-ManifestAuditError([object]$Manifest) {
    try {
        Assert-AlsP2aPublishManifest -Manifest $Manifest -Label 'Fixture' | Out-Null
        return ''
    }
    catch {
        return $_.Exception.Message
    }
}

function Write-TestExportRoot([string]$Root, [object]$Manifest) {
    [void][IO.Directory]::CreateDirectory($Root)
    $encoding = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText((Join-Path $Root 'als_manifest.json'), ($Manifest | ConvertTo-Json -Depth 20), $encoding)
    [IO.File]::WriteAllText((Join-Path $Root 'export_plan.json'), '{"fixture":true}', $encoding)
    [void][IO.Directory]::CreateDirectory((Join-Path $Root 'partial'))
    [IO.File]::WriteAllText((Join-Path $Root 'partial\als_manifest.partial.json'), '{"fixture":true}', $encoding)
    [void][IO.Directory]::CreateDirectory((Join-Path $Root 'audit'))
    [IO.File]::WriteAllText((Join-Path $Root 'audit\export_report.json'), '{"fixture":true}', $encoding)
    [IO.File]::WriteAllText((Join-Path $Root 'audit\export_report.txt'), 'fixture', $encoding)
    foreach ($file in $Manifest.files) {
        $path = Join-Path $Root $file.relativePath
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
        [IO.File]::WriteAllBytes($path, [byte[]]@(0x61))
        $file.sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    [IO.File]::WriteAllText((Join-Path $Root 'als_manifest.json'), ($Manifest | ConvertTo-Json -Depth 20), $encoding)
}

function New-PublicationFixture([string]$Name) {
    $repo = Join-Path $TestDrive "repo-$Name"
    $canonical = Join-Path $repo 'assets\generated\als_v4'
    $candidate = Join-Path $repo 'artifacts\p2a-publication\canonical-candidate'
    $determinism = Join-Path $repo 'artifacts\p2a-determinism\als_v4'
    $lock = Join-Path $repo 'reference\als-v4-export.lock.json'
    $lockCandidate = Join-Path $repo 'artifacts\p2a-publication\als-v4-export.lock.candidate.json'
    $canonicalBackup = Join-Path $repo 'artifacts\p2a-publication\als_v4.canonical.backup'
    $lockBackup = Join-Path $repo 'artifacts\p2a-publication\als-v4-export.lock.backup.json'
    $journal = Join-Path $repo 'artifacts\p2a-publication\transaction.json'
    [void][IO.Directory]::CreateDirectory($canonical)
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($lock))
    [IO.File]::WriteAllText((Join-Path $canonical 'old.bin'), 'old-canonical', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($lock, "old-lock`r`n", [Text.UTF8Encoding]::new($false))
    $manifest = New-TestP5aManifest
    Write-TestExportRoot -Root $candidate -Manifest $manifest
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($determinism))
    Copy-Item -LiteralPath $candidate -Destination $determinism -Recurse
    return [pscustomobject]@{
        RepositoryRoot = $repo; CanonicalRoot = $canonical; CandidateRoot = $candidate
        DeterminismRoot = $determinism; LockPath = $lock; LockCandidatePath = $lockCandidate
        CanonicalBackupRoot = $canonicalBackup; LockBackupPath = $lockBackup; JournalPath = $journal
    }
}

function Get-TreeByteSnapshot([string]$Root) {
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) { return '<absent>' }
    return @(Get-ChildItem -LiteralPath $Root -File -Recurse | Sort-Object FullName | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\', '/')
        "$relative=$([Convert]::ToBase64String([IO.File]::ReadAllBytes($_.FullName)))"
    }) -join "`n"
}

function Get-FileByteSnapshot([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return '<absent>' }
    return [Convert]::ToBase64String([IO.File]::ReadAllBytes($Path))
}

function Invoke-TestPublication([object]$Fixture, [string]$FaultInjectionPoint = '', [switch]$UpdateAssetLock) {
    Invoke-AlsP2aJointPublication -GateToken 'P2A_EXPORT_GATES_COMPLETE' `
        -RepositoryRoot $Fixture.RepositoryRoot -CanonicalRoot $Fixture.CanonicalRoot `
        -CandidateRoot $Fixture.CandidateRoot -DeterminismRoot $Fixture.DeterminismRoot `
        -LockPath $Fixture.LockPath -LockCandidatePath $Fixture.LockCandidatePath `
        -CanonicalBackupRoot $Fixture.CanonicalBackupRoot -LockBackupPath $Fixture.LockBackupPath `
        -JournalPath $Fixture.JournalPath -ComparisonScriptPath $script:CompareExportsPath `
        -FaultInjectionPoint $FaultInjectionPoint -UpdateAssetLock:$UpdateAssetLock
}

function Assert-NoPublicationResidue([object]$Fixture) {
    foreach ($path in @(
        $Fixture.CandidateRoot, $Fixture.DeterminismRoot, $Fixture.LockCandidatePath,
        $Fixture.CanonicalBackupRoot, $Fixture.LockBackupPath, $Fixture.JournalPath
    )) {
        (Test-Path -LiteralPath $path) | Should Be $false
    }
}

Describe 'P5A complete ALS v2 export audit' {
    It 'accepts exact inventory and audits Sequence Montage IDs tick totals terminal links and audio absence' {
        (Get-Command Assert-AlsP2aPublishManifest -ErrorAction SilentlyContinue) | Should Not BeNullOrEmpty
        if (-not (Get-Command Assert-AlsP2aPublishManifest -ErrorAction SilentlyContinue)) { return }
        $result = Assert-AlsP2aPublishManifest -Manifest (New-TestP5aManifest) -Label 'Fixture'

        $result.SchemaVersion | Should Be 2
        $result.ExporterVersion | Should Be '2.0.0'
        $result.AssetCount | Should Be 267
        $result.FileCount | Should Be 141
        $result.AnimationCount | Should Be 126
        $result.SequenceEventCount | Should Be 2
        $result.MontageEventCount | Should Be 1
        $result.EventCount | Should Be 3
        $result.QueuedCount | Should Be 2
        $result.BranchingPointCount | Should Be 1
        ($result.QueuedCount + $result.BranchingPointCount) | Should Be $result.EventCount
        $result.SyncMarkerCount | Should Be 2
        $result.TerminalSectionCount | Should Be 1
        $result.AudioAssetCount | Should Be 0
    }

    It 'rejects missing Sequence or Montage timeline arrays' {
        (Get-Command Assert-AlsP2aPublishManifest -ErrorAction SilentlyContinue) | Should Not BeNullOrEmpty
        if (-not (Get-Command Assert-AlsP2aPublishManifest -ErrorAction SilentlyContinue)) { return }
        $sequence = New-TestP5aManifest
        $sequence.animations[1].metadata.PSObject.Properties.Remove('timeline')
        (Get-ManifestAuditError $sequence) | Should Match 'Sequence.*timeline'

        $montage = New-TestP5aManifest
        $montage.montages[1].metadata.PSObject.Properties.Remove('timeline')
        (Get-ManifestAuditError $montage) | Should Match 'Montage.*timeline'
    }

    It 'rejects event and marker IDs that are malformed duplicated or not derived from the stable formula' {
        (Get-Command Assert-AlsP2aPublishManifest -ErrorAction SilentlyContinue) | Should Not BeNullOrEmpty
        if (-not (Get-Command Assert-AlsP2aPublishManifest -ErrorAction SilentlyContinue)) { return }
        $cases = @(
            @{ Name = 'malformed event'; Mutate = { param($m) $m.animations[0].metadata.timeline[0].stableEventId = 'bad' }; Pattern = 'stableEventId' },
            @{ Name = 'duplicate event'; Mutate = { param($m) $m.animations[0].metadata.timeline[1].stableEventId = $m.animations[0].metadata.timeline[0].stableEventId }; Pattern = 'duplicate.*event' },
            @{ Name = 'wrong event formula'; Mutate = { param($m) $m.animations[0].metadata.timeline[0].stableEventId = ('0' * 40) }; Pattern = 'stable.*formula' },
            @{ Name = 'malformed marker'; Mutate = { param($m) $m.animations[0].metadata.syncMarkers[0].stableMarkerId = 'bad' }; Pattern = 'stableMarkerId' },
            @{ Name = 'duplicate marker'; Mutate = { param($m) $m.animations[0].metadata.syncMarkers[1].stableMarkerId = $m.animations[0].metadata.syncMarkers[0].stableMarkerId }; Pattern = 'duplicate.*marker' },
            @{ Name = 'wrong marker formula'; Mutate = { param($m) $m.animations[0].metadata.syncMarkers[0].stableMarkerId = ('0' * 40) }; Pattern = 'stable.*formula' }
        )
        foreach ($case in $cases) {
            $manifest = New-TestP5aManifest
            & $case.Mutate $manifest
            (Get-ManifestAuditError $manifest) | Should Match $case.Pattern
        }
    }

    It 'rejects unknown tick modes literal None section links audio and inventory drift' {
        (Get-Command Assert-AlsP2aPublishManifest -ErrorAction SilentlyContinue) | Should Not BeNullOrEmpty
        if (-not (Get-Command Assert-AlsP2aPublishManifest -ErrorAction SilentlyContinue)) { return }
        $tick = New-TestP5aManifest
        $tick.animations[0].metadata.timeline[0].tickMode = 'Unknown'
        (Get-ManifestAuditError $tick) | Should Match 'tickMode'

        $section = New-TestP5aManifest
        $section.montages[0].metadata.sections[1].nextSection = 'None'
        (Get-ManifestAuditError $section) | Should Match 'nextSection.*None'

        $audio = New-TestP5aManifest
        $audio.textures[0].objectPath = '/Game/AdvancedLocomotionV4/Audio/Footstep.Footstep'
        (Get-ManifestAuditError $audio) | Should Match 'audio'

        $assetCount = New-TestP5aManifest
        $assetCount.auditSummary.assetCount = 266
        (Get-ManifestAuditError $assetCount) | Should Match 'assets=266'

        $fileCount = New-TestP5aManifest
        $fileCount.files = @($fileCount.files | Select-Object -First 140)
        (Get-ManifestAuditError $fileCount) | Should Match 'files=140'

        $animationCount = New-TestP5aManifest
        $animationCount.animations = @($animationCount.animations | Select-Object -First 125)
        (Get-ManifestAuditError $animationCount) | Should Match 'animations=125'
    }
}

Describe 'P5A two-root comparison' {
    It 'requires independent roots to contain the exact same complete file set and bytes' {
        $fixture = New-PublicationFixture 'compare'
        & $script:CompareExportsPath -ReferenceRoot $fixture.CandidateRoot -CandidateRoot $fixture.DeterminismRoot
        [IO.File]::WriteAllText((Join-Path $fixture.DeterminismRoot 'unexpected.bin'), 'extra')
        $errorMessage = ''
        try { & $script:CompareExportsPath -ReferenceRoot $fixture.CandidateRoot -CandidateRoot $fixture.DeterminismRoot }
        catch { $errorMessage = $_.Exception.Message }
        $errorMessage | Should Match 'file set differs'

        Remove-Item -LiteralPath (Join-Path $fixture.DeterminismRoot 'unexpected.bin')
        [IO.File]::AppendAllText((Join-Path $fixture.DeterminismRoot 'export_plan.json'), 'different')
        $errorMessage = ''
        try { & $script:CompareExportsPath -ReferenceRoot $fixture.CandidateRoot -CandidateRoot $fixture.DeterminismRoot }
        catch { $errorMessage = $_.Exception.Message }
        $errorMessage | Should Match 'Export differs'
    }
}

Describe 'P5A joint canonical and lock publication' {
    It 'publishes canonical and lock together only after comparison while keeping lock schemaVersion one' {
        (Get-Command Invoke-AlsP2aJointPublication -ErrorAction SilentlyContinue) | Should Not BeNullOrEmpty
        if (-not (Get-Command Invoke-AlsP2aJointPublication -ErrorAction SilentlyContinue)) { return }
        $fixture = New-PublicationFixture 'success'
        $candidateBefore = Get-TreeByteSnapshot $fixture.CandidateRoot

        Invoke-TestPublication -Fixture $fixture -UpdateAssetLock

        (Get-TreeByteSnapshot $fixture.CanonicalRoot) | Should Be $candidateBefore
        $lock = Get-Content -Raw $fixture.LockPath | ConvertFrom-Json
        $lock.schemaVersion | Should Be 1
        $lock.exporterVersion | Should Be '2.0.0'
        $lock.assetCount | Should Be 267
        $lock.fileCount | Should Be 141
        $lock.animationCount | Should Be 126
        $lock.manifestSha256 | Should Be (Get-FileHash (Join-Path $fixture.CanonicalRoot 'als_manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant()
        Assert-NoPublicationResidue $fixture
    }

    It 'never publishes either resource when UpdateAssetLock is absent' {
        (Get-Command Invoke-AlsP2aJointPublication -ErrorAction SilentlyContinue) | Should Not BeNullOrEmpty
        if (-not (Get-Command Invoke-AlsP2aJointPublication -ErrorAction SilentlyContinue)) { return }
        $fixture = New-PublicationFixture 'no-update'
        $canonicalBefore = Get-TreeByteSnapshot $fixture.CanonicalRoot
        $lockBefore = Get-FileByteSnapshot $fixture.LockPath

        Invoke-TestPublication -Fixture $fixture

        (Get-TreeByteSnapshot $fixture.CanonicalRoot) | Should Be $canonicalBefore
        (Get-FileByteSnapshot $fixture.LockPath) | Should Be $lockBefore
        Assert-NoPublicationResidue $fixture
    }

    foreach ($fault in @('Comparison', 'CanonicalSwap', 'LockSwap')) {
        It "restores old canonical and lock bytes and clears all residue after $fault fault injection" {
            (Get-Command Invoke-AlsP2aJointPublication -ErrorAction SilentlyContinue) | Should Not BeNullOrEmpty
            if (-not (Get-Command Invoke-AlsP2aJointPublication -ErrorAction SilentlyContinue)) { return }
            $fixture = New-PublicationFixture "fault-$fault"
            $canonicalBefore = Get-TreeByteSnapshot $fixture.CanonicalRoot
            $lockBefore = Get-FileByteSnapshot $fixture.LockPath
            $rejected = $false

            try { Invoke-TestPublication -Fixture $fixture -FaultInjectionPoint $fault -UpdateAssetLock }
            catch { $rejected = $true }

            $rejected | Should Be $true
            (Get-TreeByteSnapshot $fixture.CanonicalRoot) | Should Be $canonicalBefore
            (Get-FileByteSnapshot $fixture.LockPath) | Should Be $lockBefore
            Assert-NoPublicationResidue $fixture
        }
    }

    It 'recovers an incomplete journal before starting a new publication' {
        (Get-Command Repair-AlsP2aPublication -ErrorAction SilentlyContinue) | Should Not BeNullOrEmpty
        if (-not (Get-Command Repair-AlsP2aPublication -ErrorAction SilentlyContinue)) { return }
        $fixture = New-PublicationFixture 'startup-recovery'
        $canonicalBefore = Get-TreeByteSnapshot $fixture.CanonicalRoot
        $lockBefore = Get-FileByteSnapshot $fixture.LockPath
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fixture.CanonicalBackupRoot))
        Move-Item -LiteralPath $fixture.CanonicalRoot -Destination $fixture.CanonicalBackupRoot
        Move-Item -LiteralPath $fixture.CandidateRoot -Destination $fixture.CanonicalRoot
        Move-Item -LiteralPath $fixture.LockPath -Destination $fixture.LockBackupPath
        [IO.File]::WriteAllText($fixture.LockPath, 'new-lock', [Text.UTF8Encoding]::new($false))
        $journal = [ordered]@{
            schemaVersion = 1; state = 'prepared'; repositoryRoot = $fixture.RepositoryRoot
            canonicalRoot = $fixture.CanonicalRoot; candidateRoot = $fixture.CandidateRoot
            determinismRoot = $fixture.DeterminismRoot; canonicalBackupRoot = $fixture.CanonicalBackupRoot
            lockPath = $fixture.LockPath; lockCandidatePath = $fixture.LockCandidatePath
            lockBackupPath = $fixture.LockBackupPath; journalPath = $fixture.JournalPath
            canonicalOriginalExisted = $true; lockOriginalExisted = $true
        }
        [IO.File]::WriteAllText($fixture.JournalPath, ($journal | ConvertTo-Json), [Text.UTF8Encoding]::new($false))

        Repair-AlsP2aPublication -RepositoryRoot $fixture.RepositoryRoot -JournalPath $fixture.JournalPath

        (Get-TreeByteSnapshot $fixture.CanonicalRoot) | Should Be $canonicalBefore
        (Get-FileByteSnapshot $fixture.LockPath) | Should Be $lockBefore
        Assert-NoPublicationResidue $fixture
    }

    It 'rejects every move or delete path outside the repository before comparison or mutation' {
        (Get-Command Invoke-AlsP2aJointPublication -ErrorAction SilentlyContinue) | Should Not BeNullOrEmpty
        if (-not (Get-Command Invoke-AlsP2aJointPublication -ErrorAction SilentlyContinue)) { return }
        foreach ($property in @(
            'CanonicalRoot', 'CandidateRoot', 'DeterminismRoot', 'LockPath', 'LockCandidatePath',
            'CanonicalBackupRoot', 'LockBackupPath', 'JournalPath'
        )) {
            $fixture = New-PublicationFixture "escape-$property"
            $canonicalBefore = Get-TreeByteSnapshot $fixture.CanonicalRoot
            $lockBefore = Get-FileByteSnapshot $fixture.LockPath
            $outside = Join-Path $TestDrive "outside-$property"
            $fixture.$property = $outside
            $rejected = $false

            try { Invoke-TestPublication -Fixture $fixture -UpdateAssetLock }
            catch { $rejected = $true }

            $rejected | Should Be $true
            (Get-TreeByteSnapshot (Join-Path $fixture.RepositoryRoot 'assets\generated\als_v4')) | Should Be $canonicalBefore
            (Get-FileByteSnapshot (Join-Path $fixture.RepositoryRoot 'reference\als-v4-export.lock.json')) | Should Be $lockBefore
            (Test-Path -LiteralPath $outside) | Should Be $false
        }
    }
}

$script:RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:P2aPublicationFunctionsPath = Join-Path $script:RepositoryRoot 'scripts\p2a-publication-functions.ps1'
$script:CompareExportsPath = Join-Path $script:RepositoryRoot 'scripts\compare-p2a-exports.ps1'

if (Test-Path -LiteralPath $script:P2aPublicationFunctionsPath -PathType Leaf) {
    . $script:P2aPublicationFunctionsPath
}

function Get-TestStableId([int]$Value) {
    return ([long]$Value).ToString('x40')
}

function Get-TestSha1([string]$Value) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    return [Convert]::ToHexString([Security.Cryptography.SHA1]::HashData($bytes)).ToLowerInvariant()
}

function Get-TestManifestAssetCount([object]$Manifest) {
    $count = 0
    foreach ($collection in @(
        'skeletons', 'skeletalMeshes', 'staticMeshes', 'animations', 'montages', 'blendSpaces',
        'aimOffsets', 'materials', 'textures', 'physicsAssets', 'curves', 'configAssets'
    )) {
        $count += @($Manifest.$collection).Count
    }
    return $count
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
        $animation.metadata = [pscustomobject][ordered]@{ playLength = 1.0; timeline = @(); syncMarkers = @() }
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
        $montage.metadata = [pscustomobject][ordered]@{ playLength = 1.0; timeline = @(); sections = @() }
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

    $manifest = [pscustomobject][ordered]@{
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
            status = 'complete'; assetCount = 0; fileCount = $files.Count; errorCount = 0; warningCount = 0
        }
    }
    $manifest.auditSummary.assetCount = Get-TestManifestAssetCount $manifest
    return $manifest
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

function Set-TestTimelineKind([object]$Event, [string]$Kind, [object]$Payload) {
    $Event.kind = $Kind
    $Event.payload = $Payload
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
    $canonicalBackup = Join-Path $repo 'artifacts\p2a-publication\als_v4.canonical.backup'
    $journal = Join-Path $repo 'artifacts\p2a-publication\transaction.json'
    [void][IO.Directory]::CreateDirectory($canonical)
    [IO.File]::WriteAllText((Join-Path $canonical 'old.bin'), 'old-canonical', [Text.UTF8Encoding]::new($false))
    $manifest = New-TestP5aManifest
    Write-TestExportRoot -Root $candidate -Manifest $manifest
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($determinism))
    Copy-Item -LiteralPath $candidate -Destination $determinism -Recurse
    return [pscustomobject]@{
        RepositoryRoot = $repo; CanonicalRoot = $canonical; CandidateRoot = $candidate
        DeterminismRoot = $determinism; CanonicalBackupRoot = $canonicalBackup; JournalPath = $journal
        LegacyLockPath = Join-Path $repo 'reference\als-v4-export.lock.json'
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

function Invoke-TestPublication([object]$Fixture, [string]$FaultInjectionPoint = '') {
    Invoke-AlsP2aPublication -GateToken 'P2A_EXPORT_GATES_COMPLETE' `
        -RepositoryRoot $Fixture.RepositoryRoot -CanonicalRoot $Fixture.CanonicalRoot `
        -CandidateRoot $Fixture.CandidateRoot -DeterminismRoot $Fixture.DeterminismRoot `
        -CanonicalBackupRoot $Fixture.CanonicalBackupRoot `
        -JournalPath $Fixture.JournalPath -ComparisonScriptPath $script:CompareExportsPath `
        -FaultInjectionPoint $FaultInjectionPoint
}

function Assert-NoPublicationResidue([object]$Fixture) {
    foreach ($path in @(
        $Fixture.CandidateRoot, $Fixture.DeterminismRoot,
        $Fixture.CanonicalBackupRoot, $Fixture.JournalPath
    )) {
        (Test-Path -LiteralPath $path) | Should Be $false
    }
}

function Write-TestPublicationJournal([object]$Fixture, [string]$State, [bool]$CanonicalOriginalExisted) {
    $journal = [ordered]@{
        schemaVersion = 1; state = $State; repositoryRoot = $Fixture.RepositoryRoot
        canonicalRoot = $Fixture.CanonicalRoot; candidateRoot = $Fixture.CandidateRoot
        determinismRoot = $Fixture.DeterminismRoot; canonicalBackupRoot = $Fixture.CanonicalBackupRoot
        journalPath = $Fixture.JournalPath; canonicalOriginalExisted = $CanonicalOriginalExisted
    }
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Fixture.JournalPath))
    [IO.File]::WriteAllText($Fixture.JournalPath, ($journal | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
}

function New-TestTopLevelManifest([string]$AuditStatus) {
    $manifest = New-TestP5aManifest
    $manifest.animations[0].metadata.timeline[0].kind = 'SetAction'
    $manifest.animations[0].metadata.timeline[0].payload = [pscustomobject]@{ action = 'Mantling' }
    foreach ($skeleton in $manifest.skeletons) {
        $skeleton.metadata = [pscustomobject][ordered]@{
            boneCount = 4
            bones = @(
                [pscustomobject]@{ name = 'root' }, [pscustomobject]@{ name = 'pelvis' },
                [pscustomobject]@{ name = 'foot_l' }, [pscustomobject]@{ name = 'foot_r' }
            )
            restPoseHash = ('a' * 40)
            sockets = @()
        }
    }
    foreach ($animation in $manifest.animations) {
        $animationFields = [ordered]@{
            loop = $false; interpolation = 'Linear'; forceRootLock = $false
            useNormalizedRootMotionScale = $false; additiveBasePoseType = 'None'
            additiveBasePoseFrame = 0; additiveBasePoseId = ''; additiveBasePoseObjectPath = ''
        }
        foreach ($entry in $animationFields.GetEnumerator()) {
            $animation.metadata | Add-Member -NotePropertyName $entry.Key -NotePropertyValue $entry.Value
        }
    }
    $manifest.animations[0].objectPath = '/Game/AdvancedLocomotionV4/Overlay/Fixture.Fixture'
    foreach ($montage in $manifest.montages) {
        $montageFields = [ordered]@{
            blendInTime = 0.2; blendInOption = 'Linear'; blendOutTime = 0.2
            blendOutOption = 'Linear'; blendOutTriggerTime = -1.0; enableAutoBlendOut = $true
        }
        foreach ($entry in $montageFields.GetEnumerator()) {
            $montage.metadata | Add-Member -NotePropertyName $entry.Key -NotePropertyValue $entry.Value
        }
    }
    foreach ($blendAsset in @($manifest.blendSpaces) + @($manifest.aimOffsets)) {
        $blendAsset.metadata = [pscustomobject]@{ samples = @() }
    }
    $manifest.blendSpaces[0].metadata.samples = @([pscustomobject]@{ id = 'sample' })
    foreach ($physicsAsset in $manifest.physicsAssets) {
        $physicsAsset.metadata = [pscustomobject]@{
            bodies = @([pscustomobject]@{ id = 'body' })
            constraints = @([pscustomobject]@{ id = 'constraint' })
            constraintCount = 1
        }
    }
    $manifest.staticMeshes[0].objectPath = '/Game/AdvancedLocomotionV4/Props/Fixture.Fixture'
    $manifest.materials[0].classPath = '/Script/Engine.MaterialInstanceConstant'
    $manifest.materials[0].metadata = [pscustomobject]@{
        scalarParameterOverrides = @([pscustomobject]@{ name = 'Value' })
        vectorParameterOverrides = @()
        textureParameterOverrides = @()
    }
    $manifest.auditSummary.status = $AuditStatus
    return $manifest
}

function New-TestTopLevelPlan([int]$ExportableCount) {
    $kinds = @(
        'Skeleton', 'SkeletalMesh', 'StaticMesh', 'AnimationSequence', 'AnimMontage',
        'BlendSpace', 'MaterialInstance', 'PhysicsAsset', 'Texture', 'Blueprint'
    )
    $assets = @()
    for ($index = 0; $index -lt $kinds.Count; $index++) {
        $assets += [pscustomobject][ordered]@{
            id = Get-TestStableId ($index + 1)
            kind = $kinds[$index]
            objectPath = "/Game/AdvancedLocomotionV4/Fixture/$($kinds[$index])"
        }
    }
    return [pscustomobject][ordered]@{
        assets = $assets
        summary = [pscustomobject]@{ assetCount = $assets.Count; exportableCount = $ExportableCount }
    }
}

function New-TestTopLevelVerifierFixture([string]$Name, [string]$Stage, [string]$Corruption) {
    $repo = Join-Path $TestDrive "top-level-$Name"
    $scripts = Join-Path $repo 'scripts'
    $template = Join-Path $repo 'fixture-data\export-template'
    $canonical = Join-Path $repo 'assets\generated\als_v4'
    $engineRoot = Join-Path $repo 'engine'
    $editor = Join-Path $engineRoot 'Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
    [void][IO.Directory]::CreateDirectory($scripts)
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($editor))
    [void][IO.Directory]::CreateDirectory($canonical)
    $buildVersionPath = Join-Path $engineRoot 'Engine\Build\Build.version'
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($buildVersionPath))
    [IO.File]::WriteAllText($buildVersionPath, '{"MajorVersion":5,"MinorVersion":9,"PatchVersion":0}')
    Copy-Item $script:P2aPublicationFunctionsPath (Join-Path $scripts 'p2a-publication-functions.ps1')
    Copy-Item $script:CompareExportsPath (Join-Path $scripts 'compare-p2a-exports.ps1')
    Copy-Item (Join-Path $script:RepositoryRoot 'scripts\unreal-version-functions.ps1') `
        (Join-Path $scripts 'unreal-version-functions.ps1')
    Copy-Item (Join-Path $script:RepositoryRoot 'scripts\verify-p2a.ps1') (Join-Path $scripts 'verify-p2a.ps1')
    [IO.File]::WriteAllText(
        (Join-Path $scripts 'build-als-exporter.ps1'),
        "Write-Output 'GODOT_ALS_EXPORTER_READY engine=5.9.0 plugin=9.4-preview'; `$global:LASTEXITCODE = 0",
        [Text.UTF8Encoding]::new($false))
    $editorSource = Join-Path $repo 'fixture-editor.cs'
    [IO.File]::WriteAllText($editorSource, @'
using System;
using System.IO;

internal static class FixtureEditor
{
    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(directory.Replace(source, destination));
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(source, destination), true);
    }

    public static int Main(string[] args)
    {
        string output = null;
        bool dryRun = false;
        bool export = false;
        foreach (string argument in args)
        {
            if (argument.StartsWith("-Output=", StringComparison.Ordinal))
                output = argument.Substring("-Output=".Length);
            if (String.Equals(argument, "-DryRun", StringComparison.Ordinal)) dryRun = true;
            if (String.Equals(argument, "-Export", StringComparison.Ordinal)) export = true;
        }
        if (String.IsNullOrEmpty(output) || args.Length == 0) return 11;
        string template = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0])),
            "fixture-data", "export-template");
        Directory.CreateDirectory(output);
        if (dryRun)
        {
            Directory.CreateDirectory(Path.Combine(output, "partial"));
            File.Copy(Path.Combine(template, "export_plan.json"), Path.Combine(output, "export_plan.json"), true);
            File.Copy(Path.Combine(template, "partial", "als_manifest.partial.json"),
                Path.Combine(output, "partial", "als_manifest.partial.json"), true);
            Console.WriteLine("GODOT_ALS_P2A_PLAN_OK assets=10 exportable=1 config=1 excluded=0");
            return 0;
        }
        if (export)
        {
            CopyTree(template, output);
            Console.WriteLine("GODOT_ALS_P2A_EXPORT_OK assets=10 files=1 fbx=1 textures=0 warnings=0");
            return 0;
        }
        return 12;
    }
}
'@, [Text.UTF8Encoding]::new($false))
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $compilerOutput = @(& $compiler /nologo /target:exe "/out:$editor" $editorSource 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Fixture editor compilation failed: $($compilerOutput -join ' ')" }

    $formal = New-TestTopLevelManifest 'complete'
    Write-TestExportRoot -Root $template -Manifest $formal
    [IO.File]::WriteAllText(
        (Join-Path $template 'export_plan.json'),
        (New-TestTopLevelPlan $formal.files.Count | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText(
        (Join-Path $template 'partial\als_manifest.partial.json'),
        (New-TestTopLevelManifest 'planned' | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))

    $target = if ($Stage -ceq 'Partial') {
        Join-Path $template 'partial\als_manifest.partial.json'
    }
    else { Join-Path $template 'als_manifest.json' }
    if ($Corruption -ceq 'Duplicate') {
        $raw = [IO.File]::ReadAllText($target)
        $corrupted = [regex]::new('"kind"\s*:\s*"Generic"').Replace(
            $raw, '"kind":"Generic","kind":"Generic"', 1)
        if ($corrupted -ceq $raw) { throw "Failed to inject duplicate property into $target" }
        [IO.File]::WriteAllText($target, $corrupted, [Text.UTF8Encoding]::new($false))
    }
    else {
        [IO.File]::WriteAllText($target, '{"schemaVersion":2,', [Text.UTF8Encoding]::new($false))
    }

    $project = Join-Path $repo 'fixture.uproject'
    [IO.File]::WriteAllText($project, '{}', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $canonical 'old.bin'), 'old-canonical', [Text.UTF8Encoding]::new($false))
    return [pscustomobject]@{
        RepositoryRoot = $repo; Scripts = $scripts; EngineRoot = $engineRoot; UnrealProject = $project
        CanonicalRoot = $canonical
        CandidateRoot = Join-Path $repo 'artifacts\p2a-publication\canonical-candidate'
        DeterminismRoot = Join-Path $repo 'artifacts\p2a-determinism\als_v4'
        CanonicalBackupRoot = Join-Path $repo 'artifacts\p2a-publication\als_v4.canonical.backup'
        JournalPath = Join-Path $repo 'artifacts\p2a-publication\transaction.json'
    }
}

Describe 'P5A complete ALS v2 export audit' {
    It 'accepts a self-consistent inventory and audits Sequence Montage IDs tick totals terminal links and audio absence' {
        (Get-Command Assert-AlsP2aPublishManifest -ErrorAction SilentlyContinue) | Should Not BeNullOrEmpty
        if (-not (Get-Command Assert-AlsP2aPublishManifest -ErrorAction SilentlyContinue)) { return }
        $manifest = New-TestP5aManifest
        $manifest.animations = @($manifest.animations | Select-Object -First 2)
        $manifest.auditSummary.assetCount = Get-TestManifestAssetCount $manifest
        $manifest.exporterVersion = '9.4-preview'
        $result = Assert-AlsP2aPublishManifest -Manifest $manifest -Label 'Fixture'

        $result.SchemaVersion | Should Be 2
        $result.ExporterVersion | Should Be '9.4-preview'
        $result.AssetCount | Should Be $manifest.auditSummary.assetCount
        $result.FileCount | Should Be $manifest.files.Count
        $result.AnimationCount | Should Be $manifest.animations.Count
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
        (Get-ManifestAuditError $animationCount) | Should Match 'inventory summary does not match its contents.*actualAssets='
    }

    It 'accepts the exact six timeline kinds and their frozen payload domains' {
        $cases = @(
            @{ Kind = 'Generic'; Payload = [pscustomobject]@{} },
            @{ Kind = 'Footstep'; Payload = [pscustomobject][ordered]@{ foot = 'Left' } },
            @{ Kind = 'SetAction'; Payload = [pscustomobject][ordered]@{ action = 'Mantling' } },
            @{ Kind = 'SetGroundedEntry'; Payload = [pscustomobject][ordered]@{ mode = 'FromRoll' } },
            @{ Kind = 'EarlyBlendOut'; Payload = [pscustomobject][ordered]@{
                    blendOutSeconds = 0.2; checkInput = $true; checkLocomotionMode = $true
                    locomotionMode = 'Grounded'; checkRotationMode = $true; rotationMode = 'Aiming'
                    checkStance = $true; stance = 'Standing'
                } },
            @{ Kind = 'RootMotionScale'; Payload = [pscustomobject][ordered]@{ translationScale = 1.0 } }
        )
        foreach ($case in $cases) {
            $manifest = New-TestP5aManifest
            Set-TestTimelineKind $manifest.animations[0].metadata.timeline[0] $case.Kind $case.Payload
            (Get-ManifestAuditError $manifest) | Should Be ''
        }
    }

    It 'rejects unknown kinds and invalid frozen enum values before publication' {
        $cases = @(
            @{ Kind = 'UnknownKind'; Payload = [pscustomobject]@{}; Pattern = 'kind' },
            @{ Kind = 'Footstep'; Payload = [pscustomobject]@{ foot = 'Front' }; Pattern = 'foot' },
            @{ Kind = 'SetAction'; Payload = [pscustomobject]@{ action = 'InvalidAction' }; Pattern = 'action' },
            @{ Kind = 'SetGroundedEntry'; Payload = [pscustomobject]@{ mode = 'Walking' }; Pattern = 'mode' },
            @{ Kind = 'EarlyBlendOut'; Payload = [pscustomobject][ordered]@{
                    blendOutSeconds = 0.2; checkInput = $true; checkLocomotionMode = $true
                    locomotionMode = 'Swimming'; checkRotationMode = $true; rotationMode = 'ViewDirection'
                    checkStance = $true; stance = 'Prone'
                }; Pattern = 'locomotionMode|rotationMode|stance' }
        )
        foreach ($case in $cases) {
            $manifest = New-TestP5aManifest
            Set-TestTimelineKind $manifest.animations[0].metadata.timeline[0] $case.Kind $case.Payload
            (Get-ManifestAuditError $manifest) | Should Match $case.Pattern
        }
    }

    It 'rejects missing wrong-shaped and extra payload properties' {
        $cases = @(
            @{ Kind = 'SetAction'; Payload = [pscustomobject]@{}; Pattern = 'payload.*action' },
            @{ Kind = 'SetAction'; Payload = [pscustomobject]@{ mode = 'FromRoll' }; Pattern = 'payload' },
            @{ Kind = 'Generic'; Payload = [pscustomobject]@{ unexpected = $true }; Pattern = 'payload.*unexpected' },
            @{ Kind = 'RootMotionScale'; Payload = [pscustomobject]@{ translationScale = 1.0; extra = 0 }; Pattern = 'payload.*extra' }
        )
        foreach ($case in $cases) {
            $manifest = New-TestP5aManifest
            Set-TestTimelineKind $manifest.animations[0].metadata.timeline[0] $case.Kind $case.Payload
            (Get-ManifestAuditError $manifest) | Should Match $case.Pattern
        }
    }

    It 'rejects missing or extra event properties and empty required strings' {
        $missing = New-TestP5aManifest
        $missing.animations[0].metadata.timeline[0].PSObject.Properties.Remove('displayName')
        (Get-ManifestAuditError $missing) | Should Match 'displayName'

        $extra = New-TestP5aManifest
        $extra.animations[0].metadata.timeline[0] | Add-Member -NotePropertyName unexpected -NotePropertyValue $true
        (Get-ManifestAuditError $extra) | Should Match 'unexpected'

        $empty = New-TestP5aManifest
        $empty.animations[0].metadata.timeline[0].sourceClassPath = ''
        (Get-ManifestAuditError $empty) | Should Match 'sourceClassPath'
    }

    It 'rejects non-finite negative overflowing and out-of-bounds event scalars' {
        $cases = @(
            @{ Field = 'timeSeconds'; Value = [double]::NaN },
            @{ Field = 'timeSeconds'; Value = -0.01 },
            @{ Field = 'timeSeconds'; Value = 1e100 },
            @{ Field = 'timeSeconds'; Value = 1.01 },
            @{ Field = 'durationSeconds'; Value = -0.01 },
            @{ Field = 'durationSeconds'; Value = 0.95 },
            @{ Field = 'triggerWeightThreshold'; Value = -0.01 },
            @{ Field = 'triggerWeightThreshold'; Value = 1.01 }
        )
        foreach ($case in $cases) {
            $manifest = New-TestP5aManifest
            $manifest.animations[0].metadata.timeline[0].($case.Field) = $case.Value
            (Get-ManifestAuditError $manifest) | Should Match $case.Field
        }
    }

    It 'rejects negative fractional and duplicate event indices' {
        $cases = @(
            @{ Field = 'sourceIndex'; Value = -1; Pattern = 'sourceIndex' },
            @{ Field = 'sourceIndex'; Value = 0.5; Pattern = 'sourceIndex' },
            @{ Field = 'trackIndex'; Value = -1; Pattern = 'trackIndex' }
        )
        foreach ($case in $cases) {
            $manifest = New-TestP5aManifest
            $manifest.animations[0].metadata.timeline[0].($case.Field) = $case.Value
            (Get-ManifestAuditError $manifest) | Should Match $case.Pattern
        }
        $duplicate = New-TestP5aManifest
        $duplicate.animations[0].metadata.timeline[1].sourceIndex = 0
        (Get-ManifestAuditError $duplicate) | Should Match 'sourceIndex.*duplicate|duplicate.*sourceIndex'
    }

    foreach ($field in @('sourceIndex', 'trackIndex')) {
        It "rejects event $field above the Int32 consumer range" {
            $manifest = New-TestP5aManifest
            $event = $manifest.animations[0].metadata.timeline[0]
            $event.$field = [int64]2147483648
            if ($field -ceq 'sourceIndex') {
                $event.stableEventId = Get-TestSha1 "$($manifest.animations[0].id)|timeline|2147483648|$($event.sourceClassPath)"
            }

            (Get-ManifestAuditError $manifest) | Should Match "$field.*Int32|$field.*2147483647"
        }
    }

    It 'accepts the Int32 maximum for every event and marker index field' {
        $manifest = New-TestP5aManifest
        $event = $manifest.animations[0].metadata.timeline[0]
        $event.sourceIndex = [int64]2147483647
        $event.trackIndex = [int64]2147483647
        $event.stableEventId = Get-TestSha1 "$($manifest.animations[0].id)|timeline|2147483647|$($event.sourceClassPath)"
        $marker = $manifest.animations[0].metadata.syncMarkers[0]
        $marker.sourceIndex = [int64]2147483647
        $marker.trackIndex = [int64]2147483647
        $marker.stableMarkerId = Get-TestSha1 "$($manifest.animations[0].id)|marker|2147483647|$($marker.name)"

        (Get-ManifestAuditError $manifest) | Should Be ''
    }

    It 'rejects malformed sync marker shape scalars and source indices' {
        $missing = New-TestP5aManifest
        $missing.animations[0].metadata.syncMarkers[0].PSObject.Properties.Remove('name')
        (Get-ManifestAuditError $missing) | Should Match 'name'

        $extra = New-TestP5aManifest
        $extra.animations[0].metadata.syncMarkers[0] | Add-Member -NotePropertyName time -NotePropertyValue 0.15
        (Get-ManifestAuditError $extra) | Should Match 'time'

        foreach ($mutation in @(
            @{ Field = 'timeSeconds'; Value = [double]::PositiveInfinity },
            @{ Field = 'timeSeconds'; Value = -0.01 },
            @{ Field = 'timeSeconds'; Value = 1.01 },
            @{ Field = 'sourceIndex'; Value = -1 },
            @{ Field = 'trackIndex'; Value = 0.5 }
        )) {
            $manifest = New-TestP5aManifest
            $manifest.animations[0].metadata.syncMarkers[0].($mutation.Field) = $mutation.Value
            (Get-ManifestAuditError $manifest) | Should Match $mutation.Field
        }
        $duplicate = New-TestP5aManifest
        $duplicate.animations[0].metadata.syncMarkers[1].sourceIndex = 0
        (Get-ManifestAuditError $duplicate) | Should Match 'sourceIndex.*duplicate|duplicate.*sourceIndex'
    }

    foreach ($field in @('sourceIndex', 'trackIndex')) {
        It "rejects sync marker $field above the Int32 consumer range" {
            $manifest = New-TestP5aManifest
            $marker = $manifest.animations[0].metadata.syncMarkers[0]
            $marker.$field = [int64]2147483648
            if ($field -ceq 'sourceIndex') {
                $marker.stableMarkerId = Get-TestSha1 "$($manifest.animations[0].id)|marker|2147483648|$($marker.name)"
            }

            (Get-ManifestAuditError $manifest) | Should Match "$field.*Int32|$field.*2147483647"
        }
    }

    It 'rejects malformed duplicate unordered out-of-range and dangling Montage sections' {
        $cases = @(
            @{ Mutate = { param($m) $m.montages[0].metadata.sections[0].PSObject.Properties.Remove('name') }; Pattern = 'name' },
            @{ Mutate = { param($m) $m.montages[0].metadata.sections[0] | Add-Member -NotePropertyName extra -NotePropertyValue 1 }; Pattern = 'extra' },
            @{ Mutate = { param($m) $m.montages[0].metadata.sections[1].name = 'Default' }; Pattern = 'duplicate.*section|section.*duplicate' },
            @{ Mutate = { param($m) $m.montages[0].metadata.sections[1].startTime = -0.1 }; Pattern = 'startTime' },
            @{ Mutate = { param($m) $m.montages[0].metadata.sections[1].startTime = [double]::NaN }; Pattern = 'startTime' },
            @{ Mutate = { param($m) $m.montages[0].metadata.sections[1].startTime = 0.0 }; Pattern = 'startTime|increasing' },
            @{ Mutate = { param($m) $m.montages[0].metadata.sections[1].startTime = 1.1 }; Pattern = 'startTime' },
            @{ Mutate = { param($m) $m.montages[0].metadata.sections[0].nextSection = 'Missing' }; Pattern = 'nextSection' }
        )
        foreach ($case in $cases) {
            $manifest = New-TestP5aManifest
            & $case.Mutate $manifest
            (Get-ManifestAuditError $manifest) | Should Match $case.Pattern
        }
    }

    foreach ($collectionShape in @('timeline', 'syncMarkers', 'sections')) {
        It "requires $collectionShape metadata to be a JSON array while allowing empty arrays" {
            $manifest = New-TestP5aManifest
            switch ($collectionShape) {
                'timeline' { $manifest.animations[0].metadata.timeline = $manifest.animations[0].metadata.timeline[0] }
                'syncMarkers' { $manifest.animations[0].metadata.syncMarkers = $manifest.animations[0].metadata.syncMarkers[0] }
                'sections' { $manifest.montages[0].metadata.sections = $manifest.montages[0].metadata.sections[0] }
            }
            (Get-ManifestAuditError $manifest) | Should Match "$collectionShape.*array|array.*$collectionShape"

            $empty = New-TestP5aManifest
            if ($collectionShape -ceq 'timeline') { $empty.animations[1].metadata.timeline = @() }
            elseif ($collectionShape -ceq 'syncMarkers') { $empty.animations[1].metadata.syncMarkers = @() }
            else { $empty.montages[1].metadata.sections = @() }
            (Get-ManifestAuditError $empty) | Should Be ''
        }
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

    It 'rejects a case-only full relative path difference even when file bytes match' {
        $fixture = New-PublicationFixture 'compare-case'
        $original = Join-Path $fixture.DeterminismRoot 'export_plan.json'
        $caseVariant = Join-Path $fixture.DeterminismRoot 'EXPORT_PLAN.JSON'
        $bytes = [IO.File]::ReadAllBytes($original)
        Remove-Item -LiteralPath $original
        [IO.File]::WriteAllBytes($caseVariant, $bytes)

        $errorMessage = ''
        try { & $script:CompareExportsPath -ReferenceRoot $fixture.CandidateRoot -CandidateRoot $fixture.DeterminismRoot }
        catch { $errorMessage = $_.Exception.Message }

        $errorMessage | Should Match 'file set differs'
    }

    It 'rejects the same Windows export root passed through a case-only path alias' {
        if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { return }
        $fixture = New-PublicationFixture 'compare-same-root-alias'

        $errorMessage = ''
        try {
            & $script:CompareExportsPath -ReferenceRoot $fixture.CandidateRoot `
                -CandidateRoot $fixture.CandidateRoot.ToUpperInvariant()
        }
        catch { $errorMessage = $_.Exception.Message }

        $errorMessage | Should Match 'independent'
    }

    It 'rejects a reparse-point export root before reading comparison files' {
        if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { return }
        $fixture = New-PublicationFixture 'compare-junction'
        $externalParent = Join-Path $TestDrive 'compare-junction-external'
        $externalRoot = Join-Path $externalParent 'export'
        [void][IO.Directory]::CreateDirectory($externalParent)
        Copy-Item -LiteralPath $fixture.CandidateRoot -Destination $externalRoot -Recurse
        $externalSentinel = Join-Path $externalParent 'sentinel.txt'
        [IO.File]::WriteAllText($externalSentinel, 'outside')
        Remove-Item -LiteralPath $fixture.DeterminismRoot -Recurse
        New-Item -ItemType Junction -Path $fixture.DeterminismRoot -Target $externalRoot | Out-Null

        $errorMessage = ''
        try { & $script:CompareExportsPath -ReferenceRoot $fixture.CandidateRoot -CandidateRoot $fixture.DeterminismRoot }
        catch { $errorMessage = $_.Exception.Message }

        $errorMessage | Should Match 'reparse'
        [IO.File]::ReadAllText($externalSentinel) | Should Be 'outside'
    }
}

Describe 'P5A canonical publication without asset locks' {
    It 'publishes the deterministic candidate without writing a historical asset lock' {
        (Get-Command Invoke-AlsP2aPublication -ErrorAction SilentlyContinue) | Should Not BeNullOrEmpty
        if (-not (Get-Command Invoke-AlsP2aPublication -ErrorAction SilentlyContinue)) { return }
        $fixture = New-PublicationFixture 'success'
        $candidateBefore = Get-TreeByteSnapshot $fixture.CandidateRoot

        Invoke-TestPublication -Fixture $fixture

        (Get-TreeByteSnapshot $fixture.CanonicalRoot) | Should Be $candidateBefore
        (Test-Path -LiteralPath $fixture.LegacyLockPath) | Should Be $false
        Assert-NoPublicationResidue $fixture
    }

    It 'rejects recursively duplicated raw manifest properties before publication' {
        $fixture = New-PublicationFixture 'duplicate-raw-property'
        $canonicalBefore = Get-TreeByteSnapshot $fixture.CanonicalRoot
        $candidateManifest = Join-Path $fixture.CandidateRoot 'als_manifest.json'
        $determinismManifest = Join-Path $fixture.DeterminismRoot 'als_manifest.json'
        $raw = [IO.File]::ReadAllText($candidateManifest)
        $duplicate = [regex]::new('"kind"\s*:\s*"Generic"').Replace(
            $raw, '"kind":"Generic","kind":"Generic"', 1)
        $duplicate | Should Not Be $raw
        $encoding = [Text.UTF8Encoding]::new($false)
        [IO.File]::WriteAllText($candidateManifest, $duplicate, $encoding)
        [IO.File]::WriteAllText($determinismManifest, $duplicate, $encoding)

        $message = ''
        try { Invoke-TestPublication -Fixture $fixture }
        catch { $message = $_.Exception.Message }

        $message | Should Match 'duplicate.*kind|kind.*duplicate'
        (Get-TreeByteSnapshot $fixture.CanonicalRoot) | Should Be $canonicalBefore
        Assert-NoPublicationResidue $fixture
    }

    It 'rejects invalid manifest JSON and cleans both export stages' {
        $fixture = New-PublicationFixture 'invalid-raw-json'
        $canonicalBefore = Get-TreeByteSnapshot $fixture.CanonicalRoot
        $invalid = '{"schemaVersion":2,'
        $encoding = [Text.UTF8Encoding]::new($false)
        [IO.File]::WriteAllText((Join-Path $fixture.CandidateRoot 'als_manifest.json'), $invalid, $encoding)
        [IO.File]::WriteAllText((Join-Path $fixture.DeterminismRoot 'als_manifest.json'), $invalid, $encoding)

        $message = ''
        try { Invoke-TestPublication -Fixture $fixture }
        catch { $message = $_.Exception.Message }

        $message | Should Match 'manifest is not valid JSON'
        (Get-TreeByteSnapshot $fixture.CanonicalRoot) | Should Be $canonicalBefore
        Assert-NoPublicationResidue $fixture
    }

    foreach ($fault in @('Comparison', 'CanonicalSwap')) {
        It "restores the previous canonical directory after $fault failure" {
            $fixture = New-PublicationFixture "fault-$fault"
            $canonicalBefore = Get-TreeByteSnapshot $fixture.CanonicalRoot
            $rejected = $false

            try { Invoke-TestPublication -Fixture $fixture -FaultInjectionPoint $fault }
            catch { $rejected = $true }

            $rejected | Should Be $true
            (Get-TreeByteSnapshot $fixture.CanonicalRoot) | Should Be $canonicalBefore
            Assert-NoPublicationResidue $fixture
        }
    }

    It 'recovers an interrupted canonical swap from its transaction journal' {
        $fixture = New-PublicationFixture 'startup-recovery'
        $canonicalBefore = Get-TreeByteSnapshot $fixture.CanonicalRoot
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fixture.CanonicalBackupRoot))
        Move-Item -LiteralPath $fixture.CanonicalRoot -Destination $fixture.CanonicalBackupRoot
        Move-Item -LiteralPath $fixture.CandidateRoot -Destination $fixture.CanonicalRoot
        Write-TestPublicationJournal $fixture 'prepared' $true

        Repair-AlsP2aPublication -RepositoryRoot $fixture.RepositoryRoot -JournalPath $fixture.JournalPath

        (Get-TreeByteSnapshot $fixture.CanonicalRoot) | Should Be $canonicalBefore
        Assert-NoPublicationResidue $fixture
    }

    It 'rejects publication paths outside the repository before comparison or mutation' {
        foreach ($property in @('CanonicalRoot', 'CandidateRoot', 'DeterminismRoot', 'CanonicalBackupRoot', 'JournalPath')) {
            $fixture = New-PublicationFixture "escape-$property"
            $canonicalRoot = Join-Path $fixture.RepositoryRoot 'assets\generated\als_v4'
            $canonicalBefore = Get-TreeByteSnapshot $canonicalRoot
            $outside = Join-Path $TestDrive "outside-$property"
            $fixture.$property = $outside
            $rejected = $false

            try { Invoke-TestPublication -Fixture $fixture }
            catch { $rejected = $true }

            $rejected | Should Be $true
            (Get-TreeByteSnapshot $canonicalRoot) | Should Be $canonicalBefore
            (Test-Path -LiteralPath $outside) | Should Be $false
        }
    }

    It 'rejects ancestor and descendant transaction paths before running comparison' {
        foreach ($case in @(
            @{ Name = 'candidate-under-canonical'; Mutate = { param($fixture) $fixture.CandidateRoot = Join-Path $fixture.CanonicalRoot 'candidate' } },
            @{ Name = 'journal-parent'; Mutate = { param($fixture) $fixture.JournalPath = [IO.Path]::GetDirectoryName($fixture.CanonicalBackupRoot) } }
        )) {
            $fixture = New-PublicationFixture "topology-$($case.Name)"
            $canonicalBefore = Get-TreeByteSnapshot $fixture.CanonicalRoot
            $probe = Join-Path $fixture.RepositoryRoot 'comparison-probe.ps1'
            $probeMarker = Join-Path $fixture.RepositoryRoot 'comparison-called.txt'
            [IO.File]::WriteAllText($probe, "[IO.File]::WriteAllText('$($probeMarker.Replace("'", "''"))', 'called')", [Text.UTF8Encoding]::new($false))
            & $case.Mutate $fixture

            $message = ''
            try { Invoke-TestPublication -Fixture $fixture }
            catch { $message = $_.Exception.Message }

            $message | Should Match 'ancestor|descendant|overlap'
            (Test-Path -LiteralPath $probeMarker) | Should Be $false
            (Get-TreeByteSnapshot $fixture.CanonicalRoot) | Should Be $canonicalBefore
        }
    }

    It 'rejects a junction candidate before comparison and preserves the external sentinel' {
        if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { return }
        $fixture = New-PublicationFixture 'junction'
        $external = Join-Path $TestDrive 'junction-external'
        [void][IO.Directory]::CreateDirectory($external)
        $externalSentinel = Join-Path $external 'sentinel.txt'
        [IO.File]::WriteAllText($externalSentinel, 'outside', [Text.UTF8Encoding]::new($false))
        Remove-Item -LiteralPath $fixture.CandidateRoot -Recurse
        New-Item -ItemType Junction -Path $fixture.CandidateRoot -Target $external | Out-Null
        $probe = Join-Path $fixture.RepositoryRoot 'comparison-probe.ps1'
        $probeMarker = Join-Path $fixture.RepositoryRoot 'comparison-called.txt'
        [IO.File]::WriteAllText($probe, "[IO.File]::WriteAllText('$($probeMarker.Replace("'", "''"))', 'called')", [Text.UTF8Encoding]::new($false))

        $message = ''
        try { Invoke-TestPublication -Fixture $fixture }
        catch { $message = $_.Exception.Message }

        $message | Should Match 'reparse'
        [IO.File]::ReadAllText($externalSentinel) | Should Be 'outside'
        (Test-Path -LiteralPath $probeMarker) | Should Be $false
    }

    It 'finishes cleanup after a committed transaction with partial residue' {
        $fixture = New-PublicationFixture 'committed-cleanup'
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fixture.CanonicalBackupRoot))
        Move-Item -LiteralPath $fixture.CanonicalRoot -Destination $fixture.CanonicalBackupRoot
        Move-Item -LiteralPath $fixture.CandidateRoot -Destination $fixture.CanonicalRoot
        $canonicalCommitted = Get-TreeByteSnapshot $fixture.CanonicalRoot
        Remove-Item -LiteralPath $fixture.CanonicalBackupRoot -Recurse
        Write-TestPublicationJournal $fixture 'committed' $true

        Repair-AlsP2aPublication -RepositoryRoot $fixture.RepositoryRoot -JournalPath $fixture.JournalPath

        (Get-TreeByteSnapshot $fixture.CanonicalRoot) | Should Be $canonicalCommitted
        Assert-NoPublicationResidue $fixture
    }
}
Describe 'P2A workflow staging cleanup' {
    foreach ($phase in @('DryRun', 'FullExport', 'DeterminismExport')) {
        It "cleans all staging without touching canonical output when $phase fails" {
            (Get-Command Invoke-AlsP2aStagingWorkflow -ErrorAction SilentlyContinue) | Should Not BeNullOrEmpty
            if (-not (Get-Command Invoke-AlsP2aStagingWorkflow -ErrorAction SilentlyContinue)) { return }
            $fixture = New-PublicationFixture "workflow-$phase"
            Remove-Item $fixture.CandidateRoot -Recurse
            Remove-Item $fixture.DeterminismRoot -Recurse
            $canonicalBefore = Get-TreeByteSnapshot $fixture.CanonicalRoot

            $rejected = $false
            try {
                Invoke-AlsP2aStagingWorkflow -RepositoryRoot $fixture.RepositoryRoot `
                    -CandidateRoot $fixture.CandidateRoot -DeterminismRoot $fixture.DeterminismRoot `
                    -Action {
                        [void][IO.Directory]::CreateDirectory($fixture.CandidateRoot)
                        [IO.File]::WriteAllText((Join-Path $fixture.CandidateRoot 'candidate.bin'), 'candidate')
                        if ($phase -eq 'DryRun') { throw 'injected dry-run failure' }
                        [void][IO.Directory]::CreateDirectory($fixture.DeterminismRoot)
                        [IO.File]::WriteAllText((Join-Path $fixture.DeterminismRoot 'determinism.bin'), 'determinism')
                        if ($phase -eq 'FullExport') { throw 'injected full-export failure' }
                        throw 'injected determinism failure'
                    }
            }
            catch { $rejected = $true }

            $rejected | Should Be $true
            (Get-TreeByteSnapshot $fixture.CanonicalRoot) | Should Be $canonicalBefore
            Assert-NoPublicationResidue $fixture
        }
    }
}

Describe 'P5A verify exact canonical boundary' {
    It 'rejects Output at the assets ancestor before any cleanup or build side effect' {
        $repo = Join-Path $TestDrive 'verify-boundary'
        $scripts = Join-Path $repo 'scripts'
        [void][IO.Directory]::CreateDirectory($scripts)
        Copy-Item $script:P2aPublicationFunctionsPath (Join-Path $scripts 'p2a-publication-functions.ps1')
        Copy-Item (Join-Path $script:RepositoryRoot 'scripts\unreal-version-functions.ps1') `
            (Join-Path $scripts 'unreal-version-functions.ps1')
        Copy-Item (Join-Path $script:RepositoryRoot 'scripts\verify-p2a.ps1') (Join-Path $scripts 'verify-p2a.ps1')
        $engineRoot = Join-Path $repo 'engine'
        $buildVersion = Join-Path $engineRoot 'Engine\Build\Build.version'
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($buildVersion))
        [IO.File]::WriteAllText($buildVersion, '{"MajorVersion":5,"MinorVersion":9,"PatchVersion":0}')
        [IO.File]::WriteAllText((Join-Path $scripts 'build-als-exporter.ps1'), "throw 'build must not run'", [Text.UTF8Encoding]::new($false))
        $config = Join-Path $repo 'assets\config'
        $candidate = Join-Path $repo 'artifacts\p2a-publication\canonical-candidate'
        [void][IO.Directory]::CreateDirectory($config)
        [void][IO.Directory]::CreateDirectory($candidate)
        $configSentinel = Join-Path $config 'sentinel.txt'
        $stagingSentinel = Join-Path $candidate 'sentinel.txt'
        [IO.File]::WriteAllText($configSentinel, 'config')
        [IO.File]::WriteAllText($stagingSentinel, 'staging')
        $project = Join-Path $repo 'fixture.uproject'
        [IO.File]::WriteAllText($project, '{}')

        $errorMessage = ''
        try { & (Join-Path $scripts 'verify-p2a.ps1') -EngineRoot $engineRoot -UnrealProject $project -Output (Join-Path $repo 'assets') }
        catch { $errorMessage = $_.Exception.Message }

        $errorMessage | Should Match 'assets.generated.als_v4|canonical publication root'
        [IO.File]::ReadAllText($configSentinel) | Should Be 'config'
        [IO.File]::ReadAllText($stagingSentinel) | Should Be 'staging'
        (Test-Path (Join-Path $repo 'artifacts\p2a-publication\transaction.json')) | Should Be $false
        (Test-Path (Join-Path $repo 'artifacts\p2a-publication\als_v4.canonical.backup')) | Should Be $false
    }
}

Describe 'P5A top-level raw manifest gates' {
    foreach ($stage in @('Partial', 'Formal')) {
        foreach ($corruption in @('Duplicate', 'Invalid')) {
            It "rejects $corruption raw JSON at the $stage manifest before its success marker or transaction" {
                if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { return }
                $fixture = New-TestTopLevelVerifierFixture "$stage-$corruption" $stage $corruption
                $canonicalBefore = Get-TreeByteSnapshot $fixture.CanonicalRoot
                $output = @()
                $errorMessage = ''

                try {
                    & (Join-Path $fixture.Scripts 'verify-p2a.ps1') `
                        -EngineRoot $fixture.EngineRoot -UnrealProject $fixture.UnrealProject *>&1 |
                        ForEach-Object { $output += $_ }
                }
                catch {
                    $errorMessage = $_.Exception.Message
                    $output += $_.ToString()
                }

                $expectedError = if ($corruption -ceq 'Duplicate') { "$stage.*duplicate.*kind" }
                    else { "$stage.*not valid JSON" }
                $errorMessage | Should Match $expectedError
                $text = $output -join "`n"
                if ($stage -ceq 'Partial') {
                    $text | Should Not Match 'GODOT_ALS_P2A_METADATA_OK'
                }
                else {
                    $text | Should Match 'GODOT_ALS_P2A_METADATA_OK'
                }
                $text | Should Not Match 'GODOT_ALS_P2A_FULL_EXPORT_OK|GODOT_ALS_P2A_PUBLICATION_OK'
                (Get-TreeByteSnapshot $fixture.CanonicalRoot) | Should Be $canonicalBefore
                Assert-NoPublicationResidue $fixture
            }
        }
    }
}

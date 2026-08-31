function Read-AlsExportLock {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$LockPath)

    if (-not (Test-Path -LiteralPath $LockPath -PathType Leaf)) { throw "ALS export lock does not exist: $LockPath" }
    try { $document = [System.Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText($LockPath)) }
    catch { throw "ALS export lock is not valid JSON: $($_.Exception.Message)" }
    try {
        if ($document.RootElement.ValueKind -ne [Text.Json.JsonValueKind]::Object) { throw 'ALS export lock must be a JSON object.' }
        $required = @('schemaVersion', 'manifestSha256', 'assetCount', 'fileCount', 'animationCount', 'exporterVersion', 'sourceProjectId')
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $values = @{}
        foreach ($property in $document.RootElement.EnumerateObject()) {
            if ($property.Name -cnotin $required) { throw "ALS export lock contains unknown property: $($property.Name)" }
            if (-not $seen.Add($property.Name)) { throw "ALS export lock contains duplicate property: $($property.Name)" }
            $values[$property.Name] = $property.Value.Clone()
        }
        foreach ($name in $required) { if (-not $seen.Contains($name)) { throw "ALS export lock is missing property: $name" } }
        $schemaVersion = 0; $assetCount = 0; $fileCount = 0; $animationCount = 0
        if (-not $values.schemaVersion.TryGetInt32([ref]$schemaVersion) -or $schemaVersion -ne 1) { throw 'ALS export lock schemaVersion must be exactly 1.' }
        if (-not $values.assetCount.TryGetInt32([ref]$assetCount) -or $assetCount -ne 267) { throw 'ALS export lock assetCount must be exactly 267.' }
        if (-not $values.fileCount.TryGetInt32([ref]$fileCount) -or $fileCount -ne 141) { throw 'ALS export lock fileCount must be exactly 141.' }
        if (-not $values.animationCount.TryGetInt32([ref]$animationCount) -or $animationCount -ne 126) { throw 'ALS export lock animationCount must be exactly 126.' }
        $manifestSha256 = $values.manifestSha256.GetString()
        $exporterVersion = $values.exporterVersion.GetString()
        $sourceProjectId = $values.sourceProjectId.GetString()
        if ($manifestSha256 -cnotmatch '^[0-9a-f]{64}$') { throw 'ALS export lock manifestSha256 must be lowercase SHA-256.' }
        if ([string]::IsNullOrWhiteSpace($exporterVersion)) { throw 'ALS export lock exporterVersion must be non-empty.' }
        if ([string]::IsNullOrWhiteSpace($sourceProjectId)) { throw 'ALS export lock sourceProjectId must be non-empty.' }
        [pscustomobject]@{ SchemaVersion=$schemaVersion; ManifestSha256=$manifestSha256; AssetCount=$assetCount; FileCount=$fileCount; AnimationCount=$animationCount; ExporterVersion=$exporterVersion; SourceProjectId=$sourceProjectId }
    }
    finally { $document.Dispose() }
}

function Publish-AlsExportLock {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ManifestPath,
        [Parameter(Mandatory)][string]$LockPath,
        [string]$RepositoryRoot = ''
    )

    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    $assetCount = [int]$manifest.auditSummary.assetCount
    $fileCount = @($manifest.files).Count
    $animationCount = @($manifest.animations).Count
    if ($manifest.auditSummary.status -cne 'complete' -or [int]$manifest.auditSummary.errorCount -ne 0 -or
        $assetCount -ne 267 -or $fileCount -ne 141 -or $animationCount -ne 126) {
        throw "Refusing to publish ALS export lock from incomplete or unexpected manifest counts: assets=$assetCount files=$fileCount animations=$animationCount"
    }
    $value = [ordered]@{
        schemaVersion = 1
        manifestSha256 = (Get-FileHash -LiteralPath $ManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
        assetCount = $assetCount
        fileCount = $fileCount
        animationCount = $animationCount
        exporterVersion = [string]$manifest.exporterVersion
        sourceProjectId = [string]$manifest.sourceProjectId
    }
    if ([string]::IsNullOrWhiteSpace($value.exporterVersion) -or [string]::IsNullOrWhiteSpace($value.sourceProjectId)) { throw 'Refusing to publish ALS export lock without producer identity.' }
    $lockFullPath = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
        [IO.Path]::GetFullPath($LockPath)
    }
    else {
        Resolve-AlsRepositoryDescendantPath -RepositoryRoot $RepositoryRoot -Path $LockPath -Label 'LockPath'
    }
    $directory = [IO.Path]::GetDirectoryName($lockFullPath)
    [void][IO.Directory]::CreateDirectory($directory)
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes("$($value | ConvertTo-Json)$([Environment]::NewLine)")
    $temporaryPath = Join-Path $directory ".$([IO.Path]::GetFileName($lockFullPath)).$([guid]::NewGuid().ToString('N')).tmp"
    if (-not [string]::IsNullOrWhiteSpace($RepositoryRoot)) {
        $temporaryPath = Resolve-AlsRepositoryDescendantPath -RepositoryRoot $RepositoryRoot -Path $temporaryPath -Label 'Lock temporary path'
    }
    try {
        $stream = [System.IO.FileStream]::new($temporaryPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
        try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
        if ([System.IO.File]::Exists($lockFullPath)) { [System.IO.File]::Replace($temporaryPath, $lockFullPath, [Management.Automation.Language.NullString]::Value) }
        else { [System.IO.File]::Move($temporaryPath, $lockFullPath) }
    }
    catch { throw "Failed to publish ALS export lock atomically: $($_.Exception.Message)" }
    finally {
        if ([IO.File]::Exists($temporaryPath)) {
            if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { [IO.File]::Delete($temporaryPath) }
            else { Remove-AlsRepositoryDescendantPath -RepositoryRoot $RepositoryRoot -Path $temporaryPath -Label 'Lock temporary residue' }
        }
    }
}

function Get-AlsP2aSha1 {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Value)

    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    return [Convert]::ToHexString([Security.Cryptography.SHA1]::HashData($bytes)).ToLowerInvariant()
}

function Assert-AlsP2aPublishManifest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Manifest,
        [Parameter(Mandatory)][string]$Label
    )

    if ($null -eq $Manifest.PSObject.Properties['schemaVersion'] -or [int]$Manifest.schemaVersion -ne 2) {
        throw "$Label manifest schemaVersion must be exactly 2."
    }
    if ($null -eq $Manifest.PSObject.Properties['exporterVersion'] -or [string]$Manifest.exporterVersion -cne '2.0.0') {
        throw "$Label manifest exporterVersion must be exactly 2.0.0."
    }
    $assetCollections = @(
        'skeletons', 'skeletalMeshes', 'staticMeshes', 'animations', 'montages', 'blendSpaces',
        'aimOffsets', 'materials', 'textures', 'physicsAssets', 'curves', 'configAssets'
    )
    $allAssets = @()
    foreach ($collection in $assetCollections) {
        if ($null -eq $Manifest.PSObject.Properties[$collection]) {
            throw "$Label manifest is missing asset collection '$collection'."
        }
        $allAssets += @($Manifest.$collection)
    }
    $assetCount = [int]$Manifest.auditSummary.assetCount
    $fileCount = @($Manifest.files).Count
    $animationCount = @($Manifest.animations).Count
    if ([string]$Manifest.auditSummary.status -cne 'complete' -or [int]$Manifest.auditSummary.errorCount -ne 0 -or
        $assetCount -ne 267 -or $allAssets.Count -ne 267 -or $fileCount -ne 141 -or
        [int]$Manifest.auditSummary.fileCount -ne 141 -or $animationCount -ne 126) {
        throw "$Label manifest inventory is incomplete or unexpected: assets=$assetCount actualAssets=$($allAssets.Count) files=$fileCount animations=$animationCount."
    }

    $audioAssets = @($allAssets | Where-Object {
        $values = @($_.objectPath, $_.packagePath, $_.assetName, $_.classPath, $_.outputPath) -join '|'
        $values -match '(?i)(^|[/\\.])Audio([/\\.]|$)|SoundWave|SoundCue|MetaSound|\\.(wav|ogg|mp3)(\||$)'
    })
    $audioFiles = @($Manifest.files | Where-Object {
        [string]$_.relativePath -match '(?i)(^|[/\\])Audio([/\\]|$)|\.(wav|ogg|mp3)$'
    })
    if ($audioAssets.Count + $audioFiles.Count -ne 0) {
        throw "$Label manifest contains audio assets or files: assets=$($audioAssets.Count) files=$($audioFiles.Count)."
    }

    $eventIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $markerIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $sequenceEventCount = 0
    $montageEventCount = 0
    $queuedCount = 0
    $branchingPointCount = 0
    $syncMarkerCount = 0
    $terminalSectionCount = 0

    foreach ($kind in @('Sequence', 'Montage')) {
        $assets = if ($kind -ceq 'Sequence') { @($Manifest.animations) } else { @($Manifest.montages) }
        foreach ($asset in $assets) {
            if ($null -eq $asset.PSObject.Properties['metadata'] -or
                $null -eq $asset.metadata.PSObject.Properties['timeline'] -or
                $null -eq $asset.metadata.timeline) {
                throw "$Label $kind '$($asset.objectPath)' is missing its timeline array."
            }
            $assetId = [string]$asset.id
            if ($assetId -cnotmatch '^[0-9a-f]{40}$') {
                throw "$Label $kind '$($asset.objectPath)' has an invalid stable asset ID."
            }
            foreach ($event in @($asset.metadata.timeline)) {
                $stableEventId = [string]$event.stableEventId
                if ($stableEventId -cnotmatch '^[0-9a-f]{40}$') {
                    throw "$Label $kind '$($asset.objectPath)' has an invalid stableEventId."
                }
                if (-not $eventIds.Add($stableEventId)) {
                    throw "$Label manifest contains a duplicate stable event ID: $stableEventId."
                }
                $sourceClassPath = [string]$event.sourceClassPath
                $sourceIndex = [int]$event.sourceIndex
                $expectedEventId = Get-AlsP2aSha1 "$assetId|timeline|$sourceIndex|$sourceClassPath"
                if ($stableEventId -cne $expectedEventId) {
                    throw "$Label stable event ID does not match the stable formula for '$($asset.objectPath)' sourceIndex=$sourceIndex."
                }
                switch -CaseSensitive ([string]$event.tickMode) {
                    'Queued' { $queuedCount++ }
                    'BranchingPoint' { $branchingPointCount++ }
                    default { throw "$Label event '$stableEventId' has an invalid tickMode: $($event.tickMode)." }
                }
                if ($kind -ceq 'Sequence') { $sequenceEventCount++ } else { $montageEventCount++ }
            }
            if ($kind -ceq 'Sequence') {
                if ($null -eq $asset.metadata.PSObject.Properties['syncMarkers'] -or
                    $null -eq $asset.metadata.syncMarkers) {
                    throw "$Label Sequence '$($asset.objectPath)' is missing its syncMarkers array."
                }
                foreach ($marker in @($asset.metadata.syncMarkers)) {
                    $stableMarkerId = [string]$marker.stableMarkerId
                    if ($stableMarkerId -cnotmatch '^[0-9a-f]{40}$') {
                        throw "$Label Sequence '$($asset.objectPath)' has an invalid stableMarkerId."
                    }
                    if (-not $markerIds.Add($stableMarkerId)) {
                        throw "$Label manifest contains a duplicate stable marker ID: $stableMarkerId."
                    }
                    $sourceIndex = [int]$marker.sourceIndex
                    $name = [string]$marker.name
                    $expectedMarkerId = Get-AlsP2aSha1 "$assetId|marker|$sourceIndex|$name"
                    if ($stableMarkerId -cne $expectedMarkerId) {
                        throw "$Label stable marker ID does not match the stable formula for '$($asset.objectPath)' sourceIndex=$sourceIndex."
                    }
                    $syncMarkerCount++
                }
            }
            else {
                foreach ($section in @($asset.metadata.sections)) {
                    if ([string]$section.nextSection -ceq 'None') {
                        throw "$Label Montage '$($asset.objectPath)' has forbidden nextSection literal None."
                    }
                    if ([string]$section.nextSection -ceq '') { $terminalSectionCount++ }
                }
            }
        }
    }
    $eventCount = $sequenceEventCount + $montageEventCount
    if ($sequenceEventCount -le 0 -or $montageEventCount -le 0 -or $syncMarkerCount -le 0) {
        throw "$Label manifest lacks Sequence events, Montage events, or sync markers."
    }
    if ($queuedCount + $branchingPointCount -ne $eventCount) {
        throw "$Label manifest tick-mode totals do not equal the total event count."
    }

    return [pscustomobject]@{
        SchemaVersion = 2; ExporterVersion = '2.0.0'; AssetCount = $assetCount; FileCount = $fileCount
        AnimationCount = $animationCount; SequenceEventCount = $sequenceEventCount
        MontageEventCount = $montageEventCount; EventCount = $eventCount; QueuedCount = $queuedCount
        BranchingPointCount = $branchingPointCount; SyncMarkerCount = $syncMarkerCount
        TerminalSectionCount = $terminalSectionCount; AudioAssetCount = 0
    }
}

function ConvertTo-AlsCanonicalExistingPath {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { return $resolved }
    if ($null -eq ('AlsP2a.NativePathMethods' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
namespace AlsP2a {
    public static class NativePathMethods {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint GetLongPathName(string shortPath, StringBuilder longPath, uint bufferLength);
    }
}
'@
    }
    $buffer = [Text.StringBuilder]::new(32768)
    $length = [AlsP2a.NativePathMethods]::GetLongPathName($resolved, $buffer, [uint32]$buffer.Capacity)
    if ($length -eq 0 -or $length -ge $buffer.Capacity) {
        throw "Failed to resolve canonical Windows path: $resolved"
    }
    return $buffer.ToString()
}

function Resolve-AlsRepositoryDescendantPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Label
    )

    if (-not [IO.Path]::IsPathFullyQualified($RepositoryRoot)) { throw "RepositoryRoot must be absolute: $RepositoryRoot" }
    if (-not [IO.Path]::IsPathFullyQualified($Path)) { throw "$Label must be absolute: $Path" }
    if (-not (Test-Path -LiteralPath $RepositoryRoot -PathType Container)) { throw "RepositoryRoot does not exist: $RepositoryRoot" }
    $root = (ConvertTo-AlsCanonicalExistingPath -Path $RepositoryRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $inputPath = [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    if (Test-Path -LiteralPath $inputPath) {
        $fullPath = (ConvertTo-AlsCanonicalExistingPath -Path $inputPath).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    }
    else {
        $probe = $inputPath
        $tail = [Collections.Generic.List[string]]::new()
        while (-not (Test-Path -LiteralPath $probe)) {
            $leaf = [IO.Path]::GetFileName($probe)
            if ([string]::IsNullOrEmpty($leaf)) { throw "$Label has no existing ancestor: $inputPath" }
            $tail.Insert(0, $leaf)
            $parent = [IO.Path]::GetDirectoryName($probe)
            if ([string]::IsNullOrEmpty($parent) -or $parent -ceq $probe) { throw "$Label has no existing ancestor: $inputPath" }
            $probe = $parent
        }
        $fullPath = ConvertTo-AlsCanonicalExistingPath -Path $probe
        foreach ($segment in $tail) { $fullPath = Join-Path $fullPath $segment }
        $fullPath = [IO.Path]::GetFullPath($fullPath).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    }
    $prefix = $root + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Label must resolve to a repository descendant: path=$fullPath repository=$root"
    }
    $cursor = $fullPath
    while ($cursor.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "$Label traverses a reparse point and cannot be used for publication: $cursor"
            }
        }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if ([string]::IsNullOrEmpty($parent) -or $parent -ceq $cursor) { break }
        $cursor = $parent.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    }
    return $fullPath
}

function Remove-AlsRepositoryDescendantPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Label
    )

    $resolved = Resolve-AlsRepositoryDescendantPath -RepositoryRoot $RepositoryRoot -Path $Path -Label $Label
    if (Test-Path -LiteralPath $resolved) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

function Move-AlsRepositoryDescendantPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][string]$Label
    )

    $sourcePath = Resolve-AlsRepositoryDescendantPath -RepositoryRoot $RepositoryRoot -Path $Source -Label "$Label source"
    $destinationPath = Resolve-AlsRepositoryDescendantPath -RepositoryRoot $RepositoryRoot -Path $Destination -Label "$Label destination"
    if (-not (Test-Path -LiteralPath $sourcePath)) { throw "$Label source does not exist: $sourcePath" }
    if (Test-Path -LiteralPath $destinationPath) { throw "$Label destination already exists: $destinationPath" }
    $parent = Resolve-AlsRepositoryDescendantPath -RepositoryRoot $RepositoryRoot -Path ([IO.Path]::GetDirectoryName($destinationPath)) -Label "$Label destination parent"
    [void][IO.Directory]::CreateDirectory($parent)
    Move-Item -LiteralPath $sourcePath -Destination $destinationPath
}

function Write-AlsP2aPublicationJournal {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$JournalPath,
        [Parameter(Mandatory)][object]$Value
    )

    $path = Resolve-AlsRepositoryDescendantPath -RepositoryRoot $RepositoryRoot -Path $JournalPath -Label 'JournalPath'
    $directory = Resolve-AlsRepositoryDescendantPath -RepositoryRoot $RepositoryRoot -Path ([IO.Path]::GetDirectoryName($path)) -Label 'Journal directory'
    [void][IO.Directory]::CreateDirectory($directory)
    $temporaryPath = Resolve-AlsRepositoryDescendantPath -RepositoryRoot $RepositoryRoot `
        -Path (Join-Path $directory ".$([IO.Path]::GetFileName($path)).$([guid]::NewGuid().ToString('N')).tmp") -Label 'Journal temporary path'
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes("$($Value | ConvertTo-Json -Depth 8)$([Environment]::NewLine)")
    try {
        $stream = [IO.FileStream]::new($temporaryPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
        try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
        if ([IO.File]::Exists($path)) { [IO.File]::Replace($temporaryPath, $path, [Management.Automation.Language.NullString]::Value) }
        else { [IO.File]::Move($temporaryPath, $path) }
    }
    finally {
        if ([IO.File]::Exists($temporaryPath)) { [IO.File]::Delete($temporaryPath) }
    }
}

function Repair-AlsP2aPublication {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$JournalPath
    )

    $root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
    $journalFullPath = Resolve-AlsRepositoryDescendantPath -RepositoryRoot $root -Path $JournalPath -Label 'JournalPath'
    if (-not (Test-Path -LiteralPath $journalFullPath -PathType Leaf)) { return }
    try { $journal = Get-Content -LiteralPath $journalFullPath -Raw | ConvertFrom-Json }
    catch { throw "P2A publication journal is invalid JSON and requires manual recovery: $($_.Exception.Message)" }
    if ([int]$journal.schemaVersion -ne 1 -or [string]$journal.state -cnotin @('prepared', 'committed')) {
        throw 'P2A publication journal has an unsupported schema or state.'
    }
    if ([IO.Path]::GetFullPath([string]$journal.repositoryRoot) -cne [IO.Path]::GetFullPath($root)) {
        throw 'P2A publication journal repositoryRoot does not match the active repository.'
    }
    $paths = @{}
    foreach ($property in @(
        'canonicalRoot', 'candidateRoot', 'determinismRoot', 'canonicalBackupRoot', 'lockPath',
        'lockCandidatePath', 'lockBackupPath', 'journalPath'
    )) {
        $paths[$property] = Resolve-AlsRepositoryDescendantPath -RepositoryRoot $root -Path ([string]$journal.$property) -Label "journal.$property"
    }
    if ($paths.journalPath -cne $journalFullPath) { throw 'P2A publication journal path does not match its location.' }

    if ([string]$journal.state -ceq 'committed') {
        if (-not (Test-Path -LiteralPath $paths.canonicalRoot -PathType Container) -or
            -not (Test-Path -LiteralPath $paths.lockPath -PathType Leaf)) {
            throw 'Committed P2A publication is missing canonical or lock output.'
        }
    }
    else {
        if ([bool]$journal.canonicalOriginalExisted) {
            if (Test-Path -LiteralPath $paths.canonicalBackupRoot -PathType Container) {
                Remove-AlsRepositoryDescendantPath -RepositoryRoot $root -Path $paths.canonicalRoot -Label 'rollback canonical candidate'
                Move-AlsRepositoryDescendantPath -RepositoryRoot $root -Source $paths.canonicalBackupRoot -Destination $paths.canonicalRoot -Label 'restore canonical backup'
            }
            elseif (-not (Test-Path -LiteralPath $paths.canonicalRoot -PathType Container)) {
                throw 'Cannot recover the previous canonical export because both canonical and backup are absent.'
            }
        }
        elseif (-not (Test-Path -LiteralPath $paths.candidateRoot) -and (Test-Path -LiteralPath $paths.canonicalRoot)) {
            Remove-AlsRepositoryDescendantPath -RepositoryRoot $root -Path $paths.canonicalRoot -Label 'rollback newly installed canonical'
        }

        if ([bool]$journal.lockOriginalExisted) {
            if (Test-Path -LiteralPath $paths.lockBackupPath -PathType Leaf) {
                Remove-AlsRepositoryDescendantPath -RepositoryRoot $root -Path $paths.lockPath -Label 'rollback lock candidate'
                Move-AlsRepositoryDescendantPath -RepositoryRoot $root -Source $paths.lockBackupPath -Destination $paths.lockPath -Label 'restore lock backup'
            }
            elseif (-not (Test-Path -LiteralPath $paths.lockPath -PathType Leaf)) {
                throw 'Cannot recover the previous export lock because both lock and backup are absent.'
            }
        }
        elseif (-not (Test-Path -LiteralPath $paths.lockCandidatePath) -and (Test-Path -LiteralPath $paths.lockPath)) {
            Remove-AlsRepositoryDescendantPath -RepositoryRoot $root -Path $paths.lockPath -Label 'rollback newly installed lock'
        }
    }

    foreach ($entry in @(
        @{ Path = $paths.candidateRoot; Label = 'candidate staging' },
        @{ Path = $paths.determinismRoot; Label = 'determinism staging' },
        @{ Path = $paths.lockCandidatePath; Label = 'lock candidate' },
        @{ Path = $paths.canonicalBackupRoot; Label = 'canonical backup residue' },
        @{ Path = $paths.lockBackupPath; Label = 'lock backup residue' }
    )) {
        Remove-AlsRepositoryDescendantPath -RepositoryRoot $root -Path $entry.Path -Label $entry.Label
    }
    Remove-AlsRepositoryDescendantPath -RepositoryRoot $root -Path $journalFullPath -Label 'publication journal'
    Write-Host "P2A_PUBLICATION_RECOVERY_OK state=$($journal.state)"
}

function Invoke-AlsP2aJointPublication {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$GateToken,
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$CanonicalRoot,
        [Parameter(Mandatory)][string]$CandidateRoot,
        [Parameter(Mandatory)][string]$DeterminismRoot,
        [Parameter(Mandatory)][string]$LockPath,
        [Parameter(Mandatory)][string]$LockCandidatePath,
        [Parameter(Mandatory)][string]$CanonicalBackupRoot,
        [Parameter(Mandatory)][string]$LockBackupPath,
        [Parameter(Mandatory)][string]$JournalPath,
        [Parameter(Mandatory)][string]$ComparisonScriptPath,
        [ValidateSet('', 'Comparison', 'CanonicalSwap', 'LockSwap')]
        [string]$FaultInjectionPoint = '',
        [switch]$UpdateAssetLock
    )

    if ($GateToken -cne 'P2A_EXPORT_GATES_COMPLETE') {
        throw 'P2A joint publication requires the completed export gate token.'
    }
    $root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
    $resolved = @{}
    foreach ($entry in @(
        @{ Name = 'CanonicalRoot'; Value = $CanonicalRoot }, @{ Name = 'CandidateRoot'; Value = $CandidateRoot },
        @{ Name = 'DeterminismRoot'; Value = $DeterminismRoot }, @{ Name = 'LockPath'; Value = $LockPath },
        @{ Name = 'LockCandidatePath'; Value = $LockCandidatePath }, @{ Name = 'CanonicalBackupRoot'; Value = $CanonicalBackupRoot },
        @{ Name = 'LockBackupPath'; Value = $LockBackupPath }, @{ Name = 'JournalPath'; Value = $JournalPath }
    )) {
        $resolved[$entry.Name] = Resolve-AlsRepositoryDescendantPath -RepositoryRoot $root -Path $entry.Value -Label $entry.Name
    }
    if (@($resolved.Values | Sort-Object -Unique).Count -ne $resolved.Count) {
        throw 'P2A publication paths must all resolve to distinct repository descendants.'
    }
    if (-not (Test-Path -LiteralPath $ComparisonScriptPath -PathType Leaf)) {
        throw "P2A comparison script does not exist: $ComparisonScriptPath"
    }

    Repair-AlsP2aPublication -RepositoryRoot $root -JournalPath $resolved.JournalPath
    try {
        if ($FaultInjectionPoint -ceq 'Comparison') { throw 'Injected P2A comparison failure.' }
        & $ComparisonScriptPath -ReferenceRoot $resolved.CandidateRoot -CandidateRoot $resolved.DeterminismRoot
        $manifestPath = Join-Path $resolved.CandidateRoot 'als_manifest.json'
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $audit = Assert-AlsP2aPublishManifest -Manifest $manifest -Label 'Canonical candidate'
        Write-Host "P2A_MANIFEST_AUDIT_OK assets=$($audit.AssetCount) files=$($audit.FileCount) animations=$($audit.AnimationCount) sequence_events=$($audit.SequenceEventCount) montage_events=$($audit.MontageEventCount) events=$($audit.EventCount) queued=$($audit.QueuedCount) branching_points=$($audit.BranchingPointCount) sync_markers=$($audit.SyncMarkerCount) terminal_sections=$($audit.TerminalSectionCount) audio=$($audit.AudioAssetCount)"

        if (-not $UpdateAssetLock) {
            Remove-AlsRepositoryDescendantPath -RepositoryRoot $root -Path $resolved.CandidateRoot -Label 'unpublished candidate staging'
            Remove-AlsRepositoryDescendantPath -RepositoryRoot $root -Path $resolved.DeterminismRoot -Label 'unpublished determinism staging'
            return
        }

        Publish-AlsExportLock -ManifestPath $manifestPath -LockPath $resolved.LockCandidatePath -RepositoryRoot $root
        $journal = [ordered]@{
            schemaVersion = 1; state = 'prepared'; repositoryRoot = $root
            canonicalRoot = $resolved.CanonicalRoot; candidateRoot = $resolved.CandidateRoot
            determinismRoot = $resolved.DeterminismRoot; canonicalBackupRoot = $resolved.CanonicalBackupRoot
            lockPath = $resolved.LockPath; lockCandidatePath = $resolved.LockCandidatePath
            lockBackupPath = $resolved.LockBackupPath; journalPath = $resolved.JournalPath
            canonicalOriginalExisted = [bool](Test-Path -LiteralPath $resolved.CanonicalRoot -PathType Container)
            lockOriginalExisted = [bool](Test-Path -LiteralPath $resolved.LockPath -PathType Leaf)
        }
        foreach ($backup in @($resolved.CanonicalBackupRoot, $resolved.LockBackupPath)) {
            if (Test-Path -LiteralPath $backup) { throw "P2A publication has orphaned backup residue without a journal: $backup" }
        }
        Write-AlsP2aPublicationJournal -RepositoryRoot $root -JournalPath $resolved.JournalPath -Value $journal

        if ($journal.canonicalOriginalExisted) {
            Move-AlsRepositoryDescendantPath -RepositoryRoot $root -Source $resolved.CanonicalRoot -Destination $resolved.CanonicalBackupRoot -Label 'backup canonical'
        }
        if ($journal.lockOriginalExisted) {
            Move-AlsRepositoryDescendantPath -RepositoryRoot $root -Source $resolved.LockPath -Destination $resolved.LockBackupPath -Label 'backup lock'
        }
        Move-AlsRepositoryDescendantPath -RepositoryRoot $root -Source $resolved.CandidateRoot -Destination $resolved.CanonicalRoot -Label 'install canonical candidate'
        if ($FaultInjectionPoint -ceq 'CanonicalSwap') { throw 'Injected P2A canonical swap failure.' }
        Move-AlsRepositoryDescendantPath -RepositoryRoot $root -Source $resolved.LockCandidatePath -Destination $resolved.LockPath -Label 'install lock candidate'
        if ($FaultInjectionPoint -ceq 'LockSwap') { throw 'Injected P2A lock swap failure.' }

        $journal.state = 'committed'
        Write-AlsP2aPublicationJournal -RepositoryRoot $root -JournalPath $resolved.JournalPath -Value $journal
        Repair-AlsP2aPublication -RepositoryRoot $root -JournalPath $resolved.JournalPath
        Write-Host 'GODOT_ALS_P2A_JOINT_PUBLISH_OK'
    }
    catch {
        $failure = $_
        try {
            if (Test-Path -LiteralPath $resolved.JournalPath -PathType Leaf) {
                Repair-AlsP2aPublication -RepositoryRoot $root -JournalPath $resolved.JournalPath
            }
            else {
                foreach ($entry in @(
                    @{ Path = $resolved.CandidateRoot; Label = 'failed candidate staging' },
                    @{ Path = $resolved.DeterminismRoot; Label = 'failed determinism staging' },
                    @{ Path = $resolved.LockCandidatePath; Label = 'failed lock candidate' }
                )) {
                    Remove-AlsRepositoryDescendantPath -RepositoryRoot $root -Path $entry.Path -Label $entry.Label
                }
            }
        }
        catch {
            throw "P2A publication failed ('$($failure.Exception.Message)') and recovery also failed: $($_.Exception.Message)"
        }
        throw $failure
    }
}

function Assert-AlsExportLock {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ManifestPath, [Parameter(Mandatory)][string]$AssetRoot, [Parameter(Mandatory)][string]$LockPath)

    $lock = Read-AlsExportLock -LockPath $LockPath
    $actualManifestHash = (Get-FileHash -LiteralPath $ManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualManifestHash -cne $lock.ManifestSha256) { throw "Formal ALS manifest SHA-256 mismatch: expected=$($lock.ManifestSha256) actual=$actualManifestHash" }
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    if ([int]$manifest.auditSummary.assetCount -ne $lock.AssetCount -or @($manifest.files).Count -ne $lock.FileCount -or
        @($manifest.animations).Count -ne $lock.AnimationCount -or [string]$manifest.exporterVersion -cne $lock.ExporterVersion -or
        [string]$manifest.sourceProjectId -cne $lock.SourceProjectId) { throw 'Formal ALS manifest counts or producer identity differ from tracked lock.' }
    $assetRootFullPath = [IO.Path]::GetFullPath($AssetRoot)
    if (-not (Test-Path -LiteralPath $assetRootFullPath -PathType Container)) { throw "ALS asset root does not exist: $assetRootFullPath" }
    $canonicalRoot = $assetRootFullPath.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $canonicalRelativePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $files = @($manifest.files)
    for ($index = 0; $index -lt $files.Count; $index++) {
        $file = $files[$index]
        $relativePath = [string]$file.relativePath
        $fieldPath = "files[$index].relativePath"
        if ([string]::IsNullOrWhiteSpace($relativePath)) { throw "$fieldPath must be a non-empty relative file path." }
        if ([IO.Path]::IsPathRooted($relativePath)) { throw "$fieldPath must be relative, not rooted or absolute: $relativePath" }
        $segments = @($relativePath -split '[\\/]')
        if ($segments | Where-Object { $_ -ceq '.' -or $_ -ceq '..' }) {
            throw "$fieldPath contains a forbidden '.' or '..' path segment: $relativePath"
        }
        try { $path = [IO.Path]::GetFullPath([IO.Path]::Combine($assetRootFullPath, $relativePath)) }
        catch { throw "$fieldPath is not a valid relative path '$relativePath': $($_.Exception.Message)" }
        if (-not $path.StartsWith($canonicalRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "$fieldPath escapes outside the ALS asset root: $relativePath"
        }
        $canonicalRelativePath = ([IO.Path]::GetRelativePath($assetRootFullPath, $path)).Replace([IO.Path]::DirectorySeparatorChar, '/')
        if (-not $canonicalRelativePaths.Add($canonicalRelativePath)) {
            throw "$fieldPath duplicates a normalized manifest file path: $relativePath"
        }
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "$fieldPath target must resolve to a file: $relativePath" }
        $info = Get-Item -LiteralPath $path
        if ($info.Length -ne [long]$file.size) { throw "Locked ALS export file size mismatch at ${fieldPath}: $relativePath" }
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -cne [string]$file.sha256) { throw "Locked ALS export file SHA-256 mismatch at ${fieldPath}: $relativePath" }
    }
    return $lock
}

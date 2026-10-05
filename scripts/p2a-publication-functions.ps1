function Get-AlsP2aSha1 {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Value)

    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    return [Convert]::ToHexString([Security.Cryptography.SHA1]::HashData($bytes)).ToLowerInvariant()
}

function Assert-AlsP2aNoDuplicateJsonProperties {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][Text.Json.JsonElement]$Element,
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Label
    )

    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            if (-not $seen.Add($property.Name)) {
                throw "$Label manifest JSON contains duplicate property '$($property.Name)' at $Path."
            }
            Assert-AlsP2aNoDuplicateJsonProperties -Element $property.Value `
                -Path "$Path.$($property.Name)" -Label $Label
        }
    }
    elseif ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
        $index = 0
        foreach ($item in $Element.EnumerateArray()) {
            Assert-AlsP2aNoDuplicateJsonProperties -Element $item -Path "$Path[$index]" -Label $Label
            $index++
        }
    }
}

function Read-AlsP2aManifestJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ManifestPath,
        [Parameter(Mandatory)][string]$Label
    )

    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        throw "$Label manifest does not exist: $ManifestPath"
    }
    $raw = [IO.File]::ReadAllText($ManifestPath)
    $document = $null
    try {
        try { $document = [Text.Json.JsonDocument]::Parse($raw) }
        catch { throw "$Label manifest is not valid JSON: $($_.Exception.Message)" }
        Assert-AlsP2aNoDuplicateJsonProperties -Element $document.RootElement -Path '$' -Label $Label
        return ($raw | ConvertFrom-Json)
    }
    finally {
        if ($null -ne $document) { $document.Dispose() }
    }
}

function Assert-AlsP2aManifestFiles {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Manifest,
        [Parameter(Mandatory)][string]$AssetRoot,
        [string]$Label = 'Export'
    )

    $assetRootFullPath = [IO.Path]::GetFullPath($AssetRoot)
    if (-not (Test-Path -LiteralPath $assetRootFullPath -PathType Container)) {
        throw "$Label asset root does not exist: $assetRootFullPath"
    }
    $canonicalRoot = $assetRootFullPath.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    $declaredPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $files = @($Manifest.files)
    for ($index = 0; $index -lt $files.Count; $index++) {
        $file = $files[$index]
        $relativePath = [string]$file.relativePath
        $fieldPath = "$Label files[$index].relativePath"
        if ([string]::IsNullOrWhiteSpace($relativePath) -or $relativePath.Contains('\') -or
            [IO.Path]::IsPathRooted($relativePath)) {
            throw "$fieldPath must be a canonical relative path: $relativePath"
        }
        $segments = @($relativePath -split '/')
        if ($segments | Where-Object { $_ -in @('', '.', '..') }) {
            throw "$fieldPath contains an empty, '.' or '..' path segment: $relativePath"
        }
        try { $path = [IO.Path]::GetFullPath([IO.Path]::Combine($assetRootFullPath, $relativePath)) }
        catch { throw "$fieldPath is invalid: $($_.Exception.Message)" }
        if (-not $path.StartsWith($canonicalRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw "$fieldPath escapes outside the asset root: $relativePath"
        }
        if (-not $declaredPaths.Add($relativePath)) {
            throw "$fieldPath duplicates a case-insensitive file path: $relativePath"
        }
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "$fieldPath target does not exist as a file: $relativePath"
        }
        $info = Get-Item -LiteralPath $path
        if ([long]$file.size -le 0 -or $info.Length -ne [long]$file.size) {
            throw "$Label file size does not match the manifest: $relativePath"
        }
        if ([string]$file.sha256 -cnotmatch '^[0-9a-f]{64}$') {
            throw "$Label files[$index].sha256 is not a lowercase SHA-256: $relativePath"
        }
        $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -cne [string]$file.sha256) {
            throw "$Label file SHA-256 does not match the manifest: $relativePath"
        }
    }

    foreach ($collection in @(
        'skeletons', 'skeletalMeshes', 'staticMeshes', 'animations', 'montages', 'blendSpaces',
        'aimOffsets', 'materials', 'textures', 'physicsAssets', 'curves', 'configAssets'
    )) {
        foreach ($asset in @($Manifest.$collection)) {
            if ($null -ne $asset.outputPath -and -not $declaredPaths.Contains([string]$asset.outputPath)) {
                throw "$Label $collection output is missing from files[]: $($asset.outputPath)"
            }
        }
    }
}

function Assert-AlsP2aExactObjectProperties {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Expected,
        [AllowNull()][object]$Value,
        [Parameter(Mandatory)][string]$Path
    )

    if ($null -eq $Value -or $Value -is [string] -or $Value -is [array] -or $Value -is [ValueType]) {
        throw "$Path must be an object."
    }
    $actual = @($Value.PSObject.Properties | ForEach-Object { $_.Name })
    $expectedSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in $Expected) { [void]$expectedSet.Add($name) }
    foreach ($name in $actual) {
        if (-not $expectedSet.Contains($name)) { throw "$Path contains unknown property '$name'." }
    }
    $actualSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in $actual) { [void]$actualSet.Add($name) }
    foreach ($name in $Expected) {
        if (-not $actualSet.Contains($name)) { throw "$Path is missing required property '$name'." }
    }
    if ($actual.Count -ne $Expected.Count) { throw "$Path contains duplicate or unexpected properties." }
}

function Assert-AlsP2aNonEmptyString {
    [CmdletBinding()]
    param([AllowNull()][object]$Value, [Parameter(Mandatory)][string]$Path)

    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value)) {
        throw "$Path must be a non-empty string."
    }
}

function Assert-AlsP2aFiniteNumber {
    [CmdletBinding()]
    param(
        [AllowNull()][object]$Value,
        [Parameter(Mandatory)][string]$Path,
        [switch]$Nonnegative,
        [double]$Maximum = [single]::MaxValue
    )

    $numericTypes = @(
        [byte], [sbyte], [int16], [uint16], [int32], [uint32], [int64], [uint64], [single], [double], [decimal]
    )
    $isNumber = $false
    foreach ($type in $numericTypes) {
        if ($Value -is $type) { $isNumber = $true; break }
    }
    if (-not $isNumber) { throw "$Path must be a number." }
    $number = [double]$Value
    if (-not [double]::IsFinite($number) -or [Math]::Abs($number) -gt [single]::MaxValue) {
        throw "$Path must be a finite float-representable number."
    }
    if ($Nonnegative -and $number -lt 0.0) { throw "$Path must be nonnegative." }
    if ($number -gt $Maximum) { throw "$Path must be at most $Maximum." }
    return $number
}

function Assert-AlsP2aNonnegativeInteger {
    [CmdletBinding()]
    param([AllowNull()][object]$Value, [Parameter(Mandatory)][string]$Path)

    $integerTypes = @([byte], [sbyte], [int16], [uint16], [int32], [uint32], [int64], [uint64])
    $isInteger = $false
    foreach ($type in $integerTypes) {
        if ($Value -is $type) { $isInteger = $true; break }
    }
    if (-not $isInteger -or [decimal]$Value -lt 0) { throw "$Path must be a nonnegative integer." }
    if ([decimal]$Value -gt [int32]::MaxValue) {
        throw "$Path must be in the Int32 range 0..2147483647."
    }
    return [int32]$Value
}

function Assert-AlsP2aTimelinePayload {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Kind,
        [AllowNull()][object]$Payload,
        [Parameter(Mandatory)][string]$Path
    )

    switch -CaseSensitive ($Kind) {
        'Generic' {
            Assert-AlsP2aExactObjectProperties -Expected @() -Value $Payload -Path $Path
        }
        'Footstep' {
            Assert-AlsP2aExactObjectProperties -Expected @('foot') -Value $Payload -Path $Path
            if ([string]$Payload.foot -cnotin @('Unspecified', 'Left', 'Right')) {
                throw "$Path.foot must be one of Unspecified, Left, or Right."
            }
        }
        'SetAction' {
            Assert-AlsP2aExactObjectProperties -Expected @('action') -Value $Payload -Path $Path
            if ([string]$Payload.action -cnotin @('None', 'Rolling', 'Mantling', 'Ragdolling', 'GettingUp')) {
                throw "$Path.action has an invalid frozen action value."
            }
        }
        'SetGroundedEntry' {
            Assert-AlsP2aExactObjectProperties -Expected @('mode') -Value $Payload -Path $Path
            if ([string]$Payload.mode -cnotin @('None', 'FromRoll')) {
                throw "$Path.mode has an invalid frozen grounded-entry value."
            }
        }
        'EarlyBlendOut' {
            $required = @(
                'blendOutSeconds', 'checkInput', 'checkLocomotionMode', 'locomotionMode',
                'checkRotationMode', 'rotationMode', 'checkStance', 'stance'
            )
            Assert-AlsP2aExactObjectProperties -Expected $required -Value $Payload -Path $Path
            [void](Assert-AlsP2aFiniteNumber -Value $Payload.blendOutSeconds -Path "$Path.blendOutSeconds" -Nonnegative)
            foreach ($field in @('checkInput', 'checkLocomotionMode', 'checkRotationMode', 'checkStance')) {
                if ($Payload.$field -isnot [bool]) { throw "$Path.$field must be boolean." }
            }
            if ([string]$Payload.locomotionMode -cnotin @('Grounded', 'InAir', 'Mantling', 'Ragdoll', 'Recovering')) {
                throw "$Path.locomotionMode has an invalid frozen value."
            }
            if ([string]$Payload.rotationMode -cnotin @('VelocityDirection', 'LookingDirection', 'Aiming')) {
                throw "$Path.rotationMode has an invalid frozen value."
            }
            if ([string]$Payload.stance -cnotin @('Standing', 'Crouching')) {
                throw "$Path.stance has an invalid frozen value."
            }
        }
        'RootMotionScale' {
            Assert-AlsP2aExactObjectProperties -Expected @('translationScale') -Value $Payload -Path $Path
            [void](Assert-AlsP2aFiniteNumber -Value $Payload.translationScale -Path "$Path.translationScale" -Nonnegative)
        }
        default { throw "$Path has unknown timeline kind '$Kind'." }
    }
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
    if ($null -eq $Manifest.PSObject.Properties['exporterVersion'] -or
        [string]::IsNullOrWhiteSpace([string]$Manifest.exporterVersion)) {
        throw "$Label manifest exporterVersion must be present as informational metadata."
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
        $assetCount -ne $allAssets.Count -or $fileCount -ne [int]$Manifest.auditSummary.fileCount) {
        throw "$Label manifest inventory summary does not match its contents: assets=$assetCount actualAssets=$($allAssets.Count) files=$fileCount summaryFiles=$($Manifest.auditSummary.fileCount)."
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
        for ($assetIndex = 0; $assetIndex -lt $assets.Count; $assetIndex++) {
            $asset = $assets[$assetIndex]
            $assetPath = if ($kind -ceq 'Sequence') { "animations[$assetIndex]" } else { "montages[$assetIndex]" }
            if ($null -eq $asset.PSObject.Properties['metadata'] -or
                $null -eq $asset.metadata.PSObject.Properties['timeline'] -or
                $null -eq $asset.metadata.timeline) {
                throw "$Label $kind '$($asset.objectPath)' is missing its timeline array."
            }
            if ($asset.metadata.timeline -isnot [array]) {
                throw "$Label $assetPath.metadata.timeline must be an array."
            }
            if ($null -eq $asset.metadata.PSObject.Properties['playLength']) {
                throw "$Label $assetPath.metadata is missing required property 'playLength'."
            }
            $playLength = Assert-AlsP2aFiniteNumber -Value $asset.metadata.playLength `
                -Path "$Label $assetPath.metadata.playLength" -Nonnegative
            $assetId = [string]$asset.id
            if ($assetId -cnotmatch '^[0-9a-f]{40}$') {
                throw "$Label $kind '$($asset.objectPath)' has an invalid stable asset ID."
            }
            $eventSourceIndices = [Collections.Generic.HashSet[int64]]::new()
            $timeline = @($asset.metadata.timeline)
            for ($eventIndex = 0; $eventIndex -lt $timeline.Count; $eventIndex++) {
                $event = $timeline[$eventIndex]
                $eventPath = "$Label $assetPath.metadata.timeline[$eventIndex]"
                Assert-AlsP2aExactObjectProperties -Expected @(
                    'stableEventId', 'kind', 'sourceClassPath', 'displayName', 'timeSeconds', 'durationSeconds',
                    'triggerWeightThreshold', 'tickMode', 'sourceIndex', 'trackIndex', 'payload'
                ) -Value $event -Path $eventPath
                $stableEventId = [string]$event.stableEventId
                if ($stableEventId -cnotmatch '^[0-9a-f]{40}$') {
                    throw "$eventPath.stableEventId must be a lowercase SHA-1."
                }
                if (-not $eventIds.Add($stableEventId)) {
                    throw "$Label manifest contains a duplicate stable event ID: $stableEventId."
                }
                Assert-AlsP2aNonEmptyString -Value $event.kind -Path "$eventPath.kind"
                Assert-AlsP2aNonEmptyString -Value $event.sourceClassPath -Path "$eventPath.sourceClassPath"
                Assert-AlsP2aNonEmptyString -Value $event.displayName -Path "$eventPath.displayName"
                $sourceClassPath = [string]$event.sourceClassPath
                $sourceIndex = Assert-AlsP2aNonnegativeInteger -Value $event.sourceIndex -Path "$eventPath.sourceIndex"
                [void](Assert-AlsP2aNonnegativeInteger -Value $event.trackIndex -Path "$eventPath.trackIndex")
                if (-not $eventSourceIndices.Add($sourceIndex)) {
                    throw "$eventPath.sourceIndex is duplicated within the asset: $sourceIndex."
                }
                $timeSeconds = Assert-AlsP2aFiniteNumber -Value $event.timeSeconds -Path "$eventPath.timeSeconds" -Nonnegative
                $durationSeconds = Assert-AlsP2aFiniteNumber -Value $event.durationSeconds -Path "$eventPath.durationSeconds" -Nonnegative
                [void](Assert-AlsP2aFiniteNumber -Value $event.triggerWeightThreshold `
                    -Path "$eventPath.triggerWeightThreshold" -Nonnegative -Maximum 1.0)
                if ($timeSeconds -gt $playLength) { throw "$eventPath.timeSeconds exceeds playLength." }
                if ($durationSeconds -gt ($playLength - $timeSeconds)) { throw "$eventPath.durationSeconds exceeds playLength." }
                Assert-AlsP2aTimelinePayload -Kind ([string]$event.kind) -Payload $event.payload -Path "$eventPath.payload"
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
                if ($asset.metadata.syncMarkers -isnot [array]) {
                    throw "$Label $assetPath.metadata.syncMarkers must be an array."
                }
                $markerSourceIndices = [Collections.Generic.HashSet[int64]]::new()
                $markers = @($asset.metadata.syncMarkers)
                for ($markerIndex = 0; $markerIndex -lt $markers.Count; $markerIndex++) {
                    $marker = $markers[$markerIndex]
                    $markerPath = "$Label $assetPath.metadata.syncMarkers[$markerIndex]"
                    Assert-AlsP2aExactObjectProperties -Expected @(
                        'stableMarkerId', 'name', 'timeSeconds', 'sourceIndex', 'trackIndex'
                    ) -Value $marker -Path $markerPath
                    $stableMarkerId = [string]$marker.stableMarkerId
                    if ($stableMarkerId -cnotmatch '^[0-9a-f]{40}$') {
                        throw "$markerPath.stableMarkerId must be a lowercase SHA-1."
                    }
                    if (-not $markerIds.Add($stableMarkerId)) {
                        throw "$Label manifest contains a duplicate stable marker ID: $stableMarkerId."
                    }
                    Assert-AlsP2aNonEmptyString -Value $marker.name -Path "$markerPath.name"
                    $sourceIndex = Assert-AlsP2aNonnegativeInteger -Value $marker.sourceIndex -Path "$markerPath.sourceIndex"
                    [void](Assert-AlsP2aNonnegativeInteger -Value $marker.trackIndex -Path "$markerPath.trackIndex")
                    if (-not $markerSourceIndices.Add($sourceIndex)) {
                        throw "$markerPath.sourceIndex is duplicated within the asset: $sourceIndex."
                    }
                    $markerTime = Assert-AlsP2aFiniteNumber -Value $marker.timeSeconds -Path "$markerPath.timeSeconds" -Nonnegative
                    if ($markerTime -gt $playLength) { throw "$markerPath.timeSeconds exceeds playLength." }
                    $name = [string]$marker.name
                    $expectedMarkerId = Get-AlsP2aSha1 "$assetId|marker|$sourceIndex|$name"
                    if ($stableMarkerId -cne $expectedMarkerId) {
                        throw "$Label stable marker ID does not match the stable formula for '$($asset.objectPath)' sourceIndex=$sourceIndex."
                    }
                    $syncMarkerCount++
                }
            }
            else {
                if ($null -ne $asset.metadata.PSObject.Properties['syncMarkers']) {
                    throw "$Label $assetPath.metadata contains Sequence-only property 'syncMarkers'."
                }
                if ($null -eq $asset.metadata.PSObject.Properties['sections'] -or $null -eq $asset.metadata.sections) {
                    throw "$Label $assetPath.metadata is missing required property 'sections'."
                }
                if ($asset.metadata.sections -isnot [array]) {
                    throw "$Label $assetPath.metadata.sections must be an array."
                }
                $sectionNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                $sections = @($asset.metadata.sections)
                $previousStart = -1.0
                for ($sectionIndex = 0; $sectionIndex -lt $sections.Count; $sectionIndex++) {
                    $section = $sections[$sectionIndex]
                    $sectionPath = "$Label $assetPath.metadata.sections[$sectionIndex]"
                    Assert-AlsP2aExactObjectProperties -Expected @('name', 'nextSection', 'startTime') `
                        -Value $section -Path $sectionPath
                    Assert-AlsP2aNonEmptyString -Value $section.name -Path "$sectionPath.name"
                    if ($section.nextSection -isnot [string]) { throw "$sectionPath.nextSection must be a string." }
                    if ([string]$section.nextSection -ceq 'None') { throw "$sectionPath.nextSection forbids literal None." }
                    if (-not $sectionNames.Add([string]$section.name)) { throw "$sectionPath has a duplicate section name." }
                    $startTime = Assert-AlsP2aFiniteNumber -Value $section.startTime -Path "$sectionPath.startTime" -Nonnegative
                    if ($startTime -gt $playLength) { throw "$sectionPath.startTime exceeds playLength." }
                    if ($sectionIndex -gt 0 -and $startTime -le $previousStart) {
                        throw "$sectionPath.startTime must be strictly increasing."
                    }
                    $previousStart = $startTime
                    if ([string]$section.nextSection -ceq '') { $terminalSectionCount++ }
                }
                for ($sectionIndex = 0; $sectionIndex -lt $sections.Count; $sectionIndex++) {
                    $nextSection = [string]$sections[$sectionIndex].nextSection
                    if ($nextSection -cne '' -and -not $sectionNames.Contains($nextSection)) {
                        throw "$Label $assetPath.metadata.sections[$sectionIndex].nextSection references missing section '$nextSection'."
                    }
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
        SchemaVersion = 2; ExporterVersion = [string]$Manifest.exporterVersion; AssetCount = $assetCount; FileCount = $fileCount
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

function Test-AlsP2aPathEqual {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Left, [Parameter(Mandatory)][string]$Right)

    $comparison = if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        [StringComparison]::OrdinalIgnoreCase
    }
    else { [StringComparison]::Ordinal }
    return $Left.Equals($Right, $comparison)
}

function Test-AlsP2aPathAncestor {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Ancestor, [Parameter(Mandatory)][string]$Descendant)

    $comparison = if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        [StringComparison]::OrdinalIgnoreCase
    }
    else { [StringComparison]::Ordinal }
    $prefix = $Ancestor.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    return $Descendant.StartsWith($prefix, $comparison)
}

function Assert-AlsP2aCanonicalPublicationRoot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$CanonicalRoot
    )

    $expected = Resolve-AlsRepositoryDescendantPath -RepositoryRoot $RepositoryRoot `
        -Path (Join-Path $RepositoryRoot 'assets\generated\als_v4') -Label 'ExpectedCanonicalRoot'
    $actual = Resolve-AlsRepositoryDescendantPath -RepositoryRoot $RepositoryRoot `
        -Path $CanonicalRoot -Label 'CanonicalRoot'
    if (-not (Test-AlsP2aPathEqual $actual $expected)) {
        throw "P2A canonical publication root must be exactly '$expected'; received '$actual'."
    }
    return $actual
}

function Assert-AlsP2aPublicationPathTopology {
    [CmdletBinding()]
    param([Parameter(Mandatory)][hashtable]$Paths)

    $entries = @($Paths.GetEnumerator())
    for ($leftIndex = 0; $leftIndex -lt $entries.Count; $leftIndex++) {
        for ($rightIndex = $leftIndex + 1; $rightIndex -lt $entries.Count; $rightIndex++) {
            $left = [string]$entries[$leftIndex].Value
            $right = [string]$entries[$rightIndex].Value
            if ((Test-AlsP2aPathEqual $left $right) -or (Test-AlsP2aPathAncestor $left $right) -or
                (Test-AlsP2aPathAncestor $right $left)) {
                throw "P2A publication paths must be distinct and must not have ancestor/descendant overlap: $($entries[$leftIndex].Key)='$left', $($entries[$rightIndex].Key)='$right'."
            }
        }
    }
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
        'canonicalRoot', 'candidateRoot', 'determinismRoot', 'canonicalBackupRoot', 'journalPath'
    )) {
        $paths[$property] = Resolve-AlsRepositoryDescendantPath -RepositoryRoot $root -Path ([string]$journal.$property) -Label "journal.$property"
    }
    if ($paths.journalPath -cne $journalFullPath) { throw 'P2A publication journal path does not match its location.' }
    [void](Assert-AlsP2aCanonicalPublicationRoot -RepositoryRoot $root -CanonicalRoot $paths.canonicalRoot)
    Assert-AlsP2aPublicationPathTopology -Paths $paths

    if ([string]$journal.state -ceq 'committed') {
        if (-not (Test-Path -LiteralPath $paths.canonicalRoot -PathType Container)) {
            throw 'Committed P2A publication is missing the canonical export.'
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
    }

    foreach ($entry in @(
        @{ Path = $paths.candidateRoot; Label = 'candidate staging' },
        @{ Path = $paths.determinismRoot; Label = 'determinism staging' },
        @{ Path = $paths.canonicalBackupRoot; Label = 'canonical backup residue' }
    )) {
        Remove-AlsRepositoryDescendantPath -RepositoryRoot $root -Path $entry.Path -Label $entry.Label
    }
    Remove-AlsRepositoryDescendantPath -RepositoryRoot $root -Path $journalFullPath -Label 'publication journal'
    Write-Host "P2A_PUBLICATION_RECOVERY_OK state=$($journal.state)"
}

function Invoke-AlsP2aStagingWorkflow {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$CandidateRoot,
        [Parameter(Mandatory)][string]$DeterminismRoot,
        [Parameter(Mandatory)][scriptblock]$Action
    )

    $root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
    $staging = @{
        CandidateRoot = Resolve-AlsRepositoryDescendantPath -RepositoryRoot $root -Path $CandidateRoot -Label 'CandidateRoot'
        DeterminismRoot = Resolve-AlsRepositoryDescendantPath -RepositoryRoot $root -Path $DeterminismRoot -Label 'DeterminismRoot'
    }
    Assert-AlsP2aPublicationPathTopology -Paths $staging
    try {
        & $Action
    }
    catch {
        $failure = $_
        try {
            foreach ($entry in @(
                @{ Path = $staging.CandidateRoot; Label = 'failed workflow candidate staging' },
                @{ Path = $staging.DeterminismRoot; Label = 'failed workflow determinism staging' }
            )) {
                Remove-AlsRepositoryDescendantPath -RepositoryRoot $root -Path $entry.Path -Label $entry.Label
            }
        }
        catch {
            throw "P2A export workflow failed ('$($failure.Exception.Message)') and staging cleanup also failed: $($_.Exception.Message)"
        }
        throw $failure
    }
}

function Invoke-AlsP2aPublication {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$GateToken,
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$CanonicalRoot,
        [Parameter(Mandatory)][string]$CandidateRoot,
        [Parameter(Mandatory)][string]$DeterminismRoot,
        [Parameter(Mandatory)][string]$CanonicalBackupRoot,
        [Parameter(Mandatory)][string]$JournalPath,
        [Parameter(Mandatory)][string]$ComparisonScriptPath,
        [ValidateSet('', 'Comparison', 'CanonicalSwap')]
        [string]$FaultInjectionPoint = ''
    )

    if ($GateToken -cne 'P2A_EXPORT_GATES_COMPLETE') {
        throw 'P2A joint publication requires the completed export gate token.'
    }
    $root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
    $resolved = @{}
    foreach ($entry in @(
        @{ Name = 'CanonicalRoot'; Value = $CanonicalRoot }, @{ Name = 'CandidateRoot'; Value = $CandidateRoot },
        @{ Name = 'DeterminismRoot'; Value = $DeterminismRoot },
        @{ Name = 'CanonicalBackupRoot'; Value = $CanonicalBackupRoot }, @{ Name = 'JournalPath'; Value = $JournalPath }
    )) {
        $resolved[$entry.Name] = Resolve-AlsRepositoryDescendantPath -RepositoryRoot $root -Path $entry.Value -Label $entry.Name
    }
    [void](Assert-AlsP2aCanonicalPublicationRoot -RepositoryRoot $root -CanonicalRoot $resolved.CanonicalRoot)
    Assert-AlsP2aPublicationPathTopology -Paths $resolved
    $comparisonScript = [IO.Path]::GetFullPath($ComparisonScriptPath)
    if (-not (Test-Path -LiteralPath $comparisonScript -PathType Leaf)) {
        throw "P2A comparison script does not exist: $comparisonScript"
    }

    Repair-AlsP2aPublication -RepositoryRoot $root -JournalPath $resolved.JournalPath
    try {
        if ($FaultInjectionPoint -ceq 'Comparison') { throw 'Injected P2A comparison failure.' }
        & $comparisonScript -ReferenceRoot $resolved.CandidateRoot -CandidateRoot $resolved.DeterminismRoot
        $manifestPath = Join-Path $resolved.CandidateRoot 'als_manifest.json'
        $manifest = Read-AlsP2aManifestJson -ManifestPath $manifestPath -Label 'Canonical candidate'
        $audit = Assert-AlsP2aPublishManifest -Manifest $manifest -Label 'Canonical candidate'
        Write-Host "P2A_MANIFEST_AUDIT_OK assets=$($audit.AssetCount) files=$($audit.FileCount) animations=$($audit.AnimationCount) sequence_events=$($audit.SequenceEventCount) montage_events=$($audit.MontageEventCount) events=$($audit.EventCount) queued=$($audit.QueuedCount) branching_points=$($audit.BranchingPointCount) sync_markers=$($audit.SyncMarkerCount) terminal_sections=$($audit.TerminalSectionCount) audio=$($audit.AudioAssetCount)"

        $journal = [ordered]@{
            schemaVersion = 1; state = 'prepared'; repositoryRoot = $root
            canonicalRoot = $resolved.CanonicalRoot; candidateRoot = $resolved.CandidateRoot
            determinismRoot = $resolved.DeterminismRoot; canonicalBackupRoot = $resolved.CanonicalBackupRoot
            journalPath = $resolved.JournalPath
            canonicalOriginalExisted = [bool](Test-Path -LiteralPath $resolved.CanonicalRoot -PathType Container)
        }
        foreach ($backup in @($resolved.CanonicalBackupRoot)) {
            if (Test-Path -LiteralPath $backup) { throw "P2A publication has orphaned backup residue without a journal: $backup" }
        }
        Write-AlsP2aPublicationJournal -RepositoryRoot $root -JournalPath $resolved.JournalPath -Value $journal

        if ($journal.canonicalOriginalExisted) {
            Move-AlsRepositoryDescendantPath -RepositoryRoot $root -Source $resolved.CanonicalRoot -Destination $resolved.CanonicalBackupRoot -Label 'backup canonical'
        }
        Move-AlsRepositoryDescendantPath -RepositoryRoot $root -Source $resolved.CandidateRoot -Destination $resolved.CanonicalRoot -Label 'install canonical candidate'
        if ($FaultInjectionPoint -ceq 'CanonicalSwap') { throw 'Injected P2A canonical swap failure.' }

        $journal.state = 'committed'
        Write-AlsP2aPublicationJournal -RepositoryRoot $root -JournalPath $resolved.JournalPath -Value $journal
        Repair-AlsP2aPublication -RepositoryRoot $root -JournalPath $resolved.JournalPath
        Write-Host 'GODOT_ALS_P2A_PUBLICATION_OK'
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
                    @{ Path = $resolved.DeterminismRoot; Label = 'failed determinism staging' }
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

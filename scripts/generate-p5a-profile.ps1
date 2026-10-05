param(
    [string]$ManifestPath = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..')).Path 'assets\generated\als_v4\als_manifest.json'),
    [string]$OutputPath = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..')).Path 'assets\config\p5a_animation_runtime.json')
)

$ErrorActionPreference = 'Stop'
$manifestFullPath = [IO.Path]::GetFullPath($ManifestPath)
$outputFullPath = [IO.Path]::GetFullPath($OutputPath)
$pathComparison = if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
    [StringComparison]::OrdinalIgnoreCase
}
else { [StringComparison]::Ordinal }
if ([string]::Equals($manifestFullPath, $outputFullPath, $pathComparison)) {
    throw 'Manifest and output paths must not identify the same file.'
}
if (-not [IO.File]::Exists($manifestFullPath)) { throw "Manifest does not exist: $manifestFullPath" }

function Assert-P5aNoDuplicateJsonProperties {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][Text.Json.JsonElement]$Element,
        [Parameter(Mandatory)][string]$Path
    )

    if ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            if (-not $seen.Add($property.Name)) {
                throw "Canonical manifest JSON contains duplicate property '$($property.Name)' at $Path."
            }
            Assert-P5aNoDuplicateJsonProperties -Element $property.Value -Path "$Path.$($property.Name)"
        }
    }
    elseif ($Element.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
        $index = 0
        foreach ($item in $Element.EnumerateArray()) {
            Assert-P5aNoDuplicateJsonProperties -Element $item -Path "$Path[$index]"
            $index++
        }
    }
}

function Read-P5aManifestJson([string]$Path) {
    $raw = [IO.File]::ReadAllText($Path)
    $document = $null
    try {
        try { $document = [Text.Json.JsonDocument]::Parse($raw) }
        catch { throw "Canonical manifest is not valid JSON: $($_.Exception.Message)" }
        Assert-P5aNoDuplicateJsonProperties -Element $document.RootElement -Path '$'
        return ($raw | ConvertFrom-Json)
    }
    finally {
        if ($null -ne $document) { $document.Dispose() }
    }
}

$manifest = Read-P5aManifestJson $manifestFullPath
if ($manifest.schemaVersion -isnot [long] -or $manifest.schemaVersion -ne 2) {
    throw 'Canonical manifest schemaVersion must be integer 2.'
}
if ($manifest.exporterVersion -isnot [string] -or [string]::IsNullOrWhiteSpace($manifest.exporterVersion)) {
    throw 'Canonical manifest exporterVersion metadata is missing.'
}
$sourceContentRoot = [string]$manifest.sourceContentRoot
$invalidContentRootSegment = @($sourceContentRoot.Substring([Math]::Min(6, $sourceContentRoot.Length)).Split('/') |
    Where-Object { $_ -in @('', '.', '..') }).Count -gt 0
if ($sourceContentRoot -cnotmatch '^/Game/[^/]+(?:/[^/]+)*$' -or
    $sourceContentRoot.Contains('\') -or $invalidContentRootSegment) {
    throw "Canonical manifest sourceContentRoot must be a canonical Unreal content path: $sourceContentRoot"
}

function Resolve-ExactManifestAsset([object]$Manifest, [string]$Collection, [string]$ObjectPath) {
    $assets = @($Manifest.$Collection | Where-Object { $_.objectPath -ceq $ObjectPath })
    if ($assets.Count -eq 0) { throw "Source map has zero exact object path matches in ${Collection}: $ObjectPath" }
    if ($assets.Count -ne 1) { throw "Source map has multiple exact object path matches in ${Collection}: $ObjectPath" }
    $id = [string]$assets[0].id
    if ($id -cnotmatch '^[0-9a-f]{40}$') {
        throw "Selected asset ID must be lowercase 40-hex for object path: $ObjectPath"
    }
    $sha1 = [Security.Cryptography.SHA1]::Create()
    try {
        $expectedId = [Convert]::ToHexString(
            $sha1.ComputeHash([Text.Encoding]::UTF8.GetBytes($ObjectPath))).ToLowerInvariant()
    }
    finally { $sha1.Dispose() }
    if ($id -cne $expectedId) {
        throw "Selected asset stable ID does not match its exact object path: $ObjectPath"
    }
    return $assets[0]
}

function Resolve-Id([string]$Collection, [string]$ObjectPath) {
    return [string](Resolve-ExactManifestAsset $manifest $Collection $ObjectPath).id
}

$locomotionRoot = "$sourceContentRoot/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion"
$memberNames = @(
    'ALS_CLF_Walk_L', 'ALS_N_Run_RB', 'ALS_N_Walk_B', 'ALS_N_Walk_LF', 'ALS_N_Run_F',
    'ALS_N_Walk_F', 'ALS_N_Run_B', 'ALS_N_Run_LF', 'ALS_N_Sprint_F', 'ALS_CLF_Walk_F',
    'ALS_N_Run_RF', 'ALS_N_Walk_LB', 'ALS_N_Run_LB', 'ALS_CLF_Walk_R', 'ALS_N_Walk_RB',
    'ALS_CLF_Walk_B', 'ALS_N_Walk_RF'
)
$members = @($memberNames | ForEach-Object {
    [ordered]@{
        animation = Resolve-Id 'animations' "$locomotionRoot/$($_).$($_)"
        loopPolicy = 'Loop'
        canLead = $true
    }
})

$transitionRoot = "$sourceContentRoot/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Transitions"
$leftTransition = Resolve-Id 'animations' "$transitionRoot/ALS_N_Transition_L.ALS_N_Transition_L"
$rightTransition = Resolve-Id 'animations' "$transitionRoot/ALS_N_Transition_R.ALS_N_Transition_R"
$actionRoot = "$sourceContentRoot/CharacterAssets/MannequinSkeleton/AnimationExamples/Actions"
$rollMontageAsset = Resolve-ExactManifestAsset $manifest 'montages' `
    "$actionRoot/ALS_N_LandRoll_F_Montage_Default.ALS_N_LandRoll_F_Montage_Default"
$rollSequenceAsset = Resolve-ExactManifestAsset $manifest 'animations' `
    "$actionRoot/ALS_N_LandRoll_F.ALS_N_LandRoll_F"
$rollMontage = [string]$rollMontageAsset.id
$rollSequence = [string]$rollSequenceAsset.id
$montageSlots = @($rollMontageAsset.metadata.slots)
if ($montageSlots.Count -ne 1 -or $montageSlots[0].slotName -cne 'BaseLayer') {
    throw 'Roll Montage must contain exactly one slot and it must be BaseLayer.'
}
$defaultSections = @($rollMontageAsset.metadata.sections | Where-Object { $_.name -ceq 'Default' })
if ($defaultSections.Count -ne 1) {
    throw 'Roll Montage must contain exactly one Default section.'
}
$baseLayerSegments = @($montageSlots[0].segments)
if ($baseLayerSegments.Count -ne 1) {
    throw 'Roll Montage must contain exactly one BaseLayer segment.'
}
if ([string]$baseLayerSegments[0].animationId -cne $rollSequence) {
    throw 'Roll Montage BaseLayer segment must reference the exact Roll Sequence.'
}
if ([string]$baseLayerSegments[0].animationObjectPath -cne [string]$rollSequenceAsset.objectPath) {
    throw 'Roll Montage BaseLayer segment object path must match the exact Roll Sequence.'
}

$profile = [ordered]@{
    schemaVersion = 1
    eventSemantics = @(
        [ordered]@{ kind = 'Generic'; semanticId = 0 }
        [ordered]@{ kind = 'Footstep'; semanticId = 1 }
        [ordered]@{ kind = 'SetAction'; semanticId = 2 }
        [ordered]@{ kind = 'SetGroundedEntry'; semanticId = 3 }
        [ordered]@{ kind = 'EarlyBlendOut'; semanticId = 4 }
        [ordered]@{ kind = 'RootMotionScale'; semanticId = 5 }
    )
    curveSemantics = [ordered]@{
        allowTransitions = [ordered]@{
            sourceName = 'Enable_Transition'
            missingValue = [double]1.0
            blendMode = 'AdditiveToDefault'
            clampMinimum = [double]0.0
            clampMaximum = [double]1.0
        }
    }
    syncGroups = @(
        [ordered]@{
            name = 'Grounded'
            leftMarker = 'Left'
            rightMarker = 'Right'
            members = $members
        }
    )
    dynamicTransition = [ordered]@{
        distanceMeters = [double]0.08
        blendSeconds = [double]0.2
        playRate = [double]1.5
        cooldownFrames = 2
        slots = @(
            [ordered]@{ stance = 'Standing'; foot = 'Left'; animation = $leftTransition }
            [ordered]@{ stance = 'Standing'; foot = 'Right'; animation = $rightTransition }
            [ordered]@{ stance = 'Crouching'; foot = 'Left'; animation = $leftTransition }
            [ordered]@{ stance = 'Crouching'; foot = 'Right'; animation = $rightTransition }
        )
    }
    actions = @(
        [ordered]@{
            name = 'Roll'
            montage = $rollMontage
            slot = 'BaseLayer'
            startSection = 'Default'
            priority = 100
            interruptible = $true
            playRate = [double]1.0
            blendSeconds = [double]0.2
            loopPolicy = 'Once'
        }
    )
    demoCases = [ordered]@{
        transitionStance = 'Standing'
        transitionFoot = 'Left'
        rollAction = 'Roll'
    }
}

$outputDirectory = [IO.Path]::GetDirectoryName($outputFullPath)
[void][IO.Directory]::CreateDirectory($outputDirectory)
$json = $profile | ConvertTo-Json -Depth 20
$bytes = [Text.UTF8Encoding]::new($false).GetBytes("$json$([Environment]::NewLine)")
$temporaryPath = Join-Path $outputDirectory ".$([IO.Path]::GetFileName($outputFullPath)).$([guid]::NewGuid().ToString('N')).tmp"
try {
    $stream = [IO.FileStream]::new(
        $temporaryPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write,
        [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
    try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) }
    finally { $stream.Dispose() }
    if ([IO.File]::Exists($outputFullPath)) {
        [IO.File]::Replace($temporaryPath, $outputFullPath, [Management.Automation.Language.NullString]::Value)
    }
    else { [IO.File]::Move($temporaryPath, $outputFullPath) }
}
catch { throw "Failed to publish P5A profile atomically: $($_.Exception.Message)" }
finally { if ([IO.File]::Exists($temporaryPath)) { [IO.File]::Delete($temporaryPath) } }

Write-Output 'P5A_PROFILE_GENERATION_OK groups=1 members=17 transitions=4 actions=1 segments=1'

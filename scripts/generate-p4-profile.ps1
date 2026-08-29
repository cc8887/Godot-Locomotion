param(
    [string]$ManifestPath = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..')).Path 'assets\generated\als_v4\als_manifest.json'),
    [string]$OutputPath = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..')).Path 'assets\config\p4_pose_profile.json')
)

$ErrorActionPreference = 'Stop'
$manifestFullPath = [System.IO.Path]::GetFullPath($ManifestPath)
$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
if ([string]::Equals($manifestFullPath, $outputFullPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Manifest and output paths must not identify the same file.'
}
if (-not [IO.File]::Exists($manifestFullPath)) { throw "Manifest does not exist: $manifestFullPath" }
$manifest = Get-Content -LiteralPath $manifestFullPath -Raw | ConvertFrom-Json

function Resolve-ExactManifestAsset([object]$Manifest, [string]$Collection, [string]$ObjectPath) {
    $matches = @($Manifest.$Collection | Where-Object { $_.objectPath -ceq $ObjectPath })
    if ($matches.Count -eq 0) { throw "Source map has zero exact object path matches in ${Collection}: $ObjectPath" }
    if ($matches.Count -ne 1) { throw "Source map has multiple exact object path matches in ${Collection}: $ObjectPath" }
    return $matches[0]
}

$aimRoot = '/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/AimOffsets'
$turnRoot = '/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/TurnInPlace'
$skeletonPath = '/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/ALS_Mannequin_Skeleton.ALS_Mannequin_Skeleton'
function AssetId([string]$Collection, [string]$Path) { [string](Resolve-ExactManifestAsset $manifest $Collection $Path).id }
function AimPath([string]$Name) { "$aimRoot/$Name.$Name" }
function TurnPath([string]$Name) { "$turnRoot/$Name.$Name" }
function New-Turn([string]$Name, [string]$Stance, [int]$Direction, [int]$Degrees) {
    [ordered]@{ animation = AssetId 'animations' (TurnPath $Name); stance = $Stance; direction = $Direction; nominalDegrees = $Degrees; basePlayRate = [double]1.2; blendSeconds = [double]0.2; scaleAngle = $true }
}
function New-Rotate([string]$Name, [string]$Stance, [int]$Direction) {
    [ordered]@{ animation = AssetId 'animations' (TurnPath $Name); stance = $Stance; direction = $Direction }
}
function New-Mask([string]$Kind, [string]$Root, [string[]]$Boundaries) {
    [ordered]@{ kind = $Kind; root = $Root; boundaries = @($Boundaries) }
}

$profile = [ordered]@{
    schemaVersion = 1
    skeleton = AssetId 'skeletons' $skeletonPath
    aim = [ordered]@{
        aimOffset = AssetId 'aimOffsets' (AimPath 'ALS_N_Look')
        down = AssetId 'animations' (AimPath 'ALS_N_Look_D_Sweep')
        forward = AssetId 'animations' (AimPath 'ALS_N_Look_F_Sweep')
        up = AssetId 'animations' (AimPath 'ALS_N_Look_U_Sweep')
    }
    turns = @(
        New-Turn 'ALS_N_TurnIP_L90' 'standing' -1 90
        New-Turn 'ALS_N_TurnIP_R90' 'standing' 1 90
        New-Turn 'ALS_N_TurnIP_L180' 'standing' -1 180
        New-Turn 'ALS_N_TurnIP_R180' 'standing' 1 180
        New-Turn 'ALS_CLF_TurnIP_L90' 'crouching' -1 90
        New-Turn 'ALS_CLF_TurnIP_R90' 'crouching' 1 90
        New-Turn 'ALS_CLF_TurnIP_L180' 'crouching' -1 180
        New-Turn 'ALS_CLF_TurnIP_R180' 'crouching' 1 180
    )
    rotates = @(
        New-Rotate 'ALS_N_Rotate_L90' 'standing' -1
        New-Rotate 'ALS_N_Rotate_R90' 'standing' 1
        New-Rotate 'ALS_CLF_Rotate_L90' 'crouching' -1
        New-Rotate 'ALS_CLF_Rotate_R90' 'crouching' 1
    )
    masks = @(
        New-Mask 'upperBody' 'spine_01' @('neck_01', 'clavicle_l', 'clavicle_r')
        New-Mask 'head' 'neck_01' @()
        New-Mask 'leftArm' 'clavicle_l' @('Hand_L')
        New-Mask 'rightArm' 'clavicle_r' @('hand_r')
        New-Mask 'leftHand' 'Hand_L' @()
        New-Mask 'rightHand' 'hand_r' @()
        New-Mask 'pelvis' 'Pelvis' @('spine_01', 'Thigh_L', 'Thigh_R')
        New-Mask 'leftLeg' 'Thigh_L' @('Foot_L')
        New-Mask 'rightLeg' 'Thigh_R' @('Foot_R')
        New-Mask 'leftFoot' 'Foot_L' @()
        New-Mask 'rightFoot' 'Foot_R' @()
    )
    feet = [ordered]@{
        leftLegRoot = 'Thigh_L'
        rightLegRoot = 'Thigh_R'
        leftFootRoot = 'Foot_L'
        rightFootRoot = 'Foot_R'
        traceUpMeters = [double]0.5
        traceDownMeters = [double]0.75
        footHeightMeters = [double]0.13
        maxPelvisCorrectionMeters = [double]0.4
        pelvisUpHalfLifeSeconds = [double]0.08
        pelvisDownHalfLifeSeconds = [double]0.1
        positionHalfLifeSeconds = [double]0.08
        rotationHalfLifeSeconds = [double]0.1
        lockReleaseHalfLifeSeconds = [double]0.12
        maxLegReachMeters = [double]1.2
        capsuleHalfHeightSource = 'characterController'
        maxThighAngleDegrees = [double]90.0
        maxFootAngleDegrees = [double]40.0
        platformTeleportDistanceMeters = [double]1.0
        platformTeleportAngleDegrees = [double]45.0
        lockWeightEpsilon = [double]0.0001
        curves = [ordered]@{
            leftLock = 'FootLock_L'
            rightLock = 'FootLock_R'
            missingLockDefault = [double]0.0
        }
        ikStateDefaults = [ordered]@{
            grounded = [double]1.0
            jumpStart = [double]0.0
            fallLoop = [double]0.0
            landRecovery = [double]1.0
        }
    }
}

$outputDirectory = [IO.Path]::GetDirectoryName($outputFullPath)
[void][IO.Directory]::CreateDirectory($outputDirectory)
$json = $profile | ConvertTo-Json -Depth 20
$bytes = [Text.UTF8Encoding]::new($false).GetBytes("$json$([Environment]::NewLine)")
$temporaryPath = Join-Path $outputDirectory ".$([IO.Path]::GetFileName($outputFullPath)).$([guid]::NewGuid().ToString('N')).tmp"
try {
    $stream = [System.IO.FileStream]::new($temporaryPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
    try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
    if ([System.IO.File]::Exists($outputFullPath)) { [System.IO.File]::Replace($temporaryPath, $outputFullPath, [Management.Automation.Language.NullString]::Value) }
    else { [System.IO.File]::Move($temporaryPath, $outputFullPath) }
}
catch { throw "Failed to publish P4 profile atomically: $($_.Exception.Message)" }
finally { if ([IO.File]::Exists($temporaryPath)) { [IO.File]::Delete($temporaryPath) } }

Write-Output 'P4_PROFILE_GENERATION_OK turns=8 rotates=4 masks=11'

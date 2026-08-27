[CmdletBinding()]
param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ManifestPath,
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ManifestPath))
{
    $ManifestPath = Join-Path $ProjectRoot 'assets\generated\als_v4\als_manifest.json'
}
if ([string]::IsNullOrWhiteSpace($OutputPath))
{
    $OutputPath = Join-Path $ProjectRoot 'assets\config\p3_locomotion_profile.json'
}
$ManifestPath = [System.IO.Path]::GetFullPath($ManifestPath)
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
if ([System.StringComparer]::OrdinalIgnoreCase.Equals($ManifestPath, $OutputPath))
{
    throw "Manifest and output resolve to the same file: $ManifestPath"
}

function Resolve-ExactManifestAsset(
    [object]$Manifest,
    [string]$Section,
    [string]$ObjectPath)
{
    $property = $Manifest.PSObject.Properties[$Section]
    if ($null -eq $property)
    {
        throw "Manifest is missing required section '$Section'."
    }

    $matchingAssets = @($property.Value | Where-Object {
        [string]$_.objectPath -ceq $ObjectPath
    })
    if ($matchingAssets.Count -eq 0)
    {
        throw "Found zero exact object path matches in '$Section' for '$ObjectPath'."
    }
    if ($matchingAssets.Count -ne 1)
    {
        throw "Found multiple exact object path matches in '$Section' for '$ObjectPath'."
    }
    if ([string]$matchingAssets[0].id -cnotmatch '^[0-9a-f]{40}$')
    {
        throw "Exact object path '$ObjectPath' has an invalid stable ID."
    }
    return $matchingAssets[0]
}

function New-LocomotionSample(
    [object]$Manifest,
    [string]$ObjectPath,
    [double]$X,
    [double]$Y)
{
    $asset = Resolve-ExactManifestAsset $Manifest 'animations' $ObjectPath
    return [ordered]@{
        animation = [string]$asset.id
        x = $X
        y = $Y
        rateScale = 1.0
    }
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$baseRoot = '/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base'
$locomotionRoot = "$baseRoot/Locomotion"

$mannequinPath = '/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin.Mannequin'
$standingIdlePath = '/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/BasePoses/ALS_N_Pose.ALS_N_Pose'
$crouchingIdlePath = '/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/BasePoses/ALS_CLF_Pose.ALS_CLF_Pose'
$jumpStartPath = '/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/InAir/ALS_N_JumpRun_LF.ALS_N_JumpRun_LF'
$fallLoopPath = '/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/InAir/ALS_N_FallLoop.ALS_N_FallLoop'
$landPath = '/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/InAir/ALS_N_Land_Light.ALS_N_Land_Light'
$leanAdditivePath = '/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/Detail/ALS_N_Lean.ALS_N_Lean'

$standingSourceMap = @(
    [pscustomobject]@{ Token = 'ALS_N_Walk_F'; X = 0.0; Y = 0.5 },
    [pscustomobject]@{ Token = 'ALS_N_Walk_LF'; X = -0.353553; Y = 0.353553 },
    [pscustomobject]@{ Token = 'ALS_N_Walk_RF'; X = 0.353553; Y = 0.353553 },
    [pscustomobject]@{ Token = 'ALS_N_Walk_B'; X = 0.0; Y = -0.5 },
    [pscustomobject]@{ Token = 'ALS_N_Walk_LB'; X = -0.353553; Y = -0.353553 },
    [pscustomobject]@{ Token = 'ALS_N_Walk_RB'; X = 0.353553; Y = -0.353553 },
    [pscustomobject]@{ Token = 'ALS_N_Run_F'; X = 0.0; Y = 1.0 },
    [pscustomobject]@{ Token = 'ALS_N_Run_LF'; X = -0.707107; Y = 0.707107 },
    [pscustomobject]@{ Token = 'ALS_N_Run_RF'; X = 0.707107; Y = 0.707107 },
    [pscustomobject]@{ Token = 'ALS_N_Run_B'; X = 0.0; Y = -1.0 },
    [pscustomobject]@{ Token = 'ALS_N_Run_LB'; X = -0.707107; Y = -0.707107 },
    [pscustomobject]@{ Token = 'ALS_N_Run_RB'; X = 0.707107; Y = -0.707107 },
    [pscustomobject]@{ Token = 'ALS_N_Sprint_F'; X = 0.0; Y = 1.5 }
)
$crouchingSourceMap = @(
    [pscustomobject]@{ Token = 'ALS_CLF_Walk_F'; X = 0.0; Y = 1.0 },
    [pscustomobject]@{ Token = 'ALS_CLF_Walk_L'; X = -1.0; Y = 0.0 },
    [pscustomobject]@{ Token = 'ALS_CLF_Walk_R'; X = 1.0; Y = 0.0 },
    [pscustomobject]@{ Token = 'ALS_CLF_Walk_B'; X = 0.0; Y = -1.0 }
)

$standingSamples = @($standingSourceMap | ForEach-Object {
    $token = [string]$_.Token
    New-LocomotionSample $manifest "$locomotionRoot/$token.$token" ([double]$_.X) ([double]$_.Y)
})
$crouchingSamples = @($crouchingSourceMap | ForEach-Object {
    $token = [string]$_.Token
    New-LocomotionSample $manifest "$locomotionRoot/$token.$token" ([double]$_.X) ([double]$_.Y)
})

$profile = [ordered]@{
    schemaVersion = 2
    presentation = [ordered]@{
        translationMeters = @([double]0.0, [double]-0.92, [double]0.0)
        yawRadians = [double]([Math]::PI / 2.0)
    }
    mannequin = [string](Resolve-ExactManifestAsset $manifest 'skeletalMeshes' $mannequinPath).id
    standingIdle = [string](Resolve-ExactManifestAsset $manifest 'animations' $standingIdlePath).id
    crouchingIdle = [string](Resolve-ExactManifestAsset $manifest 'animations' $crouchingIdlePath).id
    standingSamples = $standingSamples
    crouchingSamples = $crouchingSamples
    jumpStart = [string](Resolve-ExactManifestAsset $manifest 'animations' $jumpStartPath).id
    fallLoop = [string](Resolve-ExactManifestAsset $manifest 'animations' $fallLoopPath).id
    land = [string](Resolve-ExactManifestAsset $manifest 'animations' $landPath).id
    leanAdditive = [string](Resolve-ExactManifestAsset $manifest 'blendSpaces' $leanAdditivePath).id
}

$outputDirectory = [System.IO.Path]::GetDirectoryName($OutputPath)
if (-not [string]::IsNullOrWhiteSpace($outputDirectory))
{
    [void][System.IO.Directory]::CreateDirectory($outputDirectory)
}
$json = $profile | ConvertTo-Json -Depth 10
$bytes = [System.Text.UTF8Encoding]::new($false).GetBytes("$json$([Environment]::NewLine)")
$temporaryPath = Join-Path $outputDirectory `
    ".$([System.IO.Path]::GetFileName($OutputPath)).$([guid]::NewGuid().ToString('N')).tmp"
try
{
    $stream = [System.IO.FileStream]::new($temporaryPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None, 4096, [System.IO.FileOptions]::WriteThrough)
    try
    {
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)
    }
    finally
    {
        $stream.Dispose()
    }

    if ([System.IO.File]::Exists($OutputPath))
    {
        [System.IO.File]::Replace(
            $temporaryPath,
            $OutputPath,
            [System.Management.Automation.Language.NullString]::Value)
    }
    else
    {
        [System.IO.File]::Move($temporaryPath, $OutputPath)
    }
}
catch
{
    throw "Failed to publish P3 profile atomically: $($_.Exception.Message)"
}
finally
{
    if ([System.IO.File]::Exists($temporaryPath))
    {
        [System.IO.File]::Delete($temporaryPath)
    }
}

Write-Output "P3_PROFILE_GENERATION_OK standing=$($standingSamples.Count) crouching=$($crouchingSamples.Count)"

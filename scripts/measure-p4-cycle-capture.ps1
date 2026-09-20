param(
    [Parameter(Mandatory)][string]$CaptureDirectory,
    [float]$MaximumStartupSlideCentimeters = 0
)

$ErrorActionPreference = 'Stop'
$capturePath = (Resolve-Path -LiteralPath $CaptureDirectory).Path
$frames = Get-Content -LiteralPath (Join-Path $capturePath 'frames.json') -Raw | ConvertFrom-Json
if ($frames.Count -ne 720 -or $null -eq $frames[0].Cycle) { throw 'Expected 720 cycle diagnostic frames.' }
$startup = foreach ($frameIndex in 60..71) {
    foreach ($side in @('Left', 'Right')) {
        $previous = $frames[$frameIndex - 1].FootPose."${side}FootWorldPosition"
        $current = $frames[$frameIndex].FootPose."${side}FootWorldPosition"
        if ($current.Y -gt 0.16 -or $previous.Y -gt 0.16) { continue }
        [pscustomobject]@{
            Frame = $frameIndex + 1
            Foot = $side
            HorizontalStepCentimeters = 100 * [Math]::Sqrt(
                [Math]::Pow($current.X - $previous.X, 2) + [Math]::Pow($current.Z - $previous.Z, 2))
        }
    }
}
$hasTransitionStack = $null -ne $frames[0].Cycle.PSObject.Properties['TransitionsStartedThisFrame']
$hipStarts = @($frames | Where-Object {
    $_.Cycle.HipTransition -and $(if ($hasTransitionStack) {
        $_.Cycle.TransitionsStartedThisFrame -gt 0
    } else { $_.Cycle.TransitionElapsed -eq 0 })
})
foreach ($frame in $hipStarts) {
    if ($frame.Cycle.Crossing -gt 0.00001) { throw "Hip transition violated Feet_Crossing gate at frame $($frame.Frame)." }
}
$peak = ($startup | Measure-Object HorizontalStepCentimeters -Maximum).Maximum
$summary = [pscustomobject]@{
    Frames = $frames.Count
    HipStartFrames = @($hipStarts | ForEach-Object Frame)
    WaitingFrames = @($frames | Where-Object { $_.Cycle.WaitingForFeet }).Count
    MaximumActiveTransitions = $(if ($hasTransitionStack) { ($frames.Cycle | Measure-Object ActiveTransitionCount -Maximum).Maximum } else { $null })
    StartupLowFootPeakCentimeters = $peak
    StartupLowFootLeftPeakCentimeters = ($startup | Where-Object Foot -eq Left | Measure-Object HorizontalStepCentimeters -Maximum).Maximum
    StartupThresholdCentimeters = $MaximumStartupSlideCentimeters
    StartupAccepted = $MaximumStartupSlideCentimeters -gt 0 -and $peak -le $MaximumStartupSlideCentimeters
}
$summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $capturePath 'cycle-metrics.json') -Encoding utf8
$summary | Format-List
if ($MaximumStartupSlideCentimeters -gt 0 -and -not $summary.StartupAccepted) {
    throw "Startup sliding remains above the requested acceptance threshold: $peak cm/frame."
}

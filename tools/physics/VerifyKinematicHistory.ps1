param([Parameter(Mandatory)][string]$Reference,
      [Parameter(Mandatory)][string]$CoreAssembly)
$ErrorActionPreference='Stop'
Add-Type -Path (Resolve-Path -LiteralPath $CoreAssembly).Path
$data=Get-Content -LiteralPath $Reference -Raw | ConvertFrom-Json
function Pose($row) {
    $p=$row.position; $q=$row.rotation
    return [GodotAls.Core.Locomotion.AlsPrecisePose]::new(
        [GodotAls.Core.Locomotion.AlsDoubleVector]::new($p[0],$p[1],$p[2]),
        [GodotAls.Core.Locomotion.AlsQuaternion]::new($q[0],$q[1],$q[2],$q[3]),
        [GodotAls.Core.Locomotion.AlsDoubleVector]::One)
}
if($data.cases.Count -ne 4){throw 'Missing history cases.'}
$checks=0; $maxV=0.0; $maxW=0.0
foreach($case in $data.cases) {
    if(($case.history.stage -join ',') -ne 'history_initial,target_queued,target_stepped,entered_after_step,no_target_step,before_teleport,teleported,teleport_stepped') {throw 'History stages differ.'}
    $initial=$case.history[0].bodies; $queued=$case.history[1].bodies
    $stepped=$case.history[2].bodies; $entered=$case.history[3].bodies
    for($i=0;$i -lt $initial.Count;$i++) {
        $result=[GodotAls.Core.Physics.AlsKinematicMotion]::PositionTarget((Pose $initial[$i].actor),(Pose $queued[$i].actor),[double][float]$case.historyDt)
        $v=@($result.Velocity.Linear.X,$result.Velocity.Linear.Y,$result.Velocity.Linear.Z)
        $w=@($result.Velocity.Angular.X,$result.Velocity.Angular.Y,$result.Velocity.Angular.Z)
        for($axis=0;$axis -lt 3;$axis++) {
            # Restore native float storage before comparing JSON numbers.
            $maxV=[Math]::Max($maxV,[Math]::Abs([double]$v[$axis]-[double][float]$stepped[$i].physicsLinear[$axis]))
            $maxW=[Math]::Max($maxW,[Math]::Abs([double]$w[$axis]-[double][float]$stepped[$i].physicsAngular[$axis]))
        }
        foreach($stage in $case.history) {
            $body=$stage.bodies[$i]
            if($stage.stage -eq 'before_teleport' -and (($body.linear -join ',') -eq '0,0,0' -or ($body.angular -join ',') -eq '0,0,0')) {throw 'Teleport must clear nonzero linear and angular velocities.'}
            if($body.bone -ne $initial[$i].bone){throw 'History binding differs.'}
            if($stage.stage -eq 'teleported') {
                $before=$case.history[5].bodies[$i]
                if(($body.physicsLinear -join ',') -ne ($before.physicsLinear -join ',') -or
                    ($body.physicsAngular -join ',') -ne ($before.physicsAngular -join ',')){throw 'Pre-step teleport unexpectedly changed physics-thread history.'}
            } elseif(($body.linear -join ',') -ne ($body.physicsLinear -join ',') -or
                ($body.angular -join ',') -ne ($body.physicsAngular -join ',')){throw 'Completed thread state differs.'}
            if($stage.stage -in @('history_initial','target_queued','no_target_step','teleported','teleport_stepped')) {
                if(($body.linear -join ',') -ne '0,0,0' -or ($body.angular -join ',') -ne '0,0,0'){throw "Expected zero velocity at $($stage.stage)."}
            }
        }
        if(($stepped[$i].linear -join ',') -ne ($entered[$i].linear -join ',') -or
            ($stepped[$i].angular -join ',') -ne ($entered[$i].angular -join ',')){throw 'Dynamic transition lost kinematic velocity.'}
        $checks++
    }
}
if($checks -ne 80 -or $maxV -gt .0001 -or $maxW -gt .00001){throw "Native kinematic delta exceeds transport tolerance: count=$checks V=$maxV W=$maxW"}
Write-Output "KINEMATIC_HISTORY_OK bodies=$checks max_linear_cmps=$maxV max_angular_radps=$maxW"

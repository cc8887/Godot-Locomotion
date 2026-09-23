param([Parameter(Mandatory)][string]$Native,[Parameter(Mandatory)][string]$Core,[switch]$AllFrames)
$ErrorActionPreference='Stop'
$nativeData=Get-Content -LiteralPath $Native -Raw | ConvertFrom-Json
$coreData=Get-Content -LiteralPath $Core -Raw | ConvertFrom-Json
function MaxDelta($a,$b,[bool]$single=$false) {
 $max=0.0
 for($i=0;$i -lt $a.Count;$i++) {
  $x=[double]$a[$i];$y=[double]$b[$i]
  if($single){$x=[double][single]$x;$y=[double][single]$y}
  $max=[Math]::Max($max,[Math]::Abs($x-$y))
 }
 return $max
}
function QuatDelta($a,$b) {
 $dot=0.0;for($i=0;$i -lt 4;$i++){$dot+=$a[$i]*$b[$i]}
 if($dot -lt 0){$b=@($b|ForEach-Object { -$_ })}
 return MaxDelta $a $b
}
foreach($case in $nativeData.cases) {
 $rows=@($coreData.samples|Where-Object mesh -EQ $case.setup.mesh)
 $steps=[int]$case.setup.steps
 if($rows.Count -ne $steps -or $case.samples.Count -ne $steps+1){throw 'Incomplete capture'}
 $frames=if($AllFrames){1..$steps}else{@(1,2,3,10,30,60,120,300,$steps)|Where-Object {$_ -le $steps}|Sort-Object -Unique}
 foreach($frame in $frames) {
  $n=$case.samples[$frame];$g=$rows[$frame-1].sample
  if($n.frame -ne $frame -or $g.frame -ne $frame){throw 'Capture frame order differs'}
  $q=0.0;$k=0.0;$p=0.0;$v=0.0;$w=0.0;$bodyQ=0.0
  foreach($motor in $g.motors) {
   # All-free connectivity-only joints have no solver target or enabled drive.
   if(($motor.target|Measure-Object -Sum).Sum -eq 0 -and ($motor.stiffness|Measure-Object -Sum).Sum -eq 0){continue}
   $q=[Math]::Max($q,(QuatDelta $motor.target $n.flail.motors[$motor.index].target.rotation))
   # Native coefficients exist on disabled axes; compare enabled nonzero axes.
   for($axis=0;$axis -lt 3;$axis++) {if($motor.stiffness[$axis] -ne 0){$k=[Math]::Max($k,[Math]::Abs($motor.stiffness[$axis]-$n.flail.motors[$motor.index].stiffness[$axis]))}}
  }
  for($i=0;$i -lt $n.bodies.Count;$i++) {
   $p=[Math]::Max($p,(MaxDelta $g.bodies[$i].position $n.bodies[$i].world.position))
   $bodyQ=[Math]::Max($bodyQ,(QuatDelta $g.bodies[$i].rotation $n.bodies[$i].world.rotation))
   $v=[Math]::Max($v,(MaxDelta $g.bodies[$i].v $n.bodies[$i].linearVelocity $true))
   $w=[Math]::Max($w,(MaxDelta $g.bodies[$i].w $n.bodies[$i].angularVelocity $true))
  }
  [pscustomobject]@{mesh=$case.setup.mesh.Split('.')[-1];frame=$frame;
   time_delta=[Math]::Abs([double][single]$g.time-[double][single]$n.flail.time);
   rate_delta=[Math]::Abs([double][single]$g.rate-[double][single]$n.flail.rate);
   input_v_delta=(MaxDelta $g.pelvisVelocity $n.flail.pelvisVelocity $true);
   target_delta=$q;k_delta=$k;p_delta=$p;q_delta=$bodyQ;v_delta=$v;w_delta=$w}|ConvertTo-Json -Compress
 }
}

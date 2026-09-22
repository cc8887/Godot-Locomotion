param(
    [Parameter(Mandatory=$true)][string]$WorldReference,
    [Parameter(Mandatory=$true)][string]$CaptureDirectory,
    [Parameter(Mandatory=$true)][string]$Report
)
$ErrorActionPreference='Stop'
if(-not [IO.Path]::IsPathFullyQualified($Report) -or (Test-Path -LiteralPath $Report)) {throw 'Report must be a new absolute file.'}
$world=Get-Content -LiteralPath $WorldReference -Raw | ConvertFrom-Json
$rows=[Collections.Generic.List[object]]::new()
function Distance($a,$b) {
    if($a.Count -ne 3 -or $b.Count -ne 3) {throw 'Expected three vector components.'}
    return [Math]::Sqrt([Math]::Pow($a[0]-$b[0],2)+[Math]::Pow($a[1]-$b[1],2)+[Math]::Pow($a[2]-$b[2],2))
}
function RotationDifference($a,$b) {
    if($a.Count -ne 4 -or $b.Count -ne 4) {throw 'Expected four stored rotation components.'}
    $maximum=0.0
    for($axis=0;$axis -lt 4;$axis++) {$maximum=[Math]::Max($maximum,[Math]::Abs($a[$axis]-$b[$axis]))}
    return $maximum
}
function StoredFloatDistance($a,$b) {
    # System.Text.Json emits short round-trip float literals; reading those as
    # doubles would invent differences against UE's promoted float literals.
    return Distance @($a|ForEach-Object {[double][single]$_}) @($b|ForEach-Object {[double][single]$_})
}
foreach($case in $world.cases) {
    $mesh=$case.setup.mesh.Split('.')[-1]
    foreach($sample in $case.samples) {
        if($sample.PSObject.Properties.Name -notcontains 'stepObservations') {continue}
        $path=Join-Path $CaptureDirectory "$mesh-$($sample.frame-1).json"
        if(-not(Test-Path -LiteralPath $path)) {continue}
        $capture=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        $captureInput=$capture.input
        if($captureInput.mesh -ne $case.setup.mesh -or $captureInput.frame+1 -ne $sample.frame -or $captureInput.dt -ne $case.dtUsed) {
            throw 'Mesh/frame/exact duration mismatch.'
        }
        foreach($stage in @('postIntegrate','preSolve')) {
            $observations=@($sample.stepObservations|Where-Object stage -EQ $stage)
            if($observations.Count -ne 1 -or $observations[0].dt -ne $captureInput.dt) {throw 'Missing or repeated native stage/duration.'}
            $native=$observations[0].bodies
            if($native.Count -ne $case.setup.bodies.Count -or $native.Count -gt $captureInput.bodies.Count) {throw 'Body topology differs.'}
            $errors=@()
            for($i=0;$i -lt $native.Count;$i++) {
                $a=$native[$i];$b=$captureInput.bodies[$i]
                if($a.name -ne $b.name -or $a.name -ne $case.setup.bodies[$i].name) {throw 'Body order differs.'}
                if(-not $case.setup.bodies[$i].dynamic) {continue}
                $errors+=[ordered]@{name=$a.name;initialPosition=Distance $a.initialCom.position $b.initial.position;
                    initialRotation=RotationDifference $a.initialCom.rotation $b.initial.rotation;
                    predictedPosition=Distance $a.predictedCom.position $b.predicted.position;
                    predictedRotation=RotationDifference $a.predictedCom.rotation $b.predicted.rotation;
                    linearVelocity=StoredFloatDistance $a.v $b.v;angularVelocity=StoredFloatDistance $a.w $b.w;
                    inverseMass=[Math]::Abs([double][single]$a.inverseMass-$b.inverseMass);
                    inverseInertia=Distance $a.conditionedInverseInertia $b.inverseInertia}
            }
            $maximum=[ordered]@{}
            foreach($field in @('initialPosition','initialRotation','predictedPosition','predictedRotation','linearVelocity','angularVelocity','inverseMass','inverseInertia')) {
                $worst=$errors|Sort-Object {$_[$field]} -Descending|Select-Object -First 1
                $maximum[$field]=[ordered]@{difference=$worst[$field];bone=$worst.name}
            }
            $rows.Add([ordered]@{mesh=$case.setup.mesh;completedStep=$sample.frame;stage=$stage;dt=$captureInput.dt;
                maximum=$maximum;bodies=$errors;captureSha256=(Get-FileHash -LiteralPath $path).Hash})
        }
    }
}
if($rows.Count -eq 0) {throw 'No matching native observation and Core input steps.'}
$output=[ordered]@{schemaVersion=1;observation='Independent native world integration/pre-solve versus Core solver input. Caller must supply matching initial setup. Later steps may inherit trajectory differences.';
    worldSha256=(Get-FileHash -LiteralPath $WorldReference).Hash;samples=$rows.ToArray()}
$stream=[IO.File]::Open($Report,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write)
try {$bytes=[Text.UTF8Encoding]::new($false).GetBytes(($output|ConvertTo-Json -Depth 12));$stream.Write($bytes,0,$bytes.Length)}
finally {$stream.Dispose()}
Write-Output "Compared $($rows.Count) native world stages."

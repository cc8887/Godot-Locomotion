param(
    [Parameter(Mandatory=$true)][string]$WorldReference,
    [Parameter(Mandatory=$true)][string]$CaptureDirectory,
    [Parameter(Mandatory=$true)][string]$Report
)
$ErrorActionPreference='Stop'
if(-not [IO.Path]::IsPathFullyQualified($Report) -or (Test-Path -LiteralPath $Report)) {throw 'Report must be a new absolute file.'}
$world=Get-Content -LiteralPath $WorldReference -Raw | ConvertFrom-Json
$rows=[Collections.Generic.List[object]]::new()
function CompareValues($a,$b,[string]$path,[Collections.Generic.List[object]]$differences) {
    if($a -is [System.Management.Automation.PSCustomObject] -and $b -is [System.Management.Automation.PSCustomObject]) {
        $names=@($a.PSObject.Properties.Name)+@($b.PSObject.Properties.Name)|Sort-Object -Unique
        foreach($name in $names) {CompareValues $a.$name $b.$name "$path.$name" $differences}
    } elseif($a -is [array] -and $b -is [array]) {
        if($a.Count -ne $b.Count) {$differences.Add([ordered]@{field=$path;nativeCount=$a.Count;coreCount=$b.Count});return}
        for($i=0;$i -lt $a.Count;$i++) {CompareValues $a[$i] $b[$i] "$path[$i]" $differences}
    } elseif($a -cne $b) {$differences.Add([ordered]@{field=$path;native=$a;core=$b})}
}
foreach($case in $world.cases) {
    $mesh=$case.setup.mesh.Split('.')[-1]
    foreach($sample in $case.samples) {
        $steps=@($sample.stepObservations|Where-Object stage -EQ 'preSolve')
        if($steps.Count -eq 0 -or $steps[0].PSObject.Properties.Name -notcontains 'joints') {continue}
        $path=Join-Path $CaptureDirectory "$mesh-$($sample.frame-1).json"
        if(-not(Test-Path -LiteralPath $path)) {continue}
        $capture=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
        $captureInput=$capture.input
        if($captureInput.mesh -ne $case.setup.mesh -or $captureInput.frame+1 -ne $sample.frame -or $captureInput.dt -ne $case.dtUsed) {throw 'Mesh/frame/duration differs.'}
        $stage=$steps[0];$native=@($stage.joints|Where-Object {$_.inGraph -and -not $_.graphSleeping}|Sort-Object graphOrder)
        if($native.Count -ne $captureInput.joints.Count) {throw 'Joint counts differ.'}
        $errors=[Collections.Generic.List[object]]::new()
        CompareValues $stage.jointSolverSettings $captureInput.solverSettings 'solverSettings' $errors
        for($i=0;$i -lt $native.Count;$i++) {
            $a=$native[$i];$b=$captureInput.joints[$i]
            $parent=$captureInput.bodies[$b.parent].name;$child=$captureInput.bodies[$b.child].name
            CompareValues $a.parent $parent "joints[$i].parent" $errors
            CompareValues $a.child $child "joints[$i].child" $errors
            # Compare frames/settings by identity even if the graph order differs.
            $matching=@($native|Where-Object {$_.parent -eq $parent -and $_.child -eq $child})
            if($matching.Count -ne 1) {throw 'Missing or ambiguous native joint identity.'}
            $a=$matching[0]
            CompareValues $a.parentComFrame $b.parentFrame "joint($child).parentComFrame" $errors
            CompareValues $a.childComFrame $b.childFrame "joint($child).childComFrame" $errors
            CompareValues $a.settings $b.jointSettings "joint($child).settings" $errors
        }
        $rows.Add([ordered]@{mesh=$case.setup.mesh;completedStep=$sample.frame;joints=$native.Count;
            nativeColors=@($native.graphColor|Sort-Object -Unique);differences=$errors.ToArray();captureSha256=(Get-FileHash -LiteralPath $path).Hash})
    }
}
if($rows.Count -eq 0) {throw 'No matching native joint observations and Core captures.'}
$result=[ordered]@{schemaVersion=1;observation='Actual pre-solve joint graph order, COM connectors and settings versus Core captured inputs. This is not proof of matching Gather rows or solver output.';
    worldSha256=(Get-FileHash -LiteralPath $WorldReference).Hash;samples=$rows.ToArray()}
$stream=[IO.File]::Open($Report,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write)
try {$bytes=[Text.UTF8Encoding]::new($false).GetBytes(($result|ConvertTo-Json -Depth 16));$stream.Write($bytes,0,$bytes.Length)}
finally {$stream.Dispose()}
Write-Output "Compared $($rows.Count) native joint input frames."

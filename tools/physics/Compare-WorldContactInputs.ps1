param(
    [Parameter(Mandatory=$true)][string]$WorldReference,
    [Parameter(Mandatory=$true)][string]$CaptureDirectory,
    [Parameter(Mandatory=$true)][string]$Report
)
$ErrorActionPreference='Stop'
if(-not [IO.Path]::IsPathFullyQualified($Report) -or (Test-Path -LiteralPath $Report)) {throw 'Report must be a new absolute file.'}
$world=Get-Content -LiteralPath $WorldReference -Raw|ConvertFrom-Json
$rows=[Collections.Generic.List[object]]::new()
function CompareVector($native,$core,[string]$field,[bool]$storedFloat,[string]$where,$maximum) {
    if($native.Count -ne $core.Count -or $native.Count -notin @(3,4)){throw "Vector shape differs: $where $field"}
    for($axis=0;$axis -lt $native.Count;$axis++) {
        $a=[double]$native[$axis];$b=[double]$core[$axis]
        if($storedFloat){$a=[double][single]$a;$b=[double][single]$b}
        $difference=[Math]::Abs($a-$b)
        if(-not [double]::IsFinite($difference)){throw 'Nonfinite contact input.'}
        if($difference -gt $maximum[$field].difference){$maximum[$field]=[ordered]@{difference=$difference;where=$where;axis=$axis;native=$a;core=$b}}
    }
}
foreach($case in $world.cases) {
    $mesh=$case.setup.mesh.Split('.')[-1]
    foreach($sample in $case.samples) {
        $pre=@($sample.stepObservations|Where-Object stage -eq 'preSolve')
        if($pre.Count -ne 1 -or $pre[0].PSObject.Properties.Name -notcontains 'contacts'){continue}
        $path=Join-Path $CaptureDirectory "$mesh-$($sample.frame-1).json"
        if(-not(Test-Path -LiteralPath $path)){continue}
        $capture=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
        if($capture.input.mesh -ne $case.setup.mesh -or $capture.input.frame+1 -ne $sample.frame){throw 'Mesh/frame mismatch.'}
        $offsets=@();$total=0
        for($i=0;$i -lt $sample.bodies.Count;$i++) {
            if($sample.bodies[$i].name -ne $capture.input.bodies[$i].name){throw 'Body topology differs.'}
            $offsets+=$total;$total+=$sample.bodies[$i].shapeFilters.Count
        }
        for($i=0;$i -lt $case.setup.environment.Count;$i++){$offsets+=($total+$i)}
        $maximum=[ordered]@{}
        foreach($field in @('shapePosition','shapeRotation','point0','point1','normal1','anchor0','anchor1')){$maximum[$field]=[ordered]@{difference=0.0;where='';axis=0}}
        $flags=[Collections.Generic.List[object]]::new();$counts=[Collections.Generic.List[object]]::new();$pointsChecked=0;$pairs=0
        foreach($pair in $capture.input.contacts) {
            $names=@();$shapes=@()
            for($side=0;$side -lt 2;$side++) {
                $body=[int]$pair."body$side";$key=$pair.key."shape$side"
                if($key.body -ne $body+1 -or $body -ge $offsets.Count){throw 'Core key/body mismatch.'}
                $names+=if($body -lt $sample.bodies.Count){$sample.bodies[$body].name}else{"environment_$($body-$sample.bodies.Count)"}
                $shapes+=([int]$key.shape-$offsets[$body])
            }
            $native=@($pre[0].contacts|Where-Object {$_.body0 -ceq $names[0] -and $_.body1 -ceq $names[1] -and $_.shape0 -eq $shapes[0] -and $_.shape1 -eq $shapes[1]})
            $label="$($names[0])#$($shapes[0])>$($names[1])#$($shapes[1])"
            if($native.Count -ne 1){throw "Missing/duplicate native pair: $mesh/$($sample.frame) $label"}
            $n=$native[0];$pairs++
            for($side=0;$side -lt 2;$side++) {
                $a=$n."shapeWorld$side";$b=$pair.gather."body$side".shapeWorld
                CompareVector $a.position $b.position 'shapePosition' $false "$label/$side" $maximum
                CompareVector $a.rotation $b.rotation 'shapeRotation' $false "$label/$side" $maximum
            }
            $np=@($n.points|Where-Object {-not $_.disabled});$cp=@($pair.gather.points)
            if($np.Count -ne $cp.Count){$counts.Add(@{pair=$label;native=$np.Count;core=$cp.Count});continue}
            for($i=0;$i -lt $np.Count;$i++) {
                $pointsChecked++
                foreach($field in @('point0','point1','normal1')){CompareVector $np[$i].$field $cp[$i].$field $field $true "$label/$i" $maximum}
                foreach($field in @('hasAnchor','initialContact')) {
                    if($np[$i].$field -cne $cp[$i].$field){$flags.Add(@{pair=$label;point=$i;field=$field;native=$np[$i].$field;core=$cp[$i].$field})}
                }
                if($np[$i].hasAnchor -and $cp[$i].hasAnchor){foreach($field in @('anchor0','anchor1')){CompareVector $np[$i].$field $cp[$i].$field $field $true "$label/$i" $maximum}}
            }
        }
        $rows.Add([ordered]@{mesh=$case.setup.mesh;completedStep=$sample.frame;pairs=$pairs;points=$pointsChecked;maximum=$maximum;
            flagDifferences=$flags.ToArray();pointCountDifferences=$counts.ToArray();captureSha256=(Get-FileHash -LiteralPath $path).Hash})
    }
}
if($rows.Count -eq 0){throw 'No overlapping pre-solve contact input observations.'}
$result=[ordered]@{schemaVersion=1;observation='Actual world pre-solve shape transforms, local geometry and matched friction anchors versus Core Gather inputs. Later frames may inherit trajectory differences.';
    sourceSha256=(Get-FileHash -LiteralPath $WorldReference).Hash;samples=$rows.ToArray()}
$stream=[IO.File]::Open($Report,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write)
try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes(($result|ConvertTo-Json -Depth 15));$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
Write-Output "Compared $($rows.Count) pre-solve contact input frames."

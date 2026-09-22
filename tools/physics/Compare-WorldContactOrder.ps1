param(
    [Parameter(Mandatory=$true)][string]$WorldReference,
    [Parameter(Mandatory=$true)][string]$CaptureDirectory,
    [Parameter(Mandatory=$true)][string]$Report
)
$ErrorActionPreference='Stop'
if(-not [IO.Path]::IsPathFullyQualified($Report) -or (Test-Path -LiteralPath $Report)) { throw 'Report must be a new absolute file.' }
$world=Get-Content -LiteralPath $WorldReference -Raw | ConvertFrom-Json
$results=[Collections.Generic.List[object]]::new()
foreach($case in $world.cases) {
    $mesh=$case.setup.mesh.Split('.')[-1]
    foreach($sample in $case.samples) {
        if($sample.PSObject.Properties.Name -notcontains 'contactsAfterSolve') { continue }
        $path=Join-Path $CaptureDirectory "$mesh-$($sample.frame-1).json"
        if(-not(Test-Path -LiteralPath $path)) { continue }
        $capture=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        if($capture.input.mesh -ne $case.setup.mesh -or $capture.input.frame+1 -ne $sample.frame) { throw 'Mesh/frame mismatch.' }
        $offsets=@();$shapeCount=0
        for($i=0;$i -lt $sample.bodies.Count;$i++) {
            if($sample.bodies[$i].name -ne $capture.input.bodies[$i].name) { throw 'Body topology differs.' }
            $offsets+=$shapeCount;$shapeCount+=$sample.bodies[$i].shapeFilters.Count
        }
        for($i=0;$i -lt $case.setup.environment.Count;$i++) {$offsets+=($shapeCount+$i)}
        $native=@($sample.contactsAfterSolve | Where-Object {$_.inGraph -and -not $_.disabled -and -not $_.graphSleeping} | Sort-Object graphOrder)
        $nativeByPair=@{}
        foreach($pair in $native) {
            $a="$($pair.body0)#$($pair.shape0)";$b="$($pair.body1)#$($pair.shape1)"
            $key=(@($a,$b)|Sort-Object)-join '='
            if($nativeByPair.ContainsKey($key)) { throw 'Duplicate native shape pair.' }
            $nativeByPair[$key]=@{orientation="$a>$b";order=$pair.graphOrder}
        }
        $seen=[Collections.Generic.HashSet[string]]::new()
        $orientationDifferences=@();$orderDifferences=@();$coreOnly=@()
        $index=0
        foreach($pair in $capture.input.contacts) {
            if($null -eq $pair.key) { throw 'Core capture must contain prepared contact keys.' }
            $endpoints=@()
            for($side=0;$side -lt 2;$side++) {
                $body=[int]$pair."body$side";$shape=$pair.key."shape$side"
                if($shape.body -ne $body+1 -or $body -ge $offsets.Count) { throw 'Core key/body mismatch.' }
                $local=[int]$shape.shape-$offsets[$body]
                $count=if($body -lt $sample.bodies.Count){$sample.bodies[$body].shapeFilters.Count}else{1}
                if($local -lt 0 -or $local -ge $count) { throw 'Registry shape order differs.' }
                $name=if($body -lt $sample.bodies.Count){$sample.bodies[$body].name}else{"environment_$($body-$sample.bodies.Count)"}
                $endpoints+="$name#$local"
            }
            $key=($endpoints|Sort-Object)-join '='
            if(-not $seen.Add($key)) { throw 'Duplicate Core shape pair.' }
            $orientation=$endpoints-join '>'
            if(-not $nativeByPair.ContainsKey($key)) {$coreOnly+=$orientation}
            else {
                $expected=$nativeByPair[$key]
                if($expected.orientation -cne $orientation) {$orientationDifferences+=@{native=$expected.orientation;core=$orientation}}
                if($expected.order -ne $index) {$orderDifferences+=@{pair=$key;native=$expected.order;core=$index}}
            }
            $index++
        }
        $nativeOnly=@($nativeByPair.Keys | Where-Object {-not $seen.Contains($_)} | Sort-Object)
        $results.Add([ordered]@{mesh=$case.setup.mesh;completedStep=$sample.frame;nativePairs=$native.Count;corePairs=$index;
            orientationDifferences=$orientationDifferences;orderDifferences=$orderDifferences;nativeOnly=$nativeOnly;coreOnly=$coreOnly;
            captureSha256=(Get-FileHash -LiteralPath $path).Hash})
    }
}
if($results.Count -eq 0) {throw 'No overlapping diagnostic frames.'}
$output=[ordered]@{schemaVersion=1;observation='Actual oriented shape pairs and island graph order; world poses may differ; no same-input geometry or whole-trajectory equivalence';
    sourceSha256=(Get-FileHash -LiteralPath $WorldReference).Hash;samples=$results.ToArray()}
$stream=[IO.File]::Open($Report,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write)
try {$bytes=[Text.UTF8Encoding]::new($false).GetBytes(($output | ConvertTo-Json -Depth 12));$stream.Write($bytes,0,$bytes.Length)}
finally {$stream.Dispose()}
Write-Output "Compared $($results.Count) actual contact-order frames."

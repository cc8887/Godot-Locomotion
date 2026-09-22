param(
    [Parameter(Mandatory=$true)][string[]]$CaptureDirectories,
    [Parameter(Mandatory=$true)][string]$SetupDirectory,
    [Parameter(Mandatory=$true)][string]$RuntimeShapes,
    [Parameter(Mandatory=$true)][string]$OutputFile,
    [switch]$RequireCapturedCache,
    [switch]$RequireRetainedManifold,
    [string]$BoneName='hand_r'
)
$ErrorActionPreference='Stop'
if($RequireCapturedCache -and $RequireRetainedManifold) { throw 'Select either GJK or manifold restoration replay.' }
if(-not [IO.Path]::IsPathFullyQualified($OutputFile) -or (Test-Path -LiteralPath $OutputFile)) { throw 'Output must be a new absolute file.' }
$runtime=Get-Content -LiteralPath $RuntimeShapes -Raw | ConvertFrom-Json
$setups=@{}
foreach($file in Get-ChildItem -LiteralPath $SetupDirectory -Filter '*.json' -File) {
    $setup=Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    if($setups.ContainsKey($setup.mesh)) { throw 'Ambiguous mesh setup.' }
    $setups[$setup.mesh]=@{data=$setup;hash=(Get-FileHash -LiteralPath $file.FullName).Hash}
}
$cases=[Collections.Generic.List[object]]::new()
$seen=[Collections.Generic.HashSet[string]]::new()
foreach($directory in $CaptureDirectories) {
    foreach($file in Get-ChildItem -LiteralPath $directory -Filter '*.json' -File | Sort-Object Name) {
        $capture=Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        $captureInput=$capture.input
        if($capture.schemaVersion -ne 1 -or -not $setups.ContainsKey($captureInput.mesh)) { throw 'Capture/setup mismatch.' }
        $setup=$setups[$captureInput.mesh].data
        if($setup.hz -ne 120 -or [Math]::Abs($captureInput.dt-1.0/120) -gt 1e-12) { throw 'Expected 120 Hz capture.' }
        $floorBody=$setup.bodies.Count
        if($captureInput.bodies[$floorBody].name -ne "environment_$floorBody") { throw 'Environment slot naming differs.' }
        $pairs=@($captureInput.contacts | Where-Object { $captureInput.bodies[$_.body0].name -eq $BoneName -and $_.body1 -eq $floorBody })
        if($pairs.Count -eq 0) { continue }
        if($pairs.Count -ne 1 -or -not $seen.Add("$($captureInput.mesh)/$($captureInput.frame)")) { throw 'Ambiguous hand/floor capture.' }
        if($RequireCapturedCache -and $pairs[0].PSObject.Properties.Name -notcontains 'polygonQuery') { throw 'Capture lacks polygon query snapshots.' }
        if($RequireCapturedCache -and $null -eq $pairs[0].polygonQuery) { continue }
        $rig=@($runtime.meshes | Where-Object {$_.mesh -eq $captureInput.mesh})
        if($rig.Count -ne 1) { throw 'Missing runtime mesh.' }
        $body=@($rig[0].bodies | Where-Object {$_.bone -eq $BoneName})
        if($body.Count -ne 1 -or $body[0].runtimeShapes.Count -ne 1) { throw 'Expected one hand shape.' }
        $shape=$body[0].runtimeShapes[0];$floor=$setup.environment[0]
        if($shape.type -ne 'Box' -or $shape.wrapper -ne 'plain' -or $floor.geometry.type -ne 'box' -or $floor.kinematic) { throw 'Expected dynamic hand box and static floor box.' }
        $half0=@();$half1=@()
        for($axis=0;$axis -lt 3;$axis++) {
            if($shape.boundsMinCm[$axis] -ne -$shape.boundsMaxCm[$axis]) { throw 'Expected centered hand box.' }
            $half0+= $shape.boundsMaxCm[$axis];$half1+= $floor.geometry.size[$axis]*0.5
        }
        $g=$pairs[0].gather
        if($g.body0.inverseMass -le 0 -or $g.body1.inverseMass -ne 0) { throw 'Unexpected body motion.' }
        $row=[ordered]@{mesh=$captureInput.mesh;frame=$captureInput.frame;captureSha256=(Get-FileHash -LiteralPath $file.FullName).Hash;
            setupSha256=$setups[$captureInput.mesh].hash;half0=$half0;half1=$half1;margin0=$shape.marginCm;
            pose0=$g.body0.shapeWorld;pose1=$g.body1.shapeWorld;capturedPoints=$g.points}
        if($BoneName -ne 'hand_r') {$row.bone=$BoneName}
        if($RequireCapturedCache) {$row.cacheBefore=@($pairs[0].polygonQuery.before);$row.cacheAfter=@($pairs[0].polygonQuery.after)}
        if($RequireRetainedManifold) {
            $history=@($capture.historyInputs | Where-Object {$_.key.shape0.body -eq $pairs[0].body0+1 -and $_.key.shape1.body -eq $pairs[0].body1+1})
            if($history.Count -ne 1 -or $null -eq $history[0].retained) { throw 'Expected one captured retained manifold.' }
            $h=$history[0];$r=$h.retained
            if($r.points.Count -eq 0) { continue }
            if(($h.key | ConvertTo-Json -Compress -Depth 4) -ne ($r.key | ConvertTo-Json -Compress -Depth 4) -or
                $h.epoch -ne $r.epoch+1 -or $h.manifoldTolerance -ne $r.tolerance) { throw 'Restore input has ineligible identity, epoch or tolerance.' }
            $row.retained=$r;$row.key=$h.key;$row.epoch=$h.epoch;$row.cullDistance=$h.cullDistance
            $row.capturedRestored=$h.restored;$row.capturedDetected=@($h.detected)
        }
        $cases.Add($row)
    }
}
if($cases.Count -eq 0) { throw 'No supported hand/floor captures.' }
$scope=if($RequireCapturedCache){'Captured hand_r/static environment_0 poses, verified runtime box geometry and actual GJK query input/output; cull 6 cm'}else{'Captured hand_r/static environment_0 poses and verified runtime box geometry; cull 6 cm; actual GJK cache not captured'}
if($RequireRetainedManifold) {$scope='Captured hand_r/static environment_0 poses, retained nonempty manifolds, reference deltas and current cull; same identity/consecutive epoch verified; native restores geometry, not full-world ownership or lifecycle'}
$scope=$scope.Replace('hand_r',$BoneName)
$result=[ordered]@{schemaVersion=1;provenance=[ordered]@{runtimeShapesSha256=(Get-FileHash -LiteralPath $RuntimeShapes).Hash;
    scope=$scope};cases=$cases.ToArray()}
$stream=[IO.File]::Open($OutputFile,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write)
try { $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($result | ConvertTo-Json -Depth 20));$stream.Write($bytes,0,$bytes.Length) }
finally { $stream.Dispose() }
Write-Output "Prepared $($cases.Count) hand/floor poses."

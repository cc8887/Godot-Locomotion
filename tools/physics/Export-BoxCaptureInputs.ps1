param(
    [Parameter(Mandatory=$true)][string[]]$CaptureDirectories,
    [Parameter(Mandatory=$true)][string]$SetupDirectory,
    [Parameter(Mandatory=$true)][string]$RuntimeShapes,
    [Parameter(Mandatory=$true)][string]$OutputFile
)
$ErrorActionPreference='Stop'
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
        $pairs=@($captureInput.contacts | Where-Object { $captureInput.bodies[$_.body0].name -eq 'hand_r' -and $_.body1 -eq $floorBody })
        if($pairs.Count -eq 0) { continue }
        if($pairs.Count -ne 1 -or -not $seen.Add("$($captureInput.mesh)/$($captureInput.frame)")) { throw 'Ambiguous hand/floor capture.' }
        $rig=@($runtime.meshes | Where-Object {$_.mesh -eq $captureInput.mesh})
        if($rig.Count -ne 1) { throw 'Missing runtime mesh.' }
        $body=@($rig[0].bodies | Where-Object {$_.bone -eq 'hand_r'})
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
        $cases.Add([ordered]@{mesh=$captureInput.mesh;frame=$captureInput.frame;captureSha256=(Get-FileHash -LiteralPath $file.FullName).Hash;
            setupSha256=$setups[$captureInput.mesh].hash;half0=$half0;half1=$half1;margin0=$shape.marginCm;
            pose0=$g.body0.shapeWorld;pose1=$g.body1.shapeWorld;capturedPoints=$g.points})
    }
}
if($cases.Count -eq 0) { throw 'No supported hand/floor captures.' }
$result=[ordered]@{schemaVersion=1;provenance=[ordered]@{runtimeShapesSha256=(Get-FileHash -LiteralPath $RuntimeShapes).Hash;
    scope='Captured hand_r/static environment_0 poses and verified runtime box geometry; cull 6 cm; actual GJK cache not captured'};cases=$cases.ToArray()}
$stream=[IO.File]::Open($OutputFile,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write)
try { $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($result | ConvertTo-Json -Depth 20));$stream.Write($bytes,0,$bytes.Length) }
finally { $stream.Dispose() }
Write-Output "Prepared $($cases.Count) hand/floor poses."

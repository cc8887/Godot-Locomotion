param(
    [Parameter(Mandatory=$true)][string]$WorldReference,
    [Parameter(Mandatory=$true)][string]$RuntimeShapes,
    [Parameter(Mandatory=$true)][string]$OutputFile
)
$ErrorActionPreference='Stop'
if(-not [IO.Path]::IsPathFullyQualified($OutputFile) -or (Test-Path -LiteralPath $OutputFile)) { throw 'Output must be a new absolute file.' }
$world=Get-Content -LiteralPath $WorldReference -Raw | ConvertFrom-Json
$runtime=Get-Content -LiteralPath $RuntimeShapes -Raw | ConvertFrom-Json
$meshes=@()
foreach($mesh in $runtime.meshes) {
    $cases=@($world.cases | Where-Object { $_.setup.mesh -eq $mesh.mesh })
    if($cases.Count -ne 1) { throw 'Expected one native case per mesh.' }
    $frames=@($cases[0].samples | Where-Object { $_.frame -gt 0 -and $_.bodies[0].shapeFilters })
    if($frames.Count -lt 3) { throw 'Need at least three observed filter frames.' }
    $bodies=@()
    foreach($body in $mesh.bodies) {
        $observed=$frames[0].bodies[$body.index]
        if($observed.name -ne $body.bone -or $observed.shapeFilters.Count -ne $body.runtimeShapes.Count) { throw 'Native body/shape binding differs.' }
        $filters=@()
        foreach($shape in $body.runtimeShapes) {
            $s=$observed.shapeFilters[$shape.nativeIndex]
            if($null -eq $s.channel -or $s.blockChannels -notmatch '^[0-9A-F]{16}$' -or $s.overlapChannels -notmatch '^[0-9A-F]{16}$') { throw 'Missing structured native filter.' }
            foreach($frame in $frames) {
                $b=$frame.bodies[$body.index]
                if($b.name -ne $body.bone -or ($b.shapeFilters[$shape.nativeIndex] | ConvertTo-Json -Compress) -cne ($s | ConvertTo-Json -Compress)) { throw 'Filter changes across observation window.' }
            }
            $filters+= [ordered]@{authoredIndex=$shape.authoredIndex;nativeIndex=$shape.nativeIndex;simulation=$s.simulation;query=$s.query;channel=$s.channel;blockChannels=$s.blockChannels;overlapChannels=$s.overlapChannels;maskFilter=$s.maskFilter}
        }
        $bodies+=[ordered]@{index=$body.index;bone=$body.bone;shapes=$filters}
    }
    $meshes+=[ordered]@{mesh=$mesh.mesh;bodies=$bodies}
}
$result=[ordered]@{schemaVersion=1;observation='Native full-world skeletal component: PhysicsBody, QueryAndPhysics, all channels Block; per-body effective shape filters after physics creation';sourceSha256=(Get-FileHash -LiteralPath $WorldReference).Hash;runtimeShapesSha256=(Get-FileHash -LiteralPath $RuntimeShapes).Hash;meshes=$meshes}
$bytes=[Text.UTF8Encoding]::new($false).GetBytes(($result | ConvertTo-Json -Depth 20 -Compress))
$stream=[IO.File]::Open($OutputFile,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write)
try {$stream.Write($bytes,0,$bytes.Length)} finally {$stream.Dispose()}

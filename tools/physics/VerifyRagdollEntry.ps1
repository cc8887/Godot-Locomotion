param([Parameter(Mandatory)][string]$Reference)
$ErrorActionPreference = 'Stop'
$data = Get-Content -LiteralPath $Reference -Raw | ConvertFrom-Json
if ($data.schemaVersion -ne 1 -or $data.cases.Count -ne 4 -or $data.alsMeshCollisionEnabled -ne 1 -or $data.alsDeferKinematicBoneUpdate) {
    throw 'Entry schema, coverage or ALS defaults changed.'
}
$seen = @{}
$checks = 0
foreach ($case in $data.cases) {
    $model = ($case.mesh -split '\.')[-1]
    $count = switch ($model) { 'Mannequin' { 19 } 'AnimMan' { 21 } default { throw 'Unexpected mesh.' } }
    $key = "$model/$($case.startedQueryOnly)"
    if ($seen.ContainsKey($key)) { throw 'Duplicate entry case.' }
    $seen[$key] = $true
    if (($case.stages.stage -join ',') -ne 'created,seeded,collision_enabled,simulate,reset_asset_types') { throw 'Wrong stage order.' }
    $seed = $case.stages[1].bodies
    foreach ($stage in $case.stages) {
        if ($stage.bodies.Count -ne $count -or ($stage.ownerVelocity -join ',') -ne '300,-400,120') { throw 'Incomplete stage.' }
        if ($stage.stage -in @('created', 'seeded')) { continue }
        for ($i = 0; $i -lt $count; $i++) {
            $body = $stage.bodies[$i]
            if ($body.bone -ne $seed[$i].bone -or $body.physicsType -ne $seed[$i].physicsType -or
                ($body.linear -join ',') -ne ($seed[$i].linear -join ',') -or
                ($body.angular -join ',') -ne ($seed[$i].angular -join ',')) { throw "Entry changed seeded body velocity: $key/$($stage.stage)/$i" }
            if ($stage.stage -eq 'reset_asset_types' -and $body.simulating -ne ($body.physicsType -ne 1)) { throw 'Asset simulation type lost.' }
            $checks++
        }
    }
    for ($i = 0; $i -lt $count; $i++) {
        if (($seed[$i].linear -join ',') -ne (@((600 + $i), (40 - $i), -80) -join ',')) { throw 'Controlled seed was not accepted.' }
    }
}
Write-Output "RAGDOLL_ENTRY_REFERENCE_OK cases=$($seen.Count) retained_body_stages=$checks"

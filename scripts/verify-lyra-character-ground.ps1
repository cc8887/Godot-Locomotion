param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug',
      [Parameter(Mandatory=$true)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$EvidenceTag)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$groundRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$groundPrefix=Join-Path $groundRoot "artifacts/lyra-analysis/character-ground-$($Configuration.ToLowerInvariant())-$EvidenceTag"
$groundDebug=Join-Path $groundRoot '.godot/mono/temp/bin/Debug'
$groundOptimize=Join-Path $groundRoot '.godot/mono/temp/bin/ExportRelease'
$groundFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
$groundSummary="$groundPrefix-verification.json"
if(Test-Path -LiteralPath $groundSummary){throw 'Preserve ground evidence.'}
if(@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains($groundRoot)}).Count){throw 'Workspace Godot is running.'}
$groundRuns=@();$groundAssemblies=@{};$groundOriginal=@{}
$groundBackup="$groundPrefix-debug-backup";$groundCopied=$false;$groundRestored=$false
try{
    if($Configuration -eq 'Optimize'){
        if(Test-Path -LiteralPath $groundBackup){throw 'Preserve ground assembly backup.'}
        New-Item -ItemType Directory -Path $groundBackup | Out-Null
        foreach($groundFile in $groundFiles){
            $groundOriginal[$groundFile]=(Get-FileHash -LiteralPath (Join-Path $groundDebug $groundFile)).Hash
            Copy-Item -LiteralPath (Join-Path $groundDebug $groundFile) -Destination (Join-Path $groundBackup $groundFile)
        }
        $groundCopied=$true
        foreach($groundFile in $groundFiles){Copy-Item -LiteralPath (Join-Path $groundOptimize $groundFile) -Destination (Join-Path $groundDebug $groundFile)}
    }
    foreach($groundFile in $groundFiles){$groundAssemblies[$groundFile]=(Get-FileHash -LiteralPath (Join-Path $groundDebug $groundFile)).Hash}
    foreach($groundHz in @(30,60,120)){
        $groundLog="$groundPrefix-steps-$groundHz.log";$groundReport="$groundPrefix-steps-$groundHz.json"
        if((Test-Path -LiteralPath $groundLog) -or (Test-Path -LiteralPath $groundReport)){throw 'Preserve step run.'}
        & (Join-Path $groundRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe') --headless --path $groundRoot res://scenes/tests/lyra_character_step_physics_smoke.tscn -- "--step-physics-hz=$groundHz" "--step-physics-report=$groundReport" *> $groundLog
        $groundExit=$LASTEXITCODE
        $groundText=Get-Content -LiteralPath $groundLog -Raw
        $groundPassed=$groundExit -eq 0 -and $groundText.Contains('LYRA_CHARACTER_STEP_PHYSICS_GODOT_OK') -and $groundText -notmatch '(?m)^\s*(ERROR|WARNING):'
        $groundRuns+=@{kind='steps';hz=$groundHz;exitCode=$groundExit;passed=$groundPassed;log=$groundLog;logSha256=(Get-FileHash -LiteralPath $groundLog).Hash;
            report=$groundReport;reportSha256=if(Test-Path -LiteralPath $groundReport){(Get-FileHash -LiteralPath $groundReport).Hash}else{''}}
        Write-Output "Ground configuration=$Configuration hz=$groundHz passed=$groundPassed exit=$groundExit"
        if(-not $groundPassed){throw "Step physics failed: $groundHz"}
    }
    $groundLog="$groundPrefix-queries.log";$groundReport="$groundPrefix-queries.json"
    if((Test-Path -LiteralPath $groundLog) -or (Test-Path -LiteralPath $groundReport)){throw 'Preserve ground query diagnostic.'}
    & (Join-Path $groundRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe') --headless --path $groundRoot res://scenes/tests/lyra_character_ground_query_smoke.tscn -- "--ground-query-report=$groundReport" *> $groundLog
    $groundExit=$LASTEXITCODE
    $groundText=Get-Content -LiteralPath $groundLog -Raw
    if(-not (Test-Path -LiteralPath $groundReport) -or -not $groundText.Contains('LYRA_CHARACTER_GROUND_QUERY_DIAGNOSTIC') -or $groundText -match '(?m)^\s*(ERROR|WARNING):'){throw 'Ground query did not complete cleanly.'}
    $groundData=Get-Content -LiteralPath $groundReport -Raw | ConvertFrom-Json
    $groundExpectedExit=if($groundData.comparisonPassed){0}else{1}
    if($groundExit -ne $groundExpectedExit -or $groundData.queries -ne 32 -or $groundData.replayedPhysicalObservations){throw 'Invalid ground query diagnostic.'}
    $groundRuns+=@{kind='query';exitCode=$groundExit;comparisonPassed=$groundData.comparisonPassed;mismatches=$groundData.mismatches;
        log=$groundLog;logSha256=(Get-FileHash -LiteralPath $groundLog).Hash;report=$groundReport;reportSha256=(Get-FileHash -LiteralPath $groundReport).Hash}
    Write-Output "Ground query configuration=$Configuration comparisonPassed=$($groundData.comparisonPassed) mismatches=$($groundData.mismatches)"
}finally{
    if($groundCopied){
        foreach($groundFile in $groundFiles){Copy-Item -LiteralPath (Join-Path $groundBackup $groundFile) -Destination (Join-Path $groundDebug $groundFile);if((Get-FileHash -LiteralPath (Join-Path $groundDebug $groundFile)).Hash -ne $groundOriginal[$groundFile]){throw 'Ground Debug restoration mismatch.'}}
        $groundRestored=$true
    }
    $groundPhysics=@($groundRuns | Where-Object {$_.kind -eq 'steps'})
    $groundQuery=@($groundRuns | Where-Object {$_.kind -eq 'query'})
    @{configuration=$Configuration;evidenceTag=$EvidenceTag;assemblies=$groundAssemblies;runs=$groundRuns;debugRestored=$groundRestored;
      stepPhysicsPassed=($groundPhysics.Count -eq 3 -and @($groundPhysics | Where-Object {-not $_.passed}).Count -eq 0);
      diagnosticsCompleted=($groundQuery.Count -eq 1);groundQueryComparisonPassed=($groundQuery.Count -eq 1 -and $groundQuery[0].comparisonPassed);
      nativeWorldTrajectoryParity=$false;completeAcceptance=$false;goalComplete=$false} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $groundSummary -Encoding utf8
}
if(-not $groundData.comparisonPassed){exit 1}

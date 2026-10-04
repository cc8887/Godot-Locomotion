param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug',
      [Parameter(Mandatory=$true)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$EvidenceTag)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$floorRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$floorPrefix=Join-Path $floorRoot "artifacts/lyra-analysis/character-floor-$($Configuration.ToLowerInvariant())-$EvidenceTag"
$floorDebug=Join-Path $floorRoot '.godot/mono/temp/bin/Debug'
$floorOptimize=Join-Path $floorRoot '.godot/mono/temp/bin/ExportRelease'
$floorFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
$floorSummary="$floorPrefix-verification.json"
if(Test-Path -LiteralPath $floorSummary){throw 'Preserve floor evidence.'}
if(@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains($floorRoot)}).Count){throw 'Workspace Godot is running.'}
$floorRuns=@();$floorAssemblies=@{};$floorOriginal=@{}
$floorBackup="$floorPrefix-debug-backup";$floorCopied=$false;$floorRestored=$false
try{
    if($Configuration -eq 'Optimize'){
        if(Test-Path -LiteralPath $floorBackup){throw 'Preserve floor assembly backup.'}
        New-Item -ItemType Directory -Path $floorBackup | Out-Null
        foreach($floorFile in $floorFiles){
            $floorOriginal[$floorFile]=(Get-FileHash -LiteralPath (Join-Path $floorDebug $floorFile)).Hash
            Copy-Item -LiteralPath (Join-Path $floorDebug $floorFile) -Destination (Join-Path $floorBackup $floorFile)
        }
        $floorCopied=$true
        foreach($floorFile in $floorFiles){Copy-Item -LiteralPath (Join-Path $floorOptimize $floorFile) -Destination (Join-Path $floorDebug $floorFile)}
    }
    foreach($floorFile in $floorFiles){$floorAssemblies[$floorFile]=(Get-FileHash -LiteralPath (Join-Path $floorDebug $floorFile)).Hash}
    foreach($floorHz in @(30,60,120)){
        $floorLog="$floorPrefix-physics-$floorHz.log";$floorReport="$floorPrefix-physics-$floorHz.json"
        if((Test-Path -LiteralPath $floorLog) -or (Test-Path -LiteralPath $floorReport)){throw 'Preserve floor run.'}
        & (Join-Path $floorRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe') --headless --path $floorRoot res://scenes/tests/lyra_character_floor_physics_smoke.tscn -- "--floor-physics-hz=$floorHz" "--floor-physics-report=$floorReport" *> $floorLog
        $floorExit=$LASTEXITCODE
        Add-Content -LiteralPath $floorLog -Value "CHARACTER_FLOOR_PROCESS_EXIT=$floorExit" -Encoding utf8
        $floorText=Get-Content -LiteralPath $floorLog -Raw -Encoding utf8
        $floorPassed=$floorExit -eq 0 -and $floorText.Contains('LYRA_CHARACTER_FLOOR_PHYSICS_GODOT_OK') -and $floorText -notmatch '(?m)^\s*(ERROR|WARNING):'
        $floorRuns+=@{kind='physics';hz=$floorHz;exitCode=$floorExit;passed=$floorPassed;log=$floorLog;logSha256=(Get-FileHash -LiteralPath $floorLog).Hash;
            report=$floorReport;reportSha256=if(Test-Path -LiteralPath $floorReport){(Get-FileHash -LiteralPath $floorReport).Hash}else{''}}
        Write-Output "Floor configuration=$Configuration hz=$floorHz passed=$floorPassed exit=$floorExit"
        if(-not $floorPassed){throw "Floor physics failed: $floorHz"}
    }
    $floorLog="$floorPrefix-queries.log";$floorReport="$floorPrefix-queries.json"
    if((Test-Path -LiteralPath $floorLog) -or (Test-Path -LiteralPath $floorReport)){throw 'Preserve floor query diagnostic.'}
    & (Join-Path $floorRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe') --headless --path $floorRoot res://scenes/tests/lyra_character_floor_query_smoke.tscn -- "--floor-query-tag=$EvidenceTag" "--floor-query-report=$floorReport" *> $floorLog
    $floorExit=$LASTEXITCODE
    Add-Content -LiteralPath $floorLog -Value "CHARACTER_FLOOR_QUERY_EXIT=$floorExit" -Encoding utf8
    $floorText=Get-Content -LiteralPath $floorLog -Raw -Encoding utf8
    if(-not (Test-Path -LiteralPath $floorReport) -or -not $floorText.Contains('LYRA_CHARACTER_FLOOR_QUERY_DIAGNOSTIC') -or $floorText -match '(?m)^\s*(ERROR|WARNING):'){throw 'Floor query did not complete cleanly.'}
    $floorData=Get-Content -LiteralPath $floorReport -Raw -Encoding utf8 | ConvertFrom-Json
    $floorExpectedExit=if($floorData.comparisonPassed){0}else{1}
    if($floorExit -ne $floorExpectedExit -or $floorData.queries -ne 144 -or $floorData.queryMutatesActor){throw 'Invalid floor query diagnostic.'}
    $floorRuns+=@{kind='query';exitCode=$floorExit;comparisonPassed=$floorData.comparisonPassed;mismatches=$floorData.mismatches;
        log=$floorLog;logSha256=(Get-FileHash -LiteralPath $floorLog).Hash;report=$floorReport;reportSha256=(Get-FileHash -LiteralPath $floorReport).Hash}
    Write-Output "Floor query configuration=$Configuration comparisonPassed=$($floorData.comparisonPassed) mismatches=$($floorData.mismatches)"
}finally{
    if($floorCopied){
        foreach($floorFile in $floorFiles){Copy-Item -LiteralPath (Join-Path $floorBackup $floorFile) -Destination (Join-Path $floorDebug $floorFile);if((Get-FileHash -LiteralPath (Join-Path $floorDebug $floorFile)).Hash -ne $floorOriginal[$floorFile]){throw 'Floor Debug restoration mismatch.'}}
        $floorRestored=$true
    }
    $floorPhysics=@($floorRuns | Where-Object {$_.kind -eq 'physics'})
    $floorQuery=@($floorRuns | Where-Object {$_.kind -eq 'query'})
    @{configuration=$Configuration;evidenceTag=$EvidenceTag;assemblies=$floorAssemblies;runs=$floorRuns;debugRestored=$floorRestored;
      floorPhysicsPassed=($floorPhysics.Count -eq 3 -and @($floorPhysics | Where-Object {-not $_.passed}).Count -eq 0);
      diagnosticsCompleted=($floorQuery.Count -eq 1);floorQueryComparisonPassed=($floorQuery.Count -eq 1 -and $floorQuery[0].comparisonPassed);
      nativeWorldTrajectoryParity=$false;completeAcceptance=$false;goalComplete=$false} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $floorSummary -Encoding utf8
}
if(-not $floorData.comparisonPassed){exit 1}

param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug',
      [Parameter(Mandatory=$true)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$EvidenceTag)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$airRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$airPrefix=Join-Path $airRoot "artifacts/lyra-analysis/character-air-$($Configuration.ToLowerInvariant())-$EvidenceTag"
$airDebug=Join-Path $airRoot '.godot/mono/temp/bin/Debug'
$airOptimize=Join-Path $airRoot '.godot/mono/temp/bin/ExportRelease'
$airFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
$airSummary="$airPrefix-verification.json"
if(Test-Path -LiteralPath $airSummary){throw 'Preserve air evidence.'}
if(@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains($airRoot)}).Count){throw 'Workspace Godot is running.'}
$airRuns=@();$airAssemblies=@{};$airOriginal=@{}
$airBackup="$airPrefix-debug-backup";$airCopied=$false;$airRestored=$false
try{
    if($Configuration -eq 'Optimize'){
        if(Test-Path -LiteralPath $airBackup){throw 'Preserve air assembly backup.'}
        New-Item -ItemType Directory -Path $airBackup | Out-Null
        foreach($airFile in $airFiles){
            $airOriginal[$airFile]=(Get-FileHash -LiteralPath (Join-Path $airDebug $airFile)).Hash
            Copy-Item -LiteralPath (Join-Path $airDebug $airFile) -Destination (Join-Path $airBackup $airFile)
        }
        $airCopied=$true
        foreach($airFile in $airFiles){Copy-Item -LiteralPath (Join-Path $airOptimize $airFile) -Destination (Join-Path $airDebug $airFile)}
    }
    foreach($airFile in $airFiles){$airAssemblies[$airFile]=(Get-FileHash -LiteralPath (Join-Path $airDebug $airFile)).Hash}
    $airLog="$airPrefix-queries.log";$airReport="$airPrefix-queries.json"
    if((Test-Path -LiteralPath $airLog) -or (Test-Path -LiteralPath $airReport)){throw 'Preserve air query run.'}
    & (Join-Path $airRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe') --headless --path $airRoot res://scenes/tests/lyra_character_air_query_smoke.tscn -- "--air-query-report=$airReport" *> $airLog
    $airExit=$LASTEXITCODE
    $airText=Get-Content -LiteralPath $airLog -Raw -Encoding utf8
    if(-not (Test-Path -LiteralPath $airReport) -or -not $airText.Contains('LYRA_CHARACTER_AIR_QUERY_DIAGNOSTIC') -or $airText -match '(?m)^\s*(ERROR|WARNING):'){throw 'Air diagnostic did not complete cleanly.'}
    $airData=Get-Content -LiteralPath $airReport -Raw -Encoding utf8 | ConvertFrom-Json
    $airExpectedExit=if($airData.comparisonPassed){0}else{1}
    if($airExit -ne $airExpectedExit -or $airData.queries -ne 112 -or $airData.replayedPhysicalObservations){throw 'Invalid air query result.'}
    $airRuns+=@{exitCode=$airExit;comparisonPassed=$airData.comparisonPassed;mismatches=$airData.mismatches;
        log=$airLog;logSha256=(Get-FileHash -LiteralPath $airLog).Hash;report=$airReport;reportSha256=(Get-FileHash -LiteralPath $airReport).Hash}
    Write-Output "Air configuration=$Configuration completed=true comparisonPassed=$($airData.comparisonPassed) mismatches=$($airData.mismatches)"

}finally{
    if($airCopied){
        foreach($airFile in $airFiles){Copy-Item -LiteralPath (Join-Path $airBackup $airFile) -Destination (Join-Path $airDebug $airFile);if((Get-FileHash -LiteralPath (Join-Path $airDebug $airFile)).Hash -ne $airOriginal[$airFile]){throw 'Air Debug restoration mismatch.'}}
        $airRestored=$true
    }
    @{configuration=$Configuration;evidenceTag=$EvidenceTag;assemblies=$airAssemblies;runs=$airRuns;debugRestored=$airRestored;
      referenceSha256=(Get-FileHash -LiteralPath (Join-Path $airRoot 'artifacts/lyra-analysis/character-air-v3-reference.json')).Hash;
      diagnosticsCompleted=($airRuns.Count -eq 1);comparisonPassed=($airRuns.Count -eq 1 -and @($airRuns | Where-Object {-not $_.comparisonPassed}).Count -eq 0);
      nativeWorldAirParity=$false;completeAcceptance=$false;goalComplete=$false} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $airSummary -Encoding utf8
}
if(@($airRuns | Where-Object {-not $_.comparisonPassed}).Count){exit 1}

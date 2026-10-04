param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug',
      [Parameter(Mandatory=$true)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$EvidenceTag)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$trajectoryRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$trajectoryPrefix=Join-Path $trajectoryRoot "artifacts/lyra-analysis/character-trajectory-$($Configuration.ToLowerInvariant())-$EvidenceTag"
$trajectoryDebug=Join-Path $trajectoryRoot '.godot/mono/temp/bin/Debug'
$trajectoryOptimize=Join-Path $trajectoryRoot '.godot/mono/temp/bin/ExportRelease'
$trajectoryFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
$trajectorySummary="$trajectoryPrefix-verification.json"
if(Test-Path -LiteralPath $trajectorySummary){throw 'Preserve trajectory evidence.'}
if(@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains($trajectoryRoot)}).Count){throw 'Workspace Godot is running.'}
$trajectoryRuns=@();$trajectoryAssemblies=@{};$trajectoryOriginal=@{}
$trajectoryBackup="$trajectoryPrefix-debug-backup";$trajectoryCopied=$false;$trajectoryRestored=$false
try{
    if($Configuration -eq 'Optimize'){
        if(Test-Path -LiteralPath $trajectoryBackup){throw 'Preserve trajectory assembly backup.'}
        New-Item -ItemType Directory -Path $trajectoryBackup | Out-Null
        foreach($trajectoryFile in $trajectoryFiles){
            $trajectoryOriginal[$trajectoryFile]=(Get-FileHash -LiteralPath (Join-Path $trajectoryDebug $trajectoryFile)).Hash
            Copy-Item -LiteralPath (Join-Path $trajectoryDebug $trajectoryFile) -Destination (Join-Path $trajectoryBackup $trajectoryFile)
        }
        $trajectoryCopied=$true
        foreach($trajectoryFile in $trajectoryFiles){Copy-Item -LiteralPath (Join-Path $trajectoryOptimize $trajectoryFile) -Destination (Join-Path $trajectoryDebug $trajectoryFile)}
    }
    foreach($trajectoryFile in $trajectoryFiles){$trajectoryAssemblies[$trajectoryFile]=(Get-FileHash -LiteralPath (Join-Path $trajectoryDebug $trajectoryFile)).Hash}
    foreach($trajectoryHz in @(30,60,120)){
        $trajectoryLog="$trajectoryPrefix-$trajectoryHz.log";$trajectoryReport="$trajectoryPrefix-$trajectoryHz.json"
        if((Test-Path -LiteralPath $trajectoryLog) -or (Test-Path -LiteralPath $trajectoryReport)){throw 'Preserve trajectory run.'}
        & (Join-Path $trajectoryRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe') --headless --path $trajectoryRoot res://scenes/tests/lyra_character_trajectory_smoke.tscn -- "--trajectory-hz=$trajectoryHz" "--trajectory-tag=$EvidenceTag" "--trajectory-report=$trajectoryReport" *> $trajectoryLog
        $trajectoryExit=$LASTEXITCODE
        Add-Content -LiteralPath $trajectoryLog -Value "CHARACTER_TRAJECTORY_PROCESS_EXIT=$trajectoryExit" -Encoding utf8
        $trajectoryText=Get-Content -LiteralPath $trajectoryLog -Raw -Encoding utf8
        if(-not (Test-Path -LiteralPath $trajectoryReport) -or -not $trajectoryText.Contains('LYRA_CHARACTER_TRAJECTORY_DIAGNOSTIC') -or $trajectoryText -match '(?m)^\s*(ERROR|WARNING):'){throw 'Trajectory diagnostic did not complete cleanly.'}
        $trajectoryData=Get-Content -LiteralPath $trajectoryReport -Raw -Encoding utf8 | ConvertFrom-Json
        $trajectoryExpectedExit=if($trajectoryData.comparisonPassed){0}else{1}
        if($trajectoryExit -ne $trajectoryExpectedExit -or $trajectoryData.frames -ne $trajectoryHz*8 -or $trajectoryData.moves -ne $trajectoryData.frames -or $trajectoryData.retries -ne $trajectoryData.frames -or $trajectoryData.replayedPhysicalObservations){throw 'Invalid trajectory diagnostic result.'}
        $trajectoryRuns+=@{hz=$trajectoryHz;exitCode=$trajectoryExit;comparisonPassed=$trajectoryData.comparisonPassed;
            mismatchFrames=$trajectoryData.mismatchFrames;groundMismatchFrames=$trajectoryData.groundMismatchFrames;
            maxPositionCm=$trajectoryData.maxPositionCm;maxPlanarCm=$trajectoryData.maxPlanarCm;maxVerticalCm=$trajectoryData.maxVerticalCm;
            maxVelocityCmps=$trajectoryData.maxVelocityCmps;log=$trajectoryLog;logSha256=(Get-FileHash -LiteralPath $trajectoryLog).Hash;
            report=$trajectoryReport;reportSha256=(Get-FileHash -LiteralPath $trajectoryReport).Hash}
        Write-Output "Trajectory configuration=$Configuration hz=$trajectoryHz completed=true comparisonPassed=$($trajectoryData.comparisonPassed) mismatches=$($trajectoryData.mismatchFrames)"
    }
}finally{
    if($trajectoryCopied){
        foreach($trajectoryFile in $trajectoryFiles){Copy-Item -LiteralPath (Join-Path $trajectoryBackup $trajectoryFile) -Destination (Join-Path $trajectoryDebug $trajectoryFile);if((Get-FileHash -LiteralPath (Join-Path $trajectoryDebug $trajectoryFile)).Hash -ne $trajectoryOriginal[$trajectoryFile]){throw 'Trajectory Debug restoration mismatch.'}}
        $trajectoryRestored=$true
    }
    @{configuration=$Configuration;evidenceTag=$EvidenceTag;assemblies=$trajectoryAssemblies;runs=$trajectoryRuns;debugRestored=$trajectoryRestored;
      referenceSha256=(Get-FileHash -LiteralPath (Join-Path $trajectoryRoot 'artifacts/lyra-analysis/character-motor-trajectory-v1-reference.json')).Hash;
      diagnosticsCompleted=($trajectoryRuns.Count -eq 3);comparisonPassed=($trajectoryRuns.Count -eq 3 -and @($trajectoryRuns | Where-Object {-not $_.comparisonPassed}).Count -eq 0);
      nativeWorldTrajectoryParity=$false;completeAcceptance=$false;goalComplete=$false} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $trajectorySummary -Encoding utf8
}
if(@($trajectoryRuns | Where-Object {-not $_.comparisonPassed}).Count){exit 1}

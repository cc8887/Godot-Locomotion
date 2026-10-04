param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug',
      [Parameter(Mandatory=$true)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$EvidenceTag)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$motorRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$motorPrefix=Join-Path $motorRoot "artifacts/lyra-analysis/character-motor-$($Configuration.ToLowerInvariant())-$EvidenceTag"
$motorDebug=Join-Path $motorRoot '.godot/mono/temp/bin/Debug'
$motorOptimize=Join-Path $motorRoot '.godot/mono/temp/bin/ExportRelease'
$motorFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
$motorReport="$motorPrefix-verification.json"
if(Test-Path -LiteralPath $motorReport){throw 'Preserve motor evidence.'}
if(@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains($motorRoot)}).Count){throw 'Workspace Godot is running.'}
$motorRuns=@();$motorAssemblies=@{};$motorBackup="$motorPrefix-debug-backup";$motorOriginal=@{};$motorCopied=$false;$motorRestored=$false
try{
    if($Configuration -eq 'Optimize'){
        if(Test-Path -LiteralPath $motorBackup){throw 'Preserve motor assembly backup.'}
        New-Item -ItemType Directory -Path $motorBackup | Out-Null
        foreach($motorFile in $motorFiles){$motorOriginal[$motorFile]=(Get-FileHash -LiteralPath (Join-Path $motorDebug $motorFile)).Hash;Copy-Item -LiteralPath (Join-Path $motorDebug $motorFile) -Destination (Join-Path $motorBackup $motorFile)}
        $motorCopied=$true
        foreach($motorFile in $motorFiles){Copy-Item -LiteralPath (Join-Path $motorOptimize $motorFile) -Destination (Join-Path $motorDebug $motorFile)}
    }
    foreach($motorFile in $motorFiles){$motorAssemblies[$motorFile]=(Get-FileHash -LiteralPath (Join-Path $motorDebug $motorFile)).Hash}
    $motorCases=@()
    foreach($motorHz in @(30,60,120)){
        $motorCases+=@{name="physics-$motorHz";scene='lyra_character_movement_physics_smoke';marker='LYRA_CHARACTER_MOTOR_PHYSICS_GODOT_OK';args=@("--character-motor-hz=$motorHz","--character-motor-report=$motorPrefix-physics-$motorHz.json")}
        $motorCases+=@{name="ordinary-$motorHz";scene='';marker='LYRA_MAIN_MULTI_DEMO_GODOT_OK';args=@('--locomotion=lyra','--lyra-profile=rifle','--lyra-characters=10','--lyra-main-smoke','--lyra-main-retry','--lyra-main-rebind','--lyra-main-weapons',"--lyra-main-hz=$motorHz","--lyra-main-report=$motorPrefix-ordinary-$motorHz.json")}
    }
    $motorCases+=@{name='root-60';scene='lyra_root_movement_physics_smoke';marker='LYRA_ROOT_MOVEMENT_PHYSICS_GODOT_OK';args=@('--root-movement-hz=60')}
    $motorCases+=@{name='warp-60';scene='lyra_motion_warping_physics_smoke';marker='LYRA_MOTION_WARPING_PHYSICS_GODOT_OK';args=@('--warp-physics-hz=60',"--warp-physics-report=$motorPrefix-warp-60.json")}
    $motorCases+=@{name='emote-60';scene='lyra_emote_physics_smoke';marker='LYRA_EMOTE_PHYSICS_GODOT_OK';args=@('--emote-hz=60',"--emote-report=$motorPrefix-emote-60.json")}
    foreach($motorCase in $motorCases){
        $motorLog="$motorPrefix-$($motorCase.name).log"
        if(Test-Path -LiteralPath $motorLog){throw 'Preserve motor log.'}
        $motorArgs=@('--headless','--path',$motorRoot)
        if($motorCase.scene){$motorArgs+="res://scenes/tests/$($motorCase.scene).tscn"}
        $motorArgs+='--';$motorArgs+=$motorCase.args
        & (Join-Path $motorRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe') @motorArgs *> $motorLog
        $motorExit=$LASTEXITCODE
        Add-Content -LiteralPath $motorLog -Value "CHARACTER_MOTOR_PROCESS_EXIT=$motorExit" -Encoding utf8
        $motorText=Get-Content -LiteralPath $motorLog -Raw -Encoding utf8
        $motorPassed=$motorExit -eq 0 -and $motorText.Contains($motorCase.marker) -and $motorText -notmatch '(?m)^\s*(ERROR|WARNING):'
        $motorRuns+=@{name=$motorCase.name;exitCode=$motorExit;passed=$motorPassed;log=$motorLog;logSha256=(Get-FileHash -LiteralPath $motorLog).Hash}
        Write-Output "Character motor configuration=$Configuration case=$($motorCase.name) passed=$motorPassed exit=$motorExit"
        if(-not $motorPassed){throw "Motor validation failed: $($motorCase.name)"}
    }
}finally{
    if($motorCopied){
        foreach($motorFile in $motorFiles){Copy-Item -LiteralPath (Join-Path $motorBackup $motorFile) -Destination (Join-Path $motorDebug $motorFile);if((Get-FileHash -LiteralPath (Join-Path $motorDebug $motorFile)).Hash -ne $motorOriginal[$motorFile]){throw 'Motor Debug restoration mismatch.'}}
        $motorRestored=$true
    }
    @{configuration=$Configuration;evidenceTag=$EvidenceTag;assemblies=$motorAssemblies;runs=$motorRuns;debugRestored=$motorRestored;
        passed=($motorRuns.Count -eq 9 -and @($motorRuns | Where-Object {-not $_.passed}).Count -eq 0);
        nativeWorldTrajectoryParity=$false;completeAcceptance=$false;goalComplete=$false} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $motorReport -Encoding utf8
}

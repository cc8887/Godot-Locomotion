param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$delegateRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$delegateTag='immediate-callbacks-v1'
$delegatePrefix=Join-Path $delegateRoot "artifacts/lyra-analysis/$delegateTag-$($Configuration.ToLowerInvariant())"
$delegateDebug=Join-Path $delegateRoot '.godot/mono/temp/bin/Debug'
$delegateRelease=Join-Path $delegateRoot '.godot/mono/temp/bin/ExportRelease'
$delegateBackup="$delegatePrefix-debug-backup"
$delegateFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
$delegateSummary="$delegatePrefix-verification.json"
if(Test-Path -LiteralPath $delegateSummary){throw 'Preserve delegate evidence.'}
$delegateCases=@(
 @{name='native-immediate';scene='lyra_montage_immediate_callback_smoke';marker='LYRA_MONTAGE_IMMEDIATE_GODOT_OK traces=27 frames=5670 retries=5670'},
 @{name='native-bank-callbacks';scene='lyra_montage_bank_callback_smoke';marker='LYRA_MONTAGE_BANK_CALLBACKS_GODOT_OK traces=12 frames=5880 retries=5880'},
 @{name='native-events';scene='lyra_montage_delegate_smoke';marker='traces=21 frames=8823 retries=8820'},
 @{name='original-emote';scene='lyra_emote_smoke';marker='traces=54 frames=27720'},
 @{name='physics-30';scene='lyra_emote_physics_smoke';hz=30;marker='LYRA_EMOTE_PHYSICS_GODOT_OK';report=$true;args=@('--emote-hz=30')},
 @{name='physics-60';scene='lyra_emote_physics_smoke';hz=60;marker='LYRA_EMOTE_PHYSICS_GODOT_OK';report=$true;args=@('--emote-hz=60')},
 @{name='physics-120';scene='lyra_emote_physics_smoke';hz=120;marker='LYRA_EMOTE_PHYSICS_GODOT_OK';report=$true;args=@('--emote-hz=120')},
 @{name='prior-warp';scene='lyra_motion_warping_physics_smoke';hz=60;marker='LYRA_MOTION_WARPING_PHYSICS_GODOT_OK';args=@('--warp-physics-hz=60')},
 @{name='prior-root';scene='lyra_root_movement_physics_smoke';hz=60;marker='LYRA_ROOT_MOVEMENT_PHYSICS_GODOT_OK hz=60 roles=6';args=@('--root-movement-hz=60')},
 @{name='ordinary-ten';main=$true;characters=10;marker='LYRA_LINKED_MONTAGE_PHASE_DEMO_OK'},
 @{name='ordinary-emote';main=$true;characters=1;emote=$true;marker='LYRA_LINKED_MONTAGE_PHASE_DEMO_OK'},
 @{name='als-ordinary';scene='refactored_stance_demo_smoke';marker='ALS_REFACTORED_STANCE_DEMO_OK hz=60';args=@('--stance-hz=60')})
$delegateBuild=Join-Path $delegateRoot "artifacts/lyra-analysis/$delegateTag-build-$($Configuration.ToLowerInvariant())-final-v2.log"
$delegateBuildText=Get-Content -LiteralPath $delegateBuild -Raw
if($delegateBuildText -notmatch '(?m)^\s*0\s*(个警告|Warning)' -or $delegateBuildText -notmatch '(?m)^\s*0\s*(个错误|Error)'){throw 'Clean final build required.'}
if(@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains('Godot-Locomotion')}).Count){throw 'Workspace Godot is running.'}
$delegateCopied=$false;$delegateHashes=@{};$delegateAssemblies=@{};$delegateRuns=@()
try{
 if($Configuration -eq 'Optimize'){
  if(Test-Path -LiteralPath $delegateBackup){throw 'Preserve assembly backup.'}
  New-Item -ItemType Directory -Path $delegateBackup | Out-Null
  foreach($delegateFile in $delegateFiles){$delegateHashes[$delegateFile]=(Get-FileHash -LiteralPath "$delegateDebug/$delegateFile" -Algorithm SHA256).Hash;Copy-Item -LiteralPath "$delegateDebug/$delegateFile" -Destination "$delegateBackup/$delegateFile"}
  $delegateCopied=$true
  foreach($delegateFile in $delegateFiles){Copy-Item -LiteralPath "$delegateRelease/$delegateFile" -Destination "$delegateDebug/$delegateFile"}
 }
 foreach($delegateFile in $delegateFiles){$delegateAssemblies[$delegateFile]=(Get-FileHash -LiteralPath "$delegateDebug/$delegateFile" -Algorithm SHA256).Hash}
 foreach($delegateCase in $delegateCases){
  $delegateLog="$delegatePrefix-$($delegateCase.name).log";$delegateReport=$null
  if(Test-Path -LiteralPath $delegateLog){throw 'Preserve runtime log.'}
  $delegateArgs=@('--headless','--path',$delegateRoot)
  if($delegateCase.ContainsKey('main')){
   $delegateReport="$delegatePrefix-$($delegateCase.name).json"
   $delegateArgs+=@('--','--locomotion=lyra','--lyra-profile=rifle','--lyra-layer-layout=per-call',"--lyra-characters=$($delegateCase.characters)",'--lyra-main-smoke','--lyra-main-retry','--lyra-main-rebind','--lyra-main-weapons','--lyra-main-montage-events','--lyra-main-hz=60',"--lyra-main-report=$delegateReport")
   if($delegateCase.ContainsKey('emote')){$delegateArgs+='--lyra-main-emote'}
  }else{
   if($delegateCase.ContainsKey('hz')){$delegateArgs+=@('--fixed-fps',[string]$delegateCase.hz)}
   $delegateArgs+="res://scenes/tests/$($delegateCase.scene).tscn"
   if($delegateCase.ContainsKey('args')){$delegateArgs+='--';$delegateArgs+=$delegateCase.args}
   if($delegateCase.ContainsKey('report')){$delegateReport="$delegatePrefix-$($delegateCase.name).json";$delegateArgs+="--emote-report=$delegateReport"}
  }
  & (Join-Path $delegateRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe') @delegateArgs *> $delegateLog
  $delegateExit=$LASTEXITCODE;Add-Content -LiteralPath $delegateLog "MONTAGE_DELEGATES_GODOT_EXIT=$delegateExit"
  $delegateText=Get-Content -LiteralPath $delegateLog -Raw
  $delegatePassed=$delegateExit -eq 0 -and $delegateText.Contains($delegateCase.marker) -and $delegateText -notmatch '(?m)^\s*(ERROR|WARNING):'
  $delegateRow=@{name=$delegateCase.name;log=$delegateLog;logSha256=(Get-FileHash -LiteralPath $delegateLog -Algorithm SHA256).Hash;exitCode=$delegateExit;passed=$delegatePassed}
  if($delegateReport){$delegateRow.report=$delegateReport;$delegateRow.reportSha256=(Get-FileHash -LiteralPath $delegateReport -Algorithm SHA256).Hash}
  $delegateRuns+=$delegateRow;Write-Output "Montage delegate $Configuration $($delegateCase.name) passed=$delegatePassed"
  if(!$delegatePassed){throw 'Montage delegate runtime verification failed.'}
 }
}finally{
 if($delegateCopied){foreach($delegateFile in $delegateFiles){Copy-Item -LiteralPath "$delegateBackup/$delegateFile" -Destination "$delegateDebug/$delegateFile";if((Get-FileHash -LiteralPath "$delegateDebug/$delegateFile" -Algorithm SHA256).Hash -ne $delegateHashes[$delegateFile]){throw 'Debug assembly restore mismatch.'}}}
 @{configuration=$Configuration;assemblies=$delegateAssemblies;runs=$delegateRuns;passed=($delegateRuns.Count -eq $delegateCases.Count -and @($delegateRuns|Where-Object {!$_.passed}).Count -eq 0);debugRestored=$delegateCopied;genericCallbackBankMutation=$false;resourceNotifyTermination=$false;goalComplete=$false} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $delegateSummary -Encoding utf8
}
& (Join-Path $delegateRoot 'scripts/verify-lyra-multi-owner-graphs.ps1') -Configuration $Configuration -RunTag linked-montage-events-v1-30-full -EvidenceTag "$delegateTag-whole-main" -Layouts per-call -Boundaries final -WorkerFields -PreUpdateFields -MovementFields -GraphFields -LeftSettings -MontageEventFields

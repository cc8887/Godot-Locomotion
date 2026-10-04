param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$eventRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $eventRoot
$eventTag='linked-montage-events-v1'
$eventFlags=@{WorkerFields=$true;PreUpdateFields=$true;MovementFields=$true;GraphFields=$true;LeftSettings=$true;MontageEventFields=$true}
& scripts/verify-lyra-multi-owner-graphs.ps1 -Configuration $Configuration -RunTag "$eventTag-30-full" -EvidenceTag "$eventTag-native" @eventFlags -Layouts single,per-call -Boundaries pre-rig,final
& scripts/verify-lyra-multi-owner-graphs.ps1 -Configuration $Configuration -RunTag linked-private-v2-30-full -EvidenceTag "$eventTag-authored" @eventFlags -Layouts three-groups,mixed -Boundaries pre-rig
foreach($eventHz in @(30,60,120)){
 $eventRef=switch($eventHz){30{'multi-layer-v3-30-full'}60{'multi-layer-v3-60-repeat-full'}120{'multi-layer-v3-120-full'}}
 & scripts/verify-lyra-multi-owner-graphs.ps1 -Configuration $Configuration -RunTag $eventRef -EvidenceTag "$eventTag-matrix-$eventHz" @eventFlags -Layouts per-call -Boundaries pre-rig
}
& scripts/verify-lyra-multi-owner-graphs.ps1 -Configuration $Configuration -RunTag linked-idle-turn-v2-30-full -EvidenceTag "$eventTag-idle" @eventFlags -Layouts per-call -Boundaries pre-rig
$eventPrefix="artifacts/lyra-analysis/$eventTag-components-$($Configuration.ToLowerInvariant())"
$eventDebug='.godot/mono/temp/bin/Debug';$eventRelease='.godot/mono/temp/bin/ExportRelease'
$eventBackup="$eventPrefix-debug-backup";$eventCopied=$false;$eventHashes=@{};$eventAssemblies=@{};$eventRuns=@()
$eventFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
$eventCases=@(
 @{name='per-call-ten';hz=60;characters=10;live=$true;emote=$false},
 @{name='ordinary-emote';hz=60;characters=1;live=$true;emote=$true},
 @{name='original-emote';scene='lyra_emote_smoke';marker='traces=54 frames=27720'},
 @{name='als-ordinary';scene='refactored_stance_demo_smoke';marker='ALS_REFACTORED_STANCE_DEMO_OK hz=60';args=@('--stance-hz=60')})
if(Test-Path -LiteralPath "$eventPrefix-verification.json"){throw 'Preserve component evidence.'}
foreach($eventCase in $eventCases){if(Test-Path -LiteralPath "$eventPrefix-$($eventCase.name).log"){throw 'Preserve component log.'}}
try{
 if($Configuration -eq 'Optimize'){
  if(Test-Path -LiteralPath $eventBackup){throw 'Preserve component backup.'}
  New-Item -ItemType Directory -Path $eventBackup | Out-Null
  foreach($eventFile in $eventFiles){
   $eventHashes[$eventFile]=(Get-FileHash -LiteralPath "$eventDebug/$eventFile" -Algorithm SHA256).Hash
   Copy-Item -LiteralPath "$eventDebug/$eventFile" -Destination "$eventBackup/$eventFile"
  }
  $eventCopied=$true
  foreach($eventFile in $eventFiles){Copy-Item -LiteralPath "$eventRelease/$eventFile" -Destination "$eventDebug/$eventFile"}
 }
 foreach($eventFile in $eventFiles){$eventAssemblies[$eventFile]=(Get-FileHash -LiteralPath "$eventDebug/$eventFile" -Algorithm SHA256).Hash}
 foreach($eventCase in $eventCases){
  $eventLog="$eventPrefix-$($eventCase.name).log"
  $eventReport=$null
  if($eventCase.ContainsKey('live')){
   $eventReport="$eventRoot/$eventPrefix-$($eventCase.name).json"
   $eventArgs=@('--headless','--path',$eventRoot,'--','--locomotion=lyra','--lyra-profile=rifle','--lyra-layer-layout=per-call',"--lyra-characters=$($eventCase.characters)",'--lyra-main-smoke','--lyra-main-retry','--lyra-main-rebind','--lyra-main-weapons','--lyra-main-montage-events',"--lyra-main-hz=$($eventCase.hz)","--lyra-main-report=$eventReport")
   if($eventCase.emote){$eventArgs+='--lyra-main-emote'}
   $eventMarker='LYRA_LINKED_MONTAGE_PHASE_DEMO_OK'
  }else{
   $eventArgs=@('--headless','--path',$eventRoot,"res://scenes/tests/$($eventCase.scene).tscn")
   if($eventCase.ContainsKey('args')){$eventArgs+='--';$eventArgs+=$eventCase.args}
   $eventMarker=$eventCase.marker
  }
  & ./Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe @eventArgs *> $eventLog
  $eventExit=$LASTEXITCODE;Add-Content -LiteralPath $eventLog "MONTAGE_PHASE_PROCESS_EXIT=$eventExit"
  $eventText=Get-Content -LiteralPath $eventLog -Raw
  $eventPassed=$eventExit -eq 0 -and $eventText.Contains($eventMarker) -and $eventText -notmatch '(?m)^\s*(ERROR|WARNING):'
  $eventRow=@{name=$eventCase.name;log=$eventLog;logSha256=(Get-FileHash -LiteralPath $eventLog -Algorithm SHA256).Hash;exitCode=$eventExit;passed=$eventPassed}
  if($eventReport){$eventRow.report=$eventReport;$eventRow.reportSha256=(Get-FileHash -LiteralPath $eventReport -Algorithm SHA256).Hash}
  $eventRuns+=$eventRow
  Write-Output "Montage phase component $Configuration $($eventCase.name) passed=$eventPassed exit=$eventExit"
  if(!$eventPassed){throw 'Montage event component verification failed.'}
 }
}finally{
 if($eventCopied){foreach($eventFile in $eventFiles){
  Copy-Item -LiteralPath "$eventBackup/$eventFile" -Destination "$eventDebug/$eventFile"
  if((Get-FileHash -LiteralPath "$eventDebug/$eventFile" -Algorithm SHA256).Hash -ne $eventHashes[$eventFile]){throw 'Debug component restore mismatch.'}
 }}
 @{configuration=$Configuration;assemblies=$eventAssemblies;runs=$eventRuns;passed=($eventRuns.Count -eq $eventCases.Count -and @($eventRuns|Where-Object {!$_.passed}).Count -eq 0);debugRestored=$eventCopied;fullMontageEventDispatch=$false;fullPrivateFieldParity=$false;goalComplete=$false} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$eventPrefix-verification.json" -Encoding utf8
}

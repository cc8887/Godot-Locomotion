param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug',
      [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$EvidenceTag='source-initialize-v2')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$routeRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$routePrefix=Join-Path $routeRoot "artifacts/lyra-analysis/$EvidenceTag-$($Configuration.ToLowerInvariant())"
$routeDebug=Join-Path $routeRoot '.godot/mono/temp/bin/Debug'
$routeRelease=Join-Path $routeRoot '.godot/mono/temp/bin/ExportRelease'
$routeBackup="$routePrefix-debug-backup"
$routeFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
$routeSummary="$routePrefix-verification.json"
if(Test-Path -LiteralPath $routeSummary){throw 'Preserve route verification evidence.'}
if(@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains('Godot-Locomotion')}).Count){throw 'Workspace Godot is running.'}
$routeCases=@(
 @{name='source-initialize';scene='lyra_sequence_initialize_smoke';marker='LYRA_SEQUENCE_INITIALIZE_NATIVE_GODOT_OK profiles=3 nodes=78 seededRows=156'},
 @{name='start-source';scene='lyra_start_source_smoke';marker='LYRA_START_SOURCE_GODOT_OK'},
 @{name='stop-source';scene='lyra_stop_source_smoke';marker='LYRA_STOP_SOURCE_GODOT_OK'},
 @{name='pivot-source';scene='lyra_pivot_source_smoke';marker='LYRA_PIVOT_SOURCE_GODOT_OK'},
 @{name='cycle-source';scene='lyra_cycle_source_smoke';marker='LYRA_CYCLE_SOURCE_GODOT_OK'},
 @{name='idle-source';scene='lyra_idle_runtime_smoke';marker='LYRA_IDLE_RUNTIME_GODOT_OK'},
 @{name='air-source';scene='lyra_air_runtime_smoke';marker='LYRA_AIR_RUNTIME_GODOT_OK'},
 @{name='additives-source';scene='lyra_additives_layer_smoke';marker='LYRA_ADDITIVES_LAYER_GODOT_OK'},
 @{name='left-source';scene='lyra_left_hand_layer_smoke';marker='LYRA_LEFT_HAND_LAYER_GODOT_OK'},
 @{name='phases';scene='lyra_graph_phases_smoke';marker='LYRA_GRAPH_PHASES_NATIVE_GODOT_OK profiles=3 cacheSteps=30 machines=15'} ,
 @{name='initial-30';scene='lyra_character_unlink_smoke';extra=@('--initial-self','--unlink-hz=30','--lyra-layer-layout=single');marker='LYRA_INITIAL_SELF_CHARACTER_OK hz=30 frames=1080 self=540 retry=540 switches=30'},
 @{name='initial-60';scene='lyra_character_unlink_smoke';extra=@('--initial-self','--unlink-hz=60','--lyra-layer-layout=single');marker='LYRA_INITIAL_SELF_CHARACTER_OK hz=60 frames=2160 self=1080 retry=1080 switches=30'},
 @{name='initial-120';scene='lyra_character_unlink_smoke';extra=@('--initial-self','--unlink-hz=120','--lyra-layer-layout=single');marker='LYRA_INITIAL_SELF_CHARACTER_OK hz=120 frames=4320 self=2160 retry=2160 switches=30'},
 @{name='initial-three';scene='lyra_character_unlink_smoke';extra=@('--initial-self','--unlink-hz=60','--lyra-layer-layout=three-groups');marker='LYRA_INITIAL_SELF_CHARACTER_OK hz=60 frames=2160 self=1080 retry=1080 switches=30'},
 @{name='initial-mixed';scene='lyra_character_unlink_smoke';extra=@('--initial-self','--unlink-hz=60','--lyra-layer-layout=mixed');marker='LYRA_INITIAL_SELF_CHARACTER_OK hz=60 frames=2160 self=1080 retry=1080 switches=30'},
 @{name='initial-per-call';scene='lyra_character_unlink_smoke';extra=@('--initial-self','--unlink-hz=60','--lyra-layer-layout=per-call');marker='LYRA_INITIAL_SELF_CHARACTER_OK hz=60 frames=2160 self=1080 retry=1080 switches=30'},
 @{name='unlink-30';scene='lyra_character_unlink_smoke';extra=@('--unlink-hz=30','--lyra-layer-layout=single');marker='LYRA_CHARACTER_UNLINK_OK hz=30 frames=1080 self=360 retry=540'},
 @{name='unlink-60';scene='lyra_character_unlink_smoke';extra=@('--unlink-hz=60','--lyra-layer-layout=single');marker='LYRA_CHARACTER_UNLINK_OK hz=60 frames=2160 self=720 retry=1080'},
 @{name='unlink-120';scene='lyra_character_unlink_smoke';extra=@('--unlink-hz=120','--lyra-layer-layout=single');marker='LYRA_CHARACTER_UNLINK_OK hz=120 frames=4320 self=1440 retry=2160'},
 @{name='unlink-three';scene='lyra_character_unlink_smoke';extra=@('--unlink-hz=60','--lyra-layer-layout=three-groups');marker='LYRA_CHARACTER_UNLINK_OK hz=60 frames=2160 self=720 retry=1080'},
 @{name='unlink-mixed';scene='lyra_character_unlink_smoke';extra=@('--unlink-hz=60','--lyra-layer-layout=mixed');marker='LYRA_CHARACTER_UNLINK_OK hz=60 frames=2160 self=720 retry=1080'},
 @{name='unlink-per-call';scene='lyra_character_unlink_smoke';extra=@('--unlink-hz=60','--lyra-layer-layout=per-call');marker='LYRA_CHARACTER_UNLINK_OK hz=60 frames=2160 self=720 retry=1080'},
 @{name='main-default';scene='lyra_main_default_root_smoke';marker='LYRA_MAIN_DEFAULT_ROOT_OK frames=1260 retry=1260'},
 @{name='routes';scene='lyra_default_layer_routes_smoke';marker='LYRA_DEFAULT_LAYER_ROUTES_OK profiles=3 layouts=4 bones=81 self=336 unbound=336 external=168 rejected=60'},
 @{name='named';scene='lyra_named_notify_smoke';marker='LYRA_NAMED_NOTIFY_GODOT_OK'},
 @{name='weapon';scene='lyra_weapon_equipment_smoke';marker='LYRA_WEAPON_EQUIPMENT_NATIVE_GODOT_OK'},
 @{name='live';scene='lyra_notify_live_smoke';marker='LYRA_NOTIFY_LIVE_GODOT_OK cases=13'},
 @{name='ordinary-ten';main=$true;characters=10;marker='LYRA_LINKED_MONTAGE_PHASE_DEMO_OK'},
 @{name='ordinary-emote';main=$true;characters=1;emote=$true;marker='LYRA_LINKED_MONTAGE_PHASE_DEMO_OK'})
$routeCopied=$false;$routeHashes=@{};$routeAssemblies=@{};$routeRuns=@()
try{
 if($Configuration -eq 'Optimize'){
  if(Test-Path -LiteralPath $routeBackup){throw 'Preserve assembly backup.'}
  New-Item -ItemType Directory -Path $routeBackup | Out-Null
  foreach($routeFile in $routeFiles){$routeHashes[$routeFile]=(Get-FileHash -LiteralPath "$routeDebug/$routeFile" -Algorithm SHA256).Hash;Copy-Item -LiteralPath "$routeDebug/$routeFile" -Destination "$routeBackup/$routeFile"}
  $routeCopied=$true
  foreach($routeFile in $routeFiles){Copy-Item -LiteralPath "$routeRelease/$routeFile" -Destination "$routeDebug/$routeFile"}
 }
 foreach($routeFile in $routeFiles){$routeAssemblies[$routeFile]=(Get-FileHash -LiteralPath "$routeDebug/$routeFile" -Algorithm SHA256).Hash}
 foreach($routeCase in $routeCases){
  $routeLog="$routePrefix-$($routeCase.name).log";$routeReport=$null
  if(Test-Path -LiteralPath $routeLog){throw 'Preserve runtime log.'}
  $routeArgs=@('--headless','--path',$routeRoot)
  if($routeCase.ContainsKey('main')){
   $routeReport="$routePrefix-$($routeCase.name).json"
   $routeArgs+=@('--','--locomotion=lyra','--lyra-profile=rifle','--lyra-layer-layout=per-call',"--lyra-characters=$($routeCase.characters)",'--lyra-main-smoke','--lyra-main-retry','--lyra-main-rebind','--lyra-main-weapons','--lyra-main-montage-events','--lyra-main-hz=60',"--lyra-main-report=$routeReport")
   if($routeCase.ContainsKey('emote')){$routeArgs+='--lyra-main-emote'}
  }else{$routeArgs+="res://scenes/tests/$($routeCase.scene).tscn";if($routeCase.ContainsKey('extra')){$routeArgs+=@('--')+$routeCase.extra}}
  & (Join-Path $routeRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe') @routeArgs *> $routeLog
  $routeExit=$LASTEXITCODE;Add-Content -LiteralPath $routeLog "LYRA_DEFAULT_ROUTES_EXIT=$routeExit"
  $routeText=Get-Content -LiteralPath $routeLog -Raw
  $routePassed=$routeExit -eq 0 -and $routeText.Contains($routeCase.marker) -and $routeText -notmatch '(?m)^\s*(ERROR|WARNING):'
  $routeRow=@{name=$routeCase.name;log=$routeLog;logSha256=(Get-FileHash -LiteralPath $routeLog -Algorithm SHA256).Hash;exitCode=$routeExit;passed=$routePassed}
  if($routeReport){$routeRow.report=$routeReport;if(Test-Path -LiteralPath $routeReport){$routeRow.reportSha256=(Get-FileHash -LiteralPath $routeReport -Algorithm SHA256).Hash}else{$routeRow.reportMissing=$true;$routeRow.passed=$false;$routePassed=$false}}
  $routeRuns+=$routeRow;Write-Output "Default routes $Configuration $($routeCase.name) passed=$routePassed"
  if(!$routePassed){throw 'Default route runtime verification failed.'}
 }
}finally{
 if($routeCopied){foreach($routeFile in $routeFiles){Copy-Item -LiteralPath "$routeBackup/$routeFile" -Destination "$routeDebug/$routeFile";if((Get-FileHash -LiteralPath "$routeDebug/$routeFile" -Algorithm SHA256).Hash -ne $routeHashes[$routeFile]){throw 'Debug assembly restore mismatch.'}}}
 @{configuration=$Configuration;assemblies=$routeAssemblies;runs=$routeRuns;passed=($routeRuns.Count -eq $routeCases.Count -and @($routeRuns|Where-Object {!$_.passed}).Count -eq 0);debugRestored=$routeCopied;productionCallSiteLookup=$true;fullMainUnlink=$true;goalComplete=$false} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $routeSummary -Encoding utf8
}
& (Join-Path $routeRoot 'scripts/verify-lyra-multi-owner-graphs.ps1') -Configuration $Configuration -RunTag linked-montage-events-v1-30-full -EvidenceTag "$EvidenceTag-whole-main" -Layouts single,per-call -Boundaries final -WorkerFields -PreUpdateFields -MovementFields -GraphFields -LeftSettings -MontageEventFields
& (Join-Path $routeRoot 'scripts/verify-lyra-multi-owner-graphs.ps1') -Configuration $Configuration -RunTag linked-private-v2-30-full -EvidenceTag "$EvidenceTag-other-groups" -Layouts three-groups,mixed -Boundaries final -WorkerFields -PreUpdateFields -MovementFields -GraphFields

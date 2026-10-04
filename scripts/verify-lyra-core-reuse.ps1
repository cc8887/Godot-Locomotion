param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug',
      [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$EvidenceTag='lyra-core-reuse-v2',
      [switch]$Render,
      [string[]]$Cases=@())
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$reuseRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$reusePrefix=Join-Path $reuseRoot "artifacts/lyra-analysis/$EvidenceTag-$($Configuration.ToLowerInvariant())"
$reuseDebug=Join-Path $reuseRoot '.godot/mono/temp/bin/Debug'
$reuseRelease=Join-Path $reuseRoot '.godot/mono/temp/bin/ExportRelease'
$reuseBackup="$reusePrefix-debug-backup"
$reuseFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
$reuseSummary="$reusePrefix-verification.json"
if(Test-Path -LiteralPath $reuseSummary){throw 'Preserve Core reuse verification evidence.'}
if(@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains('Godot-Locomotion')}).Count){throw 'Workspace Godot is running.'}
$reuseCases=@(
 @{name='root-bones';scene='lyra_root_bone_smoke';marker='LYRA_ROOT_BONES_NATIVE_GODOT_OK'},
 @{name='startup';scene='lyra_startup_smoke';marker='LYRA_STARTUP_NATIVE_GODOT_OK'},
 @{name='cache-owner';scene='lyra_cache_owner_smoke';marker='LYRA_CACHE_OWNER_NATIVE_GODOT_OK'},
 @{name='root-motion';scene='lyra_root_motion_smoke';marker='LYRA_ROOT_MOTION_GODOT_OK'},
 @{name='main-start-root';scene='lyra_main_start_lean_smoke';marker='LYRA_MAIN_START_LEAN_GODOT_OK'},
 @{name='main-pivot-root';scene='lyra_main_pivot_smoke';marker='LYRA_MAIN_PIVOT_GODOT_OK'},
 @{name='montage-sampling';scene='lyra_montage_sampling_smoke';marker='LYRA_MONTAGE_SAMPLING_GODOT_OK'},
 @{name='cycle-data';scene='lyra_cycle_layer_pose_smoke';marker='LYRA_CYCLE_LAYER_POSE_GODOT_OK'},
 @{name='source-data';scene='lyra_source_curve_smoke';marker='LYRA_SOURCE_CURVES_OK'},
 @{name='idle-data';scene='lyra_idle_runtime_smoke';marker='LYRA_IDLE_RUNTIME_GODOT_OK'},
 @{name='main-data';scene='lyra_main_composition_smoke';marker='LYRA_MAIN_COMPOSITION_GODOT_OK'},
 @{name='main-inertia';scene='lyra_main_inertia_smoke';marker='LYRA_MAIN_INERTIA_GODOT_OK'},
 @{name='main-observation';scene='lyra_main_observation_smoke';marker='LYRA_MAIN_OBSERVATION_GODOT_OK'},
 @{name='main-update';scene='lyra_main_update_smoke';marker='LYRA_MAIN_UPDATE_GODOT_OK'},
 @{name='aim-weights';scene='lyra_aim_weight_smoke';marker='LYRA_AIM_WEIGHT_GODOT_OK'},
 @{name='left-hand';scene='lyra_left_hand_layer_smoke';marker='LYRA_LEFT_HAND_LAYER_GODOT_OK'},
 @{name='aiming-data';scene='lyra_aiming_layer_smoke';marker='LYRA_AIMING_LAYER_GODOT_OK'},
 @{name='additives-data';scene='lyra_additives_layer_smoke';marker='LYRA_ADDITIVES_LAYER_GODOT_OK'},
 @{name='main-cache';scene='lyra_main_cache_smoke';marker='LYRA_MAIN_CACHE_GODOT_OK'},
 @{name='main-cache-pose';scene='lyra_main_cache_pose_smoke';marker='LYRA_MAIN_CACHE_POSE_GODOT_OK'},
 @{name='slot-composition';scene='lyra_main_slot_composition_smoke';marker='LYRA_MAIN_SLOT_COMPOSITION_GODOT_OK'},
 @{name='main-pose';scene='lyra_main_pose_host_smoke';marker='LYRA_MAIN_POSE_HOST_GODOT_OK'},
 @{name='main-pose-feedback';scene='lyra_main_pose_host_smoke';extra=@('--actual-final-feedback');marker='LYRA_MAIN_POSE_HOST_FEEDBACK_GODOT_OK'},
 @{name='main-rig';scene='lyra_main_rig_pose_host_smoke';marker='LYRA_MAIN_RIG_HOST_GODOT_OK'},
 @{name='rig-dynamics';scene='lyra_rig_dynamics_smoke';marker='LYRA_RIG_DYNAMICS_GODOT_OK'},
 @{name='rig-traversal';scene='lyra_rig_traversal_smoke';marker='LYRA_RIG_TRAVERSAL_GODOT_OK'},
 @{name='rig-construction';scene='lyra_footplant_rig_construction_smoke';marker='LYRA_FOOTPLANT_RIG_CONSTRUCTION_GODOT_OK'},
 @{name='rig-input';scene='lyra_footplant_rig_input_smoke';marker='LYRA_RIG_INPUT_GODOT_OK'},
 @{name='rig-solver';scene='lyra_rig_solver_smoke';marker='LYRA_RIG_SOLVER_GODOT_OK'},
 @{name='rig-hierarchy';scene='lyra_rig_hierarchy_smoke';marker='LYRA_RIG_HIERARCHY_GODOT_OK'},
 @{name='rig-output';scene='lyra_rig_output_smoke';marker='LYRA_RIG_OUTPUT_GODOT_OK'},
 @{name='rig-physics';scene='lyra_rig_scene_collision_smoke';reportArgument='--report=';marker='LYRA_RIG_SCENE_COLLISION_GODOT_OK'},
 @{name='root-yaw-preview';scene='lyra_root_yaw_smoke';marker='LYRA_ROOT_YAW_OK'},
 @{name='pose-preview';scene='lyra_pose_layers_smoke';marker='LYRA_POSE_LAYERS_OK'},
 @{name='sync';scene='lyra_evaluator_sync_smoke';marker='LYRA_EVALUATOR_SYNC_OK'},
 @{name='pistol';scene='lyra_pistol_catalog_smoke';marker='LYRA_PISTOL_CATALOG_OK'},
 @{name='rifle';scene='lyra_rifle_catalog_smoke';marker='LYRA_RIFLE_CATALOG_OK'},
 @{name='weapon';scene='lyra_weapon_equipment_smoke';marker='LYRA_WEAPON_EQUIPMENT_NATIVE_GODOT_OK'},
 @{name='als-demo';scene='p4_demo_smoke';extra=@('--als-smoke-frames=300');marker='P4_DEMO_OK frames=300 rigs=1'},
 @{name='als-ordinary';scene='refactored_stance_demo_smoke';extra=@('--stance-hz=60');marker='ALS_REFACTORED_STANCE_DEMO_OK hz=60'},
 @{name='als-aim';scene='aim_pose_smoke';marker='AIM_NESTED_POSE_RUNTIME_OK'},
 @{name='als-grounded';scene='main_grounded_pose_smoke';marker='MAIN_GROUNDED_POSE_OK'},
 @{name='air-30';scene='lyra_character_movement_physics_smoke';extra=@('--character-motor-hz=30');reportArgument='--character-motor-report=';marker='LYRA_CHARACTER_MOTOR_PHYSICS_GODOT_OK'},
 @{name='air-60';scene='lyra_character_movement_physics_smoke';extra=@('--character-motor-hz=60');reportArgument='--character-motor-report=';marker='LYRA_CHARACTER_MOTOR_PHYSICS_GODOT_OK'},
 @{name='air-120';scene='lyra_character_movement_physics_smoke';extra=@('--character-motor-hz=120');reportArgument='--character-motor-report=';marker='LYRA_CHARACTER_MOTOR_PHYSICS_GODOT_OK'},
 @{name='step-30';scene='lyra_character_step_physics_smoke';extra=@('--step-physics-hz=30');reportArgument='--step-physics-report=';marker='LYRA_CHARACTER_STEP_PHYSICS_GODOT_OK'},
 @{name='step-60';scene='lyra_character_step_physics_smoke';extra=@('--step-physics-hz=60');reportArgument='--step-physics-report=';marker='LYRA_CHARACTER_STEP_PHYSICS_GODOT_OK'},
 @{name='step-120';scene='lyra_character_step_physics_smoke';extra=@('--step-physics-hz=120');reportArgument='--step-physics-report=';marker='LYRA_CHARACTER_STEP_PHYSICS_GODOT_OK'},
 @{name='ordinary-ten';main=$true;characters=10;marker='LYRA_LINKED_MONTAGE_PHASE_DEMO_OK'})
if($Cases.Count){
 $reuseKnown=@($reuseCases|ForEach-Object {$_.name})
 if(@($Cases|Where-Object {$_ -notin $reuseKnown}).Count){throw 'Unknown Core reuse verification case.'}
 $reuseCases=@($reuseCases|Where-Object {$_.name -in $Cases})
}else{
 # The current ordinary ALS entry is the production regression. The historical
 # direct P4 fixture remains available explicitly, with its baseline failure recorded.
 $reuseCases=@($reuseCases|Where-Object {$_.name -ne 'als-demo'})
}
if($Render){$reuseCases+=@{name='rendered';main=$true;characters=1;render=$true;marker='LYRA_LINKED_MONTAGE_PHASE_DEMO_OK'}}
$reuseCopied=$false;$reuseHashes=@{};$reuseAssemblies=@{};$reuseRuns=@()
try{
 if($Configuration -eq 'Optimize'){
  if(Test-Path -LiteralPath $reuseBackup){throw 'Preserve assembly backup.'}
  New-Item -ItemType Directory -Path $reuseBackup | Out-Null
  foreach($reuseFile in $reuseFiles){$reuseHashes[$reuseFile]=(Get-FileHash -LiteralPath "$reuseDebug/$reuseFile" -Algorithm SHA256).Hash;Copy-Item -LiteralPath "$reuseDebug/$reuseFile" -Destination "$reuseBackup/$reuseFile"}
  $reuseCopied=$true
  foreach($reuseFile in $reuseFiles){Copy-Item -LiteralPath "$reuseRelease/$reuseFile" -Destination "$reuseDebug/$reuseFile"}
 }
 foreach($reuseFile in $reuseFiles){$reuseAssemblies[$reuseFile]=(Get-FileHash -LiteralPath "$reuseDebug/$reuseFile" -Algorithm SHA256).Hash}
 foreach($reuseCase in $reuseCases){
  $reuseLog="$reusePrefix-$($reuseCase.name).log";$reuseReport=$null
  if(Test-Path -LiteralPath $reuseLog){throw 'Preserve runtime log.'}
  $reuseArgs=@('--path',$reuseRoot)
  if(!$reuseCase.ContainsKey('render')){$reuseArgs=@('--headless')+$reuseArgs}
  if($reuseCase.ContainsKey('main')){
   $reuseReport="$reusePrefix-$($reuseCase.name).json"
   $reuseArgs+=@('--','--locomotion=lyra','--lyra-profile=rifle','--lyra-layer-layout=single',"--lyra-characters=$($reuseCase.characters)",'--lyra-main-smoke','--lyra-main-retry','--lyra-main-rebind','--lyra-main-weapons','--lyra-main-montage-events','--lyra-main-hz=60',"--lyra-main-report=$reuseReport")
   if($reuseCase.ContainsKey('render')){$reuseArgs+=@("--lyra-main-capture=$reusePrefix-rendered-frames")}
  }else{
   $reuseArgs+="res://scenes/tests/$($reuseCase.scene).tscn"
   $reuseExtra=@()
   if($reuseCase.ContainsKey('extra')){$reuseExtra+=@($reuseCase.extra)}
   if($reuseCase.ContainsKey('reportArgument')){
    $reuseReport="$reusePrefix-$($reuseCase.name).json"
    if(Test-Path -LiteralPath $reuseReport){throw 'Preserve runtime report.'}
    $reuseExtra+= "$($reuseCase.reportArgument)$reuseReport"
   }
   if($reuseExtra.Count){$reuseArgs+=@('--')+$reuseExtra}
  }
  # Engine options must precede the separator used by user arguments.
  if($reuseCase.ContainsKey('render')){$reuseArgs=@('--rendering-method','gl_compatibility')+$reuseArgs}
  & (Join-Path $reuseRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe') @reuseArgs *> $reuseLog
  $reuseExit=$LASTEXITCODE
  $reuseText=Get-Content -LiteralPath $reuseLog -Raw
  $reusePassed=$reuseExit -eq 0 -and $reuseText.Contains($reuseCase.marker) -and $reuseText -notmatch '(?m)^\s*(ERROR|WARNING):'
  $reuseRow=@{name=$reuseCase.name;log=$reuseLog;logSha256=(Get-FileHash -LiteralPath $reuseLog -Algorithm SHA256).Hash;exitCode=$reuseExit;passed=$reusePassed}
  if($reuseReport){$reuseRow.report=$reuseReport;if(Test-Path -LiteralPath $reuseReport){$reuseRow.reportSha256=(Get-FileHash -LiteralPath $reuseReport -Algorithm SHA256).Hash}else{$reuseRow.passed=$false;$reusePassed=$false}}
  $reuseRuns+=$reuseRow;Write-Output "Core reuse $Configuration $($reuseCase.name) passed=$reusePassed"
  if(!$reusePassed){throw "Core reuse runtime verification failed: $($reuseCase.name)."}
 }
}finally{
 if($reuseCopied){foreach($reuseFile in $reuseFiles){Copy-Item -LiteralPath "$reuseBackup/$reuseFile" -Destination "$reuseDebug/$reuseFile";if((Get-FileHash -LiteralPath "$reuseDebug/$reuseFile" -Algorithm SHA256).Hash -ne $reuseHashes[$reuseFile]){throw 'Debug assembly restore mismatch.'}}}
 @{configuration=$Configuration;assemblies=$reuseAssemblies;runs=$reuseRuns;passed=($reuseRuns.Count -eq $reuseCases.Count -and @($reuseRuns|Where-Object {!$_.passed}).Count -eq 0);debugRestored=$reuseCopied;rendered=[bool]$Render;goalComplete=$false} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reuseSummary -Encoding utf8
}

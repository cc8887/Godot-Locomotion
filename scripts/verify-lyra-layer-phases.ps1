param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug',
      [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$EvidenceTag='layer-phases-v4')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$phaseRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$phasePrefix=Join-Path $phaseRoot "artifacts/lyra-analysis/$EvidenceTag-$($Configuration.ToLowerInvariant())"
$phaseDebug=Join-Path $phaseRoot '.godot/mono/temp/bin/Debug'
$phaseRelease=Join-Path $phaseRoot '.godot/mono/temp/bin/ExportRelease'
$phaseBackup="$phasePrefix-debug-backup"
$phaseFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
$phaseSummary="$phasePrefix-verification.json"
if(Test-Path -LiteralPath $phaseSummary){throw 'Preserve phase verification evidence.'}
if(@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains('Godot-Locomotion')}).Count){throw 'Workspace Godot is running.'}
$phaseCases=@(
 @{name='routes';scene='lyra_default_layer_routes_smoke';marker='LYRA_LAYER_PHASE_ROUTES_OK profiles=3 layouts=4 calls=3360 externalRoots=672 inputs=360 rejected=48'},
 @{name='main-default';scene='lyra_main_default_root_smoke';marker='LYRA_MAIN_DEFAULT_ROOT_OK frames=1260 retry=1260'},
 @{name='unlink-30';scene='lyra_character_unlink_smoke';extra=@('--unlink-hz=30','--lyra-layer-layout=single');marker='LYRA_CHARACTER_UNLINK_OK hz=30 frames=1080 self=360 retry=540'})
$phaseCopied=$false;$phaseHashes=@{};$phaseAssemblies=@{};$phaseRuns=@();$phaseFailure=$null
try{
 if($Configuration -eq 'Optimize'){
  if(Test-Path -LiteralPath $phaseBackup){throw 'Preserve assembly backup.'}
  New-Item -ItemType Directory -Path $phaseBackup | Out-Null
  foreach($phaseFile in $phaseFiles){$phaseHashes[$phaseFile]=(Get-FileHash -LiteralPath "$phaseDebug/$phaseFile" -Algorithm SHA256).Hash;Copy-Item -LiteralPath "$phaseDebug/$phaseFile" -Destination "$phaseBackup/$phaseFile"}
  $phaseCopied=$true
  foreach($phaseFile in $phaseFiles){Copy-Item -LiteralPath "$phaseRelease/$phaseFile" -Destination "$phaseDebug/$phaseFile"}
 }
 foreach($phaseFile in $phaseFiles){$phaseAssemblies[$phaseFile]=(Get-FileHash -LiteralPath "$phaseDebug/$phaseFile" -Algorithm SHA256).Hash}
 foreach($phaseCase in $phaseCases){
  $phaseLog="$phasePrefix-$($phaseCase.name).log"
  if(Test-Path -LiteralPath $phaseLog){throw 'Preserve phase runtime log.'}
  $phaseArgs=@('--headless','--path',$phaseRoot,"res://scenes/tests/$($phaseCase.scene).tscn")
  if($phaseCase.ContainsKey('extra')){$phaseArgs+=@('--')+$phaseCase.extra}
  & (Join-Path $phaseRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe') @phaseArgs *> $phaseLog
  $phaseExit=$LASTEXITCODE;Add-Content -LiteralPath $phaseLog "LYRA_LAYER_PHASE_EXIT=$phaseExit"
  $phaseText=Get-Content -LiteralPath $phaseLog -Raw
  $phasePassed=$phaseExit -eq 0 -and $phaseText.Contains($phaseCase.marker) -and $phaseText -notmatch '(?m)^\s*(ERROR|WARNING):'
  $phaseRuns+=@{name=$phaseCase.name;log=$phaseLog;logSha256=(Get-FileHash -LiteralPath $phaseLog -Algorithm SHA256).Hash;exitCode=$phaseExit;passed=$phasePassed}
  Write-Output "Layer phase $Configuration $($phaseCase.name) passed=$phasePassed"
  if(!$phasePassed){throw 'Layer phase runtime verification failed.'}
 }
}catch{$phaseFailure=$_.Exception.Message}
finally{
 if($phaseCopied){foreach($phaseFile in $phaseFiles){Copy-Item -LiteralPath "$phaseBackup/$phaseFile" -Destination "$phaseDebug/$phaseFile";if((Get-FileHash -LiteralPath "$phaseDebug/$phaseFile" -Algorithm SHA256).Hash -ne $phaseHashes[$phaseFile]){throw 'Assembly restore differs.'}}}
}
@{configuration=$Configuration;passed=(!$phaseFailure -and $phaseRuns.Count -eq 3);runs=$phaseRuns;assemblies=$phaseAssemblies;
 fullMainInitialization=$false;goalComplete=$false;failure=$phaseFailure}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $phaseSummary -Encoding utf8
if($phaseFailure){throw $phaseFailure}

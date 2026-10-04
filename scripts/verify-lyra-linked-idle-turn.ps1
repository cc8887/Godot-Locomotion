param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$idleRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $idleRoot
$idleTag='linked-idle-turn-v2'
& scripts/verify-lyra-multi-owner-graphs.ps1 -Configuration $Configuration -RunTag linked-idle-turn-v2-30-full -EvidenceTag "$idleTag-native" -WorkerFields -PreUpdateFields -MovementFields -GraphFields -LeftSettings -Layouts single,per-call -Boundaries pre-rig,final
& scripts/verify-lyra-multi-owner-graphs.ps1 -Configuration $Configuration -RunTag linked-private-v2-30-full -EvidenceTag "$idleTag-authored" -WorkerFields -PreUpdateFields -MovementFields -GraphFields -LeftSettings -Boundaries pre-rig
foreach($idleHz in @(30,60,120)){
 $idleRef=switch($idleHz){30{'multi-layer-v3-30-full'}60{'multi-layer-v3-60-repeat-full'}120{'multi-layer-v3-120-full'}}
 & scripts/verify-lyra-multi-owner-graphs.ps1 -Configuration $Configuration -RunTag $idleRef -EvidenceTag "$idleTag-matrix-$idleHz" -WorkerFields -PreUpdateFields -MovementFields -GraphFields -LeftSettings -Layouts per-call -Boundaries pre-rig
}
$idlePrefix="artifacts/lyra-analysis/$idleTag-components-$($Configuration.ToLowerInvariant())"
$idleDebug='.godot/mono/temp/bin/Debug';$idleRelease='.godot/mono/temp/bin/ExportRelease'
$idleBackup="$idlePrefix-debug-backup";$idleCopied=$false;$idleHashes=@{};$idleAssemblies=@{};$idleRuns=@()
$idleFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
$idleCases=@(@{name='per-call-ten';hz=60;settings=$false})
foreach($idleHz in @(30,60,120)){$idleCases+=@{name="left-settings-$idleHz";hz=$idleHz;settings=$true}}
foreach($idleCase in $idleCases){foreach($idleExtension in @('log','json')){
 if(Test-Path -LiteralPath "$idlePrefix-$($idleCase.name).$idleExtension"){throw 'Preserve component evidence.'}
}}
try{
 if($Configuration -eq 'Optimize'){
  if(Test-Path -LiteralPath $idleBackup){throw 'Preserve component backup.'}
  New-Item -ItemType Directory -Path $idleBackup | Out-Null
  foreach($idleFile in $idleFiles){
   $idleHashes[$idleFile]=(Get-FileHash -LiteralPath "$idleDebug/$idleFile" -Algorithm SHA256).Hash
   Copy-Item -LiteralPath "$idleDebug/$idleFile" -Destination "$idleBackup/$idleFile"
  }
  $idleCopied=$true
  foreach($idleFile in $idleFiles){Copy-Item -LiteralPath "$idleRelease/$idleFile" -Destination "$idleDebug/$idleFile"}
 }
 foreach($idleFile in $idleFiles){$idleAssemblies[$idleFile]=(Get-FileHash -LiteralPath "$idleDebug/$idleFile" -Algorithm SHA256).Hash}
 foreach($idleCase in $idleCases){
  $idleLog="$idlePrefix-$($idleCase.name).log"
  $idleReport="$idleRoot/$idlePrefix-$($idleCase.name).json"
  $idleArgs=@('--headless','--path',$idleRoot,'--','--locomotion=lyra','--lyra-profile=rifle','--lyra-layer-layout=per-call','--lyra-characters=10','--lyra-main-smoke','--lyra-main-retry','--lyra-main-rebind','--lyra-main-weapons',"--lyra-main-hz=$($idleCase.hz)","--lyra-main-report=$idleReport")
  if($idleCase.settings){$idleArgs+='--lyra-main-left-settings'}
  & ./Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe @idleArgs *> $idleLog
  $idleExit=$LASTEXITCODE;Add-Content -LiteralPath $idleLog "IDLE_TURN_PROCESS_EXIT=$idleExit"
  $idleText=Get-Content -LiteralPath $idleLog -Raw
  $idlePassed=$idleExit -eq 0 -and $idleText.Contains('LYRA_MAIN_MULTI_DEMO_GODOT_OK') -and $idleText -notmatch '(?m)^\s*(ERROR|WARNING):'
  if($idleCase.settings){$idlePassed=$idlePassed -and $idleText.Contains('LYRA_LINKED_LEFT_SETTINGS_DEMO_OK')}
  $idleRuns+=@{name=$idleCase.name;hz=$idleCase.hz;leftSettings=$idleCase.settings;log=$idleLog;logSha256=(Get-FileHash -LiteralPath $idleLog -Algorithm SHA256).Hash;report=$idleReport;reportSha256=(Get-FileHash -LiteralPath $idleReport -Algorithm SHA256).Hash;exitCode=$idleExit;passed=$idlePassed}
  Write-Output "Idle turn component $Configuration $($idleCase.name) passed=$idlePassed exit=$idleExit"
  if(!$idlePassed){throw 'Live settings component verification failed.'}
 }
}finally{
 if($idleCopied){foreach($idleFile in $idleFiles){
  Copy-Item -LiteralPath "$idleBackup/$idleFile" -Destination "$idleDebug/$idleFile"
  if((Get-FileHash -LiteralPath "$idleDebug/$idleFile" -Algorithm SHA256).Hash -ne $idleHashes[$idleFile]){throw 'Debug component restore mismatch.'}
 }}
 @{configuration=$Configuration;assemblies=$idleAssemblies;runs=$idleRuns;passed=($idleRuns.Count -eq $idleCases.Count -and @($idleRuns|Where-Object {!$_.passed}).Count -eq 0);debugRestored=$idleCopied;fullPrivateFieldParity=$false} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$idlePrefix-verification.json" -Encoding utf8
}

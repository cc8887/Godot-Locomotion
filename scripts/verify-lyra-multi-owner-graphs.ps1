param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug',
      [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$RunTag='multi-layer-v3-30-full',
      [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$EvidenceTag='multi-owner-v3-30',
      [switch]$WorkerFields,
      [switch]$PreUpdateFields,
      [switch]$MovementFields,
      [switch]$GraphFields,
      [switch]$LeftSettings,
      [switch]$MontageEventFields,
      [switch]$ProxyUpdate,
      [switch]$ProxyEvaluation,
      [ValidateSet('single','three-groups','mixed','per-call')][string[]]$Layouts=@('single','three-groups','mixed','per-call'),
      [ValidateSet('pre-inertia','pre-rig','final')][string[]]$Boundaries=@('pre-inertia','pre-rig','final'))
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$multiRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$multiPrefix=Join-Path $multiRoot "artifacts/lyra-analysis/$EvidenceTag-$($Configuration.ToLowerInvariant())"
$multiDebug=Join-Path $multiRoot '.godot/mono/temp/bin/Debug'
$multiRelease=Join-Path $multiRoot '.godot/mono/temp/bin/ExportRelease'
$multiBackup="$multiPrefix-debug-backup"
$multiSummary="$multiPrefix-verification.json"
$multiFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
if(Test-Path -LiteralPath $multiSummary){throw 'Preserve runtime evidence.'}
$multiLive=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Replace('\','/').Contains($multiRoot.Replace('\','/'))})
if($multiLive.Count){throw 'Workspace Godot is running.'}
$multiRequest=Get-Content -LiteralPath (Join-Path $multiRoot "artifacts/lyra-analysis/whole-main-$RunTag-request.json") -Raw | ConvertFrom-Json
$multiLayouts=@($Layouts | Select-Object -Unique)
$multiBoundaries=@($Boundaries | Select-Object -Unique)
if($ProxyEvaluation -and !$ProxyUpdate){throw 'Provider Evaluation verification requires the joint Proxy snapshot comparison.'}
if(!$multiLayouts.Count -or !$multiBoundaries.Count){throw 'A verification needs layouts and boundaries.'}
foreach($multiLayout in $multiLayouts){foreach($multiBoundary in $multiBoundaries){
 if(Test-Path -LiteralPath "$multiPrefix-$multiLayout-$multiBoundary.log"){throw 'Preserve diagnostic log.'}
}}
$multiCopied=$false;$multiRestored=$false;$multiHashes=@{};$multiAssemblies=@{};$multiRuns=@()
try{
 if($Configuration -eq 'Optimize'){
  if(Test-Path -LiteralPath $multiBackup){throw 'Preserve assembly backup.'}
  New-Item -ItemType Directory -Path $multiBackup | Out-Null
  foreach($multiFile in $multiFiles){
   $multiHashes[$multiFile]=(Get-FileHash -LiteralPath (Join-Path $multiDebug $multiFile) -Algorithm SHA256).Hash
   Copy-Item -LiteralPath (Join-Path $multiDebug $multiFile) -Destination (Join-Path $multiBackup $multiFile)
  }
  $multiCopied=$true
  foreach($multiFile in $multiFiles){Copy-Item -LiteralPath (Join-Path $multiRelease $multiFile) -Destination (Join-Path $multiDebug $multiFile)}
 }
 foreach($multiFile in $multiFiles){$multiAssemblies[$multiFile]=(Get-FileHash -LiteralPath (Join-Path $multiDebug $multiFile) -Algorithm SHA256).Hash}
 foreach($multiLayout in $multiLayouts){
  $multiTraces=@($multiRequest.traces | Where-Object {$_.layout -eq $multiLayout})
  if($multiTraces.Count -ne 3){throw 'Incomplete provider reference set.'}
  $multiFrames=($multiTraces | ForEach-Object {$_.frames.Count} | Measure-Object -Sum).Sum
  $multiRelinks=($multiTraces | ForEach-Object {$_.frames | Where-Object {$_.PSObject.Properties.Name -contains 'relink' -and $_.relink}} | Measure-Object).Count
  foreach($multiBoundary in $multiBoundaries){
   $multiLog="$multiPrefix-$multiLayout-$multiBoundary.log"
   $multiArgs=@('--headless','--path',$multiRoot,'res://scenes/tests/lyra_whole_main_diagnostic_smoke.tscn','--',"--whole-main-run=$RunTag","--whole-main-layout=$multiLayout",'--whole-main-retry')
   if($multiBoundary -ne 'final'){$multiArgs+="--whole-main-$multiBoundary"}
   if($WorkerFields){$multiArgs+='--whole-main-worker-fields'}
   if($PreUpdateFields){$multiArgs+='--whole-main-preupdate-fields'}
   if($MovementFields){$multiArgs+='--whole-main-movement-fields'}
   if($GraphFields){$multiArgs+='--whole-main-graph-fields'}
   if($LeftSettings){$multiArgs+='--whole-main-left-settings'}
   if($MontageEventFields){$multiArgs+='--whole-main-montage-event-fields'}
   if($ProxyUpdate){$multiArgs+='--whole-main-proxy-update'}
   if($ProxyEvaluation){$multiArgs+='--whole-main-proxy-evaluation'}
   & (Join-Path $multiRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe') @multiArgs *> $multiLog
   $multiExit=$LASTEXITCODE
   Add-Content -LiteralPath $multiLog -Value "MULTI_OWNER_PROCESS_EXIT=$multiExit" -Encoding utf8
   $multiText=Get-Content -LiteralPath $multiLog -Raw
   $multiPassed=$multiExit -eq 0 -and $multiText.Contains("LYRA_MULTI_OWNER_MAIN_GRAPH_OK layout=$multiLayout frames=$multiFrames routedCalls=42 relinks=$multiRelinks") -and
    $multiText.Contains("retry=$multiFrames controlledPhysicalInputs=true") -and $multiText -notmatch '(?m)^\s*(ERROR|WARNING):'
   if($WorkerFields){
    $multiOwners=@{'single'=1;'three-groups'=3;'mixed'=4;'per-call'=14}[$multiLayout]
    $multiComparisons=[long]$multiFrames*$multiOwners*8*5
    $multiPassed=$multiPassed -and $multiText.Contains("LYRA_LINKED_WORKER_FIELDS_OK layout=$multiLayout frames=$multiFrames fields=8 comparisons=$multiComparisons fullPrivateFieldParity=false")
   }
   if($PreUpdateFields){
    $multiOwners=@{'single'=1;'three-groups'=3;'mixed'=4;'per-call'=14}[$multiLayout]
    $multiComparisons=[long]$multiFrames*$multiOwners*3*5
    $multiPassed=$multiPassed -and $multiText.Contains("LYRA_LINKED_PREUPDATE_FIELDS_OK layout=$multiLayout frames=$multiFrames fields=3 comparisons=$multiComparisons fullPrivateFieldParity=false")
   }
   if($MovementFields){
    $multiOwners=@{'single'=1;'three-groups'=3;'mixed'=4;'per-call'=14}[$multiLayout]
    $multiComparisons=[long]$multiFrames*$multiOwners*9*5
    $multiPassed=$multiPassed -and $multiText.Contains("LYRA_LINKED_MOVEMENT_FIELDS_OK layout=$multiLayout frames=$multiFrames fields=9 comparisons=$multiComparisons fullPrivateFieldParity=false")
   }
   if($GraphFields){
    $multiOwners=@{'single'=1;'three-groups'=3;'mixed'=4;'per-call'=14}[$multiLayout]
    $multiComparisons=[long]$multiFrames*$multiOwners*12*5
    $multiPassed=$multiPassed -and $multiText.Contains("LYRA_LINKED_GRAPH_FIELDS_OK layout=$multiLayout frames=$multiFrames fields=12 comparisons=$multiComparisons fullPrivateFieldParity=false")
   }
   if($LeftSettings){
    $multiOwners=@{'single'=1;'three-groups'=3;'mixed'=4;'per-call'=14}[$multiLayout]
    $multiComparisons=[long]$multiFrames*$multiOwners*5
    $multiRejected=[long]$multiFrames*2
    $multiPassed=$multiPassed -and $multiText.Contains("LYRA_LINKED_LEFT_SETTINGS_OK layout=$multiLayout frames=$multiFrames fields=1 comparisons=$multiComparisons pendingRejected=$multiRejected fullPrivateFieldParity=false")
   }
   if($MontageEventFields){
    $multiOwners=@{'single'=1;'three-groups'=3;'mixed'=4;'per-call'=14}[$multiLayout]
    $multiDispatchSnapshots=($multiTraces | ForEach-Object {$_.frames | Where-Object {$_.PSObject.Properties.Name -contains 'dispatchLinked'}} | Measure-Object).Count
    $multiComponentFrames=($multiTraces | ForEach-Object {$_.frames | Where-Object {$_.PSObject.Properties.Name -contains 'dispatchLinked' -and $_.dispatchLinked}} | Measure-Object).Count
    $multiComparisons=([long]$multiFrames*5+$multiDispatchSnapshots)*$multiOwners
    $multiMainComparisons=[long]$multiFrames*5+$multiDispatchSnapshots
    $multiPassed=$multiPassed -and $multiText.Contains("LYRA_LINKED_MONTAGE_EVENT_FIELDS_OK layout=$multiLayout frames=$multiFrames fields=1 comparisons=$multiComparisons mainComparisons=$multiMainComparisons componentDispatchFrames=$multiComponentFrames dispatchedSnapshots=$multiDispatchSnapshots fullPrivateFieldParity=false")
   }
   if($ProxyUpdate){
    $multiOwners=@{'single'=1;'three-groups'=3;'mixed'=4;'per-call'=14}[$multiLayout]
    $multiComparisons=([long]$multiFrames*7+3)*(4+$(if($ProxyEvaluation){4}else{2})*$multiOwners)
    $multiPassed=$multiPassed -and $multiText.Contains("LYRA_PROXY_UPDATE_JOINT_NATIVE_OK layout=$multiLayout frames=$multiFrames comparisons=$multiComparisons originalMainAndProviders=true controlledExternalFrames=true fullPhaseScheduler=false")
   }
   if($ProxyEvaluation){$multiPassed=$multiPassed -and $multiText.Contains("LYRA_PROXY_EVALUATION_JOINT_NATIVE_OK layout=$multiLayout frames=$multiFrames actualProviderRoots=true cacheReadsRequireEntry=true fullPhaseScheduler=false")}
   $multiRuns+=@{layout=$multiLayout;boundary=$multiBoundary;frames=$multiFrames;relinks=$multiRelinks;proxyUpdate=$ProxyUpdate.IsPresent;proxyEvaluation=$ProxyEvaluation.IsPresent;exitCode=$multiExit;passed=$multiPassed;log=$multiLog;logSha256=(Get-FileHash -LiteralPath $multiLog -Algorithm SHA256).Hash}
   Write-Output "Multi graph $Configuration $multiLayout $multiBoundary passed=$multiPassed exit=$multiExit"
   if(!$multiPassed){throw 'Multi-owner native graph comparison failed.'}
  }
 }
}finally{
 if($multiCopied){
  foreach($multiFile in $multiFiles){
   Copy-Item -LiteralPath (Join-Path $multiBackup $multiFile) -Destination (Join-Path $multiDebug $multiFile)
   if((Get-FileHash -LiteralPath (Join-Path $multiDebug $multiFile) -Algorithm SHA256).Hash -ne $multiHashes[$multiFile]){throw 'Debug restore mismatch.'}
  }
  $multiRestored=$true
 }
 @{configuration=$Configuration;runTag=$RunTag;assemblies=$multiAssemblies;runs=$multiRuns;passed=($multiRuns.Count -eq $multiLayouts.Count*$multiBoundaries.Count -and @($multiRuns | Where-Object {!$_.passed}).Count -eq 0);debugRestored=$multiRestored;workerFields=$WorkerFields.IsPresent;workerFieldCount=$(if($WorkerFields){8}else{0});preUpdateFields=$PreUpdateFields.IsPresent;preUpdateFieldCount=$(if($PreUpdateFields){3}else{0});movementFields=$MovementFields.IsPresent;movementFieldCount=$(if($MovementFields){9}else{0});graphFields=$GraphFields.IsPresent;graphFieldCount=$(if($GraphFields){12}else{0});leftSettings=$LeftSettings.IsPresent;leftSettingFieldCount=$(if($LeftSettings){1}else{0});montageEventFields=$MontageEventFields.IsPresent;montageEventFieldCount=$(if($MontageEventFields){1}else{0});fullPrivateFieldParity=$false;goalComplete=$false} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $multiSummary -Encoding utf8
}

param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug',
      [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$EvidenceTag='terrain-course-v3', [switch]$Render)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$courseRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$coursePrefix=Join-Path $courseRoot "artifacts/lyra-analysis/$EvidenceTag-$($Configuration.ToLowerInvariant())"
$courseSummary="$coursePrefix-verification.json"
if(Test-Path -LiteralPath $courseSummary){throw 'Preserve terrain evidence.'}
if(@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains('Godot-Locomotion')}).Count){throw 'Workspace Godot is running.'}
$courseDebug=Join-Path $courseRoot '.godot/mono/temp/bin/Debug'
$courseRelease=Join-Path $courseRoot '.godot/mono/temp/bin/ExportRelease'
$courseBackup="$coursePrefix-debug-backup"
$courseFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
$courseRuns=@();$courseCopied=$false;$courseHashes=@{};$courseAssemblies=@{}
$courseCases=@(@{name='30';hz=30},@{name='60';hz=60},@{name='120';hz=120})
if($Render){$courseCases+=@{name='render';hz=60;render=$true}}
try{
 if($Configuration -eq 'Optimize'){
  if(Test-Path -LiteralPath $courseBackup){throw 'Preserve Debug backup.'}
  New-Item -ItemType Directory -Path $courseBackup | Out-Null
  foreach($courseFile in $courseFiles){$courseHashes[$courseFile]=(Get-FileHash -LiteralPath "$courseDebug/$courseFile").Hash;Copy-Item -LiteralPath "$courseDebug/$courseFile" -Destination "$courseBackup/$courseFile"}
  $courseCopied=$true
  foreach($courseFile in $courseFiles){Copy-Item -LiteralPath "$courseRelease/$courseFile" -Destination "$courseDebug/$courseFile"}
 }
 foreach($courseFile in $courseFiles){$courseAssemblies[$courseFile]=(Get-FileHash -LiteralPath "$courseDebug/$courseFile").Hash}
 foreach($courseCase in $courseCases){
  $courseLog="$coursePrefix-$($courseCase.name).log";$courseReport="$coursePrefix-$($courseCase.name).json"
  if((Test-Path -LiteralPath $courseLog) -or (Test-Path -LiteralPath $courseReport)){throw 'Preserve terrain run.'}
  $courseArgs=@('--path',$courseRoot)
  if($courseCase.ContainsKey('render')){$courseArgs=@('--rendering-method','gl_compatibility')+$courseArgs}else{$courseArgs=@('--headless')+$courseArgs}
  $courseArgs+=@('--','--locomotion=lyra','--lyra-profile=rifle','--lyra-terrain-smoke',"--lyra-terrain-hz=$($courseCase.hz)","--lyra-terrain-report=$courseReport")
  if($courseCase.ContainsKey('render')){$courseArgs+="--lyra-terrain-capture=$coursePrefix-frames"}
  & (Join-Path $courseRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe') @courseArgs *> $courseLog
  $courseExit=$LASTEXITCODE;$courseText=Get-Content -LiteralPath $courseLog -Raw
  $coursePassed=$courseExit -eq 0 -and $courseText.Contains('LYRA_TERRAIN_COURSE_GODOT_OK') -and $courseText -notmatch '(?m)^\s*(ERROR|WARNING):' -and (Test-Path -LiteralPath $courseReport)
  $courseRow=@{name=$courseCase.name;hz=$courseCase.hz;log=$courseLog;logSha256=(Get-FileHash -LiteralPath $courseLog).Hash;report=$courseReport;exitCode=$courseExit;passed=$coursePassed}
  if(Test-Path -LiteralPath $courseReport){$courseRow.reportSha256=(Get-FileHash -LiteralPath $courseReport).Hash}
  $courseRuns+=$courseRow;Write-Output "Terrain $Configuration $($courseCase.name) passed=$coursePassed"
  if(!$coursePassed){throw "Terrain run failed: $($courseCase.name)."}
 }
}finally{
 if($courseCopied){foreach($courseFile in $courseFiles){Copy-Item -LiteralPath "$courseBackup/$courseFile" -Destination "$courseDebug/$courseFile";if((Get-FileHash -LiteralPath "$courseDebug/$courseFile").Hash -ne $courseHashes[$courseFile]){throw 'Debug assembly restore mismatch.'}}}
 @{configuration=$Configuration;assemblies=$courseAssemblies;runs=$courseRuns;passed=($courseRuns.Count -eq $courseCases.Count -and @($courseRuns|Where-Object {!$_.passed}).Count -eq 0);debugRestored=$courseCopied} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $courseSummary -Encoding utf8
}

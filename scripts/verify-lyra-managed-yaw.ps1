param([ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$EvidenceTag='pose-data-inertia-core-v4-managed')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$yawRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$yawOutput=Join-Path $yawRoot "artifacts/lyra-analysis/$EvidenceTag"
$yawClosure="$yawOutput-closure.json"
if(Test-Path -LiteralPath $yawClosure){throw 'Preserve managed yaw evidence.'}
if(@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains('Godot-Locomotion')}).Count){throw 'Workspace Godot is running.'}
$yawCandidates=@(
 (Join-Path $yawRoot 'LyraNativeMath.dll'),
 (Join-Path $yawRoot '.godot/mono/temp/bin/Debug/LyraNativeMath.dll'),
 (Join-Path $yawRoot 'Godot_v4.7.2-stable_mono_win64/LyraNativeMath.dll'),
 (Join-Path $yawRoot 'assets/generated/lyra_als/native_win64/LyraNativeMath.dll'))
$yawFiles=@();$yawAbsent=$false;$yawRestored=$false;$yawPassed=$false
try{
 for($yawIndex=0;$yawIndex -lt $yawCandidates.Count;$yawIndex++){
  $yawSource=$yawCandidates[$yawIndex]
  if(Test-Path -LiteralPath $yawSource){
   $yawDestination="$yawOutput-native-backup-$yawIndex.dll"
   if(Test-Path -LiteralPath $yawDestination){throw 'Preserve native yaw backup.'}
   $yawHash=(Get-FileHash -LiteralPath $yawSource -Algorithm SHA256).Hash
   Move-Item -LiteralPath $yawSource -Destination $yawDestination
   $yawFiles+=@{source=$yawSource;backup=$yawDestination;sha256=$yawHash}
  }
 }
 $yawAbsent=@($yawCandidates|Where-Object {Test-Path -LiteralPath $_}).Count -eq 0
 if(!$yawAbsent){throw 'Native yaw candidate remains.'}
 & (Join-Path $PSScriptRoot 'verify-lyra-core-reuse.ps1') -Configuration Debug -EvidenceTag $EvidenceTag -Cases @('ordinary-ten')
 $yawSummary=Get-Content -LiteralPath "$yawOutput-debug-verification.json" -Raw | ConvertFrom-Json
 $yawPassed=[bool]$yawSummary.passed
}finally{
 foreach($yawFile in $yawFiles){
  if(Test-Path -LiteralPath $yawFile.source){throw 'Native yaw source changed during verification; preserve backup.'}
  Move-Item -LiteralPath $yawFile.backup -Destination $yawFile.source
  if((Get-FileHash -LiteralPath $yawFile.source -Algorithm SHA256).Hash -ne $yawFile.sha256){throw 'Native yaw restore mismatch.'}
 }
 $yawRestored=$true
 @{passed=$yawPassed;nativeCandidates=$yawCandidates;nativeAbsentDuringRun=$yawAbsent;nativeFiles=$yawFiles;nativeRestored=$yawRestored;goalComplete=$false} |
  ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $yawClosure -Encoding utf8
}
if(!$yawPassed){throw 'Managed yaw ordinary scene failed.'}
Write-Output 'LYRA_MANAGED_YAW_ORDINARY_OK'

param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug',
      [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$RunTag='final')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$mwRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$mwName=$Configuration.ToLowerInvariant()
$mwLog=Join-Path $mwRoot "artifacts/lyra-analysis/motion-warping-$mwName-gate-$RunTag.log"
$mwReport=Join-Path $mwRoot "artifacts/lyra-analysis/motion-warping-$mwName-main-$RunTag.json"
$mwDebug=Join-Path $mwRoot '.godot/mono/temp/bin/Debug'
$mwRelease=Join-Path $mwRoot '.godot/mono/temp/bin/ExportRelease'
$mwBackup=Join-Path $mwRoot "artifacts/lyra-analysis/motion-warping-debug-backup-$RunTag"
$mwFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
if((Test-Path -LiteralPath $mwLog) -or (Test-Path -LiteralPath $mwReport)){throw 'Preserve MW evidence.'}
$mwLive=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains($mwRoot)})
if($mwLive.Count){throw 'Workspace Godot is running.'}
$mwBuildName=if($Configuration -eq 'Optimize'){'motion-warping-export-release-build-final.log'}else{'motion-warping-debug-build-second.log'}
$mwBuild=Get-Content -LiteralPath (Join-Path $mwRoot "artifacts/lyra-analysis/$mwBuildName") -Raw -Encoding UTF8
if($mwBuild -notmatch '(?m)^\s*0\s*(\u4e2a\u8b66\u544a|Warning)' -or $mwBuild -notmatch '(?m)^\s*0\s*(\u4e2a\u9519\u8bef|Error)'){throw 'Clean MW build required.'}
$mwHashes=@{};$mwCopied=$false
function Invoke-MwRun([string]$Name,[string[]]$Arguments,[string]$Marker){
    Add-Content -LiteralPath $mwLog -Value "LYRA_MOTION_WARPING_RUN name=$Name" -Encoding UTF8
    & (Join-Path $mwRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe') @Arguments 2>&1 | Out-File -LiteralPath $mwLog -Encoding UTF8 -Append
    $mwExit=$LASTEXITCODE
    Add-Content -LiteralPath $mwLog -Value "LYRA_MOTION_WARPING_RUN_EXIT name=$Name code=$mwExit" -Encoding UTF8
    $mwText=Get-Content -LiteralPath $mwLog -Raw -Encoding UTF8
    if($mwExit -ne 0 -or $mwText -match '(?m)^\s*(ERROR|WARNING):' -or $mwText -notmatch $Marker){throw "MotionWarping gate failed: $Name"}
}
Push-Location $mwRoot
try {
    if($Configuration -eq 'Optimize'){
        if(Test-Path -LiteralPath $mwBackup){throw 'Preserve assembly backup.'}
        New-Item -ItemType Directory -Path $mwBackup | Out-Null
        foreach($mwFile in $mwFiles){$mwHashes[$mwFile]=(Get-FileHash -LiteralPath (Join-Path $mwDebug $mwFile) -Algorithm SHA256).Hash;Copy-Item -LiteralPath (Join-Path $mwDebug $mwFile) -Destination (Join-Path $mwBackup $mwFile)}
        $mwCopied=$true
        foreach($mwFile in $mwFiles){Copy-Item -LiteralPath (Join-Path $mwRelease $mwFile) -Destination (Join-Path $mwDebug $mwFile)}
    }
    Set-Content -LiteralPath $mwLog -Value "LYRA_MOTION_WARPING_MATRIX configuration=$Configuration" -Encoding UTF8
    foreach($mwFile in @('GodotALS.dll','Als.Core.dll','Als.Import.dll')){Add-Content -LiteralPath $mwLog -Value "LYRA_MOTION_WARPING_ASSEMBLY file=$mwFile sha256=$((Get-FileHash -LiteralPath (Join-Path $mwDebug $mwFile) -Algorithm SHA256).Hash)" -Encoding UTF8}
    Invoke-MwRun 'native-warp' @('--headless','--path','.','res://scenes/tests/lyra_motion_warping_smoke.tscn') 'LYRA_MOTION_WARPING_NATIVE_GODOT_OK traces=120 frames=16800 nonzero=3030'
    Invoke-MwRun 'prior-native-root' @('--headless','--path','.','res://scenes/tests/lyra_root_movement_smoke.tscn') 'LYRA_ROOT_MOVEMENT_NATIVE_GODOT_OK traces=53 frames=8564'
    Invoke-MwRun 'prior-physics-60' @('--headless','--fixed-fps','60','--path','.','res://scenes/tests/lyra_root_movement_physics_smoke.tscn','--','--root-movement-hz=60') 'LYRA_ROOT_MOVEMENT_PHYSICS_GODOT_OK hz=60 roles=6'
    Invoke-MwRun 'prior-montage-sampling' @('--headless','--path','.','res://scenes/tests/lyra_montage_sampling_smoke.tscn') 'LYRA_MONTAGE_SAMPLING_GODOT_OK frames=15870'
    Invoke-MwRun 'prior-weapon-equipment' @('--headless','--fixed-fps','60','--path','.','res://scenes/tests/lyra_weapon_equipment_smoke.tscn','--','--weapon-equipment-hz=60') 'LYRA_WEAPON_EQUIPMENT_ROLE_GODOT_OK hz=60 roles=6'
    Invoke-MwRun 'ordinary-ten-rebind' @('--headless','--path','.','--','--locomotion=lyra','--lyra-profile=rifle','--lyra-characters=10','--lyra-main-smoke','--lyra-main-retry','--lyra-main-rebind','--lyra-main-weapons','--lyra-main-hz=60',"--lyra-main-report=$mwReport") 'LYRA_MAIN_MULTI_DEMO_GODOT_OK'
    Add-Content -LiteralPath $mwLog -Value "LYRA_MOTION_WARPING_MATRIX_OK configuration=$Configuration" -Encoding UTF8
} finally {
    if($mwCopied){
        foreach($mwFile in $mwFiles){Copy-Item -LiteralPath (Join-Path $mwBackup $mwFile) -Destination (Join-Path $mwDebug $mwFile);if((Get-FileHash -LiteralPath (Join-Path $mwDebug $mwFile) -Algorithm SHA256).Hash -ne $mwHashes[$mwFile]){throw 'Debug restore mismatch.'}}
        Add-Content -LiteralPath $mwLog -Value 'LYRA_MOTION_WARPING_DEBUG_RESTORED hashVerified=true' -Encoding UTF8
    }
    Pop-Location
}

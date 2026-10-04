param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug',
      [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$RunTag='final2')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$weaponRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$weaponName=$Configuration.ToLowerInvariant()
$weaponLog=Join-Path $weaponRoot "artifacts/lyra-analysis/weapon-equipment-$weaponName-gate-$RunTag.log"
$weaponDebug=Join-Path $weaponRoot '.godot/mono/temp/bin/Debug'
$weaponRelease=Join-Path $weaponRoot '.godot/mono/temp/bin/ExportRelease'
$weaponBackup=Join-Path $weaponRoot 'artifacts/lyra-analysis/weapon-equipment-debug-backup'
$weaponReport=Join-Path $weaponRoot "artifacts/lyra-analysis/weapon-equipment-$weaponName-main-$RunTag.json"
$weaponFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
if((Test-Path -LiteralPath $weaponLog) -or (Test-Path -LiteralPath $weaponReport)){throw 'Preserve weapon Montage evidence.'}
$weaponLive=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains($weaponRoot)})
if($weaponLive.Count){throw 'Workspace Godot is running.'}
$weaponBuildName=if($Configuration -eq 'Optimize'){"weapon-equipment-export-release-build-accepted-$RunTag.log"}else{"weapon-equipment-debug-build-accepted-$RunTag.log"}
$weaponBuild=Get-Content -LiteralPath (Join-Path $weaponRoot "artifacts/lyra-analysis/$weaponBuildName") -Raw -Encoding UTF8
if($weaponBuild -notmatch '0\s*(\u4e2a\u8b66\u544a|Warning)' -or $weaponBuild -notmatch '0\s*(\u4e2a\u9519\u8bef|Error)'){throw 'Matching clean weapon Montage build required.'}
$weaponHashes=@{};$weaponCopied=$false
function Invoke-WeaponRun([string]$Name,[string[]]$Arguments,[string]$Marker){
    Add-Content -LiteralPath $weaponLog -Value "LYRA_WEAPON_EQUIPMENT_RUN name=$Name" -Encoding UTF8
    & (Join-Path $weaponRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe') @Arguments 2>&1 | Out-File -LiteralPath $weaponLog -Encoding UTF8 -Append
    $weaponExit=$LASTEXITCODE
    Add-Content -LiteralPath $weaponLog -Value "LYRA_WEAPON_EQUIPMENT_RUN_EXIT name=$Name code=$weaponExit" -Encoding UTF8
    $weaponText=Get-Content -LiteralPath $weaponLog -Raw -Encoding UTF8
    if($weaponExit -ne 0 -or $weaponText -match '(?m)^\s*(ERROR|WARNING):' -or $weaponText -notmatch $Marker){throw "Weapon Montage run failed: $Name"}
}
Push-Location $weaponRoot
try {
    if($Configuration -eq 'Optimize'){
        if(Test-Path -LiteralPath $weaponBackup){throw 'Preserve assembly backup.'}
        New-Item -ItemType Directory -Path $weaponBackup | Out-Null
        foreach($weaponFile in $weaponFiles){
            $weaponHashes[$weaponFile]=(Get-FileHash -LiteralPath (Join-Path $weaponDebug $weaponFile) -Algorithm SHA256).Hash
            Copy-Item -LiteralPath (Join-Path $weaponDebug $weaponFile) -Destination (Join-Path $weaponBackup $weaponFile)
        }
        $weaponCopied=$true
        foreach($weaponFile in $weaponFiles){Copy-Item -LiteralPath (Join-Path $weaponRelease $weaponFile) -Destination (Join-Path $weaponDebug $weaponFile)}
    }
    Set-Content -LiteralPath $weaponLog -Value "LYRA_WEAPON_EQUIPMENT_MATRIX configuration=$Configuration" -Encoding UTF8
    foreach($weaponFile in @('GodotALS.dll','Als.Core.dll','Als.Import.dll')){
        Add-Content -LiteralPath $weaponLog -Value "LYRA_WEAPON_EQUIPMENT_ASSEMBLY file=$weaponFile sha256=$((Get-FileHash -LiteralPath (Join-Path $weaponDebug $weaponFile) -Algorithm SHA256).Hash)" -Encoding UTF8
    }
    foreach($weaponHz in @(30,60,120)){
        Invoke-WeaponRun "equipment-$weaponHz" @('--headless','--fixed-fps',"$weaponHz",'--path','.','res://scenes/tests/lyra_weapon_equipment_smoke.tscn','--',"--weapon-equipment-hz=$weaponHz") "LYRA_WEAPON_EQUIPMENT_ROLE_GODOT_OK hz=$weaponHz roles=6"
        $weaponText=Get-Content -LiteralPath $weaponLog -Raw -Encoding UTF8
        if($weaponText -notmatch 'LYRA_WEAPON_EQUIPMENT_NATIVE_GODOT_OK traces=9 frames=5040 poses=4323 bones=30261 .*positionCm=0 quaternion=0 scale=0' -or
            $weaponText -notmatch 'LYRA_WEAPON_NOTIFY_CONSUMER_GODOT_OK calls=36 played=18 missing=18' -or
            $weaponText -notmatch 'LYRA_WEAPON_CALLBACK_LIFETIME_GODOT_OK disposed=1 queuedDeletion=1 rebind=1'){throw 'Incomplete equipment gates.'}
    }
    Invoke-WeaponRun 'prior-weapon-graph' @('--headless','--fixed-fps','60','--path','.','res://scenes/tests/lyra_weapon_montage_smoke.tscn') 'LYRA_WEAPON_MONTAGE_GODOT_OK traces=9 frames=8820 poses=7560 bones=52920 .*positionCm=0 quaternion=0 scale=0'
    Invoke-WeaponRun 'prior-gameplay' @('--headless','--path','.','res://scenes/tests/lyra_gameplay_notify_smoke.tscn') 'LYRA_GAMEPLAY_NOTIFY_GODOT_OK'
    Invoke-WeaponRun 'prior-context' @('--headless','--fixed-fps','60','--path','.','res://scenes/tests/lyra_context_effects_smoke.tscn') 'LYRA_CONTEXT_EFFECTS_GODOT_OK'
    Invoke-WeaponRun 'ordinary-ten-rebind' @('--headless','--path','.','--','--locomotion=lyra','--lyra-profile=rifle','--lyra-characters=10','--lyra-main-smoke','--lyra-main-retry','--lyra-main-rebind','--lyra-main-weapons','--lyra-main-hz=60',"--lyra-main-report=$weaponReport") 'LYRA_MAIN_MULTI_DEMO_GODOT_OK'
    Add-Content -LiteralPath $weaponLog -Value "LYRA_WEAPON_EQUIPMENT_MATRIX_OK configuration=$Configuration" -Encoding UTF8
} finally {
    if($weaponCopied){
        foreach($weaponFile in $weaponFiles){
            Copy-Item -LiteralPath (Join-Path $weaponBackup $weaponFile) -Destination (Join-Path $weaponDebug $weaponFile)
            if((Get-FileHash -LiteralPath (Join-Path $weaponDebug $weaponFile) -Algorithm SHA256).Hash -ne $weaponHashes[$weaponFile]){throw 'Debug restore mismatch.'}
        }
        Add-Content -LiteralPath $weaponLog -Value 'LYRA_WEAPON_EQUIPMENT_DEBUG_RESTORED hashVerified=true' -Encoding UTF8
    }
    Pop-Location
}

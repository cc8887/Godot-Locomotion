param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug',
      [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$RunTag='final6')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$rootTask=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$rootName=$Configuration.ToLowerInvariant()
$rootLog=Join-Path $rootTask "artifacts/lyra-analysis/root-movement-$rootName-gate-$RunTag.log"
$rootReport=Join-Path $rootTask "artifacts/lyra-analysis/root-movement-$rootName-main-$RunTag.json"
$rootDebug=Join-Path $rootTask '.godot/mono/temp/bin/Debug'
$rootRelease=Join-Path $rootTask '.godot/mono/temp/bin/ExportRelease'
$rootBackup=Join-Path $rootTask "artifacts/lyra-analysis/root-movement-debug-backup-$RunTag"
$rootFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
if((Test-Path -LiteralPath $rootLog) -or (Test-Path -LiteralPath $rootReport)){throw 'Preserve root movement evidence.'}
$rootLive=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains($rootTask)})
if($rootLive.Count){throw 'Workspace Godot is running.'}
$rootBuildName=if($Configuration -eq 'Optimize'){"root-movement-export-release-build-$RunTag.log"}else{"root-movement-debug-build-$RunTag.log"}
$rootBuild=Get-Content -LiteralPath (Join-Path $rootTask "artifacts/lyra-analysis/$rootBuildName") -Raw -Encoding UTF8
if($rootBuild -notmatch '0\s*(\u4e2a\u8b66\u544a|Warning)' -or $rootBuild -notmatch '0\s*(\u4e2a\u9519\u8bef|Error)'){throw 'Matching clean root build required.'}
$rootHashes=@{};$rootCopied=$false
function Invoke-RootRun([string]$Name,[string[]]$Arguments,[string]$Marker){
    Add-Content -LiteralPath $rootLog -Value "LYRA_ROOT_MOVEMENT_RUN name=$Name" -Encoding UTF8
    & (Join-Path $rootTask 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe') @Arguments 2>&1 | Out-File -LiteralPath $rootLog -Encoding UTF8 -Append
    $rootExit=$LASTEXITCODE
    Add-Content -LiteralPath $rootLog -Value "LYRA_ROOT_MOVEMENT_RUN_EXIT name=$Name code=$rootExit" -Encoding UTF8
    $rootText=Get-Content -LiteralPath $rootLog -Raw -Encoding UTF8
    if($rootExit -ne 0 -or $rootText -match '(?m)^\s*(ERROR|WARNING):' -or $rootText -notmatch $Marker){throw "Root movement failed: $Name"}
}
Push-Location $rootTask
try {
    if($Configuration -eq 'Optimize'){
        if(Test-Path -LiteralPath $rootBackup){throw 'Preserve assembly backup.'}
        New-Item -ItemType Directory -Path $rootBackup | Out-Null
        foreach($rootFile in $rootFiles){
            $rootHashes[$rootFile]=(Get-FileHash -LiteralPath (Join-Path $rootDebug $rootFile) -Algorithm SHA256).Hash
            Copy-Item -LiteralPath (Join-Path $rootDebug $rootFile) -Destination (Join-Path $rootBackup $rootFile)
        }
        $rootCopied=$true
        foreach($rootFile in $rootFiles){Copy-Item -LiteralPath (Join-Path $rootRelease $rootFile) -Destination (Join-Path $rootDebug $rootFile)}
    }
    Set-Content -LiteralPath $rootLog -Value "LYRA_ROOT_MOVEMENT_MATRIX configuration=$Configuration" -Encoding UTF8
    foreach($rootFile in @('GodotALS.dll','Als.Core.dll','Als.Import.dll')){
        Add-Content -LiteralPath $rootLog -Value "LYRA_ROOT_MOVEMENT_ASSEMBLY file=$rootFile sha256=$((Get-FileHash -LiteralPath (Join-Path $rootDebug $rootFile) -Algorithm SHA256).Hash)" -Encoding UTF8
    }
    Invoke-RootRun 'native-root' @('--headless','--path','.','res://scenes/tests/lyra_root_movement_smoke.tscn') 'LYRA_ROOT_MOVEMENT_NATIVE_GODOT_OK traces=53 frames=8564 present=5569 retries=8564'
    foreach($rootHz in @(30,60,120)){
        Invoke-RootRun "physics-$rootHz" @('--headless','--fixed-fps',"$rootHz",'--path','.','res://scenes/tests/lyra_root_movement_physics_smoke.tscn','--',"--root-movement-hz=$rootHz") "LYRA_ROOT_MOVEMENT_PHYSICS_GODOT_OK hz=$rootHz roles=6"
    }
    Invoke-RootRun 'prior-montage-sampling' @('--headless','--path','.','res://scenes/tests/lyra_montage_sampling_smoke.tscn') 'LYRA_MONTAGE_SAMPLING_GODOT_OK frames=15870'
    Invoke-RootRun 'prior-weapon-equipment' @('--headless','--fixed-fps','60','--path','.','res://scenes/tests/lyra_weapon_equipment_smoke.tscn','--','--weapon-equipment-hz=60') 'LYRA_WEAPON_EQUIPMENT_ROLE_GODOT_OK hz=60 roles=6'
    Invoke-RootRun 'ordinary-ten-rebind' @('--headless','--path','.','--','--locomotion=lyra','--lyra-profile=rifle','--lyra-characters=10','--lyra-main-smoke','--lyra-main-retry','--lyra-main-rebind','--lyra-main-weapons','--lyra-main-hz=60',"--lyra-main-report=$rootReport") 'LYRA_MAIN_MULTI_DEMO_GODOT_OK'
    Add-Content -LiteralPath $rootLog -Value "LYRA_ROOT_MOVEMENT_MATRIX_OK configuration=$Configuration" -Encoding UTF8
} finally {
    if($rootCopied){
        foreach($rootFile in $rootFiles){
            Copy-Item -LiteralPath (Join-Path $rootBackup $rootFile) -Destination (Join-Path $rootDebug $rootFile)
            if((Get-FileHash -LiteralPath (Join-Path $rootDebug $rootFile) -Algorithm SHA256).Hash -ne $rootHashes[$rootFile]){throw 'Debug restore mismatch.'}
        }
        Add-Content -LiteralPath $rootLog -Value 'LYRA_ROOT_MOVEMENT_DEBUG_RESTORED hashVerified=true' -Encoding UTF8
    }
    Pop-Location
}

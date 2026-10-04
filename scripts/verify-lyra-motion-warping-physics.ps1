param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug',
      [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$RunTag='final')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$warpRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$warpName=$Configuration.ToLowerInvariant()
$warpLog=Join-Path $warpRoot "artifacts/lyra-analysis/warp-physical-$warpName-$RunTag.log"
$warpReport=Join-Path $warpRoot "artifacts/lyra-analysis/warp-physical-$warpName-main-$RunTag.json"
$warpDebug=Join-Path $warpRoot '.godot/mono/temp/bin/Debug'
$warpRelease=Join-Path $warpRoot '.godot/mono/temp/bin/ExportRelease'
$warpBackup=Join-Path $warpRoot "artifacts/lyra-analysis/warp-physical-debug-backup-$RunTag"
$warpFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
if((Test-Path -LiteralPath $warpLog) -or (Test-Path -LiteralPath $warpReport)){throw 'Preserve Warp physics evidence.'}
$warpLive=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains($warpRoot)})
if($warpLive.Count){throw 'Workspace Godot is running.'}
$warpBuildName=if($Configuration -eq 'Optimize'){'motion-warping-physical-optimize-build-complete.log'}else{'motion-warping-physical-build-complete.log'}
$warpBuild=Get-Content -LiteralPath (Join-Path $warpRoot "artifacts/lyra-analysis/$warpBuildName") -Raw -Encoding UTF8
if($warpBuild -notmatch '(?m)^\s*0\s*(\u4e2a\u8b66\u544a|Warning)' -or $warpBuild -notmatch '(?m)^\s*0\s*(\u4e2a\u9519\u8bef|Error)'){throw 'Clean Warp physics build required.'}
$warpHashes=@{};$warpCopied=$false
function Invoke-WarpRun([string]$Name,[string[]]$Arguments,[string]$Marker){
    Add-Content -LiteralPath $warpLog -Value "LYRA_WARP_PHYSICAL_RUN name=$Name" -Encoding UTF8
    & (Join-Path $warpRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe') @Arguments 2>&1 | Out-File -LiteralPath $warpLog -Encoding UTF8 -Append
    $warpExit=$LASTEXITCODE
    Add-Content -LiteralPath $warpLog -Value "LYRA_WARP_PHYSICAL_RUN_EXIT name=$Name code=$warpExit" -Encoding UTF8
    $warpText=Get-Content -LiteralPath $warpLog -Raw -Encoding UTF8
    if($warpExit -ne 0 -or $warpText -match '(?m)^\s*(ERROR|WARNING):' -or $warpText -notmatch $Marker){throw "Warp physics gate failed: $Name"}
    Write-Output "Warp physics passed: $Name"
}
Push-Location $warpRoot
try {
    if($Configuration -eq 'Optimize'){
        if(Test-Path -LiteralPath $warpBackup){throw 'Preserve assembly backup.'}
        New-Item -ItemType Directory -Path $warpBackup | Out-Null
        foreach($warpFile in $warpFiles){$warpHashes[$warpFile]=(Get-FileHash -LiteralPath (Join-Path $warpDebug $warpFile) -Algorithm SHA256).Hash;Copy-Item -LiteralPath (Join-Path $warpDebug $warpFile) -Destination (Join-Path $warpBackup $warpFile)}
        $warpCopied=$true
        foreach($warpFile in $warpFiles){Copy-Item -LiteralPath (Join-Path $warpRelease $warpFile) -Destination (Join-Path $warpDebug $warpFile)}
    }
    Set-Content -LiteralPath $warpLog -Value "LYRA_WARP_PHYSICAL_MATRIX configuration=$Configuration" -Encoding UTF8
    foreach($warpFile in @('GodotALS.dll','Als.Core.dll','Als.Import.dll')){Add-Content -LiteralPath $warpLog -Value "LYRA_WARP_PHYSICAL_ASSEMBLY file=$warpFile sha256=$((Get-FileHash -LiteralPath (Join-Path $warpDebug $warpFile) -Algorithm SHA256).Hash)" -Encoding UTF8}
    Invoke-WarpRun 'native-live-context' @('--headless','--path','.','res://scenes/tests/lyra_motion_warping_smoke.tscn') 'bankContextFrames=15120'
    foreach($warpHz in @(30,60,120)){
        $warpPhysics=Join-Path $warpRoot "artifacts/lyra-analysis/warp-physical-$warpName-$warpHz-$RunTag.json"
        Invoke-WarpRun "physics-$warpHz" @('--headless','--fixed-fps',"$warpHz",'--path','.','res://scenes/tests/lyra_motion_warping_physics_smoke.tscn','--',"--warp-physics-hz=$warpHz","--warp-physics-report=$warpPhysics") 'LYRA_MOTION_WARPING_PHYSICS_GODOT_OK'
    }
    Invoke-WarpRun 'prior-root-physics' @('--headless','--fixed-fps','60','--path','.','res://scenes/tests/lyra_root_movement_physics_smoke.tscn','--','--root-movement-hz=60') 'LYRA_ROOT_MOVEMENT_PHYSICS_GODOT_OK hz=60 roles=6'
    Invoke-WarpRun 'prior-weapon-equipment' @('--headless','--fixed-fps','60','--path','.','res://scenes/tests/lyra_weapon_equipment_smoke.tscn','--','--weapon-equipment-hz=60') 'LYRA_WEAPON_EQUIPMENT_ROLE_GODOT_OK hz=60 roles=6'
    Invoke-WarpRun 'ordinary-ten-rebind' @('--headless','--path','.','--','--locomotion=lyra','--lyra-profile=rifle','--lyra-characters=10','--lyra-main-smoke','--lyra-main-retry','--lyra-main-rebind','--lyra-main-weapons','--lyra-main-hz=60',"--lyra-main-report=$warpReport") 'LYRA_MAIN_MULTI_DEMO_GODOT_OK'
    Add-Content -LiteralPath $warpLog -Value "LYRA_WARP_PHYSICAL_MATRIX_OK configuration=$Configuration" -Encoding UTF8
} finally {
    if($warpCopied){
        foreach($warpFile in $warpFiles){Copy-Item -LiteralPath (Join-Path $warpBackup $warpFile) -Destination (Join-Path $warpDebug $warpFile);if((Get-FileHash -LiteralPath (Join-Path $warpDebug $warpFile) -Algorithm SHA256).Hash -ne $warpHashes[$warpFile]){throw 'Debug restore mismatch.'}}
        Add-Content -LiteralPath $warpLog -Value 'LYRA_WARP_PHYSICAL_DEBUG_RESTORED hashVerified=true' -Encoding UTF8
    }
    Pop-Location
}

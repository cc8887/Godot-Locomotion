param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug',
      [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$RunTag='final')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$dispatchRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dispatchName=$Configuration.ToLowerInvariant()
$dispatchLog=Join-Path $dispatchRoot "artifacts/lyra-analysis/notify-dispatch-$dispatchName-$RunTag.log"
$dispatchDebug=Join-Path $dispatchRoot '.godot/mono/temp/bin/Debug'
$dispatchRelease=Join-Path $dispatchRoot '.godot/mono/temp/bin/ExportRelease'
$dispatchBackup=Join-Path $dispatchRoot "artifacts/lyra-analysis/notify-dispatch-debug-backup-$RunTag"
$dispatchFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
if(Test-Path -LiteralPath $dispatchLog){throw 'Preserve notify dispatch evidence.'}
$dispatchLive=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains($dispatchRoot)})
if($dispatchLive.Count){throw 'Workspace Godot is running.'}
$dispatchHashes=@{};$dispatchCopied=$false
function Invoke-DispatchRun([string]$Name,[string[]]$Arguments,[string]$Marker){
    Add-Content -LiteralPath $dispatchLog -Value "LYRA_NOTIFY_DISPATCH_RUN name=$Name" -Encoding UTF8
    & (Join-Path $dispatchRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe') @Arguments 2>&1 | Out-File -LiteralPath $dispatchLog -Encoding UTF8 -Append
    $dispatchExit=$LASTEXITCODE
    Add-Content -LiteralPath $dispatchLog -Value "LYRA_NOTIFY_DISPATCH_RUN_EXIT name=$Name code=$dispatchExit" -Encoding UTF8
    $dispatchText=Get-Content -LiteralPath $dispatchLog -Raw -Encoding UTF8
    if($dispatchExit -ne 0 -or $dispatchText -match '(?m)^\s*(ERROR|WARNING):' -or $dispatchText -notmatch $Marker){throw "Notify dispatch gate failed: $Name"}
    Write-Output "Notify dispatch passed: $Name"
}
Push-Location $dispatchRoot
try {
    if($Configuration -eq 'Optimize'){
        if(Test-Path -LiteralPath $dispatchBackup){throw 'Preserve assembly backup.'}
        New-Item -ItemType Directory -Path $dispatchBackup | Out-Null
        foreach($dispatchFile in $dispatchFiles){$dispatchHashes[$dispatchFile]=(Get-FileHash -LiteralPath (Join-Path $dispatchDebug $dispatchFile) -Algorithm SHA256).Hash;Copy-Item -LiteralPath (Join-Path $dispatchDebug $dispatchFile) -Destination (Join-Path $dispatchBackup $dispatchFile)}
        $dispatchCopied=$true
        foreach($dispatchFile in $dispatchFiles){Copy-Item -LiteralPath (Join-Path $dispatchRelease $dispatchFile) -Destination (Join-Path $dispatchDebug $dispatchFile)}
    }
    Set-Content -LiteralPath $dispatchLog -Value "LYRA_NOTIFY_DISPATCH_MATRIX configuration=$Configuration" -Encoding UTF8
    foreach($dispatchFile in @('GodotALS.dll','Als.Core.dll','Als.Import.dll')){Add-Content -LiteralPath $dispatchLog -Value "LYRA_NOTIFY_DISPATCH_ASSEMBLY file=$dispatchFile sha256=$((Get-FileHash -LiteralPath (Join-Path $dispatchDebug $dispatchFile) -Algorithm SHA256).Hash)" -Encoding UTF8}
    Invoke-DispatchRun 'original-rule-history' @('--headless','--path','.','res://scenes/tests/lyra_notify_dispatch_smoke.tscn') 'traces=237 frames=36920'
    foreach($dispatchHz in @(30,60,120)){
        $dispatchPhysics=Join-Path $dispatchRoot "artifacts/lyra-analysis/notify-dispatch-$dispatchName-$dispatchHz-$RunTag.json"
        Invoke-DispatchRun "pivot-physics-$dispatchHz" @('--headless','--fixed-fps',"$dispatchHz",'--path','.','res://scenes/tests/lyra_pivot_notify_physics_smoke.tscn','--',"--pivot-hz=$dispatchHz","--pivot-report=$dispatchPhysics") 'LYRA_PIVOT_NOTIFY_PHYSICS_GODOT_OK'
    }
    Invoke-DispatchRun 'prior-emote' @('--headless','--fixed-fps','60','--path','.','res://scenes/tests/lyra_emote_physics_smoke.tscn','--','--emote-hz=60') 'LYRA_EMOTE_PHYSICS_GODOT_OK'
    Invoke-DispatchRun 'prior-warp' @('--headless','--fixed-fps','60','--path','.','res://scenes/tests/lyra_motion_warping_physics_smoke.tscn','--','--warp-physics-hz=60') 'LYRA_MOTION_WARPING_PHYSICS_GODOT_OK'
    Invoke-DispatchRun 'prior-root' @('--headless','--fixed-fps','60','--path','.','res://scenes/tests/lyra_root_movement_physics_smoke.tscn','--','--root-movement-hz=60') 'LYRA_ROOT_MOVEMENT_PHYSICS_GODOT_OK hz=60 roles=6'
    foreach($dispatchHz in @(30,60,120)){
        $dispatchMain=Join-Path $dispatchRoot "artifacts/lyra-analysis/notify-dispatch-$dispatchName-main-$dispatchHz-$RunTag.json"
        Invoke-DispatchRun "ordinary-ten-rebind-$dispatchHz" @('--headless','--path','.','--','--locomotion=lyra','--lyra-profile=rifle','--lyra-characters=10','--lyra-main-smoke','--lyra-main-retry','--lyra-main-rebind','--lyra-main-weapons',"--lyra-main-hz=$dispatchHz","--lyra-main-report=$dispatchMain") 'LYRA_MAIN_MULTI_DEMO_GODOT_OK'
    }
    $dispatchInput=Join-Path $dispatchRoot "artifacts/lyra-analysis/notify-dispatch-$dispatchName-input-$RunTag.json"
    Invoke-DispatchRun 'ordinary-E-input' @('--headless','--path','.','--','--locomotion=lyra','--lyra-profile=rifle','--lyra-main-smoke','--lyra-main-retry','--lyra-main-emote','--lyra-main-hz=60',"--lyra-main-report=$dispatchInput") 'LYRA_MAIN_MODEL_GODOT_OK'
    Add-Content -LiteralPath $dispatchLog -Value "LYRA_NOTIFY_DISPATCH_MATRIX_OK configuration=$Configuration" -Encoding UTF8
} finally {
    if($dispatchCopied){
        foreach($dispatchFile in $dispatchFiles){Copy-Item -LiteralPath (Join-Path $dispatchBackup $dispatchFile) -Destination (Join-Path $dispatchDebug $dispatchFile);if((Get-FileHash -LiteralPath (Join-Path $dispatchDebug $dispatchFile) -Algorithm SHA256).Hash -ne $dispatchHashes[$dispatchFile]){throw 'Debug restore mismatch.'}}
        Add-Content -LiteralPath $dispatchLog -Value 'LYRA_NOTIFY_DISPATCH_DEBUG_RESTORED hashVerified=true' -Encoding UTF8
    }
    Pop-Location
}

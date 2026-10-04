param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug',
      [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$RunTag='final')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$namedRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$namedName=$Configuration.ToLowerInvariant()
$namedLog=Join-Path $namedRoot "artifacts/lyra-analysis/named-notify-$namedName-$RunTag.log"
$namedDebug=Join-Path $namedRoot '.godot/mono/temp/bin/Debug'
$namedRelease=Join-Path $namedRoot '.godot/mono/temp/bin/ExportRelease'
$namedBackup=Join-Path $namedRoot "artifacts/lyra-analysis/named-notify-debug-backup-$RunTag"
$namedFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
if(Test-Path -LiteralPath $namedLog){throw 'Preserve notify named evidence.'}
$namedLive=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains($namedRoot)})
if($namedLive.Count){throw 'Workspace Godot is running.'}
$namedHashes=@{};$namedCopied=$false
function Invoke-NamedRun([string]$Name,[string[]]$Arguments,[string]$Marker){
    Add-Content -LiteralPath $namedLog -Value "LYRA_NAMED_NOTIFY_RUN name=$Name" -Encoding UTF8
    & (Join-Path $namedRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe') @Arguments 2>&1 | Out-File -LiteralPath $namedLog -Encoding UTF8 -Append
    $namedExit=$LASTEXITCODE
    Add-Content -LiteralPath $namedLog -Value "LYRA_NAMED_NOTIFY_RUN_EXIT name=$Name code=$namedExit" -Encoding UTF8
    $namedText=Get-Content -LiteralPath $namedLog -Raw -Encoding UTF8
    if($namedExit -ne 0 -or $namedText -match '(?m)^\s*(ERROR|WARNING):' -or $namedText -notmatch $Marker){throw "Notify named gate failed: $Name"}
    Write-Output "Notify named passed: $Name"
}
Push-Location $namedRoot
try {
    if($Configuration -eq 'Optimize'){
        if(Test-Path -LiteralPath $namedBackup){throw 'Preserve assembly backup.'}
        New-Item -ItemType Directory -Path $namedBackup | Out-Null
        foreach($namedFile in $namedFiles){$namedHashes[$namedFile]=(Get-FileHash -LiteralPath (Join-Path $namedDebug $namedFile) -Algorithm SHA256).Hash;Copy-Item -LiteralPath (Join-Path $namedDebug $namedFile) -Destination (Join-Path $namedBackup $namedFile)}
        $namedCopied=$true
        foreach($namedFile in $namedFiles){Copy-Item -LiteralPath (Join-Path $namedRelease $namedFile) -Destination (Join-Path $namedDebug $namedFile)}
    }
    Set-Content -LiteralPath $namedLog -Value "LYRA_NAMED_NOTIFY_MATRIX configuration=$Configuration" -Encoding UTF8
    foreach($namedFile in @('GodotALS.dll','Als.Core.dll','Als.Import.dll')){Add-Content -LiteralPath $namedLog -Value "LYRA_NAMED_NOTIFY_ASSEMBLY file=$namedFile sha256=$((Get-FileHash -LiteralPath (Join-Path $namedDebug $namedFile) -Algorithm SHA256).Hash)" -Encoding UTF8}
    Invoke-NamedRun 'original-rule-history' @('--headless','--path','.','res://scenes/tests/lyra_notify_dispatch_smoke.tscn') 'traces=237 frames=36920'
    Invoke-NamedRun 'original-named-receivers' @('--headless','--path','.','res://scenes/tests/lyra_named_notify_smoke.tscn') 'traces=55 frames=890 retries=890 events=229'
    foreach($namedHz in @(30,60,120)){
        $namedPhysics=Join-Path $namedRoot "artifacts/lyra-analysis/named-notify-$namedName-$namedHz-$RunTag.json"
        Invoke-NamedRun "named-physics-$namedHz" @('--headless','--fixed-fps',"$namedHz",'--path','.','res://scenes/tests/lyra_named_notify_physics_smoke.tscn','--',"--named-hz=$namedHz","--named-report=$namedPhysics") 'LYRA_NAMED_NOTIFY_PHYSICS_GODOT_OK'
    }
    foreach($namedHz in @(30,60,120)){
        $namedPhysics=Join-Path $namedRoot "artifacts/lyra-analysis/named-notify-$namedName-pivot-$namedHz-$RunTag.json"
        Invoke-NamedRun "pivot-physics-$namedHz" @('--headless','--fixed-fps',"$namedHz",'--path','.','res://scenes/tests/lyra_pivot_notify_physics_smoke.tscn','--',"--pivot-hz=$namedHz","--pivot-report=$namedPhysics") 'LYRA_PIVOT_NOTIFY_PHYSICS_GODOT_OK'
    }
    Invoke-NamedRun 'prior-emote' @('--headless','--fixed-fps','60','--path','.','res://scenes/tests/lyra_emote_physics_smoke.tscn','--','--emote-hz=60') 'LYRA_EMOTE_PHYSICS_GODOT_OK'
    Invoke-NamedRun 'prior-warp' @('--headless','--fixed-fps','60','--path','.','res://scenes/tests/lyra_motion_warping_physics_smoke.tscn','--','--warp-physics-hz=60') 'LYRA_MOTION_WARPING_PHYSICS_GODOT_OK'
    Invoke-NamedRun 'prior-root' @('--headless','--fixed-fps','60','--path','.','res://scenes/tests/lyra_root_movement_physics_smoke.tscn','--','--root-movement-hz=60') 'LYRA_ROOT_MOVEMENT_PHYSICS_GODOT_OK hz=60 roles=6'
    foreach($namedHz in @(30,60,120)){
        $namedMain=Join-Path $namedRoot "artifacts/lyra-analysis/named-notify-$namedName-main-$namedHz-$RunTag.json"
        Invoke-NamedRun "ordinary-ten-rebind-$namedHz" @('--headless','--path','.','--','--locomotion=lyra','--lyra-profile=rifle','--lyra-characters=10','--lyra-main-smoke','--lyra-main-retry','--lyra-main-rebind','--lyra-main-weapons',"--lyra-main-hz=$namedHz","--lyra-main-report=$namedMain") 'LYRA_MAIN_MULTI_DEMO_GODOT_OK'
    }
    $namedInput=Join-Path $namedRoot "artifacts/lyra-analysis/named-notify-$namedName-input-$RunTag.json"
    Invoke-NamedRun 'ordinary-E-input' @('--headless','--path','.','--','--locomotion=lyra','--lyra-profile=rifle','--lyra-main-smoke','--lyra-main-retry','--lyra-main-emote','--lyra-main-hz=60',"--lyra-main-report=$namedInput") 'LYRA_MAIN_MODEL_GODOT_OK'
    Add-Content -LiteralPath $namedLog -Value "LYRA_NAMED_NOTIFY_MATRIX_OK configuration=$Configuration" -Encoding UTF8
} finally {
    if($namedCopied){
        foreach($namedFile in $namedFiles){Copy-Item -LiteralPath (Join-Path $namedBackup $namedFile) -Destination (Join-Path $namedDebug $namedFile);if((Get-FileHash -LiteralPath (Join-Path $namedDebug $namedFile) -Algorithm SHA256).Hash -ne $namedHashes[$namedFile]){throw 'Debug restore mismatch.'}}
        Add-Content -LiteralPath $namedLog -Value 'LYRA_NAMED_NOTIFY_DEBUG_RESTORED hashVerified=true' -Encoding UTF8
    }
    Pop-Location
}

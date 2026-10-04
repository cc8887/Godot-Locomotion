param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug',
      [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$RunTag='final')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$emoteRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$emoteName=$Configuration.ToLowerInvariant()
$emoteLog=Join-Path $emoteRoot "artifacts/lyra-analysis/emote-$emoteName-$RunTag.log"
$emoteDebug=Join-Path $emoteRoot '.godot/mono/temp/bin/Debug'
$emoteRelease=Join-Path $emoteRoot '.godot/mono/temp/bin/ExportRelease'
$emoteBackup=Join-Path $emoteRoot "artifacts/lyra-analysis/emote-debug-backup-$RunTag"
$emoteFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
if(Test-Path -LiteralPath $emoteLog){throw 'Preserve Emote evidence.'}
$emoteLive=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains($emoteRoot)})
if($emoteLive.Count){throw 'Workspace Godot is running.'}
$emoteBuildName=if($Configuration -eq 'Optimize'){'emote-build-optimize-verified.log'}else{'emote-build-debug-verified.log'}
$emoteBuild=Get-Content -LiteralPath (Join-Path $emoteRoot "artifacts/lyra-analysis/$emoteBuildName") -Raw -Encoding UTF8
if($emoteBuild -notmatch '(?m)^\s*0\s*(\u4e2a\u8b66\u544a|Warning)' -or $emoteBuild -notmatch '(?m)^\s*0\s*(\u4e2a\u9519\u8bef|Error)'){throw 'Clean Emote build required.'}
$emoteHashes=@{};$emoteCopied=$false
function Invoke-EmoteRun([string]$Name,[string[]]$Arguments,[string]$Marker){
    Add-Content -LiteralPath $emoteLog -Value "LYRA_EMOTE_RUN name=$Name" -Encoding UTF8
    & (Join-Path $emoteRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe') @Arguments 2>&1 | Out-File -LiteralPath $emoteLog -Encoding UTF8 -Append
    $emoteExit=$LASTEXITCODE
    Add-Content -LiteralPath $emoteLog -Value "LYRA_EMOTE_RUN_EXIT name=$Name code=$emoteExit" -Encoding UTF8
    $emoteText=Get-Content -LiteralPath $emoteLog -Raw -Encoding UTF8
    if($emoteExit -ne 0 -or $emoteText -match '(?m)^\s*(ERROR|WARNING):' -or $emoteText -notmatch $Marker){throw "Emote gate failed: $Name"}
    Write-Output "Emote passed: $Name"
}
Push-Location $emoteRoot
try {
    if($Configuration -eq 'Optimize'){
        if(Test-Path -LiteralPath $emoteBackup){throw 'Preserve assembly backup.'}
        New-Item -ItemType Directory -Path $emoteBackup | Out-Null
        foreach($emoteFile in $emoteFiles){$emoteHashes[$emoteFile]=(Get-FileHash -LiteralPath (Join-Path $emoteDebug $emoteFile) -Algorithm SHA256).Hash;Copy-Item -LiteralPath (Join-Path $emoteDebug $emoteFile) -Destination (Join-Path $emoteBackup $emoteFile)}
        $emoteCopied=$true
        foreach($emoteFile in $emoteFiles){Copy-Item -LiteralPath (Join-Path $emoteRelease $emoteFile) -Destination (Join-Path $emoteDebug $emoteFile)}
    }
    Set-Content -LiteralPath $emoteLog -Value "LYRA_EMOTE_MATRIX configuration=$Configuration" -Encoding UTF8
    foreach($emoteFile in @('GodotALS.dll','Als.Core.dll','Als.Import.dll')){Add-Content -LiteralPath $emoteLog -Value "LYRA_EMOTE_ASSEMBLY file=$emoteFile sha256=$((Get-FileHash -LiteralPath (Join-Path $emoteDebug $emoteFile) -Algorithm SHA256).Hash)" -Encoding UTF8}
    Invoke-EmoteRun 'original-ability' @('--headless','--path','.','res://scenes/tests/lyra_emote_smoke.tscn') 'traces=54 frames=27720'
    foreach($emoteHz in @(30,60,120)){
        $emotePhysics=Join-Path $emoteRoot "artifacts/lyra-analysis/emote-$emoteName-$emoteHz-$RunTag.json"
        Invoke-EmoteRun "physics-$emoteHz" @('--headless','--fixed-fps',"$emoteHz",'--path','.','res://scenes/tests/lyra_emote_physics_smoke.tscn','--',"--emote-hz=$emoteHz","--emote-report=$emotePhysics") 'LYRA_EMOTE_PHYSICS_GODOT_OK'
    }
    Invoke-EmoteRun 'prior-warp' @('--headless','--fixed-fps','60','--path','.','res://scenes/tests/lyra_motion_warping_physics_smoke.tscn','--','--warp-physics-hz=60') 'LYRA_MOTION_WARPING_PHYSICS_GODOT_OK'
    Invoke-EmoteRun 'prior-root' @('--headless','--fixed-fps','60','--path','.','res://scenes/tests/lyra_root_movement_physics_smoke.tscn','--','--root-movement-hz=60') 'LYRA_ROOT_MOVEMENT_PHYSICS_GODOT_OK hz=60 roles=6'
    $emoteMain=Join-Path $emoteRoot "artifacts/lyra-analysis/emote-$emoteName-main-$RunTag.json"
    Invoke-EmoteRun 'ordinary-ten-rebind' @('--headless','--path','.','--','--locomotion=lyra','--lyra-profile=rifle','--lyra-characters=10','--lyra-main-smoke','--lyra-main-retry','--lyra-main-rebind','--lyra-main-weapons','--lyra-main-hz=60',"--lyra-main-report=$emoteMain") 'LYRA_MAIN_MULTI_DEMO_GODOT_OK'
    $emoteInput=Join-Path $emoteRoot "artifacts/lyra-analysis/emote-$emoteName-input-$RunTag.json"
    Invoke-EmoteRun 'ordinary-E-input' @('--headless','--path','.','--','--locomotion=lyra','--lyra-profile=rifle','--lyra-main-smoke','--lyra-main-retry','--lyra-main-emote','--lyra-main-hz=60',"--lyra-main-report=$emoteInput") 'LYRA_MAIN_MODEL_GODOT_OK'
    Add-Content -LiteralPath $emoteLog -Value "LYRA_EMOTE_MATRIX_OK configuration=$Configuration" -Encoding UTF8
} finally {
    if($emoteCopied){
        foreach($emoteFile in $emoteFiles){Copy-Item -LiteralPath (Join-Path $emoteBackup $emoteFile) -Destination (Join-Path $emoteDebug $emoteFile);if((Get-FileHash -LiteralPath (Join-Path $emoteDebug $emoteFile) -Algorithm SHA256).Hash -ne $emoteHashes[$emoteFile]){throw 'Debug restore mismatch.'}}
        Add-Content -LiteralPath $emoteLog -Value 'LYRA_EMOTE_DEBUG_RESTORED hashVerified=true' -Encoding UTF8
    }
    Pop-Location
}

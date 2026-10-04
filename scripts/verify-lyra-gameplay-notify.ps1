param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$gameplayRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$gameplayLogs=Join-Path $gameplayRoot 'artifacts/lyra-analysis'
$gameplayName=$Configuration.ToLowerInvariant()
$gameplayLog=Join-Path $gameplayLogs "notify-gameplay-$gameplayName-matrix.log"
$gameplayExe=Join-Path $gameplayRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$gameplayDebug=Join-Path $gameplayRoot '.godot/mono/temp/bin/Debug'
$gameplayRelease=Join-Path $gameplayRoot '.godot/mono/temp/bin/ExportRelease'
$gameplayBackup=Join-Path $gameplayLogs 'notify-gameplay-debug-assemblies'
$gameplayFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
if(Test-Path -LiteralPath $gameplayLog){throw 'Preserve gameplay matrix evidence.'}
$gameplayLive=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains($gameplayRoot)})
if($gameplayLive.Count){throw 'Workspace Godot is running.'}
$gameplayBuild=Get-Content -LiteralPath (Join-Path $gameplayLogs "notify-gameplay-$gameplayName-build-final.log") -Raw
if($gameplayBuild -notmatch '0\s*(个警告|Warning)' -or $gameplayBuild -notmatch '0\s*(个错误|Error)'){throw 'Clean matching build required.'}
$gameplayHashes=@{};$gameplayCopied=$false
Set-Content -LiteralPath $gameplayLog -Value "LYRA_GAMEPLAY_MATRIX_BEGIN configuration=$Configuration"
Push-Location $gameplayRoot
try {
    if($Configuration -eq 'Optimize'){
        if(Test-Path -LiteralPath $gameplayBackup){throw 'Debug backup already exists.'}
        New-Item -ItemType Directory -Path $gameplayBackup | Out-Null
        foreach($gameplayFile in $gameplayFiles){
            $gameplayHashes[$gameplayFile]=(Get-FileHash -LiteralPath (Join-Path $gameplayDebug $gameplayFile) -Algorithm SHA256).Hash
            Copy-Item -LiteralPath (Join-Path $gameplayDebug $gameplayFile) -Destination (Join-Path $gameplayBackup $gameplayFile)
        }
        $gameplayCopied=$true
        foreach($gameplayFile in $gameplayFiles){Copy-Item -LiteralPath (Join-Path $gameplayRelease $gameplayFile) -Destination (Join-Path $gameplayDebug $gameplayFile)}
    }
    foreach($gameplayFile in @('GodotALS.dll','Als.Core.dll','Als.Import.dll')){
        Add-Content -LiteralPath $gameplayLog -Value "LYRA_GAMEPLAY_ASSEMBLY file=$gameplayFile sha256=$((Get-FileHash -LiteralPath (Join-Path $gameplayDebug $gameplayFile) -Algorithm SHA256).Hash)"
    }
    function Invoke-GameplayRun([string]$Label,[string[]]$Arguments){
        & $gameplayExe @Arguments *>> $gameplayLog
        $gameplayExit=$LASTEXITCODE
        Add-Content -LiteralPath $gameplayLog -Value "LYRA_GAMEPLAY_PROCESS_EXIT label=$Label code=$gameplayExit"
        if($gameplayExit -ne 0){throw "Gameplay validation failed: $Label"}
    }
    Invoke-GameplayRun 'original-blueprints' @('--headless','--path','.','res://scenes/tests/lyra_gameplay_notify_smoke.tscn')
    foreach($gameplayHz in @(30,60,120)){
        $gameplayReport=Join-Path $gameplayLogs "notify-gameplay-pairs-$gameplayName-$gameplayHz.json"
        Invoke-GameplayRun "pairs-$gameplayHz" @('--headless','--path','.','res://scenes/tests/lyra_multi_character_smoke.tscn','--',"--lyra-multi-hz=$gameplayHz","--lyra-multi-report=$gameplayReport")
    }
    $gameplayReport=Join-Path $gameplayLogs "notify-gameplay-ten-$gameplayName.json"
    Invoke-GameplayRun 'ordinary-ten-rebind' @('--headless','--path','.','--','--locomotion=lyra','--lyra-profile=rifle','--lyra-characters=10','--lyra-main-smoke','--lyra-main-retry','--lyra-main-rebind','--lyra-main-hz=60',"--lyra-main-report=$gameplayReport")
    Invoke-GameplayRun 'montage-queue-regression' @('--headless','--path','.','res://scenes/tests/lyra_montage_notify_smoke.tscn')
    Invoke-GameplayRun 'source-queue-regression' @('--headless','--path','.','res://scenes/tests/lyra_source_notify_smoke.tscn','--','--notify-queue')
    $gameplayText=Get-Content -LiteralPath $gameplayLog -Raw
    if($gameplayText -match '(?m)^\s*(ERROR|WARNING):' -or
        $gameplayText -notmatch 'LYRA_GAMEPLAY_NOTIFY_GODOT_OK calls=3735 events=36 .* native=True' -or
        $gameplayText -notmatch 'LYRA_MONTAGE_NOTIFY_OK .* native=True' -or $gameplayText -notmatch 'LYRA_NOTIFY_QUEUE_OK .* native=True ' -or
        ([regex]::Matches($gameplayText,'LYRA_MULTI_CHARACTER_GODOT_OK ')).Count -ne 3 -or $gameplayText -notmatch 'LYRA_MAIN_MULTI_DEMO_GODOT_OK '){throw 'Missing gate or unexpected diagnostic.'}
} finally {
    if($gameplayCopied){
        foreach($gameplayFile in $gameplayFiles){
            Copy-Item -LiteralPath (Join-Path $gameplayBackup $gameplayFile) -Destination (Join-Path $gameplayDebug $gameplayFile)
            if((Get-FileHash -LiteralPath (Join-Path $gameplayDebug $gameplayFile) -Algorithm SHA256).Hash -ne $gameplayHashes[$gameplayFile]){throw 'Debug restore hash mismatch.'}
        }
        Add-Content -LiteralPath $gameplayLog -Value 'LYRA_GAMEPLAY_DEBUG_RESTORED hashVerified=true'
    }
    Pop-Location
}

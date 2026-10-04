param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$queueRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$queueLogs=Join-Path $queueRoot 'artifacts/lyra-analysis'
$queueName=$Configuration.ToLowerInvariant()
$queueLog=Join-Path $queueLogs "notify-queue-$queueName-matrix.log"
$queueExe=Join-Path $queueRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$queueDebug=Join-Path $queueRoot '.godot/mono/temp/bin/Debug'
$queueRelease=Join-Path $queueRoot '.godot/mono/temp/bin/ExportRelease'
$queueBackup=Join-Path $queueLogs 'notify-queue-debug-assemblies'
$queueFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
if(Test-Path -LiteralPath $queueLog){throw 'Preserve queue validation evidence.'}
$queueLive=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains($queueRoot)})
if($queueLive.Count){throw 'Workspace Godot is running.'}
$queueBuild=Get-Content -LiteralPath (Join-Path $queueLogs "notify-queue-$queueName-build-final.log") -Raw
if($queueBuild -notmatch '0\s*(个警告|Warning)' -or $queueBuild -notmatch '0\s*(个错误|Error)'){throw 'Clean matching build required.'}
$queueHashes=@{};$queueCopied=$false
Set-Content -LiteralPath $queueLog -Value "LYRA_NOTIFY_QUEUE_MATRIX_BEGIN configuration=$Configuration"
Push-Location $queueRoot
try{
    if($Configuration -eq 'Optimize'){
        if(Test-Path -LiteralPath $queueBackup){throw 'Debug backup already exists.'}
        New-Item -ItemType Directory -Path $queueBackup | Out-Null
        foreach($queueFile in $queueFiles){
            $queueHashes[$queueFile]=(Get-FileHash -LiteralPath (Join-Path $queueDebug $queueFile) -Algorithm SHA256).Hash
            Copy-Item -LiteralPath (Join-Path $queueDebug $queueFile) -Destination (Join-Path $queueBackup $queueFile)
        }
        $queueCopied=$true
        foreach($queueFile in $queueFiles){Copy-Item -LiteralPath (Join-Path $queueRelease $queueFile) -Destination (Join-Path $queueDebug $queueFile)}
    }
    foreach($queueFile in @('GodotALS.dll','Als.Core.dll','Als.Import.dll')){
        Add-Content -LiteralPath $queueLog -Value "LYRA_QUEUE_ASSEMBLY file=$queueFile sha256=$((Get-FileHash -LiteralPath (Join-Path $queueDebug $queueFile) -Algorithm SHA256).Hash)"
    }
    function Invoke-QueueRun([string]$Label,[string[]]$Arguments){
        & $queueExe @Arguments *>> $queueLog
        $queueExit=$LASTEXITCODE
        Add-Content -LiteralPath $queueLog -Value "LYRA_QUEUE_PROCESS_EXIT label=$Label code=$queueExit"
        if($queueExit -ne 0){throw "Notify queue validation failed: $Label"}
    }
    Invoke-QueueRun 'native-source-queue' @('--headless','--path','.','res://scenes/tests/lyra_source_notify_smoke.tscn','--','--notify-queue')
    foreach($queueHz in @(30,60,120)){
        $queueReport=Join-Path $queueLogs "notify-queue-pairs-$queueName-$queueHz.json"
        Invoke-QueueRun "pairs-$queueHz" @('--headless','--path','.','res://scenes/tests/lyra_multi_character_smoke.tscn','--',"--lyra-multi-hz=$queueHz","--lyra-multi-report=$queueReport")
    }
    $queueReport=Join-Path $queueLogs "notify-queue-ten-$queueName.json"
    Invoke-QueueRun 'ordinary-ten-rebind' @('--headless','--path','.','--','--locomotion=lyra','--lyra-profile=rifle','--lyra-characters=10','--lyra-main-smoke','--lyra-main-retry','--lyra-main-rebind','--lyra-main-hz=60',"--lyra-main-report=$queueReport")
    Invoke-QueueRun 'main-native-regression' @('--headless','--path','.','res://scenes/tests/lyra_main_als_native_smoke.tscn')
    $queueText=Get-Content -LiteralPath $queueLog -Raw
    if($queueText -match '(?m)^\s*(ERROR|WARNING):' -or $queueText -notmatch 'LYRA_NOTIFY_QUEUE_OK .* native=True ' -or
        ([regex]::Matches($queueText,'LYRA_MULTI_CHARACTER_GODOT_OK ')).Count -ne 3 -or $queueText -notmatch 'LYRA_MAIN_MULTI_DEMO_GODOT_OK '){throw 'Missing gate or unexpected diagnostic.'}
}finally{
    if($queueCopied){
        foreach($queueFile in $queueFiles){
            Copy-Item -LiteralPath (Join-Path $queueBackup $queueFile) -Destination (Join-Path $queueDebug $queueFile)
            if((Get-FileHash -LiteralPath (Join-Path $queueDebug $queueFile) -Algorithm SHA256).Hash -ne $queueHashes[$queueFile]){throw 'Debug restore hash mismatch.'}
        }
        Add-Content -LiteralPath $queueLog -Value 'LYRA_QUEUE_DEBUG_RESTORED hashVerified=true'
    }
    Pop-Location
}

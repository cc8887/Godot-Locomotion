param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$notifyRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$notifyLogs=Join-Path $notifyRoot 'artifacts/lyra-analysis'
$notifyName=$Configuration.ToLowerInvariant()
$notifyLog=Join-Path $notifyLogs "notify-source-$notifyName-matrix.log"
$notifyExe=Join-Path $notifyRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$notifyDebug=Join-Path $notifyRoot '.godot/mono/temp/bin/Debug'
$notifyRelease=Join-Path $notifyRoot '.godot/mono/temp/bin/ExportRelease'
$notifyBackup=Join-Path $notifyLogs 'notify-source-debug-assemblies'
$notifyFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
if(Test-Path -LiteralPath $notifyLog){throw 'Preserve notify validation evidence.'}
$notifyLive=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains($notifyRoot)})
if($notifyLive.Count){throw 'Workspace Godot is running.'}
$notifyBuildName=if($Configuration -eq 'Debug'){'notify-source-debug-build-verified.log'}else{'notify-source-optimize-build-verified.log'}
$notifyBuild=Get-Content -LiteralPath (Join-Path $notifyLogs $notifyBuildName) -Raw
if($notifyBuild -notmatch '0\s*(个警告|Warning)' -or $notifyBuild -notmatch '0\s*(个错误|Error)'){throw 'Clean matching build required.'}
$notifyHashes=@{}
$notifyCopied=$false
Set-Content -LiteralPath $notifyLog -Value "LYRA_SOURCE_NOTIFY_MATRIX_BEGIN configuration=$Configuration"
Push-Location $notifyRoot
try{
    if($Configuration -eq 'Optimize'){
        if(Test-Path -LiteralPath $notifyBackup){throw 'Debug backup already exists.'}
        New-Item -ItemType Directory -Path $notifyBackup | Out-Null
        foreach($notifyFile in $notifyFiles){
            $notifyHashes[$notifyFile]=(Get-FileHash -LiteralPath (Join-Path $notifyDebug $notifyFile) -Algorithm SHA256).Hash
            Copy-Item -LiteralPath (Join-Path $notifyDebug $notifyFile) -Destination (Join-Path $notifyBackup $notifyFile)
        }
        $notifyCopied=$true
        foreach($notifyFile in $notifyFiles){Copy-Item -LiteralPath (Join-Path $notifyRelease $notifyFile) -Destination (Join-Path $notifyDebug $notifyFile)}
    }
    foreach($notifyFile in @('GodotALS.dll','Als.Core.dll','Als.Import.dll')){
        Add-Content -LiteralPath $notifyLog -Value "LYRA_NOTIFY_ASSEMBLY file=$notifyFile sha256=$((Get-FileHash -LiteralPath (Join-Path $notifyDebug $notifyFile) -Algorithm SHA256).Hash)"
    }
    function Invoke-NotifyRun([string]$Label,[string[]]$Arguments){
        & $notifyExe @Arguments *>> $notifyLog
        $notifyExit=$LASTEXITCODE
        Add-Content -LiteralPath $notifyLog -Value "LYRA_NOTIFY_PROCESS_EXIT label=$Label code=$notifyExit"
        if($notifyExit -ne 0){throw "Source notify validation failed: $Label"}
    }
    Invoke-NotifyRun 'source-notify' @('--headless','--path','.', 'res://scenes/tests/lyra_source_notify_smoke.tscn')
    Invoke-NotifyRun 'main-native-regression' @('--headless','--path','.', 'res://scenes/tests/lyra_main_als_native_smoke.tscn')
    $notifyReport=Join-Path $notifyLogs "notify-source-ordinary-$notifyName.json"
    Invoke-NotifyRun 'ordinary-rebind-regression' @('--headless','--path','.', '--','--locomotion=lyra','--lyra-main-smoke','--lyra-main-retry','--lyra-main-rebind','--lyra-main-hz=60',"--lyra-main-report=$notifyReport")
    $notifyText=Get-Content -LiteralPath $notifyLog -Raw
    if($notifyText -match '(?m)^\s*(ERROR|WARNING):' -or $notifyText -notmatch 'LYRA_SOURCE_NOTIFY_OK frames=7560 '){throw 'Missing gate or unexpected diagnostic.'}
}finally{
    if($notifyCopied){
        foreach($notifyFile in $notifyFiles){
            Copy-Item -LiteralPath (Join-Path $notifyBackup $notifyFile) -Destination (Join-Path $notifyDebug $notifyFile)
            if((Get-FileHash -LiteralPath (Join-Path $notifyDebug $notifyFile) -Algorithm SHA256).Hash -ne $notifyHashes[$notifyFile]){throw 'Debug restore hash mismatch.'}
        }
        Add-Content -LiteralPath $notifyLog -Value 'LYRA_NOTIFY_DEBUG_RESTORED hashVerified=true'
    }
    Pop-Location
}

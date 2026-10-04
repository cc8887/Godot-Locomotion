param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$montageRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$montageLogs=Join-Path $montageRoot 'artifacts/lyra-analysis'
$montageName=$Configuration.ToLowerInvariant()
$montageLog=Join-Path $montageLogs "notify-montage-$montageName-matrix.log"
$montageExe=Join-Path $montageRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$montageDebug=Join-Path $montageRoot '.godot/mono/temp/bin/Debug'
$montageRelease=Join-Path $montageRoot '.godot/mono/temp/bin/ExportRelease'
$montageBackup=Join-Path $montageLogs 'notify-montage-debug-assemblies'
$montageFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
if(Test-Path -LiteralPath $montageLog){throw 'Preserve Montage validation evidence.'}
$montageLive=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains($montageRoot)})
if($montageLive.Count){throw 'Workspace Godot is running.'}
$montageBuild=Get-Content -LiteralPath (Join-Path $montageLogs "notify-montage-$montageName-build-coverage-final.log") -Raw
if($montageBuild -notmatch '0\s*(个警告|Warning)' -or $montageBuild -notmatch '0\s*(个错误|Error)'){throw 'Clean matching build required.'}
$montageHashes=@{};$montageCopied=$false
Set-Content -LiteralPath $montageLog -Value "LYRA_MONTAGE_NOTIFY_MATRIX_BEGIN configuration=$Configuration"
Push-Location $montageRoot
try {
    if($Configuration -eq 'Optimize'){
        if(Test-Path -LiteralPath $montageBackup){throw 'Debug backup already exists.'}
        New-Item -ItemType Directory -Path $montageBackup | Out-Null
        foreach($montageFile in $montageFiles){
            $montageHashes[$montageFile]=(Get-FileHash -LiteralPath (Join-Path $montageDebug $montageFile) -Algorithm SHA256).Hash
            Copy-Item -LiteralPath (Join-Path $montageDebug $montageFile) -Destination (Join-Path $montageBackup $montageFile)
        }
        $montageCopied=$true
        foreach($montageFile in $montageFiles){Copy-Item -LiteralPath (Join-Path $montageRelease $montageFile) -Destination (Join-Path $montageDebug $montageFile)}
    }
    foreach($montageFile in @('GodotALS.dll','Als.Core.dll','Als.Import.dll')){
        Add-Content -LiteralPath $montageLog -Value "LYRA_MONTAGE_ASSEMBLY file=$montageFile sha256=$((Get-FileHash -LiteralPath (Join-Path $montageDebug $montageFile) -Algorithm SHA256).Hash)"
    }
    function Invoke-MontageRun([string]$Label,[string[]]$Arguments){
        & $montageExe @Arguments *>> $montageLog
        $montageExit=$LASTEXITCODE
        Add-Content -LiteralPath $montageLog -Value "LYRA_MONTAGE_PROCESS_EXIT label=$Label code=$montageExit"
        if($montageExit -ne 0){throw "Montage validation failed: $Label"}
    }
    Invoke-MontageRun 'native-montage-queue' @('--headless','--path','.','res://scenes/tests/lyra_montage_notify_smoke.tscn')
    Invoke-MontageRun 'source-queue-regression' @('--headless','--path','.','res://scenes/tests/lyra_source_notify_smoke.tscn','--','--notify-queue')
    foreach($montageHz in @(30,60,120)){
        $montageReport=Join-Path $montageLogs "notify-montage-pairs-$montageName-$montageHz.json"
        Invoke-MontageRun "pairs-$montageHz" @('--headless','--path','.','res://scenes/tests/lyra_multi_character_smoke.tscn','--',"--lyra-multi-hz=$montageHz","--lyra-multi-report=$montageReport")
    }
    $montageReport=Join-Path $montageLogs "notify-montage-ten-$montageName.json"
    Invoke-MontageRun 'ordinary-ten-rebind' @('--headless','--path','.','--','--locomotion=lyra','--lyra-profile=rifle','--lyra-characters=10','--lyra-main-smoke','--lyra-main-retry','--lyra-main-rebind','--lyra-main-hz=60',"--lyra-main-report=$montageReport")
    Invoke-MontageRun 'slot-native-regression' @('--headless','--path','.','res://scenes/tests/lyra_montage_slots_smoke.tscn')
    Invoke-MontageRun 'main-native-regression' @('--headless','--path','.','res://scenes/tests/lyra_main_als_native_smoke.tscn')
    $montageText=Get-Content -LiteralPath $montageLog -Raw
    if($montageText -match '(?m)^\s*(ERROR|WARNING):' -or $montageText -notmatch 'LYRA_MONTAGE_NOTIFY_OK .* native=True' -or
        $montageText -notmatch 'LYRA_NOTIFY_QUEUE_OK .* native=True ' -or
        ([regex]::Matches($montageText,'LYRA_MULTI_CHARACTER_GODOT_OK ')).Count -ne 3 -or $montageText -notmatch 'LYRA_MAIN_MULTI_DEMO_GODOT_OK '){throw 'Missing gate or unexpected diagnostic.'}
} finally {
    if($montageCopied){
        foreach($montageFile in $montageFiles){
            Copy-Item -LiteralPath (Join-Path $montageBackup $montageFile) -Destination (Join-Path $montageDebug $montageFile)
            if((Get-FileHash -LiteralPath (Join-Path $montageDebug $montageFile) -Algorithm SHA256).Hash -ne $montageHashes[$montageFile]){throw 'Debug restore hash mismatch.'}
        }
        Add-Content -LiteralPath $montageLog -Value 'LYRA_MONTAGE_DEBUG_RESTORED hashVerified=true'
    }
    Pop-Location
}

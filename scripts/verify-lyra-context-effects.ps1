param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$contextRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$contextLogs=Join-Path $contextRoot 'artifacts/lyra-analysis'
$contextName=$Configuration.ToLowerInvariant()
$contextLog=Join-Path $contextLogs "notify-context-$contextName-matrix.log"
$contextExe=Join-Path $contextRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$contextDebug=Join-Path $contextRoot '.godot/mono/temp/bin/Debug'
$contextRelease=Join-Path $contextRoot '.godot/mono/temp/bin/ExportRelease'
$contextBackup=Join-Path $contextLogs 'notify-context-debug-assemblies'
$contextFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
if(Test-Path -LiteralPath $contextLog){throw 'Preserve context matrix evidence.'}
$contextLive=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains($contextRoot)})
if($contextLive.Count){throw 'Workspace Godot is running.'}
$contextBuild=Get-Content -LiteralPath (Join-Path $contextLogs "notify-context-$contextName-build-accepted.log") -Raw
if($contextBuild -notmatch '0\s*(个警告|Warning)' -or $contextBuild -notmatch '0\s*(个错误|Error)'){throw 'Clean matching build required.'}
$contextHashes=@{};$contextCopied=$false
Set-Content -LiteralPath $contextLog -Value "LYRA_CONTEXT_MATRIX_BEGIN configuration=$Configuration"
Push-Location $contextRoot
try {
    if($Configuration -eq 'Optimize'){
        if(Test-Path -LiteralPath $contextBackup){throw 'Debug backup already exists.'}
        New-Item -ItemType Directory -Path $contextBackup | Out-Null
        foreach($contextFile in $contextFiles){
            $contextHashes[$contextFile]=(Get-FileHash -LiteralPath (Join-Path $contextDebug $contextFile) -Algorithm SHA256).Hash
            Copy-Item -LiteralPath (Join-Path $contextDebug $contextFile) -Destination (Join-Path $contextBackup $contextFile)
        }
        $contextCopied=$true
        foreach($contextFile in $contextFiles){Copy-Item -LiteralPath (Join-Path $contextRelease $contextFile) -Destination (Join-Path $contextDebug $contextFile)}
    }
    foreach($contextFile in @('GodotALS.dll','Als.Core.dll','Als.Import.dll')){
        Add-Content -LiteralPath $contextLog -Value "LYRA_CONTEXT_ASSEMBLY file=$contextFile sha256=$((Get-FileHash -LiteralPath (Join-Path $contextDebug $contextFile) -Algorithm SHA256).Hash)"
    }
    function Invoke-ContextRun([string]$Label,[string[]]$Arguments){
        & $contextExe @Arguments *>> $contextLog
        $contextExit=$LASTEXITCODE
        Add-Content -LiteralPath $contextLog -Value "LYRA_CONTEXT_PROCESS_EXIT label=$Label code=$contextExit"
        if($contextExit -ne 0){throw "Context validation failed: $Label"}
    }
    Invoke-ContextRun 'original-context-effects' @('--headless','--fixed-fps','60','--path','.','res://scenes/tests/lyra_context_effects_smoke.tscn')
    foreach($contextHz in @(30,60,120)){
        $contextReport=Join-Path $contextLogs "notify-context-pairs-$contextName-$contextHz.json"
        Invoke-ContextRun "pairs-$contextHz" @('--headless','--path','.','res://scenes/tests/lyra_multi_character_smoke.tscn','--',"--lyra-multi-hz=$contextHz","--lyra-multi-report=$contextReport")
    }
    $contextReport=Join-Path $contextLogs "notify-context-ten-$contextName.json"
    Invoke-ContextRun 'ordinary-ten-rebind' @('--headless','--path','.','--','--locomotion=lyra','--lyra-profile=rifle','--lyra-characters=10','--lyra-main-smoke','--lyra-main-retry','--lyra-main-rebind','--lyra-main-hz=60',"--lyra-main-report=$contextReport")
    Invoke-ContextRun 'gameplay-regression' @('--headless','--path','.','res://scenes/tests/lyra_gameplay_notify_smoke.tscn')
    Invoke-ContextRun 'montage-queue-regression' @('--headless','--path','.','res://scenes/tests/lyra_montage_notify_smoke.tscn')
    Invoke-ContextRun 'source-queue-regression' @('--headless','--path','.','res://scenes/tests/lyra_source_notify_smoke.tscn','--','--notify-queue')
    $contextText=Get-Content -LiteralPath $contextLog -Raw
    if($contextText -match '(?m)^\s*(ERROR|WARNING):' -or
        $contextText -notmatch 'LYRA_CONTEXT_EFFECTS_GODOT_OK calls=4121 .* messages=8242 .* native=True' -or
        $contextText -notmatch 'LYRA_MONTAGE_NOTIFY_OK .* native=True' -or $contextText -notmatch 'LYRA_NOTIFY_QUEUE_OK .* native=True ' -or
        ([regex]::Matches($contextText,'LYRA_MULTI_CHARACTER_GODOT_OK ')).Count -ne 3 -or $contextText -notmatch 'LYRA_MAIN_MULTI_DEMO_GODOT_OK '){throw 'Missing gate or unexpected diagnostic.'}
} finally {
    if($contextCopied){
        foreach($contextFile in $contextFiles){
            Copy-Item -LiteralPath (Join-Path $contextBackup $contextFile) -Destination (Join-Path $contextDebug $contextFile)
            if((Get-FileHash -LiteralPath (Join-Path $contextDebug $contextFile) -Algorithm SHA256).Hash -ne $contextHashes[$contextFile]){throw 'Debug restore hash mismatch.'}
        }
        Add-Content -LiteralPath $contextLog -Value 'LYRA_CONTEXT_DEBUG_RESTORED hashVerified=true'
    }
    Pop-Location
}

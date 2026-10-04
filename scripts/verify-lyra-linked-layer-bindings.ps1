param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug',
      [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$EvidenceTag='linked-layer-bindings-v2',
      [ValidateSet('single','three-groups','mixed','per-call')][string]$LayerLayout='single')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$bindingRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$bindingPrefix=Join-Path $bindingRoot "artifacts/lyra-analysis/$EvidenceTag-$($Configuration.ToLowerInvariant())"
$bindingDebug=Join-Path $bindingRoot '.godot/mono/temp/bin/Debug'
$bindingRelease=Join-Path $bindingRoot '.godot/mono/temp/bin/ExportRelease'
$bindingGodot=Join-Path $bindingRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$bindingFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
$bindingSummary="$bindingPrefix-verification.json"
$bindingBackup="$bindingPrefix-debug-backup"
if(Test-Path -LiteralPath $bindingSummary){throw 'Preserve existing verification.'}
$bindingLive=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -match 'Godot' -and $_.CommandLine -and $_.CommandLine.Contains($bindingRoot)})
if($bindingLive.Count){throw 'Workspace Godot is running.'}
$bindingCases=@(@{name='original-binding';scene='res://scenes/tests/lyra_linked_layer_binding_smoke.tscn';args=@();marker='LYRA_LINKED_BINDING_OK'})
foreach($bindingProfile in @('unarmed','pistol','rifle')){
    foreach($bindingHz in @(30,60,120)){
        $bindingName="$bindingProfile-$bindingHz"
        $bindingReport="$bindingPrefix-$bindingName.json"
        $bindingCases+=@{name=$bindingName;report=$bindingReport;args=@('--locomotion=lyra',"--lyra-profile=$bindingProfile",'--lyra-main-smoke','--lyra-main-retry','--lyra-main-rebind',"--lyra-main-hz=$bindingHz","--lyra-main-report=$bindingReport");marker='LYRA_MAIN_REBIND_GODOT_OK'}
    }
}
$bindingReport="$bindingPrefix-ten-characters.json"
$bindingCases+=@{name='ten-characters';report=$bindingReport;args=@('--locomotion=lyra','--lyra-profile=rifle','--lyra-characters=10','--lyra-main-smoke','--lyra-main-retry','--lyra-main-rebind','--lyra-main-weapons','--lyra-main-hz=60',"--lyra-main-report=$bindingReport");marker='LYRA_MAIN_MULTI_DEMO_GODOT_OK'}
if($LayerLayout -ne 'single'){
    foreach($bindingCase in $bindingCases){if($bindingCase.ContainsKey('report')){$bindingCase.args+="--lyra-layer-layout=$LayerLayout"}}
}
foreach($bindingCase in $bindingCases){
    if(Test-Path -LiteralPath "$bindingPrefix-$($bindingCase.name).log"){throw 'Preserve run log.'}
    if($bindingCase.ContainsKey('report') -and (Test-Path -LiteralPath $bindingCase.report)){throw 'Preserve run report.'}
}
$bindingCopied=$false;$bindingHashes=@{};$bindingAssemblies=@{};$bindingRuns=@();$bindingRestored=$false
try{
    if($Configuration -eq 'Optimize'){
        if(Test-Path -LiteralPath $bindingBackup){throw 'Preserve assembly backup.'}
        New-Item -ItemType Directory -Path $bindingBackup | Out-Null
        foreach($bindingFile in $bindingFiles){
            $bindingHashes[$bindingFile]=(Get-FileHash -LiteralPath (Join-Path $bindingDebug $bindingFile) -Algorithm SHA256).Hash
            Copy-Item -LiteralPath (Join-Path $bindingDebug $bindingFile) -Destination (Join-Path $bindingBackup $bindingFile)
        }
        $bindingCopied=$true
        foreach($bindingFile in $bindingFiles){
            Copy-Item -LiteralPath (Join-Path $bindingRelease $bindingFile) -Destination (Join-Path $bindingDebug $bindingFile)
            if((Get-FileHash -LiteralPath (Join-Path $bindingDebug $bindingFile) -Algorithm SHA256).Hash -ne
                (Get-FileHash -LiteralPath (Join-Path $bindingRelease $bindingFile) -Algorithm SHA256).Hash){throw 'Optimize assembly mismatch.'}
        }
    }
    foreach($bindingFile in $bindingFiles){$bindingAssemblies[$bindingFile]=(Get-FileHash -LiteralPath (Join-Path $bindingDebug $bindingFile) -Algorithm SHA256).Hash}
    foreach($bindingCase in $bindingCases){
        $bindingLog="$bindingPrefix-$($bindingCase.name).log"
        $bindingArgs=@('--headless','--path',$bindingRoot)
        if($bindingCase.ContainsKey('scene')){$bindingArgs+=$bindingCase.scene}
        if($bindingCase.args.Count){$bindingArgs+='--';$bindingArgs+=$bindingCase.args}
        & $bindingGodot @bindingArgs *> $bindingLog
        $bindingExit=$LASTEXITCODE
        $bindingText=Get-Content -LiteralPath $bindingLog -Raw
        $bindingPassed=$bindingExit -eq 0 -and $bindingText.Contains($bindingCase.marker) -and $bindingText -notmatch '(?m)^\s*(ERROR|WARNING):'
        $bindingRun=@{name=$bindingCase.name;exitCode=$bindingExit;passed=$bindingPassed;log=$bindingLog;logSha256=(Get-FileHash -LiteralPath $bindingLog -Algorithm SHA256).Hash}
        if($bindingCase.ContainsKey('report') -and (Test-Path -LiteralPath $bindingCase.report)){
            $bindingRun.report=$bindingCase.report;$bindingRun.reportSha256=(Get-FileHash -LiteralPath $bindingCase.report -Algorithm SHA256).Hash
        }
        $bindingRuns+=$bindingRun
        Write-Output "Linked binding $Configuration $($bindingCase.name) passed=$bindingPassed exit=$bindingExit"
        if(!$bindingPassed){throw 'Linked layer runtime regression failed.'}
    }
}finally{
    if($bindingCopied){
        foreach($bindingFile in $bindingFiles){
            Copy-Item -LiteralPath (Join-Path $bindingBackup $bindingFile) -Destination (Join-Path $bindingDebug $bindingFile)
            if((Get-FileHash -LiteralPath (Join-Path $bindingDebug $bindingFile) -Algorithm SHA256).Hash -ne $bindingHashes[$bindingFile]){throw 'Debug restore mismatch.'}
        }
        $bindingRestored=$true
    }
    @{configuration=$Configuration;layerLayout=$LayerLayout;assemblies=$bindingAssemblies;runs=$bindingRuns;passed=($bindingRuns.Count -eq $bindingCases.Count -and @($bindingRuns | Where-Object {!$_.passed}).Count -eq 0);debugRestored=$bindingRestored} |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $bindingSummary -Encoding utf8
}

param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug',
      [Parameter(Mandatory=$true)][ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$EvidenceTag)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$cmcRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$cmcPrefix=Join-Path $cmcRoot "artifacts/lyra-analysis/cmc-components-$($Configuration.ToLowerInvariant())-$EvidenceTag"
$cmcReport="$cmcPrefix-verification.json"
$cmcDebug=Join-Path $cmcRoot '.godot/mono/temp/bin/Debug'
$cmcRelease=Join-Path $cmcRoot '.godot/mono/temp/bin/ExportRelease'
$cmcBackup="$cmcPrefix-debug-backup"
$cmcFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
if(Test-Path -LiteralPath $cmcReport){throw 'Preserve component evidence.'}
if(@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains($cmcRoot)}).Count){throw 'Workspace Godot is running.'}
$cmcCases=@(
    @{name='idle-original';scene='lyra_idle_runtime_smoke';marker='LYRA_IDLE_RUNTIME_GODOT_OK'},
    @{name='rig-target';scene='lyra_rig_target_output_smoke';marker='LYRA_RIG_TARGET_OUTPUT_GODOT_OK'},
    @{name='rig-scene';scene='lyra_rig_scene_collision_smoke';marker='LYRA_RIG_SCENE_COLLISION_GODOT_OK';args=@('--als-reference',"--report=$cmcPrefix-rig-scene.json")})
$cmcSaved=@{};$cmcAssemblies=@{};$cmcCopied=$false;$cmcRestored=$false;$cmcRuns=@()
try{
    if($Configuration -eq 'Optimize'){
        if(Test-Path -LiteralPath $cmcBackup){throw 'Preserve assembly backup.'}
        New-Item -ItemType Directory -Path $cmcBackup | Out-Null
        foreach($cmcFile in $cmcFiles){$cmcSaved[$cmcFile]=(Get-FileHash -LiteralPath (Join-Path $cmcDebug $cmcFile) -Algorithm SHA256).Hash;Copy-Item -LiteralPath (Join-Path $cmcDebug $cmcFile) -Destination (Join-Path $cmcBackup $cmcFile)}
        $cmcCopied=$true
        foreach($cmcFile in $cmcFiles){Copy-Item -LiteralPath (Join-Path $cmcRelease $cmcFile) -Destination (Join-Path $cmcDebug $cmcFile)}
    }
    foreach($cmcFile in $cmcFiles){$cmcAssemblies[$cmcFile]=(Get-FileHash -LiteralPath (Join-Path $cmcDebug $cmcFile) -Algorithm SHA256).Hash}
    foreach($cmcCase in $cmcCases){
        $cmcLog="$cmcPrefix-$($cmcCase.name).log"
        if(Test-Path -LiteralPath $cmcLog){throw 'Preserve component log.'}
        $cmcArgs=@('--headless','--path',$cmcRoot,"res://scenes/tests/$($cmcCase.scene).tscn")
        if($cmcCase.ContainsKey('args')){$cmcArgs+='--';$cmcArgs+=$cmcCase.args}
        & (Join-Path $cmcRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe') @cmcArgs *> $cmcLog
        $cmcExit=$LASTEXITCODE
        Add-Content -LiteralPath $cmcLog -Value "WHOLE_MAIN_PROCESS_EXIT=$cmcExit" -Encoding UTF8
        $cmcText=Get-Content -LiteralPath $cmcLog -Raw -Encoding UTF8
        $cmcPassed=$cmcExit -eq 0 -and $cmcText.Contains($cmcCase.marker) -and $cmcText -notmatch '(?m)^\s*(ERROR|WARNING):'
        $cmcRuns+=@{name=$cmcCase.name;exitCode=$cmcExit;passed=$cmcPassed;log=$cmcLog}
        Write-Output "CMC component=$($cmcCase.name) passed=$cmcPassed exit=$cmcExit"
    }
}finally{
    if($cmcCopied){foreach($cmcFile in $cmcFiles){Copy-Item -LiteralPath (Join-Path $cmcBackup $cmcFile) -Destination (Join-Path $cmcDebug $cmcFile);if((Get-FileHash -LiteralPath (Join-Path $cmcDebug $cmcFile) -Algorithm SHA256).Hash -ne $cmcSaved[$cmcFile]){throw 'Debug restore mismatch.'}};$cmcRestored=$true}
    @{configuration=$Configuration;assemblies=$cmcAssemblies;runs=$cmcRuns;debugRestored=$cmcRestored;completeAcceptance=$false} | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $cmcReport -Encoding UTF8
}
if($cmcRuns.Count -ne $cmcCases.Count -or @($cmcRuns | Where-Object {-not $_.passed}).Count){exit 1}

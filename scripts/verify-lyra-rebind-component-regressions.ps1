param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug',
      [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$EvidenceTag='final')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$componentRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$componentName=$Configuration.ToLowerInvariant()
$componentPrefix=Join-Path $componentRoot "artifacts/lyra-analysis/rebind-components-$componentName-$EvidenceTag"
$componentReport="$componentPrefix-verification.json"
$componentDebug=Join-Path $componentRoot '.godot/mono/temp/bin/Debug'
$componentRelease=Join-Path $componentRoot '.godot/mono/temp/bin/ExportRelease'
$componentBackup="$componentPrefix-debug-backup"
$componentFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
if(Test-Path -LiteralPath $componentReport){throw 'Preserve component regression evidence.'}
$componentLive=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains($componentRoot)})
if($componentLive.Count){throw 'Workspace Godot is running.'}
$componentHashes=@{};$componentAssemblies=@{};$componentCopied=$false;$componentRuns=@();$componentRestored=$false
$componentCases=@(
    @{name='fixed-actions';scene='lyra_whole_main_diagnostic_smoke';marker='LYRA_WHOLE_MAIN_DIAGNOSTIC_OK frames=2160 profiles=3';args=@('--whole-main-run=actions60-asc','--whole-main-retry')},
    @{name='stride-native';scene='lyra_stride_warping_smoke';marker='LYRA_CYCLE_LAYER_SOURCES_GODOT_OK'},
    @{name='skeletal-native';scene='lyra_skeletal_controls_smoke';marker='LYRA_SKELETAL_CONTROLS_GODOT_OK'},
    @{name='als-ordinary';scene='refactored_stance_demo_smoke';marker='ALS_REFACTORED_STANCE_DEMO_OK hz=60';args=@('--stance-hz=60')})
try {
    if($Configuration -eq 'Optimize'){
        if(Test-Path -LiteralPath $componentBackup){throw 'Preserve Debug assembly backup.'}
        New-Item -ItemType Directory -Path $componentBackup | Out-Null
        foreach($componentFile in $componentFiles){
            $componentHashes[$componentFile]=(Get-FileHash -LiteralPath (Join-Path $componentDebug $componentFile) -Algorithm SHA256).Hash
            Copy-Item -LiteralPath (Join-Path $componentDebug $componentFile) -Destination (Join-Path $componentBackup $componentFile)
        }
        $componentCopied=$true
        foreach($componentFile in $componentFiles){Copy-Item -LiteralPath (Join-Path $componentRelease $componentFile) -Destination (Join-Path $componentDebug $componentFile)}
    }
    foreach($componentFile in $componentFiles){$componentAssemblies[$componentFile]=(Get-FileHash -LiteralPath (Join-Path $componentDebug $componentFile) -Algorithm SHA256).Hash}
    foreach($componentCase in $componentCases){
        $componentLog="$componentPrefix-$($componentCase.name).log"
        if(Test-Path -LiteralPath $componentLog){throw 'Preserve regression log.'}
        $componentArgs=@('--headless','--path',$componentRoot,"res://scenes/tests/$($componentCase.scene).tscn")
        if($componentCase.ContainsKey('args')){$componentArgs+='--';$componentArgs+=$componentCase.args}
        & (Join-Path $componentRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe') @componentArgs *> $componentLog
        $componentExit=$LASTEXITCODE
        Add-Content -LiteralPath $componentLog -Value "WHOLE_MAIN_PROCESS_EXIT=$componentExit" -Encoding UTF8
        $componentText=Get-Content -LiteralPath $componentLog -Raw -Encoding UTF8
        $componentPassed=$componentExit -eq 0 -and $componentText.Contains($componentCase.marker) -and $componentText -notmatch '(?m)^\s*(ERROR|WARNING):'
        $componentRuns+=@{name=$componentCase.name;exitCode=$componentExit;passed=$componentPassed;log=$componentLog}
        Write-Output "Component regression=$($componentCase.name) passed=$componentPassed exit=$componentExit"
    }
} finally {
    if($componentCopied){
        foreach($componentFile in $componentFiles){
            Copy-Item -LiteralPath (Join-Path $componentBackup $componentFile) -Destination (Join-Path $componentDebug $componentFile)
            if((Get-FileHash -LiteralPath (Join-Path $componentDebug $componentFile) -Algorithm SHA256).Hash -ne $componentHashes[$componentFile]){throw 'Debug restore mismatch.'}
        }
        $componentRestored=$true
    }
    @{configuration=$Configuration;assemblies=$componentAssemblies;runs=$componentRuns;debugRestored=$componentRestored;completeAcceptance=$false} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $componentReport -Encoding UTF8
}
if($componentRuns.Count -ne $componentCases.Count -or @($componentRuns | Where-Object {-not $_.passed}).Count){exit 1}

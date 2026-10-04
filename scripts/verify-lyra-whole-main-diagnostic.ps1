param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug',
      [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$RunTag='als-mask-cache',
      [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$EvidenceTag='final',
      [switch]$IncludeRegressions,
      [switch]$Retry)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$wholeRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$wholeName=$Configuration.ToLowerInvariant()
$wholePrefix=Join-Path $wholeRoot "artifacts/lyra-analysis/whole-main-$wholeName-$EvidenceTag"
$wholeDebug=Join-Path $wholeRoot '.godot/mono/temp/bin/Debug'
$wholeRelease=Join-Path $wholeRoot '.godot/mono/temp/bin/ExportRelease'
$wholeBackup="$wholePrefix-debug-backup"
$wholeFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
$wholeNativeMath=Join-Path $wholeRoot 'assets/generated/lyra_als/native_win64/LyraNativeMath.dll'
$wholeNativeMathHash=(Get-FileHash -LiteralPath $wholeNativeMath -Algorithm SHA256).Hash
$wholeReport="$wholePrefix-verification.json"
$wholeRequest=Get-Content -LiteralPath (Join-Path $wholeRoot "artifacts/lyra-analysis/whole-main-$RunTag-request.json") -Raw | ConvertFrom-Json
$wholeExpectedFrames=($wholeRequest.traces | ForEach-Object {$_.frames.Count} | Measure-Object -Sum).Sum
$wholeControlledText=(@($wholeRequest.traces | Where-Object {$_.case -eq 'physics'}).Count -eq 0).ToString().ToLowerInvariant()
if(Test-Path -LiteralPath $wholeReport){throw 'Preserve diagnostic evidence.'}
$wholeLive=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains($wholeRoot)})
if($wholeLive.Count){throw 'Workspace Godot is running.'}
$wholeHashes=@{};$wholeCopied=$false;$wholeRuns=@();$wholeAssemblies=@{};$wholeRegressions=@()
try {
    if($Configuration -eq 'Optimize'){
        if(Test-Path -LiteralPath $wholeBackup){throw 'Preserve assembly backup.'}
        New-Item -ItemType Directory -Path $wholeBackup | Out-Null
        foreach($wholeFile in $wholeFiles){$wholeHashes[$wholeFile]=(Get-FileHash -LiteralPath (Join-Path $wholeDebug $wholeFile) -Algorithm SHA256).Hash;Copy-Item -LiteralPath (Join-Path $wholeDebug $wholeFile) -Destination (Join-Path $wholeBackup $wholeFile)}
        $wholeCopied=$true
        foreach($wholeFile in $wholeFiles){Copy-Item -LiteralPath (Join-Path $wholeRelease $wholeFile) -Destination (Join-Path $wholeDebug $wholeFile)}
    }
    foreach($wholeFile in $wholeFiles){$wholeAssemblies[$wholeFile]=(Get-FileHash -LiteralPath (Join-Path $wholeDebug $wholeFile) -Algorithm SHA256).Hash}
    foreach($wholeBoundary in @('pre-inertia','pre-rig','final')){
        $wholeLog="$wholePrefix-$wholeBoundary.log"
        if(Test-Path -LiteralPath $wholeLog){throw 'Preserve diagnostic log.'}
        $wholeArgs=@('--headless','--path',$wholeRoot,'res://scenes/tests/lyra_whole_main_diagnostic_smoke.tscn','--',"--whole-main-run=$RunTag")
        if($wholeBoundary -ne 'final'){$wholeArgs+="--whole-main-$wholeBoundary"}
        if($Retry){$wholeArgs+='--whole-main-retry'}
        & (Join-Path $wholeRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe') @wholeArgs *> $wholeLog
        $wholeExit=$LASTEXITCODE
        Add-Content -LiteralPath $wholeLog -Value "WHOLE_MAIN_PROCESS_EXIT=$wholeExit" -Encoding UTF8
        $wholeText=Get-Content -LiteralPath $wholeLog -Raw -Encoding UTF8
        $wholeMatch=[regex]::Match($wholeText,'Whole Main diagnostic failed: ([^\r\n]+)')
        $wholePassed=$wholeExit -eq 0 -and $wholeText.Contains("LYRA_WHOLE_MAIN_DIAGNOSTIC_OK frames=$wholeExpectedFrames profiles=3") -and $wholeText -notmatch '(?m)^\s*(ERROR|WARNING):'
        if($Retry){$wholePassed=$wholePassed -and $wholeText.Contains("retry=$wholeExpectedFrames controlledPhysicalInputs=$wholeControlledText")}
        $wholeRuns+=@{boundary=$wholeBoundary;exitCode=$wholeExit;comparisonPassed=$wholePassed;firstFailure=$wholeMatch.Groups[1].Value;log=$wholeLog}
        Write-Output "Whole Main boundary=$wholeBoundary comparisonPassed=$wholePassed exit=$wholeExit"
    }
    if($IncludeRegressions){
        $wholeCases=@(
            @{name='aiming-layer';scene='lyra_aiming_layer_smoke';marker='LYRA_AIMING_LAYER_GODOT_OK frames=7560'},
            @{name='aiming-scope';scene='lyra_main_aiming_scope_smoke';marker='LYRA_MAIN_AIMING_SCOPE_GODOT_OK frames=11340'},
            @{name='lean-composition';scene='lyra_main_lean_composition_smoke';marker='LYRA_MAIN_LEAN_COMPOSITION_GODOT_OK frames=2100'},
            @{name='als-main-linked';scene='lyra_main_als_native_smoke';marker='LYRA_MAIN_ADDITIVES_PIPELINE_OK frames=11340';args=@('--main-additives')},
            @{name='inertia-host';scene='lyra_main_inertia_pose_host_smoke';marker='LYRA_MAIN_INERTIA_HOST_GODOT_OK frames=40320'},
            @{name='montage-notify';scene='lyra_montage_notify_smoke';marker='LYRA_MONTAGE_NOTIFY_OK'},
            @{name='ordinary-ten';scene='';marker='LYRA_MAIN_MULTI_DEMO_GODOT_OK'})
        foreach($wholeCase in $wholeCases){
            $wholeLog="$wholePrefix-$($wholeCase.name).log"
            if(Test-Path -LiteralPath $wholeLog){throw 'Preserve regression evidence.'}
            $wholeArgs=@('--headless','--path',$wholeRoot)
            if($wholeCase.scene){$wholeArgs+="res://scenes/tests/$($wholeCase.scene).tscn"}
            else{$wholeArgs+=@('--','--locomotion=lyra','--lyra-profile=rifle','--lyra-characters=10','--lyra-main-smoke','--lyra-main-retry','--lyra-main-rebind','--lyra-main-weapons','--lyra-main-hz=60',"--lyra-main-report=$wholePrefix-ordinary-ten.json")}
            if($wholeCase.ContainsKey('args')){$wholeArgs+='--';$wholeArgs+=$wholeCase.args}
            & (Join-Path $wholeRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe') @wholeArgs *> $wholeLog
            $wholeExit=$LASTEXITCODE
            Add-Content -LiteralPath $wholeLog -Value "WHOLE_MAIN_PROCESS_EXIT=$wholeExit" -Encoding UTF8
            $wholeText=Get-Content -LiteralPath $wholeLog -Raw -Encoding UTF8
            $wholePassed=$wholeExit -eq 0 -and $wholeText.Contains($wholeCase.marker) -and $wholeText -notmatch '(?m)^\s*(ERROR|WARNING):'
            $wholeRegressions+=@{name=$wholeCase.name;exitCode=$wholeExit;passed=$wholePassed;log=$wholeLog}
            Write-Output "Whole Main regression=$($wholeCase.name) passed=$wholePassed exit=$wholeExit"
        }
    }
} finally {
    $wholeRestored=$false
    if($wholeCopied){
        foreach($wholeFile in $wholeFiles){Copy-Item -LiteralPath (Join-Path $wholeBackup $wholeFile) -Destination (Join-Path $wholeDebug $wholeFile);if((Get-FileHash -LiteralPath (Join-Path $wholeDebug $wholeFile) -Algorithm SHA256).Hash -ne $wholeHashes[$wholeFile]){throw 'Debug restore mismatch.'}}
        $wholeRestored=$true
    }
    if((Get-FileHash -LiteralPath $wholeNativeMath -Algorithm SHA256).Hash -ne $wholeNativeMathHash){throw 'Native math binary changed during verification.'}
    @{configuration=$Configuration;runTag=$RunTag;retry=[bool]$Retry;assemblies=$wholeAssemblies;nativeMathSha256=$wholeNativeMathHash;runs=$wholeRuns;regressions=$wholeRegressions;debugRestored=$wholeRestored;comparisonPassed=($wholeRuns.Count -eq 3 -and @($wholeRuns | Where-Object {-not $_.comparisonPassed}).Count -eq 0);completeAcceptance=$false} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $wholeReport -Encoding UTF8
}
if(@($wholeRuns | Where-Object {-not $_.comparisonPassed}).Count -or @($wholeRegressions | Where-Object {-not $_.passed}).Count){exit 1}

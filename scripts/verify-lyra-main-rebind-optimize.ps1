$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$rebindRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$rebindLogs=Join-Path $rebindRoot 'artifacts/lyra-analysis'
$rebindDebug=Join-Path $rebindRoot '.godot/mono/temp/bin/Debug'
$rebindRelease=Join-Path $rebindRoot '.godot/mono/temp/bin/ExportRelease'
$rebindBackup=Join-Path $rebindLogs 'main-rebind-final-debug-assemblies'
$rebindLog=Join-Path $rebindLogs 'main-rebind-godot-optimize.log'
$rebindGodot=Join-Path $rebindRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$rebindFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
if((Test-Path -LiteralPath $rebindBackup) -or (Test-Path -LiteralPath $rebindLog)){throw 'Evidence already exists; preserve prior runs.'}
$rebindLive=@(Get-CimInstance Win32_Process | Where-Object {$_.CommandLine -and $_.CommandLine.Contains($rebindGodot.Replace('/','\'))})
if($rebindLive.Count){throw 'Workspace Godot is running; do not replace loaded assemblies.'}
$rebindBuild=Get-Content -LiteralPath (Join-Path $rebindLogs 'main-rebind-optimize-build-guard.log') -Raw
if($rebindBuild -notmatch '0\s*(个警告|Warning)' -or $rebindBuild -notmatch '0\s*(个错误|Error)'){throw 'Missing clean Optimize build.'}
New-Item -ItemType Directory -Path $rebindBackup | Out-Null
$rebindHashes=@{}
foreach($rebindFile in $rebindFiles){
    $rebindPath=Join-Path $rebindDebug $rebindFile
    $rebindHashes[$rebindFile]=(Get-FileHash -LiteralPath $rebindPath -Algorithm SHA256).Hash
    Copy-Item -LiteralPath $rebindPath -Destination (Join-Path $rebindBackup $rebindFile)
}
Set-Content -LiteralPath $rebindLog -Value 'LYRA_OPTIMIZED_MAIN_REBIND_BEGIN'
Push-Location $rebindRoot
try{
    foreach($rebindFile in $rebindFiles){Copy-Item -LiteralPath (Join-Path $rebindRelease $rebindFile) -Destination (Join-Path $rebindDebug $rebindFile)}
    foreach($rebindFile in @('GodotALS.dll','Als.Core.dll','Als.Import.dll')){
        $rebindHash=(Get-FileHash -LiteralPath (Join-Path $rebindRelease $rebindFile) -Algorithm SHA256).Hash
        if((Get-FileHash -LiteralPath (Join-Path $rebindDebug $rebindFile) -Algorithm SHA256).Hash -ne $rebindHash){throw 'Optimize assembly copy mismatch.'}
        Add-Content -LiteralPath $rebindLog -Value "LYRA_OPTIMIZED_ASSEMBLY $rebindFile SHA256=$rebindHash"
    }
    foreach($rebindProfile in @('unarmed','pistol','rifle')){
        foreach($rebindHz in @(30,60,120)){
            $rebindReport=Join-Path $rebindLogs "main-rebind-optimize-$rebindProfile-$rebindHz.json"
            & $rebindGodot --headless --path . -- --locomotion=lyra "--lyra-profile=$rebindProfile" --lyra-main-smoke --lyra-main-retry --lyra-main-rebind "--lyra-main-hz=$rebindHz" "--lyra-main-report=$rebindReport" *>> $rebindLog
            $rebindExit=$LASTEXITCODE
            Add-Content -LiteralPath $rebindLog -Value "LYRA_GODOT_PROCESS_EXIT profile=$rebindProfile hz=$rebindHz code=$rebindExit"
            if($rebindExit -ne 0){throw "Optimize rebind scene failed: $rebindProfile/$rebindHz"}
        }
    }
    foreach($rebindHz in @(30,60,120)){
        $rebindReport=Join-Path $rebindLogs "main-rebind-feedback-optimize-$rebindHz.json"
        & $rebindGodot --headless --path . -- --locomotion=lyra --lyra-profile=unarmed --lyra-main-smoke --lyra-main-retry --lyra-main-rebind --lyra-main-rebind-feedback "--lyra-main-hz=$rebindHz" "--lyra-main-report=$rebindReport" *>> $rebindLog
        $rebindExit=$LASTEXITCODE
        Add-Content -LiteralPath $rebindLog -Value "LYRA_GODOT_PROCESS_EXIT feedbackHz=$rebindHz code=$rebindExit"
        if($rebindExit -ne 0){throw 'Nonzero Main Rig feedback rebind failed.'}
    }
    foreach($rebindScene in @('lyra_main_als_native_smoke','lyra_main_rig_pose_host_smoke','lyra_main_inertia_smoke','refactored_stance_demo_smoke')){
        $rebindArgs=@('--headless','--path','.',"res://scenes/tests/$rebindScene.tscn")
        if($rebindScene -eq 'refactored_stance_demo_smoke'){$rebindArgs+=@('--','--stance-hz=60')}
        & $rebindGodot @rebindArgs *>> $rebindLog
        $rebindExit=$LASTEXITCODE
        Add-Content -LiteralPath $rebindLog -Value "LYRA_GODOT_PROCESS_EXIT scene=$rebindScene code=$rebindExit"
        if($rebindExit -ne 0){throw "Optimize existing regression failed: $rebindScene"}
    }
    $rebindReport=Join-Path $rebindLogs 'main-rebind-optimize-fixed.json'
    & $rebindGodot --headless --path . -- --locomotion=lyra --lyra-profile=rifle --lyra-main-smoke --lyra-main-retry --lyra-main-hz=60 "--lyra-main-report=$rebindReport" *>> $rebindLog
    $rebindExit=$LASTEXITCODE
    Add-Content -LiteralPath $rebindLog -Value "LYRA_GODOT_PROCESS_EXIT fixedProfile=rifle code=$rebindExit"
    if($rebindExit -ne 0){throw 'Fixed-provider regression failed.'}
    $rebindOutput=Get-Content -LiteralPath $rebindLog -Raw
    if(([regex]::Matches($rebindOutput,'LYRA_MAIN_REBIND_GODOT_OK ')).Count -ne 12 -or $rebindOutput -match '(?m)^\s*(ERROR|WARNING):'){throw 'Missing successful Main rebind gate.'}
}finally{
    foreach($rebindFile in $rebindFiles){
        Copy-Item -LiteralPath (Join-Path $rebindBackup $rebindFile) -Destination (Join-Path $rebindDebug $rebindFile)
        if((Get-FileHash -LiteralPath (Join-Path $rebindDebug $rebindFile) -Algorithm SHA256).Hash -ne $rebindHashes[$rebindFile]){throw 'Debug assembly restore mismatch.'}
    }
    Add-Content -LiteralPath $rebindLog -Value 'LYRA_DEBUG_ASSEMBLIES_RESTORED hashVerified=true'
    Pop-Location
}

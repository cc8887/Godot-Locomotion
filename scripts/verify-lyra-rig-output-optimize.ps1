$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$rigRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$rigLogs=Join-Path $rigRoot 'artifacts/lyra-analysis'
$rigDebug=Join-Path $rigRoot '.godot/mono/temp/bin/Debug'
$rigRelease=Join-Path $rigRoot '.godot/mono/temp/bin/ExportRelease'
$rigBackup=Join-Path $rigLogs 'rig-output-final-debug-assemblies'
$rigLog=Join-Path $rigLogs 'rig-output-godot-optimize.log'
$rigGodot=Join-Path $rigRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$rigFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
if(Test-Path -LiteralPath $rigBackup){throw 'Debug backup already exists; preserve prior evidence.'}
$rigLive=@(Get-CimInstance Win32_Process | Where-Object {$_.CommandLine -and $_.CommandLine.Contains($rigGodot.Replace('/','\'))})
if($rigLive.Count){throw 'Workspace Godot is running; do not replace loaded assemblies.'}
$rigBuild=Get-Content -LiteralPath (Join-Path $rigLogs 'rig-output-optimize-build.log') -Raw
if($rigBuild -notmatch '0\s*(个警告|Warning)' -or $rigBuild -notmatch '0\s*(个错误|Error)'){throw 'Missing clean Optimize build.'}
New-Item -ItemType Directory -Path $rigBackup | Out-Null
$rigHashes=@{}
foreach($rigFile in $rigFiles){
    $rigPath=Join-Path $rigDebug $rigFile
    $rigHashes[$rigFile]=(Get-FileHash -LiteralPath $rigPath -Algorithm SHA256).Hash
    Copy-Item -LiteralPath $rigPath -Destination (Join-Path $rigBackup $rigFile)
}
Set-Content -LiteralPath $rigLog -Value 'LYRA_OPTIMIZED_GODOT_RUN_BEGIN'
Push-Location $rigRoot
try{
    foreach($rigFile in $rigFiles){Copy-Item -LiteralPath (Join-Path $rigRelease $rigFile) -Destination (Join-Path $rigDebug $rigFile)}
    foreach($rigFile in @('GodotALS.dll','Als.Core.dll','Als.Import.dll')){
        $rigHash=(Get-FileHash -LiteralPath (Join-Path $rigRelease $rigFile) -Algorithm SHA256).Hash
        if((Get-FileHash -LiteralPath (Join-Path $rigDebug $rigFile) -Algorithm SHA256).Hash -ne $rigHash){throw 'Optimize assembly copy mismatch.'}
        Add-Content -LiteralPath $rigLog -Value "LYRA_OPTIMIZED_ASSEMBLY $rigFile SHA256=$rigHash"
    }
    foreach($rigScene in @('lyra_rig_output_smoke','lyra_main_rig_pose_host_smoke','lyra_rig_solver_smoke')){
        & $rigGodot --headless --path . "res://scenes/tests/$rigScene.tscn" *>> $rigLog
        $rigExit=$LASTEXITCODE
        Add-Content -LiteralPath $rigLog -Value "LYRA_GODOT_PROCESS_EXIT scene=$rigScene code=$rigExit"
        if($rigExit -ne 0){throw "Optimize scene failed: $rigScene"}
    }
    $rigOutput=Get-Content -LiteralPath $rigLog -Raw
    if($rigOutput -notmatch 'LYRA_RIG_OUTPUT_GODOT_OK frames=2520 ' -or
        $rigOutput -notmatch 'LYRA_MAIN_RIG_HOST_GODOT_OK frames=7560 ' -or
        $rigOutput -notmatch 'LYRA_RIG_SOLVER_GODOT_OK frames=2520 ' -or
        $rigOutput -match '(?m)^\s*(ERROR|WARNING):'){throw 'Missing successful output/Main/regression gate.'}
}finally{
    foreach($rigFile in $rigFiles){
        Copy-Item -LiteralPath (Join-Path $rigBackup $rigFile) -Destination (Join-Path $rigDebug $rigFile)
        if((Get-FileHash -LiteralPath (Join-Path $rigDebug $rigFile) -Algorithm SHA256).Hash -ne $rigHashes[$rigFile]){throw 'Debug assembly restore mismatch.'}
    }
    Add-Content -LiteralPath $rigLog -Value 'LYRA_DEBUG_ASSEMBLIES_RESTORED hashVerified=true'
    Pop-Location
}

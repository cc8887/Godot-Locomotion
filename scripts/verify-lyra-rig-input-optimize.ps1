$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$rigRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$rigLogs=Join-Path $rigRoot 'artifacts/lyra-analysis'
$rigDebug=Join-Path $rigRoot '.godot/mono/temp/bin/Debug'
$rigRelease=Join-Path $rigRoot '.godot/mono/temp/bin/ExportRelease'
$rigBackup=Join-Path $rigLogs 'rig-input-final-debug-assemblies'
$rigLog=Join-Path $rigLogs 'rig-input-godot-optimize.log'
$rigGodot=Join-Path $rigRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$rigFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
if(Test-Path -LiteralPath $rigBackup){throw 'Debug backup already exists; inspect prior evidence before replacing it.'}
$rigLive=@(Get-CimInstance Win32_Process | Where-Object {$_.CommandLine -and $_.CommandLine.Contains($rigGodot.Replace('/','\'))})
if($rigLive.Count){throw 'Workspace Godot is running; do not replace loaded assemblies.'}
$rigBuild=Get-Content -LiteralPath (Join-Path $rigLogs 'rig-input-optimize-build.log') -Raw
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
    & $rigGodot --headless --path . res://scenes/tests/lyra_footplant_rig_input_smoke.tscn *>> $rigLog
    $rigExit=$LASTEXITCODE
    Add-Content -LiteralPath $rigLog -Value "LYRA_GODOT_PROCESS_EXIT code=$rigExit"
    if($rigExit -ne 0){throw 'Optimize FootPlant Rig Construction scene failed.'}
    & $rigGodot --headless --path . res://scenes/tests/lyra_footplant_rig_construction_smoke.tscn *>> $rigLog
    $rigRegressionExit=$LASTEXITCODE
    Add-Content -LiteralPath $rigLog -Value "LYRA_CONSTRUCTION_REGRESSION_PROCESS_EXIT code=$rigRegressionExit"
    if($rigRegressionExit -ne 0){throw 'Construction regression failed.'}
    & $rigGodot --headless --path . res://scenes/tests/lyra_rig_hierarchy_smoke.tscn *>> $rigLog
    $rigHierarchyExit=$LASTEXITCODE
    Add-Content -LiteralPath $rigLog -Value "LYRA_HIERARCHY_REGRESSION_PROCESS_EXIT code=$rigHierarchyExit"
    if($rigHierarchyExit -ne 0){throw 'Hierarchy regression failed.'}
    $rigOutput=Get-Content -LiteralPath $rigLog -Raw
    if($rigOutput -notmatch 'LYRA_RIG_INPUT_GODOT_OK frames=1260 imports=1077 ' -or $rigOutput -notmatch 'LYRA_FOOTPLANT_RIG_CONSTRUCTION_GODOT_OK constructions=12 ' -or $rigOutput -notmatch 'LYRA_RIG_HIERARCHY_GODOT_OK batches=37 ' -or $rigOutput -match '(?m)^\s*(ERROR|WARNING):'){throw 'Missing successful bounded Construction gate.'}
}finally{
    foreach($rigFile in $rigFiles){
        Copy-Item -LiteralPath (Join-Path $rigBackup $rigFile) -Destination (Join-Path $rigDebug $rigFile)
        if((Get-FileHash -LiteralPath (Join-Path $rigDebug $rigFile) -Algorithm SHA256).Hash -ne $rigHashes[$rigFile]){throw 'Debug assembly restore mismatch.'}
    }
    Add-Content -LiteralPath $rigLog -Value 'LYRA_DEBUG_ASSEMBLIES_RESTORED hashVerified=true'
    Pop-Location
}

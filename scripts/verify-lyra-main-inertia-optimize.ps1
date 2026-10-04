$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskLogs=Join-Path $taskRoot 'artifacts/lyra-analysis'
$taskDebug=Join-Path $taskRoot '.godot/mono/temp/bin/Debug'
$taskRelease=Join-Path $taskRoot '.godot/mono/temp/bin/ExportRelease'
$taskBackup=Join-Path $taskLogs 'main-inertia-debug-assemblies'
$taskLog=Join-Path $taskLogs 'main-inertia-optimize-godot.log'
$taskGodot=Join-Path $taskRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$taskFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
if(Test-Path -LiteralPath $taskBackup){throw 'Debug backup already exists; inspect the prior run before replacing it.'}
$taskLive=@(Get-CimInstance Win32_Process | Where-Object {$_.CommandLine -and $_.CommandLine.Contains($taskGodot.Replace('/','\'))})
if($taskLive.Count){throw 'Godot from this workspace is still running; do not replace loaded assemblies.'}
$taskBuild=Get-Content -LiteralPath (Join-Path $taskLogs 'main-inertia-optimize-build.log') -Raw
if($taskBuild -notmatch '0\s*(个警告|Warning)' -or $taskBuild -notmatch '0\s*(个错误|Error)'){throw 'Missing clean Optimize build.'}
New-Item -ItemType Directory -Path $taskBackup | Out-Null
$taskHashes=@{}
foreach($taskFile in $taskFiles){
    $taskPath=Join-Path $taskDebug $taskFile
    $taskHashes[$taskFile]=(Get-FileHash -LiteralPath $taskPath -Algorithm SHA256).Hash
    Copy-Item -LiteralPath $taskPath -Destination (Join-Path $taskBackup $taskFile)
}
Set-Content -LiteralPath $taskLog -Value 'LYRA_OPTIMIZED_GODOT_RUN_BEGIN'
Push-Location $taskRoot
try{
    foreach($taskFile in $taskFiles){Copy-Item -LiteralPath (Join-Path $taskRelease $taskFile) -Destination (Join-Path $taskDebug $taskFile)}
    foreach($taskFile in @('GodotALS.dll','Als.Core.dll','Als.Import.dll')){
        $taskHash=(Get-FileHash -LiteralPath (Join-Path $taskRelease $taskFile) -Algorithm SHA256).Hash
        if((Get-FileHash -LiteralPath (Join-Path $taskDebug $taskFile) -Algorithm SHA256).Hash -ne $taskHash){throw 'Optimize assembly copy mismatch.'}
        Add-Content -LiteralPath $taskLog -Value "LYRA_OPTIMIZED_ASSEMBLY $taskFile SHA256=$taskHash"
    }
    foreach($taskScene in @('lyra_main_inertia_smoke','lyra_main_inertia_pose_host_smoke')){
        & $taskGodot --headless --path . "res://scenes/tests/$taskScene.tscn" *>> $taskLog
        $taskExit=$LASTEXITCODE
        Add-Content -LiteralPath $taskLog -Value "LYRA_GODOT_PROCESS_EXIT_OK code=$taskExit"
        if($taskExit -ne 0){throw "Optimize scene $taskScene failed."}
    }
}finally{
    foreach($taskFile in $taskFiles){
        Copy-Item -LiteralPath (Join-Path $taskBackup $taskFile) -Destination (Join-Path $taskDebug $taskFile)
        if((Get-FileHash -LiteralPath (Join-Path $taskDebug $taskFile) -Algorithm SHA256).Hash -ne $taskHashes[$taskFile]){throw 'Debug assembly restore mismatch.'}
    }
    Add-Content -LiteralPath $taskLog -Value 'LYRA_DEBUG_ASSEMBLIES_RESTORED hashVerified=true'
    Pop-Location
}

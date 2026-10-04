$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$modelRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$modelLogs=Join-Path $modelRoot 'artifacts/lyra-analysis'
$modelDebug=Join-Path $modelRoot '.godot/mono/temp/bin/Debug'
$modelRelease=Join-Path $modelRoot '.godot/mono/temp/bin/ExportRelease'
$modelBackup=Join-Path $modelLogs 'main-model-final-debug-assemblies'
$modelLog=Join-Path $modelLogs 'main-model-godot-optimize.log'
$modelGodot=Join-Path $modelRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$modelFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
if((Test-Path -LiteralPath $modelBackup) -or (Test-Path -LiteralPath $modelLog)){throw 'Evidence already exists; preserve prior runs.'}
$modelLive=@(Get-CimInstance Win32_Process | Where-Object {$_.CommandLine -and $_.CommandLine.Contains($modelGodot.Replace('/','\'))})
if($modelLive.Count){throw 'Workspace Godot is running; do not replace loaded assemblies.'}
$modelBuild=Get-Content -LiteralPath (Join-Path $modelLogs 'main-model-optimize-build.log') -Raw
if($modelBuild -notmatch '0\s*(个警告|Warning)' -or $modelBuild -notmatch '0\s*(个错误|Error)'){throw 'Missing clean Optimize build.'}
foreach($modelProfile in @('unarmed','pistol','rifle')){
    foreach($modelHz in @(30,60,120)){
        if(Test-Path -LiteralPath (Join-Path $modelLogs "main-model-optimize-$modelProfile-$modelHz.json")){throw 'Report already exists.'}
    }
}
New-Item -ItemType Directory -Path $modelBackup | Out-Null
$modelHashes=@{}
foreach($modelFile in $modelFiles){
    $modelPath=Join-Path $modelDebug $modelFile
    $modelHashes[$modelFile]=(Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash
    Copy-Item -LiteralPath $modelPath -Destination (Join-Path $modelBackup $modelFile)
}
Set-Content -LiteralPath $modelLog -Value 'LYRA_OPTIMIZED_MAIN_MODEL_BEGIN'
Push-Location $modelRoot
try{
    foreach($modelFile in $modelFiles){Copy-Item -LiteralPath (Join-Path $modelRelease $modelFile) -Destination (Join-Path $modelDebug $modelFile)}
    foreach($modelFile in @('GodotALS.dll','Als.Core.dll','Als.Import.dll')){
        $modelHash=(Get-FileHash -LiteralPath (Join-Path $modelRelease $modelFile) -Algorithm SHA256).Hash
        if((Get-FileHash -LiteralPath (Join-Path $modelDebug $modelFile) -Algorithm SHA256).Hash -ne $modelHash){throw 'Optimize assembly copy mismatch.'}
        Add-Content -LiteralPath $modelLog -Value "LYRA_OPTIMIZED_ASSEMBLY $modelFile SHA256=$modelHash"
    }
    foreach($modelProfile in @('unarmed','pistol','rifle')){
        foreach($modelHz in @(30,60,120)){
            $modelReport=Join-Path $modelLogs "main-model-optimize-$modelProfile-$modelHz.json"
            & $modelGodot --headless --path . -- --locomotion=lyra "--lyra-profile=$modelProfile" --lyra-main-smoke --lyra-main-retry "--lyra-main-hz=$modelHz" "--lyra-main-report=$modelReport" *>> $modelLog
            $modelExit=$LASTEXITCODE
            Add-Content -LiteralPath $modelLog -Value "LYRA_GODOT_PROCESS_EXIT profile=$modelProfile hz=$modelHz code=$modelExit"
            if($modelExit -ne 0){throw "Optimize scene failed: $modelProfile/$modelHz"}
            $modelResult=Get-Content -LiteralPath $modelReport -Raw | ConvertFrom-Json
            if($modelResult.frames -ne $modelHz*8 -or $modelResult.published -ne $modelResult.frames -or $modelResult.retries -ne $modelResult.frames){throw 'Incomplete live character run.'}
        }
    }
    $modelOutput=Get-Content -LiteralPath $modelLog -Raw
    if(([regex]::Matches($modelOutput,'LYRA_MAIN_MODEL_GODOT_OK ')).Count -ne 9 -or $modelOutput -match '(?m)^\s*(ERROR|WARNING):'){throw 'Missing successful Main model gate.'}
}finally{
    foreach($modelFile in $modelFiles){
        Copy-Item -LiteralPath (Join-Path $modelBackup $modelFile) -Destination (Join-Path $modelDebug $modelFile)
        if((Get-FileHash -LiteralPath (Join-Path $modelDebug $modelFile) -Algorithm SHA256).Hash -ne $modelHashes[$modelFile]){throw 'Debug assembly restore mismatch.'}
    }
    Add-Content -LiteralPath $modelLog -Value 'LYRA_DEBUG_ASSEMBLIES_RESTORED hashVerified=true'
    Pop-Location
}

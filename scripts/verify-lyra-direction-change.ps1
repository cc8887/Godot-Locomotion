param(
    [ValidateSet('Debug','Optimize')][string]$Configuration='Debug',
    [ValidatePattern('^[a-z0-9-]+$')][string]$EvidenceTag='direction-change-v4',
    [Parameter(Mandatory=$true)][string]$BuildEvidenceName,
    [ValidateSet('rifle-30','rifle-60','rifle-120','pistol-60','unarmed-60','ten-60','steady-30','steady-60','steady-120')]
    [string[]]$Cases=@('rifle-30','rifle-60','rifle-120','pistol-60','unarmed-60','ten-60')
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$directionRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$directionOut=Join-Path $directionRoot 'artifacts/lyra-analysis'
$directionName="$EvidenceTag-$($Configuration.ToLowerInvariant())"
$directionSummary=Join-Path $directionOut "$directionName-verification.json"
if(Test-Path -LiteralPath $directionSummary){throw 'Evidence already exists.'}
if([IO.Path]::GetFileName($BuildEvidenceName)-ne $BuildEvidenceName){throw 'Build evidence must be a filename.'}
$directionBuild=Get-Content -LiteralPath (Join-Path $directionOut $BuildEvidenceName) -Raw
if($directionBuild -notmatch '0\s*(个警告|Warning)' -or $directionBuild -notmatch '0\s*(个错误|Error)'){throw 'Clean matching build required.'}
$directionLive=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains($directionRoot)})
if($directionLive.Count){throw 'Workspace Godot is running.'}
$directionExe=Join-Path $directionRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$directionDebug=Join-Path $directionRoot '.godot/mono/temp/bin/Debug'
$directionRelease=Join-Path $directionRoot '.godot/mono/temp/bin/ExportRelease'
$directionBackup=Join-Path $directionOut "$directionName-debug-backup"
$directionFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
$directionHashes=@{}
$directionCopied=$false
$directionRuns=[Collections.Generic.List[object]]::new()
$directionRestored=$false
Push-Location $directionRoot
try {
    if($Configuration -eq 'Optimize'){
        if(Test-Path -LiteralPath $directionBackup){throw 'Backup already exists.'}
        New-Item -ItemType Directory -Path $directionBackup | Out-Null
        foreach($directionFile in $directionFiles){
            $directionHashes[$directionFile]=(Get-FileHash -LiteralPath (Join-Path $directionDebug $directionFile)).Hash
            Copy-Item -LiteralPath (Join-Path $directionDebug $directionFile) -Destination (Join-Path $directionBackup $directionFile)
        }
        $directionCopied=$true
        foreach($directionFile in $directionFiles){Copy-Item -LiteralPath (Join-Path $directionRelease $directionFile) -Destination (Join-Path $directionDebug $directionFile)}
    }
    foreach($directionCase in $Cases){
        $directionHz=[int]($directionCase.Split('-')[-1])
        $directionProfile=if($directionCase.StartsWith('ten') -or $directionCase.StartsWith('steady')){'rifle'}else{$directionCase.Split('-')[0]}
        $directionReport=Join-Path $directionOut "$directionName-$directionCase.json"
        $directionLog=Join-Path $directionOut "$directionName-$directionCase.log"
        if((Test-Path -LiteralPath $directionReport)-or(Test-Path -LiteralPath $directionLog)){throw 'Run evidence already exists.'}
        $directionArgs=@('--headless','--path','.', '--fixed-fps',"$directionHz",'--','--locomotion=lyra',"--lyra-profile=$directionProfile",'--lyra-main-smoke','--lyra-main-retry',"--lyra-main-hz=$directionHz","--lyra-main-report=$directionReport")
        if($directionCase -eq 'ten-60'){$directionArgs+=@('--lyra-characters=10','--lyra-main-rebind','--lyra-main-weapons')}
        else{$directionArgs+='--lyra-direction-diagnostic'}
        if($directionCase.StartsWith('steady')){$directionArgs+='--lyra-direction-steady'}
        & $directionExe @directionArgs *> $directionLog
        $directionExit=$LASTEXITCODE
        if($directionExit-ne 0){throw "Run failed: $directionCase/$directionExit"}
        $directionText=Get-Content -LiteralPath $directionLog -Raw
        if($directionText -match '(?m)^\s*(ERROR|WARNING):' -or $directionText -notmatch 'LYRA_MAIN_(MODEL|MULTI_DEMO)_GODOT_OK'){throw "Runtime log failed: $directionCase"}
        $directionData=Get-Content -LiteralPath $directionReport -Raw | ConvertFrom-Json
        $directionMaximum=$null
        if($directionCase -eq 'ten-60'){
            if($directionData.characters-ne 10 -or $directionData.companions.Count-ne 9){throw 'Missing real equipment companions.'}
            foreach($directionCompanion in $directionData.companions){
                if($directionCompanion.published-ne 480 -or $directionCompanion.weaponMissing-ne 0){throw 'Companion skin/weapon failed.'}
            }
            $directionModel=$directionData.player.model
        } else {
            $directionModel=$directionData.model
            if($directionData.directionChanges.Count-ne $directionHz*8){throw 'Missing direction trace.'}
            foreach($directionRow in $directionData.directionChanges){
                if([Math]::Abs($directionRow.relativeForwardX)-gt 1e-5 -or [Math]::Abs($directionRow.relativeForwardY+1)-gt 1e-5){throw 'Wrong physical mesh relative facing.'}
            }
            # Check the last physical tick before D is released at two seconds.
            # Earlier ticks still contain the intended Warp interpolation.
            $directionEnd=if($directionCase.StartsWith('steady')){[Math]::Ceiling($directionHz*3.8)-1}else{$directionHz*2-1}
            $directionRight=@($directionData.directionChanges | Where-Object {$_.frame-eq $directionEnd -and $_.state-eq 2})
            if($directionCase.StartsWith('steady')){
                $directionForward=@($directionData.directionChanges | Where-Object {$_.frame-eq $directionHz*3-1})
                if($directionForward.Count-ne 1 -or $directionForward[0].state-ne 2 -or $directionForward[0].direction-ne 0 -or $directionForward[0].asset-eq $directionRight[0].asset){throw 'Missing actual forward Cycle to right Cycle asset change.'}
            }
            if(-not $directionRight.Count){throw 'No settled right Cycle frames.'}
            $directionMaximum=0.0
            foreach($directionRow in $directionRight){
                $directionDegrees=[Math]::Abs($directionRow.warpAngle)*180/[Math]::PI
                $directionMaximum=[Math]::Max($directionMaximum,$directionDegrees)
                # Rifle/Pistol right clips have an authored -X root delta in
                # mesh space. Unarmed has a different root and blend policy;
                # its mounting/direction gate must not require this same angle.
                if($directionRow.direction-ne 3 -or ($directionProfile-ne 'unarmed' -and $directionDegrees-gt 5)){throw "Wrong settled right leg warp: $directionDegrees"}
            }
        }
        if($directionModel.frames-ne $directionHz*8 -or $directionModel.published-ne $directionModel.frames -or $directionModel.retries-ne $directionModel.frames -or $directionModel.skinBones-ne 68 -or $directionModel.logicalBones-ne 81 -or -not $directionModel.finalRig -or -not $directionModel.actualGodotPhysics){throw 'Incomplete Main/model/physics gate.'}
        $directionRuns.Add([pscustomobject]@{name=$directionCase;hz=$directionHz;exitCode=$directionExit;passed=$true;rightWarpMaxDegrees=$directionMaximum;report=$directionReport;reportSha256=(Get-FileHash $directionReport).Hash;log=$directionLog;logSha256=(Get-FileHash $directionLog).Hash})
        Write-Output "DIRECTION_CHANGE_OK configuration=$Configuration case=$directionCase rightWarpMax=$directionMaximum"
    }
} finally {
    if($directionCopied){
        foreach($directionFile in $directionFiles){
            Copy-Item -LiteralPath (Join-Path $directionBackup $directionFile) -Destination (Join-Path $directionDebug $directionFile)
            if((Get-FileHash (Join-Path $directionDebug $directionFile)).Hash-ne $directionHashes[$directionFile]){throw 'Debug restore hash mismatch.'}
        }
        $directionRestored=$true
    }
    Pop-Location
}
@{passed=$true;configuration=$Configuration;debugRestored=$directionRestored;runs=$directionRuns} | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $directionSummary -Encoding utf8

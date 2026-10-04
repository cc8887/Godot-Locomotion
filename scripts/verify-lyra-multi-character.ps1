param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug',[ValidatePattern('^[a-z0-9-]*$')][string]$EvidenceSuffix='',
    [string]$BuildEvidenceName='')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$multiRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$multiLogs=Join-Path $multiRoot 'artifacts/lyra-analysis'
$multiName=$Configuration.ToLowerInvariant()
if($EvidenceSuffix){$multiName+="-$EvidenceSuffix"}
$multiLog=Join-Path $multiLogs "multi-character-$multiName-matrix.log"
$multiExe=Join-Path $multiRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe'
$multiDebug=Join-Path $multiRoot '.godot/mono/temp/bin/Debug'
$multiRelease=Join-Path $multiRoot '.godot/mono/temp/bin/ExportRelease'
$multiBackup=Join-Path $multiLogs "multi-character-$multiName-debug-assemblies"
$multiFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
if(Test-Path -LiteralPath $multiLog){throw 'Matrix evidence already exists.'}
$multiLive=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains($multiRoot)})
if($multiLive.Count){throw 'Workspace Godot is running.'}
$multiBuildName=if($Configuration -eq 'Debug'){'multi-character-debug-build-final.log'}else{'multi-character-optimize-build.log'}
if($EvidenceSuffix){$multiBuildName="multi-character-$($Configuration.ToLowerInvariant())-build-$EvidenceSuffix.log"}
if($BuildEvidenceName){
    if([IO.Path]::GetFileName($BuildEvidenceName) -ne $BuildEvidenceName){throw 'Build evidence must be a filename in the evidence directory.'}
    $multiBuildName=$BuildEvidenceName
}
$multiBuild=Get-Content -LiteralPath (Join-Path $multiLogs $multiBuildName) -Raw
if($multiBuild -notmatch '0\s*(个警告|Warning)' -or $multiBuild -notmatch '0\s*(个错误|Error)'){throw 'Clean matching build required.'}
$multiHashes=@{}
$multiCopied=$false
Set-Content -LiteralPath $multiLog -Value "LYRA_MULTI_MATRIX_BEGIN configuration=$Configuration"
Push-Location $multiRoot
try {
    if($Configuration -eq 'Optimize'){
        if(Test-Path -LiteralPath $multiBackup){throw 'Debug backup already exists.'}
        New-Item -ItemType Directory -Path $multiBackup | Out-Null
        foreach($multiFile in $multiFiles){
            $multiHashes[$multiFile]=(Get-FileHash -LiteralPath (Join-Path $multiDebug $multiFile) -Algorithm SHA256).Hash
            Copy-Item -LiteralPath (Join-Path $multiDebug $multiFile) -Destination (Join-Path $multiBackup $multiFile)
        }
        $multiCopied=$true
        foreach($multiFile in $multiFiles){Copy-Item -LiteralPath (Join-Path $multiRelease $multiFile) -Destination (Join-Path $multiDebug $multiFile)}
    }
    foreach($multiFile in @('GodotALS.dll','Als.Core.dll','Als.Import.dll')){
        $multiHash=(Get-FileHash -LiteralPath (Join-Path $multiDebug $multiFile) -Algorithm SHA256).Hash
        Add-Content -LiteralPath $multiLog -Value "LYRA_MULTI_ASSEMBLY file=$multiFile sha256=$multiHash"
    }
    function Invoke-MultiRun([string]$Label,[string[]]$Arguments){
        & $multiExe @Arguments *>> $multiLog
        $multiExit=$LASTEXITCODE
        Add-Content -LiteralPath $multiLog -Value "LYRA_MULTI_PROCESS_EXIT label=$Label code=$multiExit"
        if($multiExit -ne 0){throw "Multi-character run failed: $Label"}
    }
    foreach($multiHz in @(30,60,120)){
        $multiReport=Join-Path $multiLogs "multi-character-$multiName-$multiHz.json"
        Invoke-MultiRun "pairs-$multiHz" @('--headless','--path','.','res://scenes/tests/lyra_multi_character_smoke.tscn','--',"--lyra-multi-hz=$multiHz","--lyra-multi-report=$multiReport")
    }
    foreach($multiHz in @(30,60,120)){
        $multiReport=Join-Path $multiLogs "multi-demo-$multiName-$multiHz.json"
        Invoke-MultiRun "ordinary-ten-$multiHz" @('--headless','--path','.','--','--locomotion=lyra','--lyra-profile=rifle','--lyra-characters=10','--lyra-main-smoke','--lyra-main-retry','--lyra-main-rebind',"--lyra-main-hz=$multiHz","--lyra-main-report=$multiReport")
    }
    $multiReport=Join-Path $multiLogs "multi-single-rebind-$multiName.json"
    Invoke-MultiRun 'single-feedback-rebind' @('--headless','--path','.','--','--locomotion=lyra','--lyra-main-smoke','--lyra-main-retry','--lyra-main-rebind','--lyra-main-rebind-feedback','--lyra-main-hz=60',"--lyra-main-report=$multiReport")
    foreach($multiScene in @('lyra_main_als_native_smoke','lyra_main_rig_pose_host_smoke','refactored_stance_demo_smoke')){
        $multiArguments=@('--headless','--path','.',"res://scenes/tests/$multiScene.tscn")
        if($multiScene -eq 'refactored_stance_demo_smoke'){$multiArguments+=@('--','--stance-hz=60')}
        Invoke-MultiRun $multiScene $multiArguments
    }
    $multiReport=Join-Path $multiLogs "multi-rig-collision-$multiName.json"
    Invoke-MultiRun 'rig-collision' @('--headless','--path','.','res://scenes/tests/lyra_rig_scene_collision_smoke.tscn','--','--als-reference',"--report=$multiReport")
    $multiText=Get-Content -LiteralPath $multiLog -Raw
    if($multiText -match '(?m)^\s*(ERROR|WARNING):' -or ([regex]::Matches($multiText,'LYRA_MULTI_CHARACTER_GODOT_OK ')).Count -ne 3 -or ([regex]::Matches($multiText,'LYRA_MAIN_MULTI_DEMO_GODOT_OK ')).Count -ne 3){throw 'Missing successful gates or unexpected Godot diagnostic.'}
}finally{
    if($multiCopied){
        foreach($multiFile in $multiFiles){
            Copy-Item -LiteralPath (Join-Path $multiBackup $multiFile) -Destination (Join-Path $multiDebug $multiFile)
            if((Get-FileHash -LiteralPath (Join-Path $multiDebug $multiFile) -Algorithm SHA256).Hash -ne $multiHashes[$multiFile]){throw 'Debug restore hash mismatch.'}
        }
        Add-Content -LiteralPath $multiLog -Value 'LYRA_MULTI_DEBUG_RESTORED hashVerified=true'
    }
    Pop-Location
}

param([ValidateSet('Debug','Optimize')][string]$Configuration='Debug')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$weaponRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$weaponName=$Configuration.ToLowerInvariant()
$weaponLog=Join-Path $weaponRoot "artifacts/lyra-analysis/weapon-resources-$weaponName-gate-verified.log"
$weaponDebug=Join-Path $weaponRoot '.godot/mono/temp/bin/Debug'
$weaponRelease=Join-Path $weaponRoot '.godot/mono/temp/bin/ExportRelease'
$weaponBackup=Join-Path $weaponRoot 'artifacts/lyra-analysis/weapon-resources-debug-backup-verified'
$weaponFiles=@('GodotALS.dll','GodotALS.pdb','Als.Core.dll','Als.Core.pdb','Als.Import.dll','Als.Import.pdb')
if(Test-Path -LiteralPath $weaponLog){throw 'Preserve weapon validation evidence.'}
$weaponLive=@(Get-CimInstance Win32_Process | Where-Object {$_.Name -like 'Godot*' -and $_.CommandLine -and $_.CommandLine.Contains($weaponRoot)})
if($weaponLive.Count){throw 'Workspace Godot is running.'}
$weaponBuildName=if($Configuration -eq 'Optimize'){'weapon-resources-export-release-build-accepted.log'}else{'weapon-resources-debug-build-final-preview.log'}
$weaponBuild=Get-Content -LiteralPath (Join-Path $weaponRoot "artifacts/lyra-analysis/$weaponBuildName") -Raw -Encoding UTF8
if($weaponBuild -notmatch '0\s*(\u4e2a\u8b66\u544a|Warning)' -or $weaponBuild -notmatch '0\s*(\u4e2a\u9519\u8bef|Error)'){throw 'Matching clean weapon build required.'}
$weaponHashes=@{};$weaponCopied=$false
Push-Location $weaponRoot
try {
    if($Configuration -eq 'Optimize'){
        if(Test-Path -LiteralPath $weaponBackup){throw 'Preserve assembly backup.'}
        New-Item -ItemType Directory -Path $weaponBackup | Out-Null
        foreach($weaponFile in $weaponFiles){
            $weaponHashes[$weaponFile]=(Get-FileHash -LiteralPath (Join-Path $weaponDebug $weaponFile) -Algorithm SHA256).Hash
            Copy-Item -LiteralPath (Join-Path $weaponDebug $weaponFile) -Destination (Join-Path $weaponBackup $weaponFile)
        }
        $weaponCopied=$true
        foreach($weaponFile in $weaponFiles){Copy-Item -LiteralPath (Join-Path $weaponRelease $weaponFile) -Destination (Join-Path $weaponDebug $weaponFile)}
    }
    Set-Content -LiteralPath $weaponLog -Value "LYRA_WEAPON_MATRIX configuration=$Configuration" -Encoding UTF8
    foreach($weaponFile in @('GodotALS.dll','Als.Core.dll','Als.Import.dll')){
        Add-Content -LiteralPath $weaponLog -Value "LYRA_WEAPON_ASSEMBLY file=$weaponFile sha256=$((Get-FileHash -LiteralPath (Join-Path $weaponDebug $weaponFile) -Algorithm SHA256).Hash)" -Encoding UTF8
    }
    & (Join-Path $weaponRoot 'Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe') --headless --fixed-fps 60 --path . res://scenes/tests/lyra_weapon_resources_smoke.tscn 2>&1 | Out-File -LiteralPath $weaponLog -Encoding UTF8 -Append
    $weaponExit=$LASTEXITCODE
    Add-Content -LiteralPath $weaponLog -Value "LYRA_WEAPON_PROCESS_EXIT configuration=$Configuration code=$weaponExit" -Encoding UTF8
    $weaponText=Get-Content -LiteralPath $weaponLog -Raw -Encoding UTF8
    if($weaponExit -ne 0 -or $weaponText -match '(?m)^\s*(ERROR|WARNING):' -or
        $weaponText -notmatch 'LYRA_WEAPON_RESOURCES_GODOT_OK clips=6 meshes=3 samples=30 bones=230 frames=180 publications=540 retries=540 rejected=1620 positionCm=0 rotation=0 scale=0 native=True production=False'){throw 'Weapon resource validation failed.'}
} finally {
    if($weaponCopied){
        foreach($weaponFile in $weaponFiles){
            Copy-Item -LiteralPath (Join-Path $weaponBackup $weaponFile) -Destination (Join-Path $weaponDebug $weaponFile)
            if((Get-FileHash -LiteralPath (Join-Path $weaponDebug $weaponFile) -Algorithm SHA256).Hash -ne $weaponHashes[$weaponFile]){throw 'Debug restore mismatch.'}
        }
        Add-Content -LiteralPath $weaponLog -Value 'LYRA_WEAPON_DEBUG_RESTORED hashVerified=true' -Encoding UTF8
    }
    Pop-Location
}

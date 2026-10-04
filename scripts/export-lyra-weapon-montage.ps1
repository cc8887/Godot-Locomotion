param([Parameter(Mandatory=$true)][string]$EngineRoot,
      [Parameter(Mandatory=$true)][string]$UnrealProject,
      [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$PackageName='package-fixed',
      [ValidatePattern('^[a-zA-Z0-9_-]+\.log$')][string]$LogName='weapon-montage-ue-first.log')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$weaponRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$weaponPlugin=Join-Path $weaponRoot "artifacts/unreal/lyra-weapon-montage-oracle/$PackageName/LyraWeaponMontageOracle.uplugin"
$weaponRaw=Join-Path $weaponRoot 'artifacts/unreal/gasp58-lyra-rig-target/package/AlsV4AssetExporter.uplugin'
$weaponLog=Join-Path $weaponRoot "artifacts/lyra-analysis/$LogName"
if(Test-Path -LiteralPath $weaponLog){throw 'Preserve WeaponMontage evidence.'}
$weaponProjectHash=(Get-FileHash -LiteralPath $UnrealProject -Algorithm SHA256).Hash
& (Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe') $UnrealProject '-run=pythonscript' `
    "-Script=$(Join-Path $weaponRoot 'tools/unreal/export_lyra_weapon_montage.py')" "-PLUGIN=$weaponPlugin" "-PLUGIN=$weaponRaw" `
    '-DisablePlugins=ModelContextProtocol,Mocara' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1 | Out-File -LiteralPath $weaponLog -Encoding UTF8
$weaponExit=$LASTEXITCODE
if((Get-FileHash -LiteralPath $UnrealProject -Algorithm SHA256).Hash -ne $weaponProjectHash){throw 'Project descriptor changed.'}
$weaponText=Get-Content -LiteralPath $weaponLog -Raw -Encoding UTF8
if($weaponExit -ne 0 -or $weaponText -notmatch 'LYRA_WEAPON_MONTAGE_NATIVE_OK traces=9 frames=8820 skin=7 previous=837 packages=706 assets_saved=0' -or
    $weaponText -match 'Assertion failed:|Fatal error:|LogPython: Error:|LYRA_WEAPON_MONTAGE_FAILED'){throw "Native WeaponMontage failed ($weaponExit). See $weaponLog"}
Add-Content -LiteralPath $weaponLog -Value "LYRA_WEAPON_MONTAGE_PROCESS_EXIT code=$weaponExit" -Encoding UTF8
Select-String -LiteralPath $weaponLog -Pattern 'LYRA_WEAPON_MONTAGE_NATIVE_OK'

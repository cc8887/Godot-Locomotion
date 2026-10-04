param([Parameter(Mandatory=$true)][string]$EngineRoot,
      [Parameter(Mandatory=$true)][string]$UnrealProject,
      [ValidatePattern('^[a-zA-Z0-9_-]+\.log$')][string]$LogName='notify-weapon-ue-repeat.log')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$weaponRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$weaponPlugin=Join-Path $weaponRoot 'artifacts/unreal/lyra-weapon-notify-oracle/package/LyraWeaponNotifyOracle.uplugin'
$weaponLog=Join-Path $weaponRoot "artifacts/lyra-analysis/$LogName"
if(Test-Path -LiteralPath $weaponLog){throw 'Preserve WeaponNotify evidence.'}
$weaponProjectHash=(Get-FileHash -LiteralPath $UnrealProject -Algorithm SHA256).Hash
& (Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe') $UnrealProject '-run=pythonscript' `
    "-Script=$(Join-Path $weaponRoot 'tools/unreal/export_lyra_weapon_notify.py')" "-PLUGIN=$weaponPlugin" `
    '-DisablePlugins=ModelContextProtocol,Mocara' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput *> $weaponLog
$weaponExit=$LASTEXITCODE
if((Get-FileHash -LiteralPath $UnrealProject -Algorithm SHA256).Hash -ne $weaponProjectHash){throw 'Project descriptor changed.'}
$weaponMarkers=@(Get-Content -LiteralPath $weaponLog | Where-Object {$_ -match 'LYRA_WEAPON_NOTIFY_NATIVE_OK .* assets_saved=0'})
if($weaponExit -ne 0 -or $weaponMarkers.Count -ne 1){throw "Native WeaponNotify failed ($weaponExit). See $weaponLog"}
Add-Content -LiteralPath $weaponLog -Value "LYRA_WEAPON_EXPORT_PROCESS_EXIT code=$weaponExit"
$weaponMarkers

param([Parameter(Mandatory=$true)][string]$EngineRoot,
      [Parameter(Mandatory=$true)][string]$UnrealProject,
      [ValidatePattern('^[a-zA-Z0-9_-]+\.log$')][string]$LogName='notify-gameplay-ue-first.log')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$gameplayRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$gameplayPlugin=Join-Path $gameplayRoot 'artifacts/unreal/lyra-gameplay-notify-oracle/package-second/LyraGameplayNotifyOracle.uplugin'
$gameplayLog=Join-Path $gameplayRoot "artifacts/lyra-analysis/$LogName"
if(Test-Path -LiteralPath $gameplayLog){throw 'Preserve gameplay dispatch evidence.'}
$gameplayPrevious=[Environment]::GetEnvironmentVariable('LYRA_OUTPUT_ROOT','Process')
$gameplayProjectHash=(Get-FileHash -LiteralPath $UnrealProject -Algorithm SHA256).Hash
try {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT',(Join-Path $gameplayRoot 'assets/generated/lyra_als'),'Process')
    & (Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe') $UnrealProject '-run=pythonscript' `
        "-Script=$(Join-Path $gameplayRoot 'tools/unreal/export_lyra_gameplay_notify.py')" "-PLUGIN=$gameplayPlugin" `
        '-DisablePlugins=ModelContextProtocol,Mocara' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput *> $gameplayLog
    $gameplayExit=$LASTEXITCODE
    if((Get-FileHash -LiteralPath $UnrealProject -Algorithm SHA256).Hash -ne $gameplayProjectHash){throw 'Project descriptor changed.'}
    $gameplayMarkers=@(Get-Content -LiteralPath $gameplayLog | Where-Object {$_ -match 'LYRA_GAMEPLAY_NOTIFY_NATIVE_OK .* assets_saved=0'})
    if($gameplayExit -ne 0 -or $gameplayMarkers.Count -ne 1){throw "Native gameplay dispatch failed ($gameplayExit). See $gameplayLog"}
    Add-Content -LiteralPath $gameplayLog -Value "LYRA_EXPORT_PROCESS_EXIT_OK mode=gameplay-notify code=$gameplayExit"
    $gameplayMarkers
} finally {[Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT',$gameplayPrevious,'Process')}

param([Parameter(Mandatory=$true)][string]$EngineRoot,
      [Parameter(Mandatory=$true)][string]$UnrealProject,
      [ValidatePattern('^[a-zA-Z0-9_-]+\.log$')][string]$LogName='notify-context-ue-repeat.log')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$contextRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$contextPlugin=Join-Path $contextRoot 'artifacts/unreal/lyra-context-effects-oracle/package/LyraContextEffectsOracle.uplugin'
$contextLog=Join-Path $contextRoot "artifacts/lyra-analysis/$LogName"
if(Test-Path -LiteralPath $contextLog){throw 'Preserve ContextEffects evidence.'}
$contextProjectHash=(Get-FileHash -LiteralPath $UnrealProject -Algorithm SHA256).Hash
& (Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe') $UnrealProject '-run=pythonscript' `
    "-Script=$(Join-Path $contextRoot 'tools/unreal/export_lyra_context_effects.py')" "-PLUGIN=$contextPlugin" `
    '-DisablePlugins=ModelContextProtocol,Mocara' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput *> $contextLog
$contextExit=$LASTEXITCODE
if((Get-FileHash -LiteralPath $UnrealProject -Algorithm SHA256).Hash -ne $contextProjectHash){throw 'Project descriptor changed.'}
$contextMarkers=@(Get-Content -LiteralPath $contextLog | Where-Object {$_ -match 'LYRA_CONTEXT_EFFECTS_NATIVE_OK .* assets_saved=0'})
if($contextExit -ne 0 -or $contextMarkers.Count -ne 1){throw "Native ContextEffects failed ($contextExit). See $contextLog"}
Add-Content -LiteralPath $contextLog -Value "LYRA_CONTEXT_EXPORT_PROCESS_EXIT code=$contextExit"
$contextMarkers

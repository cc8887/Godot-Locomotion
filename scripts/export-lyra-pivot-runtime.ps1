[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineRoot,
      [Parameter(Mandatory=$true)][string]$UnrealProject)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$editor=Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe'
$plugin=Join-Path $root 'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter/AlsV4AssetExporter.uplugin'
$script=Join-Path $root 'tools/unreal/export_lyra_pivot_runtime.py'
$log=Join-Path $root 'artifacts/lyra-analysis/pivot-runtime-ue-export.log'
$previous=[Environment]::GetEnvironmentVariable('LYRA_OUTPUT_ROOT','Process')
try {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT',(Join-Path $root 'assets/generated/lyra_als'),'Process')
    & $editor $UnrealProject '-run=pythonscript' "-Script=$script" "-PLUGIN=$plugin" `
        '-DisablePlugins=AnimationData,ModelContextProtocol,Mocara' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput *> $log
    $exitCode=$LASTEXITCODE
    $markers=@(Get-Content -LiteralPath $log | Where-Object { $_ -match 'LYRA_PIVOT_RUNTIME_NATIVE_OK traces=9 frames=3780 poseFrames=3528 logical=81 sequences=\d+ packages=508 assets_saved=0' })
    if ($exitCode -ne 0 -or $markers.Count -ne 1) { throw "Pivot pose export failed ($exitCode). See $log" }
    Add-Content -LiteralPath $log -Value "LYRA_EXPORT_PROCESS_EXIT_OK mode=pivot-runtime code=$exitCode"
    $markers
} finally { [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT',$previous,'Process') }

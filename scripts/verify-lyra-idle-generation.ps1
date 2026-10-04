[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineRoot,
      [Parameter(Mandatory=$true)][string]$UnrealProject)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$editor = Join-Path $EngineRoot 'Engine/Binaries/Win64/UnrealEditor-Cmd.exe'
$plugin = Join-Path $root 'artifacts/unreal/gasp58-lyra-masks/package/AlsV4AssetExporter/AlsV4AssetExporter.uplugin'
$oldOutput = [Environment]::GetEnvironmentVariable('LYRA_OUTPUT_ROOT','Process')
try {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT',(Join-Path $root 'assets/generated/lyra_als'),'Process')
    $script = Join-Path $root 'tools/unreal/verify_lyra_idle_generation.py'
    $result = & $editor $UnrealProject '-run=pythonscript' "-Script=$script" "-PLUGIN=$plugin" `
        '-DisablePlugins=AnimationData,ModelContextProtocol,Mocara' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1
    $exitCode = $LASTEXITCODE
    $result | Set-Content (Join-Path $root 'artifacts/lyra-analysis/idle-generation-ue-full.log') -Encoding utf8
    $markers = @($result | Where-Object { "$_" -match 'LYRA_IDLE_GENERATION_NATIVE_OK freshTargets=5 .*assets_saved=0' })
    $errors = @($result | Where-Object { "$_" -match 'LogOutputDevice: Error:|LogPython: Error:|Ensure condition failed|Assertion failed' })
    if ($exitCode -ne 0 -or $markers.Count -ne 1 -or $errors.Count -ne 0) { throw "Fresh Idle generation failed ($exitCode).`n$($result | Select-Object -Last 35 | Out-String)" }
    $markers
} finally { [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT',$oldOutput,'Process') }

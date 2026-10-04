[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$EngineRoot,
      [Parameter(Mandatory=$true)][string]$UnrealProject)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$editor = Join-Path $EngineRoot 'Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
$plugin = Join-Path $root 'artifacts\unreal\gasp58-lyra-masks\package\AlsV4AssetExporter\AlsV4AssetExporter.uplugin'
$script = Join-Path $root 'tools\unreal\export_lyra_locomotion_blend.py'
$oldOutput = [Environment]::GetEnvironmentVariable('LYRA_OUTPUT_ROOT', 'Process')
$oldRequests = [Environment]::GetEnvironmentVariable('LYRA_BLEND_REQUESTS', 'Process')
try {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', (Join-Path $root 'assets\generated\lyra_als'), 'Process')
    [Environment]::SetEnvironmentVariable('LYRA_BLEND_REQUESTS', (Join-Path $root 'artifacts\lyra-analysis\locomotion-blend-requests.json'), 'Process')
    $result = & $editor $UnrealProject '-run=pythonscript' "-Script=$script" "-PLUGIN=$plugin" `
        '-DisablePlugins=AnimationData,ModelContextProtocol,Mocara' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1
    $exitCode = $LASTEXITCODE
    $result | Set-Content (Join-Path $root 'artifacts\lyra-analysis\locomotion-blend-ue-full.log') -Encoding utf8
    $markers = @($result | Where-Object { "$_" -match 'LYRA_LOCOMOTION_BLEND_NATIVE_OK hz=30,60,120 frames=840 bones=81 assets_saved=0' })
    if ($exitCode -ne 0 -or $markers.Count -ne 1) { throw "Locomotion blend export failed ($exitCode).`n$($result | Select-Object -Last 40 | Out-String)" }
    $markers
} finally {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $oldOutput, 'Process')
    [Environment]::SetEnvironmentVariable('LYRA_BLEND_REQUESTS', $oldRequests, 'Process')
}

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EngineRoot,
    [Parameter(Mandatory = $true)][string]$UnrealProject,
    [string]$ExternalPlugin
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$editor = Join-Path ([IO.Path]::GetFullPath($EngineRoot)) 'Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
$project = [IO.Path]::GetFullPath($UnrealProject)
if (-not $ExternalPlugin) {
    $ExternalPlugin = Join-Path $root 'artifacts\unreal\gasp58-lyra-masks\package\AlsV4AssetExporter\AlsV4AssetExporter.uplugin'
}
$output = Join-Path $root 'assets\generated\lyra_als'
foreach ($path in @($editor, $project, $ExternalPlugin)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing required file: $path" }
}
$previousOutput = [Environment]::GetEnvironmentVariable('LYRA_OUTPUT_ROOT', 'Process')
try {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $output, 'Process')
    $script = Join-Path $root 'tools\unreal\export_lyra_logical_controls.py'
    $result = & $editor $project '-run=pythonscript' "-Script=$script" "-PLUGIN=$ExternalPlugin" `
        '-ini:EditorPerProjectUserSettings:[/Script/ModelContextProtocolEngine.ModelContextProtocolSettings]:bAutoStartServer=false' `
        '-DisablePlugins=AnimationData,ModelContextProtocol,Mocara' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1
    $exit = $LASTEXITCODE
    $result | Set-Content -LiteralPath (Join-Path $root 'artifacts\lyra-analysis\logical-controls-native-ue-full.log') -Encoding utf8
    $marker = 'LYRA_LOGICAL_CONTROLS_OK ordinary=189 additive=45 raw=69 logical=81 skin=68 native=936 skinError=0 assets_saved=0'
    $markers = @($result | Where-Object { "$_" -match [regex]::Escape($marker) })
    if ($exit -ne 0 -or $markers.Count -ne 1) {
        throw "Lyra logical control export failed ($exit).`n$($result | Select-Object -Last 50 | Out-String)"
    }
    $markers
}
finally { [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $previousOutput, 'Process') }

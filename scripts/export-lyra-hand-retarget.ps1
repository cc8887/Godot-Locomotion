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
if (-not (Test-Path -LiteralPath $output -PathType Container)) { throw "Missing Lyra resource directory: $output" }
$previousOutput = [Environment]::GetEnvironmentVariable('LYRA_OUTPUT_ROOT', 'Process')
$noMcpListener = '-ini:EditorPerProjectUserSettings:[/Script/ModelContextProtocolEngine.ModelContextProtocolSettings]:bAutoStartServer=false'
try {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $output, 'Process')
    foreach ($entry in @(
        @{ File = 'export_lyra_skeletal_control_defaults.py'; Marker = 'LYRA_SKELETAL_CONTROL_DEFAULTS_OK'; Log = 'hand-retarget-defaults-ue-full.log' },
        @{ File = 'export_lyra_hand_retarget_oracle.py'; Marker = 'LYRA_HAND_RETARGET_NATIVE_OK clips=3 cases=324 physical=68 logical=79 assets_saved=0'; Log = 'hand-retarget-native-ue-full.log' }
    )) {
        $script = Join-Path $root ('tools\unreal\' + $entry.File)
        $result = & $editor $project '-run=pythonscript' "-Script=$script" "-PLUGIN=$ExternalPlugin" `
            $noMcpListener '-DisablePlugins=AnimationData,ModelContextProtocol' `
            -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1
        $exit = $LASTEXITCODE
        $result | Set-Content -LiteralPath (Join-Path $root ('artifacts\lyra-analysis\' + $entry.Log)) -Encoding utf8
        $markers = @($result | Where-Object { "$_" -match [regex]::Escape($entry.Marker) })
        if ($exit -ne 0 -or $markers.Count -ne 1) {
            throw "Lyra hand-retarget export failed ($exit): $($entry.File).`n$($result | Select-Object -Last 60 | Out-String)"
        }
        $markers
    }
}
finally {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $previousOutput, 'Process')
}

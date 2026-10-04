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
try {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $output, 'Process')
    foreach ($entry in @(
        @{ File = 'export_lyra_linked_layer_contracts.py'; Marker = 'LYRA_LINKED_CONTRACTS_OK classes=11 hooks=14 assets_saved=0'; Log = 'linked-contracts-ue-full.log' },
        @{ File = 'export_lyra_linked_layer_binding_oracle.py'; Marker = 'LYRA_LINKED_BINDING_NATIVE_OK steps=8 nodes=14 owners=4 same_class_reuse=4 assets_saved=0'; Log = 'linked-binding-ue-full.log' }
    )) {
        $script = Join-Path $root ('tools\unreal\' + $entry.File)
        $result = & $editor $project '-run=pythonscript' "-Script=$script" "-PLUGIN=$ExternalPlugin" `
            '-ini:EditorPerProjectUserSettings:[/Script/ModelContextProtocolEngine.ModelContextProtocolSettings]:bAutoStartServer=false' `
            '-DisablePlugins=AnimationData,ModelContextProtocol,Mocara' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1
        $exit = $LASTEXITCODE
        $result | Set-Content -LiteralPath (Join-Path $root ('artifacts\lyra-analysis\' + $entry.Log)) -Encoding utf8
        $markers = @($result | Where-Object { "$_" -match [regex]::Escape($entry.Marker) })
        if ($exit -ne 0 -or $markers.Count -ne 1) {
            throw "Lyra linked-layer export failed ($exit): $($entry.File).`n$($result | Select-Object -Last 60 | Out-String)"
        }
        $markers
    }
}
finally { [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $previousOutput, 'Process') }

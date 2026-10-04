[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EngineRoot,
    [Parameter(Mandatory = $true)][string]$UnrealProject
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$editor = Join-Path ([IO.Path]::GetFullPath($EngineRoot)) 'Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
$project = [IO.Path]::GetFullPath($UnrealProject)
$plugin = Join-Path $root 'artifacts\unreal\gasp58-lyra-masks\package\AlsV4AssetExporter\AlsV4AssetExporter.uplugin'
$script = Join-Path $root 'tools\unreal\export_lyra_unarmed_layer_masks.py'
$output = Join-Path $root 'assets\generated\lyra_als'
foreach ($path in @($editor, $project, $plugin, $script)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing required file: $path" }
}
if (-not (Test-Path -LiteralPath $output -PathType Container)) {
    throw "Missing Lyra resource directory: $output"
}
$descriptor = Get-Content -LiteralPath $plugin -Raw | ConvertFrom-Json
if ($descriptor.Version -ne 2) { throw 'Expected the separately packaged Lyra mask reader.' }
$noMcpListener = '-ini:EditorPerProjectUserSettings:[/Script/ModelContextProtocolEngine.ModelContextProtocolSettings]:bAutoStartServer=false'
try {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $output, 'Process')
    $result = & $editor $project '-run=pythonscript' "-Script=$script" "-PLUGIN=$plugin" `
        $noMcpListener '-DisablePlugins=AnimationData,ModelContextProtocol' `
        -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1
    $exit = $LASTEXITCODE
    $markers = @($result | Where-Object { "$_" -match 'LYRA_UNARMED_MASK_(CLIP_)?OK' })
    if ($exit -ne 0 -or -not (($markers | Out-String).Contains('LYRA_UNARMED_MASK_OK profiles=2 targetBones=68'))) {
        throw "Lyra mask export failed with exit code $exit.`n$($result | Select-Object -Last 70 | Out-String)"
    }
    $markers
}
finally {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $null, 'Process')
}

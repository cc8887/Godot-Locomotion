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
$inventoryScript = Join-Path $root 'tools\unreal\export_lyra_linked_layer_inventory.py'
$exportScript = Join-Path $root 'tools\unreal\retarget_export_lyra_unarmed_aux.py'
$metadataScript = Join-Path $root 'tools\unreal\export_lyra_unarmed_aux_metadata.py'
$manifest = Join-Path $root 'tools\unreal\lyra_unarmed_aux_clips.json'
$crouchManifest = Join-Path $root 'tools\unreal\lyra_unarmed_crouch_transitions.json'
$output = Join-Path $root 'assets\generated\lyra_als'
foreach ($path in @($editor, $project, $inventoryScript, $exportScript, $metadataScript, $manifest, $crouchManifest)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing required file: $path" }
}
if (-not (Test-Path -LiteralPath $output -PathType Container)) {
    throw "Lyra base export is required before auxiliary export: $output"
}
$noMcpListener = '-ini:EditorPerProjectUserSettings:[/Script/ModelContextProtocolEngine.ModelContextProtocolSettings]:bAutoStartServer=false'

try {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $output, 'Process')
    [Environment]::SetEnvironmentVariable('LYRA_CLIP_MANIFEST', $manifest, 'Process')
    foreach ($job in @(
        @{ Script = $inventoryScript; Manifest = $manifest; Marker = 'LYRA_LAYER_INVENTORY_OK classes=9' },
        @{ Script = $exportScript; Manifest = $manifest; Marker = 'LYRA_UNARMED_AUX_EXPORT_OK clips=12' },
        @{ Script = $exportScript; Manifest = $crouchManifest; Marker = 'LYRA_UNARMED_CROUCH_TRANSITIONS_EXPORT_OK clips=12' },
        @{ Script = $metadataScript; Manifest = $manifest; Marker = 'LYRA_UNARMED_AUX_METADATA_OK clips=24' }
    )) {
        [Environment]::SetEnvironmentVariable('LYRA_CLIP_MANIFEST', $job.Manifest, 'Process')
        $result = & $editor $project '-run=pythonscript' "-Script=$($job.Script)" `
            $noMcpListener '-DisablePlugins=AnimationData,ModelContextProtocol' `
            -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1
        $exit = $LASTEXITCODE
        if ($exit -ne 0 -or -not (($result | Out-String).Contains($job.Marker))) {
            throw "Lyra auxiliary export failed with exit code $exit.`n$($result | Select-Object -Last 80 | Out-String)"
        }
        $result | Where-Object { "$_" -match 'LYRA_(LAYER_INVENTORY|UNARMED_(AUX|CROUCH_TRANSITIONS)_(CLIP|EXPORT)|UNARMED_AUX_METADATA(_CLIP)?)_OK' }
    }
}
finally {
    [Environment]::SetEnvironmentVariable('LYRA_CLIP_MANIFEST', $null, 'Process')
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $null, 'Process')
}

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
$exportScript = Join-Path $root 'tools\unreal\retarget_export_lyra_unarmed_aux.py'
$metadataScript = Join-Path $root 'tools\unreal\export_lyra_unarmed_aux_metadata.py'
$manifest = Join-Path $root 'tools\unreal\lyra_unarmed_remaining_clips.json'
$output = Join-Path $root 'assets\generated\lyra_als'
foreach ($path in @($editor, $project, $exportScript, $metadataScript, $manifest)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing required file: $path" }
}
if (-not (Test-Path -LiteralPath $output -PathType Container)) {
    throw "Lyra base export is required before remaining asset export: $output"
}
$noMcpListener = '-ini:EditorPerProjectUserSettings:[/Script/ModelContextProtocolEngine.ModelContextProtocolSettings]:bAutoStartServer=false'

try {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $output, 'Process')
    [Environment]::SetEnvironmentVariable('LYRA_CLIP_MANIFEST', $manifest, 'Process')
    $result = & $editor $project '-run=pythonscript' "-Script=$exportScript" `
        $noMcpListener '-DisablePlugins=AnimationData,ModelContextProtocol' `
        -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1
    $exit = $LASTEXITCODE
    if ($exit -ne 0 -or -not (($result | Out-String).Contains('LYRA_UNARMED_REMAINING_EXPORT_OK clips=17'))) {
        throw "Lyra remaining export failed with exit code $exit.`n$($result | Select-Object -Last 80 | Out-String)"
    }
    $result | Where-Object { "$_" -match 'LYRA_UNARMED_REMAINING_(CLIP|EXPORT)_OK' }
    [Environment]::SetEnvironmentVariable('LYRA_METADATA_MODE', 'remaining', 'Process')
    $result = & $editor $project '-run=pythonscript' "-Script=$metadataScript" `
        $noMcpListener '-DisablePlugins=AnimationData,ModelContextProtocol' `
        -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1
    $exit = $LASTEXITCODE
    if ($exit -ne 0 -or -not (($result | Out-String).Contains('LYRA_UNARMED_REMAINING_METADATA_OK clips=17'))) {
        throw "Lyra remaining metadata export failed with exit code $exit.`n$($result | Select-Object -Last 80 | Out-String)"
    }
    $result | Where-Object { "$_" -match 'LYRA_UNARMED_REMAINING_METADATA(_CLIP)?_OK' }
}
finally {
    [Environment]::SetEnvironmentVariable('LYRA_METADATA_MODE', $null, 'Process')
    [Environment]::SetEnvironmentVariable('LYRA_CLIP_MANIFEST', $null, 'Process')
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $null, 'Process')
}

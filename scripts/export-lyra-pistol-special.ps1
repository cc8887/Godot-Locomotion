[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EngineRoot,
    [Parameter(Mandatory = $true)][string]$UnrealProject,
    [ValidateSet('pistol', 'rifle')][string]$Profile = 'pistol',
    [string]$ExternalPlugin
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$editor = Join-Path ([IO.Path]::GetFullPath($EngineRoot)) 'Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
$project = [IO.Path]::GetFullPath($UnrealProject)
$output = Join-Path $root 'assets\generated\lyra_als'
$inspect = Join-Path $root 'tools\unreal\inspect_lyra_pistol_special.py'
$defaults = Join-Path $root 'tools\unreal\export_lyra_pistol_layer_defaults.py'
$retarget = Join-Path $root 'tools\unreal\retarget_export_lyra_unarmed_special.py'
$native = Join-Path $root 'tools\unreal\export_lyra_unarmed_aim_native.py'
$pluginArgs = @()
if ($ExternalPlugin) {
    $plugin = [IO.Path]::GetFullPath($ExternalPlugin)
    if (-not (Test-Path -LiteralPath $plugin -PathType Leaf)) {
        throw "Missing external exporter plugin: $plugin"
    }
    $pluginArgs = @("-PLUGIN=$plugin")
}
foreach ($path in @($editor, $project, $inspect, $defaults, $retarget, $native,
        (Join-Path $output "${Profile}_catalog.json"),
        (Join-Path $output 'unarmed_layer_defaults.json'))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing required file: $path" }
}
$noMcpListener = '-ini:EditorPerProjectUserSettings:[/Script/ModelContextProtocolEngine.ModelContextProtocolSettings]:bAutoStartServer=false'
function Invoke-LyraPython([string]$path, [string]$expected) {
    $result = & $editor $project @pluginArgs '-run=pythonscript' "-Script=$path" `
        $noMcpListener '-DisablePlugins=AnimationData,ModelContextProtocol' `
        -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1
    $exit = $LASTEXITCODE
    $markers = @($result | Where-Object { "$_" -match [regex]::Escape($expected) })
    if ($exit -ne 0 -or $markers.Count -eq 0) {
        throw "Lyra $Profile special export failed with exit code $exit ($expected).`n$($result | Select-Object -Last 90 | Out-String)"
    }
    $markers
}
try {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $output, 'Process')
    [Environment]::SetEnvironmentVariable('LYRA_SPECIAL_PROFILE', $Profile, 'Process')
    Invoke-LyraPython $inspect "LYRA_ITEM_SPECIAL_OK profile=$Profile"
    Invoke-LyraPython $defaults "LYRA_ITEM_DEFAULTS_OK profile=$Profile"
    $inventory = Get-Content (Join-Path $output "${Profile}_special_inventory.json") -Raw | ConvertFrom-Json
    $sampleCount = @($inventory.aimOffset.samples).Count
    [Environment]::SetEnvironmentVariable('LYRA_SPECIAL_MODE', 'aim', 'Process')
    Invoke-LyraPython $retarget "LYRA_SPECIAL_EXPORT_OK mode=aim clips=$sampleCount profile=$Profile"
    [Environment]::SetEnvironmentVariable('LYRA_SPECIAL_MODE', 'jump', 'Process')
    Invoke-LyraPython $retarget "LYRA_SPECIAL_EXPORT_OK mode=jump clips=1 profile=$Profile"
    [Environment]::SetEnvironmentVariable('LYRA_AIM_NATIVE_MODE', 'grid', 'Process')
    Invoke-LyraPython $native "LYRA_AIM_NATIVE_OK mode=grid"
    [Environment]::SetEnvironmentVariable('LYRA_AIM_NATIVE_MODE', 'interp-grid', 'Process')
    Invoke-LyraPython $native "LYRA_AIM_NATIVE_OK mode=interp-grid"
}
finally {
    foreach ($name in @('LYRA_OUTPUT_ROOT', 'LYRA_SPECIAL_PROFILE', 'LYRA_SPECIAL_MODE',
            'LYRA_AIM_NATIVE_MODE')) {
        [Environment]::SetEnvironmentVariable($name, $null, 'Process')
    }
}

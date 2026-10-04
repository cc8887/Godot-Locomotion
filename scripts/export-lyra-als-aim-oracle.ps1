[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EngineRoot,
    [Parameter(Mandatory = $true)][string]$UnrealProject,
    [Parameter(Mandatory = $true)][string]$PluginPath,
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$editor = Join-Path ([IO.Path]::GetFullPath($EngineRoot)) 'Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
$project = [IO.Path]::GetFullPath($UnrealProject)
$plugin = [IO.Path]::GetFullPath($PluginPath)
$script = Join-Path $root 'tools\unreal\export_lyra_als_aim_oracle.py'
$output = Join-Path $root 'assets\generated\lyra_als'
foreach ($path in @($editor, $project, $plugin, $script,
        (Join-Path $output 'unarmed_special_inventory.json'),
        (Join-Path $output 'unarmed_aim_samples_catalog.json'))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing required file: $path" }
}
$noMcpListener = '-ini:EditorPerProjectUserSettings:[/Script/ModelContextProtocolEngine.ModelContextProtocolSettings]:bAutoStartServer=false'
try {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $output, 'Process')
    $modes = if ($Apply) { @('apply-grid', 'apply-interp') } else { @('grid', 'interp') }
    foreach ($mode in $modes) {
        [Environment]::SetEnvironmentVariable('LYRA_AIM_ORACLE_MODE', $mode, 'Process')
        $result = & $editor $project '-run=pythonscript' "-Script=$script" "-PLUGIN=$plugin" `
            $noMcpListener '-DisablePlugins=AnimationData,ModelContextProtocol' `
            -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1
        $exit = $LASTEXITCODE
        if ($exit -ne 0 -or -not (($result | Out-String).Contains("LYRA_ALS_AIM_ORACLE_OK mode=$mode"))) {
            throw "ALS AimOffset $mode oracle failed with exit code $exit.`n$($result | Select-Object -Last 80 | Out-String)"
        }
        $result | Where-Object { "$_" -match "LYRA_ALS_AIM_ORACLE_OK mode=$mode" }
    }
}
finally {
    [Environment]::SetEnvironmentVariable('LYRA_AIM_ORACLE_MODE', $null, 'Process')
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $null, 'Process')
}

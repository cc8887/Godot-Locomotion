[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EngineRoot,
    [Parameter(Mandatory = $true)][string]$UnrealProject,
    [Parameter(Mandatory = $true)][string]$PluginPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$editor = Join-Path ([IO.Path]::GetFullPath($EngineRoot)) 'Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
$project = [IO.Path]::GetFullPath($UnrealProject)
$plugin = [IO.Path]::GetFullPath($PluginPath)
$script = Join-Path $root 'tools\unreal\export_lyra_unarmed_aim_native.py'
$output = Join-Path $root 'assets\generated\lyra_als'
foreach ($path in @($editor, $project, $plugin, $script, (Join-Path $output 'unarmed_special_inventory.json'))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing required file: $path" }
}
$noMcpListener = '-ini:EditorPerProjectUserSettings:[/Script/ModelContextProtocolEngine.ModelContextProtocolSettings]:bAutoStartServer=false'
try {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $output, 'Process')
    foreach ($mode in @('grid', 'poses', 'interp-grid', 'interp-poses')) {
        [Environment]::SetEnvironmentVariable('LYRA_AIM_NATIVE_MODE', $mode, 'Process')
        $result = & $editor $project '-run=pythonscript' "-Script=$script" "-PLUGIN=$plugin" `
            $noMcpListener '-DisablePlugins=AnimationData,ModelContextProtocol' `
            -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1
        $exit = $LASTEXITCODE
        if ($exit -ne 0 -or -not (($result | Out-String).Contains("LYRA_AIM_NATIVE_OK mode=$mode"))) {
            throw "Native Lyra AimOffset $mode export failed with exit code $exit.`n$($result | Select-Object -Last 80 | Out-String)"
        }
        $result | Where-Object { "$_" -match "LYRA_AIM_NATIVE_OK mode=$mode" }
    }
}
finally {
    [Environment]::SetEnvironmentVariable('LYRA_AIM_NATIVE_MODE', $null, 'Process')
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $null, 'Process')
}

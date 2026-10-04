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
$script = Join-Path $root 'tools\unreal\retarget_export_lyra_unarmed_special.py'
$output = Join-Path $root 'assets\generated\lyra_als'
foreach ($path in @($editor, $project, $script, (Join-Path $output 'unarmed_special_inventory.json'))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing required file: $path" }
}
$noMcpListener = '-ini:EditorPerProjectUserSettings:[/Script/ModelContextProtocolEngine.ModelContextProtocolSettings]:bAutoStartServer=false'
try {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $output, 'Process')
    foreach ($mode in @('aim', 'jump')) {
        [Environment]::SetEnvironmentVariable('LYRA_SPECIAL_MODE', $mode, 'Process')
        $result = & $editor $project '-run=pythonscript' "-Script=$script" `
            $noMcpListener '-DisablePlugins=AnimationData,ModelContextProtocol' `
            -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1
        $exit = $LASTEXITCODE
        $expected = if ($mode -eq 'aim') { 15 } else { 1 }
        if ($exit -ne 0 -or -not (($result | Out-String).Contains("LYRA_SPECIAL_EXPORT_OK mode=$mode clips=$expected"))) {
            throw "Lyra $mode additive export failed with exit code $exit.`n$($result | Select-Object -Last 80 | Out-String)"
        }
        $result | Where-Object { "$_" -match "LYRA_SPECIAL_(CLIP|EXPORT)_OK mode=$mode" }
    }
}
finally {
    [Environment]::SetEnvironmentVariable('LYRA_SPECIAL_MODE', $null, 'Process')
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $null, 'Process')
}

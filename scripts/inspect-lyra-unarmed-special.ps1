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
$script = Join-Path $root 'tools\unreal\inspect_lyra_unarmed_special.py'
$output = Join-Path $root 'assets\generated\lyra_als'
foreach ($path in @($editor, $project, $script)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing required file: $path" }
}
if (-not (Test-Path -LiteralPath $output -PathType Container)) {
    throw "Lyra base export is required before special asset inspection: $output"
}
$noMcpListener = '-ini:EditorPerProjectUserSettings:[/Script/ModelContextProtocolEngine.ModelContextProtocolSettings]:bAutoStartServer=false'
try {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $output, 'Process')
    $result = & $editor $project '-run=pythonscript' "-Script=$script" `
        $noMcpListener '-DisablePlugins=AnimationData,ModelContextProtocol' `
        -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1
    $exit = $LASTEXITCODE
    if ($exit -ne 0 -or -not (($result | Out-String).Contains('LYRA_UNARMED_SPECIAL_OK'))) {
        throw "Lyra special asset inspection failed with exit code $exit.`n$($result | Select-Object -Last 80 | Out-String)"
    }
    $result | Where-Object { "$_" -match 'LYRA_UNARMED_SPECIAL_OK' }
}
finally {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $null, 'Process')
}

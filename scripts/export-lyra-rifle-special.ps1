[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EngineRoot,
    [Parameter(Mandatory = $true)][string]$UnrealProject,
    [string]$ExternalPlugin
)

$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'export-lyra-pistol-special.ps1') `
    -EngineRoot $EngineRoot -UnrealProject $UnrealProject -Profile rifle -ExternalPlugin $ExternalPlugin

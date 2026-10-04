[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EngineRoot,
    [Parameter(Mandatory = $true)][string]$UnrealProject,
    [ValidateRange(0, 64)][int]$BatchNew = 4
)

$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'export-lyra-pistol.ps1') `
    -EngineRoot $EngineRoot -UnrealProject $UnrealProject -Profile rifle -BatchNew $BatchNew

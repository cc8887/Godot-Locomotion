[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EngineRoot,

    [Parameter(Mandatory = $true)]
    [string]$UnrealProject,

    [string]$Output = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$buildScript = Join-Path $PSScriptRoot 'build-als-exporter.ps1'
if (-not (Test-Path -LiteralPath $buildScript -PathType Leaf)) {
    throw "Build script does not exist: $buildScript"
}

$outputLines = & $buildScript -EngineRoot $EngineRoot -UnrealProject $UnrealProject 2>&1
$outputLines | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) {
    throw "P2A build gate failed with exit code $LASTEXITCODE."
}

$marker = 'GODOT_ALS_EXPORTER_READY engine=5.9.0 plugin=1.0.0'
if (-not (($outputLines | Out-String).Contains($marker, [StringComparison]::Ordinal))) {
    throw "P2A ready marker was not found: $marker"
}

Write-Host 'GODOT_ALS_P2A_READY'

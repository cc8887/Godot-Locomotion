[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EngineRoot,

    [Parameter(Mandatory = $true)]
    [string]$UnrealProject,

    [Parameter(Mandatory = $true)]
    [string]$GodotExecutable
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$engineRootPath = (Resolve-Path -LiteralPath $EngineRoot).Path
$unrealProjectPath = (Resolve-Path -LiteralPath $UnrealProject).Path
$godotPath = (Resolve-Path -LiteralPath $GodotExecutable).Path
$editor = Join-Path $engineRootPath 'Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
$pythonScript = Join-Path $projectRoot 'tools\unreal\export_gasp58_raw_sources.py'
$compiledSet = Join-Path $projectRoot 'assets\generated\als_v4\compiled\als_animation_set.tres'
$requestDirectory = Join-Path $projectRoot 'artifacts\gasp58'
$outputDirectory = Join-Path $projectRoot 'assets\generated\als_v4_raw'
$oracleDirectory = Join-Path $requestDirectory 'standard_oracles'
$requestScene = 'res://scenes/tests/raw_sequence_source_request.tscn'

foreach ($required in @($editor, $pythonScript, $compiledSet)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required GASP58 raw export input is missing: $required"
    }
}
$version = Get-Content -LiteralPath (Join-Path $engineRootPath 'Engine\Build\Build.version') -Raw | ConvertFrom-Json
if ($version.MajorVersion -ne 5 -or $version.MinorVersion -ne 8) {
    throw 'GASP58 raw source export requires Unreal Engine 5.8.'
}
foreach ($directory in @($requestDirectory, $outputDirectory, $oracleDirectory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$environmentNames = @('ALS_RAW_SOURCE_REQUEST_OUTPUT', 'ALS_GASP_RAW_REQUEST_DIR',
    'ALS_GASP_RAW_OUTPUT_DIR', 'ALS_GASP_RAW_ORACLE_DIR')
$previousEnvironment = @{}
foreach ($name in $environmentNames) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
try {
    Push-Location (Split-Path -Parent $projectRoot)
    try {
        & dotnet build (Join-Path $projectRoot 'GodotALS.csproj') -c Debug --no-restore -v quiet
        if ($LASTEXITCODE -ne 0) { throw "Godot C# build failed with exit code $LASTEXITCODE." }
    }
    finally { Pop-Location }

    foreach ($request in @(
        @{ Name = 'recovery_movement'; Flag = '' },
        @{ Name = 'aim'; Flag = '--aim-source' },
        @{ Name = 'overlay'; Flag = '--overlay-source' },
        @{ Name = 'ragdoll'; Flag = '--ragdoll-source' },
        @{ Name = 'stop'; Flag = '--stop-source' },
        @{ Name = 'overlay_prop'; Flag = '--prop-source' }
    )) {
        $requestPath = Join-Path $requestDirectory ($request.Name + '-request.json')
        [Environment]::SetEnvironmentVariable('ALS_RAW_SOURCE_REQUEST_OUTPUT', $requestPath, 'Process')
        $arguments = @('--headless', '--path', $projectRoot, $requestScene)
        if ($request.Flag) { $arguments += @('--', $request.Flag) }
        $requestOutput = & $godotPath @arguments 2>&1
        if ($LASTEXITCODE -ne 0 -or -not (($requestOutput | Out-String).Contains('RAW_SOURCE_REQUEST_OK'))) {
            throw "Godot source request failed: $($request.Name)`n$($requestOutput | Out-String)"
        }
        Write-Output ($requestOutput | Where-Object { "$_" -match 'RAW_SOURCE_REQUEST_OK' })
    }

    [Environment]::SetEnvironmentVariable('ALS_GASP_RAW_REQUEST_DIR', $requestDirectory, 'Process')
    [Environment]::SetEnvironmentVariable('ALS_GASP_RAW_OUTPUT_DIR', $outputDirectory, 'Process')
    [Environment]::SetEnvironmentVariable('ALS_GASP_RAW_ORACLE_DIR', $oracleDirectory, 'Process')
    $exportOutput = & $editor $unrealProjectPath '-run=pythonscript' "-Script=$pythonScript" `
        '-DisablePlugins=AnimationData,ModelContextProtocol' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1
    $exportExitCode = $LASTEXITCODE
    $exportText = $exportOutput | Out-String
    if ($exportExitCode -ne 0 -or -not $exportText.Contains('GODOT_ALS_GASP58_RAW_EXPORT_OK indices=6')) {
        throw "GASP58 raw source export failed with exit code $exportExitCode.`n$($exportOutput | Select-Object -Last 80 | Out-String)"
    }
    $exportOutput | Where-Object { "$_" -match 'GODOT_ALS_GASP58_RAW_(GROUP|EXPORT)_OK' } | ForEach-Object { Write-Output $_ }
}
finally {
    foreach ($name in $environmentNames) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }
}

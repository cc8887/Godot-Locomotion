param(
    [Parameter(Mandatory = $true)] [string] $GodotExecutable,
    [Parameter(Mandatory = $true)] [string] $ProjectRoot
)
$ErrorActionPreference = 'Stop'

$requestDirectory = Join-Path $ProjectRoot 'artifacts\kimodo_als'
New-Item -ItemType Directory -Force -Path $requestDirectory | Out-Null
$requestScene = 'res://scenes/tests/raw_sequence_source_request.tscn'

# Kimodo compiled animation set tree; required so requests carry the Kimodo DefinitionDigest.
[Environment]::SetEnvironmentVariable('ALS_ASSET_ROOT', 'res://assets/generated/kimodo_als', 'Process')

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
    $arguments = @('--headless', '--path', $ProjectRoot, $requestScene)
    if ($request.Flag) { $arguments += @('--', $request.Flag) }
    $requestOutput = (& $GodotExecutable @arguments 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0 -or $requestOutput.IndexOf('RAW_SOURCE_REQUEST_OK') -lt 0) {
        throw ("Kimodo source request failed: " + $request.Name + "`n" + $requestOutput)
    }
    foreach ($line in ($requestOutput -split "`r?`n")) {
        if ($line -match 'RAW_SOURCE_REQUEST_OK') { Write-Output $line }
    }
}

Write-Output 'KIMODO_RAW_REQUESTS_OK groups=6'

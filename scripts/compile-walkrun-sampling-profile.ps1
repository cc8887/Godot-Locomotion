param(
    [Parameter(Mandatory)][string]$NativeTrace,
    [Parameter(Mandatory)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$trace = Get-Content -LiteralPath $NativeTrace -Raw | ConvertFrom-Json
if ($trace.schemaVersion -ne 1 -or $trace.assets.Count -ne 6) { throw 'Unsupported native WalkRun trace.' }
$names = @('F', 'B', 'FL', 'BL', 'FR', 'BR') | ForEach-Object { "ALS_N_WalkRun_$_" }
$assets = foreach ($name in $names) {
    $matches = @($trace.assets | Where-Object name -eq $name)
    if ($matches.Count -ne 1) { throw "Missing or ambiguous native BlendSpace: $name" }
    $asset = $matches[0]
    if (-not $asset.grid -or $asset.sampleWeightSpeed -ne 0 -or $asset.axes.Count -ne 2) { throw "Unsupported sampling: $name" }
    foreach ($axis in $asset.axes) {
        if ($axis.mode -ne 'Cubic' -or $axis.seconds -le 0) { throw "Unsupported axis filter: $name" }
    }
    [ordered]@{ name = $asset.name; objectPath = $asset.objectPath; grid = $asset.grid
        sampleWeightSpeed = $asset.sampleWeightSpeed; axes = $asset.axes; sampleNames = $asset.sampleNames }
}
[ordered]@{ schemaVersion = 1; source = $trace.source; nativeTraceSha256 = (Get-FileHash -LiteralPath $NativeTrace -Algorithm SHA256).Hash
    assets = @($assets) } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Output 'WALKRUN_SAMPLING_PROFILE_OK assets=6'

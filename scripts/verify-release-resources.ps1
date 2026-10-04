param(
    [string]$ProjectRoot = (Join-Path $PSScriptRoot '..'),
    [Parameter(Mandatory = $true)][string]$ExpectedTag
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$resourceRoot = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
$resourcePrefix = $resourceRoot + [IO.Path]::DirectorySeparatorChar
foreach ($resourceGroup in @('als', 'lyra')) {
    $manifestPath = Join-Path $resourceRoot "resource-bundles/$resourceGroup-assets-manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Missing resource manifest: $manifestPath" }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.tag -cne $ExpectedTag -or $manifest.bundle -cne $resourceGroup) {
        throw "Resource bundle does not match release $ExpectedTag : $manifestPath"
    }
    foreach ($resource in $manifest.files) {
        if ([IO.Path]::IsPathRooted($resource.path)) { throw "Absolute resource path: $($resource.path)" }
        $resourcePath = [IO.Path]::GetFullPath((Join-Path $resourceRoot $resource.path))
        if (-not $resourcePath.StartsWith($resourcePrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Resource path leaves the project: $($resource.path)"
        }
        if (-not (Test-Path -LiteralPath $resourcePath -PathType Leaf)) { throw "Missing resource: $($resource.path)" }
        if ((Get-Item -LiteralPath $resourcePath).Length -ne $resource.size -or
            (Get-FileHash -LiteralPath $resourcePath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $resource.sha256) {
            throw "Resource size/hash mismatch: $($resource.path)"
        }
    }
    Write-Host "GODOT_RELEASE_RESOURCES_OK bundle=$resourceGroup tag=$ExpectedTag files=$($manifest.files.Count)"
}

function Assert-P2bManifestHash {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$ManifestPath,
        [Parameter(Mandatory)]
        [string]$ExpectedSha256
    )

    $actualSha256 = (Get-FileHash -LiteralPath $ManifestPath -Algorithm SHA256).Hash
    if (-not [string]::Equals(
        $actualSha256,
        $ExpectedSha256,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw "Formal ALS manifest SHA-256 mismatch: expected=$ExpectedSha256 actual=$actualSha256"
    }
}

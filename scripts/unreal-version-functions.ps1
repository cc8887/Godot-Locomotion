function Assert-AlsSupportedEngineVersion
{
    param(
        [Parameter(Mandatory)][int]$MajorVersion,
        [Parameter(Mandatory)][int]$MinorVersion,
        [Parameter(Mandatory)][int]$PatchVersion,
        [string]$Context = 'Unreal Engine'
    )

    if ($MajorVersion -ne 5 -or $MinorVersion -notin @(8, 9) -or $PatchVersion -lt 0)
    {
        throw "$Context must use UE 5.8.x or UE 5.9.x; got $MajorVersion.$MinorVersion.$PatchVersion."
    }

    return "$MajorVersion.$MinorVersion.$PatchVersion"
}

function Get-AlsSupportedEngineVersion([string]$EngineRoot)
{
    $buildVersionPath = Join-Path $EngineRoot 'Engine\Build\Build.version'
    if (-not (Test-Path -LiteralPath $buildVersionPath -PathType Leaf))
    {
        throw "Unreal Engine Build.version is missing: $buildVersionPath"
    }

    $buildVersion = Get-Content -LiteralPath $buildVersionPath -Raw | ConvertFrom-Json
    return Assert-AlsSupportedEngineVersion `
        -MajorVersion ([int]$buildVersion.MajorVersion) `
        -MinorVersion ([int]$buildVersion.MinorVersion) `
        -PatchVersion ([int]$buildVersion.PatchVersion) `
        -Context "Unreal Engine at $EngineRoot"
}

$script:VersionFunctionsPath = Join-Path (Split-Path -Parent $PSScriptRoot) `
    'scripts\unreal-version-functions.ps1'
. $script:VersionFunctionsPath

Describe 'Unreal engine version compatibility' {
    It 'accepts UE 5.8.1 and 5.9.0 engine identities' {
        Assert-AlsSupportedEngineVersion -MajorVersion 5 -MinorVersion 8 -PatchVersion 1 |
            Should Be '5.8.1'
        Assert-AlsSupportedEngineVersion -MajorVersion 5 -MinorVersion 9 -PatchVersion 0 |
            Should Be '5.9.0'
    }

    It 'rejects engine versions outside the supported minor range' {
        foreach ($version in @(@(5, 7, 4), @(5, 10, 0)))
        {
            $message = $null
            try
            {
                Assert-AlsSupportedEngineVersion `
                    -MajorVersion $version[0] -MinorVersion $version[1] -PatchVersion $version[2]
            }
            catch
            {
                $message = $_.Exception.Message
            }
            $message | Should Match 'must use UE 5\.8\.x or UE 5\.9\.x'
        }
    }

    It 'reads the exact installed patch version from Build.version' {
        $engineRoot = Join-Path $TestDrive 'UE_5.8'
        $versionPath = Join-Path $engineRoot 'Engine\Build\Build.version'
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($versionPath))
        [IO.File]::WriteAllText($versionPath, '{"MajorVersion":5,"MinorVersion":8,"PatchVersion":1}')

        Get-AlsSupportedEngineVersion -EngineRoot $engineRoot | Should Be '5.8.1'
    }
}

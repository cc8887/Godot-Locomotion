$script:RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:Generator = Join-Path $script:RepositoryRoot 'scripts\generate-p3-profile.ps1'
$script:Manifest = Join-Path $script:RepositoryRoot 'assets\generated\als_v4\als_manifest.json'

function Invoke-P3ProfileGenerator([string]$ManifestPath, [string]$OutputPath)
{
    $output = @(& pwsh -NoProfile -File $script:Generator `
        -ManifestPath $ManifestPath -OutputPath $OutputPath 2>&1)
    [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output -join [Environment]::NewLine) }
}

function Write-JsonFixture([object]$Value, [string]$Path)
{
    [System.IO.File]::WriteAllText(
        $Path,
        ($Value | ConvertTo-Json -Depth 100),
        [System.Text.UTF8Encoding]::new($false))
}

Describe 'generate-p3-profile.ps1 strict source mapping' {
    It 'writes deterministic JSON with the exact top-level property order' {
        $first = Join-Path $TestDrive 'first.json'
        $second = Join-Path $TestDrive 'second.json'

        $firstRun = Invoke-P3ProfileGenerator $script:Manifest $first
        $secondRun = Invoke-P3ProfileGenerator $script:Manifest $second

        $firstRun.ExitCode | Should Be 0
        $secondRun.ExitCode | Should Be 0
        (Get-FileHash $first -Algorithm SHA256).Hash | Should Be `
            (Get-FileHash $second -Algorithm SHA256).Hash
        $profile = Get-Content -LiteralPath $first -Raw | ConvertFrom-Json
        $profile.schemaVersion | Should Be 2
        (@($profile.PSObject.Properties.Name) -join ',') | Should Be `
            'schemaVersion,presentation,mannequin,standingIdle,crouchingIdle,standingSamples,crouchingSamples,jumpStart,fallLoop,land,leanAdditive'
        (@($profile.presentation.PSObject.Properties.Name) -join ',') | Should Be `
            'translationMeters,yawRadians'
        (@($profile.presentation.translationMeters | ForEach-Object { [double]$_ }) -join ',') |
            Should Be '0,-0.92,0'
        [Math]::Abs([double]$profile.presentation.yawRadians - ([Math]::PI / 2.0)) |
            Should BeLessThan 1e-12
        [Convert]::ToBase64String([IO.File]::ReadAllBytes($first)) | Should Be `
            ([Convert]::ToBase64String([IO.File]::ReadAllBytes(
                (Join-Path $script:RepositoryRoot 'assets\config\p3_locomotion_profile.json'))))

        $directionLocks = @(
            [pscustomobject]@{ Grid='standingSamples'; Index=0; Path='/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/ALS_N_Walk_F.ALS_N_Walk_F'; Id='6124eafdcbeaaf04bca366add34c821faa0e4963'; X=0.0; Y=0.5 },
            [pscustomobject]@{ Grid='standingSamples'; Index=3; Path='/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/ALS_N_Walk_B.ALS_N_Walk_B'; Id='32fe18c71ccb860fe35c01d6b2b10fa2e4d98297'; X=0.0; Y=-0.5 },
            [pscustomobject]@{ Grid='crouchingSamples'; Index=1; Path='/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/ALS_CLF_Walk_L.ALS_CLF_Walk_L'; Id='21c24bd7df5192db2e2a860457f2b7b0681de41d'; X=-1.0; Y=0.0 },
            [pscustomobject]@{ Grid='crouchingSamples'; Index=2; Path='/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/ALS_CLF_Walk_R.ALS_CLF_Walk_R'; Id='db60b2c35ce5ef5216c782fc1f33549cbcf8278d'; X=1.0; Y=0.0 }
        )
        $manifest = Get-Content -LiteralPath $script:Manifest -Raw | ConvertFrom-Json
        foreach ($lock in $directionLocks) {
            $asset = @($manifest.animations | Where-Object { $_.objectPath -ceq $lock.Path })
            $asset.Count | Should Be 1
            [string]$asset[0].id | Should Be $lock.Id
            $sample = @($profile.($lock.Grid))[$lock.Index]
            [string]$sample.animation | Should Be $lock.Id
            [double]$sample.x | Should Be $lock.X
            [double]$sample.y | Should Be $lock.Y
        }
        @($profile.standingSamples).Count | Should Be 13
        @($profile.crouchingSamples).Count | Should Be 4
        foreach ($sample in @($profile.standingSamples) + @($profile.crouchingSamples))
        {
            (@($sample.PSObject.Properties.Name) -join ',') | Should Be 'animation,x,y,rateScale'
        }
        $standingCoordinates = @($profile.standingSamples | ForEach-Object {
            [string]::Format(
                [System.Globalization.CultureInfo]::InvariantCulture,
                '{0:R}|{1:R}', [double]$_.x, [double]$_.y)
        }) -join ';'
        $standingCoordinates | Should Be `
            '0|0.5;-0.353553|0.353553;0.353553|0.353553;0|-0.5;-0.353553|-0.353553;0.353553|-0.353553;0|1;-0.707107|0.707107;0.707107|0.707107;0|-1;-0.707107|-0.707107;0.707107|-0.707107;0|1.5'
        $crouchingCoordinates = @($profile.crouchingSamples | ForEach-Object {
            [string]::Format(
                [System.Globalization.CultureInfo]::InvariantCulture,
                '{0:R}|{1:R}', [double]$_.x, [double]$_.y)
        }) -join ';'
        $crouchingCoordinates | Should Be '0|1;-1|0;1|0;0|-1'
    }

    It 'rejects multiple exact full object-path matches' {
        $manifest = Get-Content -LiteralPath $script:Manifest -Raw | ConvertFrom-Json
        $manifest.animations = @($manifest.animations) + @($manifest.animations | Where-Object {
            $_.objectPath -ceq '/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/InAir/ALS_N_FallLoop.ALS_N_FallLoop'
        } | Select-Object -First 1)
        $fixture = Join-Path $TestDrive 'duplicate-manifest.json'
        Write-JsonFixture $manifest $fixture

        $result = Invoke-P3ProfileGenerator $fixture (Join-Path $TestDrive 'duplicate-output.json')

        $result.ExitCode | Should Not Be 0
        $result.Output | Should Match 'multiple.*exact.*object path'
    }

    It 'does not fall back to an identical asset basename at another path' {
        $manifest = Get-Content -LiteralPath $script:Manifest -Raw | ConvertFrom-Json
        $expected = '/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/InAir/ALS_N_FallLoop.ALS_N_FallLoop'
        $clip = $manifest.animations | Where-Object { $_.objectPath -ceq $expected } | Select-Object -First 1
        $clip.objectPath = '/Game/Other/ALS_N_FallLoop.ALS_N_FallLoop'
        $clip.packagePath = '/Game/Other/ALS_N_FallLoop'
        $fixture = Join-Path $TestDrive 'basename-only-manifest.json'
        Write-JsonFixture $manifest $fixture

        $result = Invoke-P3ProfileGenerator $fixture (Join-Path $TestDrive 'basename-output.json')

        $result.ExitCode | Should Not Be 0
        $result.Output | Should Match 'zero.*exact.*object path'
    }

    It 'contains the complete locked UE paths and no assetName lookup' {
        $source = [System.IO.File]::ReadAllText($script:Generator)

        $source | Should Match ([regex]::Escape('/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/Meshes/Mannequin.Mannequin'))
        $source | Should Match ([regex]::Escape('/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/BasePoses/ALS_N_Pose.ALS_N_Pose'))
        $source | Should Match ([regex]::Escape('/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/Base/Locomotion/Detail/ALS_N_Lean.ALS_N_Lean'))
        $assetResolutionSource = $source.Substring(
            $source.IndexOf('function Resolve-ExactManifestAsset'),
            $source.IndexOf('$outputDirectory =') - $source.IndexOf('function Resolve-ExactManifestAsset'))
        $assetResolutionSource | Should Not Match '\.assetName|AssetName|Split-Path.*-Leaf|GetFileName'
    }

    It 'rejects identical normalized manifest and output paths before changing the manifest' {
        $fixture = Join-Path $TestDrive 'same-path.json'
        [System.IO.File]::Copy($script:Manifest, $fixture)
        $beforeBytes = [System.IO.File]::ReadAllBytes($fixture)
        $beforeHash = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash
        $outputAlias = Join-Path (Split-Path -Parent $fixture) '.\same-path.json'

        $result = Invoke-P3ProfileGenerator $fixture $outputAlias

        $result.ExitCode | Should Not Be 0
        $result.Output | Should Match 'manifest.*output.*same file'
        (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash | Should Be $beforeHash
        [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($fixture)) | Should Be `
            ([Convert]::ToBase64String($beforeBytes))
    }

    It 'keeps the old output and removes same-directory temp residue when atomic replacement fails' {
        $output = Join-Path $TestDrive 'locked-output.json'
        [System.IO.File]::WriteAllText(
            $output, '{"sentinel":"original"}', [System.Text.UTF8Encoding]::new($false))
        $outputBytes = [System.IO.File]::ReadAllBytes($output)
        $outputHash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
        $manifestHash = (Get-FileHash -LiteralPath $script:Manifest -Algorithm SHA256).Hash
        $lock = [System.IO.FileStream]::new(
            $output,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::None)
        try
        {
            $result = Invoke-P3ProfileGenerator $script:Manifest $output
        }
        finally
        {
            $lock.Dispose()
        }

        $result.ExitCode | Should Not Be 0
        $result.Output | Should Match 'publish.*atomically'
        (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash | Should Be $outputHash
        [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($output)) | Should Be `
            ([Convert]::ToBase64String($outputBytes))
        (Get-FileHash -LiteralPath $script:Manifest -Algorithm SHA256).Hash | Should Be $manifestHash
        @(Get-ChildItem -LiteralPath $TestDrive -Filter '.locked-output.json.*.tmp').Count | Should Be 0
    }

    It 'atomically replaces an existing unlocked output without backup residue' {
        $output = Join-Path $TestDrive 'existing-output.json'
        [System.IO.File]::WriteAllText(
            $output, '{"sentinel":"old"}', [System.Text.UTF8Encoding]::new($false))

        $result = Invoke-P3ProfileGenerator $script:Manifest $output

        $result.ExitCode | Should Be 0
        $profile = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
        $profile.schemaVersion | Should Be 2
        @(Get-ChildItem -LiteralPath $TestDrive -Filter '.existing-output.json.*.tmp').Count | Should Be 0
        @(Get-ChildItem -LiteralPath $TestDrive -Filter '.existing-output.json.*.bak').Count | Should Be 0
    }

    It 'uses durable same-directory temporary publication without a production failure hook' {
        $source = [System.IO.File]::ReadAllText($script:Generator)

        $source.IndexOf('[System.IO.Path]::GetFullPath($ManifestPath)') | Should BeLessThan `
            $source.IndexOf('Get-Content -LiteralPath $ManifestPath')
        $source | Should Match 'OrdinalIgnoreCase\.Equals'
        $source | Should Match '\[System\.IO\.FileStream\].*WriteThrough'
        $source | Should Match '\.Flush\(\$true\)'
        $source | Should Match '\[System\.IO\.File\]::Replace'
        $source | Should Match '\[System\.IO\.File\]::Move'
        $source | Should Not Match 'Inject|TestHook|FailureHook'
    }
}

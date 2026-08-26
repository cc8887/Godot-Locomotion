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
        (@($profile.PSObject.Properties.Name) -join ',') | Should Be `
            'schemaVersion,mannequin,standingIdle,crouchingIdle,standingSamples,crouchingSamples,jumpStart,fallLoop,land,leanAdditive'
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
        $source | Should Not Match '\.assetName|AssetName|Split-Path.*-Leaf|GetFileName'
    }
}

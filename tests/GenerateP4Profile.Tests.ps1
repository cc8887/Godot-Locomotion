$script:RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:Generator = Join-Path $script:RepositoryRoot 'scripts\generate-p4-profile.ps1'
$script:Manifest = Join-Path $script:RepositoryRoot 'assets\generated\als_v4\als_manifest.json'

function Invoke-P4Generator([string]$ManifestPath, [string]$OutputPath)
{
    $output = @(& pwsh -NoProfile -File $script:Generator -ManifestPath $ManifestPath -OutputPath $OutputPath 2>&1)
    [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output -join [Environment]::NewLine) }
}

function Write-Fixture([object]$Value, [string]$Path)
{
    [System.IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 100), [System.Text.UTF8Encoding]::new($false))
}

Describe 'generate-p4-profile strict mapping and publication' {
    It 'resolves all exact source paths and emits deterministic tracked bytes' {
        $first = Join-Path $TestDrive 'first.json'
        $second = Join-Path $TestDrive 'second.json'

        $one = Invoke-P4Generator $script:Manifest $first
        $two = Invoke-P4Generator $script:Manifest $second

        $one.ExitCode | Should Be 0
        $two.ExitCode | Should Be 0
        (Get-FileHash $first -Algorithm SHA256).Hash | Should Be (Get-FileHash $second -Algorithm SHA256).Hash
        [Convert]::ToBase64String([IO.File]::ReadAllBytes($first)) | Should Be `
            ([Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $script:RepositoryRoot 'assets\config\p4_pose_profile.json'))))
        $profile = Get-Content -Raw $first | ConvertFrom-Json
        $profile.schemaVersion | Should Be 1
        @($profile.turns).Count | Should Be 8
        @($profile.rotates).Count | Should Be 4
        @($profile.masks).Count | Should Be 11
        @($profile.turns | Where-Object { $_.basePlayRate -eq 1.2 -and $_.blendSeconds -eq 0.2 -and $_.scaleAngle }).Count | Should Be 8
        @($profile.masks.root) -join ',' | Should Be 'spine_01,neck_01,clavicle_l,clavicle_r,Hand_L,hand_r,Pelvis,Thigh_L,Thigh_R,Foot_L,Foot_R'
    }

    It 'rejects zero and multiple exact object path matches without basename fallback' {
        $manifest = Get-Content -Raw $script:Manifest | ConvertFrom-Json
        $target = '/Game/AdvancedLocomotionV4/CharacterAssets/MannequinSkeleton/AnimationExamples/AimOffsets/ALS_N_Look_D_Sweep.ALS_N_Look_D_Sweep'
        $asset = @($manifest.animations | Where-Object objectPath -CEQ $target)[0]
        $asset.objectPath = '/Game/Other/ALS_N_Look_D_Sweep.ALS_N_Look_D_Sweep'
        $missing = Join-Path $TestDrive 'missing.json'
        Write-Fixture $manifest $missing
        (Invoke-P4Generator $missing (Join-Path $TestDrive 'missing-output.json')).ExitCode | Should Not Be 0

        $manifest = Get-Content -Raw $script:Manifest | ConvertFrom-Json
        $manifest.animations = @($manifest.animations) + @($manifest.animations | Where-Object objectPath -CEQ $target | Select-Object -First 1)
        $duplicate = Join-Path $TestDrive 'duplicate.json'
        Write-Fixture $manifest $duplicate
        (Invoke-P4Generator $duplicate (Join-Path $TestDrive 'duplicate-output.json')).ExitCode | Should Not Be 0
    }

    It 'keeps the old output and cleans same-directory temp files when replace fails' {
        $output = Join-Path $TestDrive 'locked.json'
        [IO.File]::WriteAllText($output, '{"sentinel":true}')
        $before = (Get-FileHash $output -Algorithm SHA256).Hash
        $lock = [IO.FileStream]::new($output, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
        try { $result = Invoke-P4Generator $script:Manifest $output } finally { $lock.Dispose() }

        $result.ExitCode | Should Not Be 0
        (Get-FileHash $output -Algorithm SHA256).Hash | Should Be $before
        @(Get-ChildItem $TestDrive -Filter '.locked.json.*.tmp').Count | Should Be 0
    }

    It 'uses same-directory durable atomic publication and no failure hook' {
        $source = [IO.File]::ReadAllText($script:Generator)
        $source | Should Match '\[System\.IO\.FileStream\].*WriteThrough'
        $source | Should Match '\.Flush\(\$true\)'
        $source | Should Match '\[System\.IO\.File\]::Replace'
        $source | Should Match '\[System\.IO\.File\]::Move'
        $source | Should Not Match 'Inject|TestHook|FailureHook'
    }
}

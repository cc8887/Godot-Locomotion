$script:RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:PrepareScript = Join-Path $script:RepositoryRoot 'scripts\prepare-p3-reference.ps1'

function New-P3ReferenceFixture
{
    param(
        [string]$Origin,
        [switch]$ExternalFileLink,
        [switch]$InternalPatch,
        [switch]$AtomicFailurePatches
    )

    $fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) "godot-als-p3-reference-$([guid]::NewGuid().ToString('N'))"
    $referenceRoot = Join-Path $fixtureRoot 'reference-repository'
    $projectRoot = Join-Path $fixtureRoot 'project'
    $projectPatchRoot = Join-Path $projectRoot 'reference\patches'
    New-Item -ItemType Directory -Path $referenceRoot, $projectPatchRoot | Out-Null

    & git -C $referenceRoot init --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize the reference fixture repository.' }
    & git -C $referenceRoot config user.email 'tests@example.invalid'
    & git -C $referenceRoot config user.name 'P3 Reference Tests'
    & git -C $referenceRoot config core.autocrlf false
    & git -C $referenceRoot config core.symlinks true
    & git -C $referenceRoot remote add origin $Origin

    Set-Content -LiteralPath (Join-Path $referenceRoot 'seed.txt') -Value 'fixture' -NoNewline
    $targetPath = Join-Path $referenceRoot 'target.txt'
    $secondTargetPath = Join-Path $referenceRoot 'target-two.txt'
    Set-Content -LiteralPath $targetPath -Value 'before' -NoNewline
    Set-Content -LiteralPath $secondTargetPath -Value 'before-two' -NoNewline
    & git -C $referenceRoot add seed.txt target.txt target-two.txt
    & git -C $referenceRoot commit --quiet -m 'fixture seed'
    if ($LASTEXITCODE -ne 0) { throw 'Could not commit the reference fixture seed.' }

    $patches = @()
    if ($ExternalFileLink)
    {
        $outsideRoot = Join-Path $fixtureRoot 'outside'
        $outsideFile = Join-Path $outsideRoot 'external.patch'
        New-Item -ItemType Directory -Path $outsideRoot | Out-Null
        Set-Content -LiteralPath $outsideFile -Value 'external patch' -NoNewline

        $linkPath = Join-Path $projectPatchRoot 'linked.patch'
        New-Item -ItemType SymbolicLink -Path $linkPath -Target $outsideFile | Out-Null

        $patches = @(
            [ordered]@{
                path = 'reference/patches/linked.patch'
                sha256 = (Get-FileHash -LiteralPath $outsideFile -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        )
    }
    elseif ($InternalPatch)
    {
        Set-Content -LiteralPath $targetPath -Value 'after' -NoNewline
        $patchPath = Join-Path $projectPatchRoot 'change.patch'
        & git -C $referenceRoot diff "--output=$patchPath" -- target.txt
        if ($LASTEXITCODE -ne 0) { throw 'Could not generate the reference fixture patch.' }
        Set-Content -LiteralPath $targetPath -Value 'before' -NoNewline

        $patches = @(
            [ordered]@{
                path = 'reference/patches/change.patch'
                sha256 = (Get-FileHash -LiteralPath $patchPath -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        )
    }
    elseif ($AtomicFailurePatches)
    {
        Set-Content -LiteralPath $targetPath -Value 'after' -NoNewline
        $firstPatchPath = Join-Path $projectPatchRoot 'first.patch'
        & git -C $referenceRoot diff "--output=$firstPatchPath" -- target.txt
        if ($LASTEXITCODE -ne 0) { throw 'Could not generate the first atomicity fixture patch.' }
        Set-Content -LiteralPath $targetPath -Value 'before' -NoNewline

        Set-Content -LiteralPath $secondTargetPath -Value 'after-two' -NoNewline
        $secondPatchPath = Join-Path $projectPatchRoot 'second.patch'
        & git -C $referenceRoot diff "--output=$secondPatchPath" -- target-two.txt
        if ($LASTEXITCODE -ne 0) { throw 'Could not generate the second atomicity fixture patch.' }
        Set-Content -LiteralPath $secondTargetPath -Value 'diverged-two' -NoNewline

        & git -C $referenceRoot add target-two.txt
        & git -C $referenceRoot commit --quiet -m 'fixture atomicity patches'
        if ($LASTEXITCODE -ne 0) { throw 'Could not commit the atomicity fixture patches.' }

        $patches = @(
            [ordered]@{
                path = 'reference/patches/first.patch'
                sha256 = (Get-FileHash -LiteralPath $firstPatchPath -Algorithm SHA256).Hash.ToLowerInvariant()
            },
            [ordered]@{
                path = 'reference/patches/second.patch'
                sha256 = (Get-FileHash -LiteralPath $secondPatchPath -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        )
    }

    $commit = (& git -C $referenceRoot rev-parse HEAD).Trim()
    $lock = [ordered]@{
        schemaVersion = 1
        repository = 'https://github.com/Sixze/ALS-Refactored.git'
        commit = $commit
        observedDate = '2026-08-26'
        targetEngine = '5.9.0'
        compatibilityPatches = $patches
    }
    $lock | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $projectRoot 'reference\als-refactored.lock.json')

    [pscustomobject]@{
        FixtureRoot = $fixtureRoot
        ProjectRoot = $projectRoot
        ReferenceRoot = $referenceRoot
        TargetPath = $targetPath
        SecondTargetPath = $secondTargetPath
        Commit = $commit
    }
}

Describe 'prepare-p3-reference.ps1' {
    It 'rejects a repository-internal link to an external patch file' {
        $fixture = New-P3ReferenceFixture -Origin 'https://github.com/Sixze/ALS-Refactored.git' -ExternalFileLink
        try
        {
            $output = @(& pwsh -NoProfile -File $script:PrepareScript -ReferenceRoot $fixture.ReferenceRoot -ProjectRoot $fixture.ProjectRoot 2>&1)
            $exitCode = $LASTEXITCODE

            $exitCode | Should Not Be 0
            ($output -join "`n") | Should Match 'reparse point'
        }
        finally
        {
            Remove-Item -LiteralPath $fixture.FixtureRoot -Recurse -Force
        }
    }

    It 'rejects an origin with an extra trailing slash' {
        $fixture = New-P3ReferenceFixture -Origin 'https://github.com/Sixze/ALS-Refactored.git/'
        try
        {
            $output = @(& pwsh -NoProfile -File $script:PrepareScript -ReferenceRoot $fixture.ReferenceRoot -ProjectRoot $fixture.ProjectRoot 2>&1)
            $exitCode = $LASTEXITCODE

            $exitCode | Should Not Be 0
            ($output -join "`n") | Should Match 'origin mismatch'
        }
        finally
        {
            Remove-Item -LiteralPath $fixture.FixtureRoot -Recurse -Force
        }
    }

    It 'applies a validated non-empty patch and reports the exact marker' {
        $fixture = New-P3ReferenceFixture -Origin 'https://github.com/Sixze/ALS-Refactored.git' -InternalPatch
        try
        {
            $output = @(& pwsh -NoProfile -File $script:PrepareScript -ReferenceRoot $fixture.ReferenceRoot -ProjectRoot $fixture.ProjectRoot 2>&1)
            $exitCode = $LASTEXITCODE

            if ($exitCode -ne 0)
            {
                throw "prepare-p3-reference.ps1 failed unexpectedly: $($output -join "`n")"
            }

            $exitCode | Should Be 0
            $output.Count | Should Be 1
            $output[0].ToString() | Should Be "P3_REFERENCE_OK commit=$($fixture.Commit) patches=1"
            (Get-Content -LiteralPath $fixture.TargetPath -Raw) | Should Be 'after'

            $secondOutput = @(& pwsh -NoProfile -File $script:PrepareScript -ReferenceRoot $fixture.ReferenceRoot -ProjectRoot $fixture.ProjectRoot 2>&1)
            $LASTEXITCODE | Should Be 0
            $secondOutput.Count | Should Be 1
            $secondOutput[0].ToString() | Should Be "P3_REFERENCE_OK commit=$($fixture.Commit) patches=1"
            (Get-Content -LiteralPath $fixture.TargetPath -Raw) | Should Be 'after'
        }
        finally
        {
            Remove-Item -LiteralPath $fixture.FixtureRoot -Recurse -Force
        }
    }

    It 'rejects unrelated dirt when the locked patch is already applied' {
        $fixture = New-P3ReferenceFixture -Origin 'https://github.com/Sixze/ALS-Refactored.git' -InternalPatch
        try
        {
            & pwsh -NoProfile -File $script:PrepareScript -ReferenceRoot $fixture.ReferenceRoot -ProjectRoot $fixture.ProjectRoot | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Initial patch preparation failed unexpectedly.' }
            Set-Content -LiteralPath (Join-Path $fixture.ReferenceRoot 'seed.txt') -Value 'unrelated' -NoNewline

            $output = @(& pwsh -NoProfile -File $script:PrepareScript -ReferenceRoot $fixture.ReferenceRoot -ProjectRoot $fixture.ProjectRoot 2>&1)
            $LASTEXITCODE | Should Not Be 0
            ($output -join "`n") | Should Match 'differs from the exact locked compatibility patch result'
        }
        finally
        {
            Remove-Item -LiteralPath $fixture.FixtureRoot -Recurse -Force
        }
    }

    It 'does not partially apply validated patches when a later patch cannot apply' {
        $fixture = New-P3ReferenceFixture -Origin 'https://github.com/Sixze/ALS-Refactored.git' -AtomicFailurePatches
        try
        {
            $output = @(& pwsh -NoProfile -File $script:PrepareScript -ReferenceRoot $fixture.ReferenceRoot -ProjectRoot $fixture.ProjectRoot 2>&1)
            $exitCode = $LASTEXITCODE

            $exitCode | Should Not Be 0
            ($output -join "`n") | Should Match 'Could not apply compatibility patch'
            (Get-Content -LiteralPath $fixture.TargetPath -Raw) | Should Be 'before'
            (Get-Content -LiteralPath $fixture.SecondTargetPath -Raw) | Should Be 'diverged-two'
            @(& git -C $fixture.ReferenceRoot status --porcelain).Count | Should Be 0
        }
        finally
        {
            Remove-Item -LiteralPath $fixture.FixtureRoot -Recurse -Force
        }
    }
}

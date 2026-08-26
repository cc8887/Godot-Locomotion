$script:RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:PrepareScript = Join-Path $script:RepositoryRoot 'scripts\prepare-p3-reference.ps1'

function New-P3ReferenceFixture
{
    param(
        [string]$Origin,
        [switch]$ExternalFileLink
    )

    $fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) "godot-als-p3-reference-$([guid]::NewGuid().ToString('N'))"
    $referenceRoot = Join-Path $fixtureRoot 'reference-repository'
    $projectRoot = Join-Path $fixtureRoot 'project'
    New-Item -ItemType Directory -Path $referenceRoot, (Join-Path $projectRoot 'reference') | Out-Null

    & git -C $referenceRoot init --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize the reference fixture repository.' }
    & git -C $referenceRoot config user.email 'tests@example.invalid'
    & git -C $referenceRoot config user.name 'P3 Reference Tests'
    & git -C $referenceRoot config core.symlinks true
    & git -C $referenceRoot remote add origin $Origin

    Set-Content -LiteralPath (Join-Path $referenceRoot 'seed.txt') -Value 'fixture' -NoNewline
    & git -C $referenceRoot add seed.txt
    & git -C $referenceRoot commit --quiet -m 'fixture seed'
    if ($LASTEXITCODE -ne 0) { throw 'Could not commit the reference fixture seed.' }

    $patches = @()
    if ($ExternalFileLink)
    {
        $outsideRoot = Join-Path $fixtureRoot 'outside'
        $outsideFile = Join-Path $outsideRoot 'external.patch'
        New-Item -ItemType Directory -Path $outsideRoot | Out-Null
        Set-Content -LiteralPath $outsideFile -Value 'external patch' -NoNewline

        $linkPath = Join-Path $referenceRoot 'linked.patch'
        New-Item -ItemType SymbolicLink -Path $linkPath -Target $outsideFile | Out-Null
        & git -C $referenceRoot add linked.patch
        & git -C $referenceRoot commit --quiet -m 'fixture link'
        if ($LASTEXITCODE -ne 0) { throw 'Could not commit the reference fixture link.' }

        $patches = @(
            [ordered]@{
                path = 'linked.patch'
                sha256 = (Get-FileHash -LiteralPath $outsideFile -Algorithm SHA256).Hash.ToLowerInvariant()
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
}

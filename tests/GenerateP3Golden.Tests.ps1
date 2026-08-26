$script:RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:GeneratorScript = Join-Path $script:RepositoryRoot 'scripts\generate-p3-golden.ps1'
$script:SchemaPath = Join-Path $script:RepositoryRoot 'tools\schemas\als_locomotion_trace.schema.json'

. $script:GeneratorScript -UnrealEditorCmd 'validation-only' -UProject 'validation-only'

function New-P3GeneratedFixture
{
    $fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) "godot-als-p3-validation-$([guid]::NewGuid().ToString('N'))"
    [void][System.IO.Directory]::CreateDirectory($fixtureRoot)
    $settingsPath = Join-Path $script:RepositoryRoot 'assets\config\p3_locomotion_settings.json'
    [System.IO.File]::Copy($settingsPath, (Join-Path $fixtureRoot 'p3_locomotion_settings.json'))
    foreach ($trace in Get-ChildItem -Path (Join-Path $script:RepositoryRoot 'tests\Als.Core.Tests\Fixtures\P3\trace_*.json'))
    {
        [System.IO.File]::Copy($trace.FullName, (Join-Path $fixtureRoot $trace.Name))
    }
    return $fixtureRoot
}

Describe 'generate-p3-golden.ps1 semantic validation' {
    It 'rejects gait labels without idle-to-moving locomotion evidence' {
        $fixtureRoot = New-P3GeneratedFixture
        try
        {
            $idlePath = Join-Path $fixtureRoot 'trace_idle_gaits.json'
            $idle = Get-Content -LiteralPath $idlePath -Raw | ConvertFrom-Json
            foreach ($frame in $idle.frames)
            {
                $frame.actual.position.x = 0.0
                $frame.actual.position.y = 0.0
                $frame.actual.position.z = 0.0
                $frame.actual.velocity.x = 0.0
                $frame.actual.velocity.y = 0.0
                $frame.actual.velocity.z = 0.0
            }
            [System.IO.File]::WriteAllText($idlePath, ($idle | ConvertTo-Json -Depth 20),
                [System.Text.UTF8Encoding]::new($false))

            $validationError = ''
            try { Validate-GeneratedOutput $fixtureRoot $script:SchemaPath }
            catch { $validationError = $_.Exception.Message }
            $validationError | Should Match 'idle_gaits.*movement evidence'
        }
        finally
        {
            Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
        }
    }

    It 'rejects full running speed for the sub-unit jump input' {
        $fixtureRoot = New-P3GeneratedFixture
        try
        {
            $jumpPath = Join-Path $fixtureRoot 'trace_jump_land.json'
            $jump = Get-Content -LiteralPath $jumpPath -Raw | ConvertFrom-Json
            foreach ($frame in @($jump.frames | Where-Object { $_.index -ge 100 -and $_.index -le 169 }))
            {
                $frame.actual.velocity.x = 3.75
                $frame.actual.velocity.y = 0.0
            }
            [System.IO.File]::WriteAllText($jumpPath, ($jump | ConvertTo-Json -Depth 20),
                [System.Text.UTF8Encoding]::new($false))

            $validationError = ''
            try { Validate-GeneratedOutput $fixtureRoot $script:SchemaPath }
            catch { $validationError = $_.Exception.Message }
            $validationError | Should Match 'jump_land.*analog-limited'
        }
        finally
        {
            Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
        }
    }

    It 'captures dynamic movement limits in every frame' {
        $jump = Get-Content -LiteralPath (Join-Path $script:RepositoryRoot `
            'tests\Als.Core.Tests\Fixtures\P3\trace_jump_land.json') -Raw | ConvertFrom-Json
        (@($jump.frames[0].actual.PSObject.Properties.Name) -contains 'maxAcceleration') | Should Be $true
        (@($jump.frames[0].actual.PSObject.Properties.Name) -contains 'maxBrakingDeceleration') | Should Be $true

        $settings = Get-Content -LiteralPath (Join-Path $script:RepositoryRoot `
            'assets\config\p3_locomotion_settings.json') -Raw | ConvertFrom-Json
        (@($settings.values.PSObject.Properties.Name) -contains 'initialMaxAcceleration') | Should Be $true
        (@($settings.values.PSObject.Properties.Name) -contains 'initialMaxBrakingDeceleration') | Should Be $true
    }
}

Describe 'generate-p3-golden.ps1 filesystem safety' {
    It 'rejects a nested owned-plugin reparse point without changing its external sentinel' {
        $fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) "godot-als-p3-sync-$([guid]::NewGuid().ToString('N'))"
        $repositoryPlugin = Join-Path $fixtureRoot 'repository-plugin'
        $projectDirectory = Join-Path $fixtureRoot 'project'
        $destination = Join-Path $projectDirectory 'Plugins\AlsLocomotionTrace'
        $external = Join-Path $fixtureRoot 'external-sentinel'
        $linkPath = Join-Path $destination 'Source'
        [void][System.IO.Directory]::CreateDirectory((Join-Path $repositoryPlugin 'Source'))
        [void][System.IO.Directory]::CreateDirectory($destination)
        [void][System.IO.Directory]::CreateDirectory($external)
        [System.IO.File]::WriteAllText((Join-Path $repositoryPlugin 'Source\Owned.txt'), 'replacement')
        $sentinelPath = Join-Path $external 'Owned.txt'
        [System.IO.File]::WriteAllText($sentinelPath, 'sentinel')
        [void](New-Item -ItemType Junction -Path $linkPath -Target $external)
        try
        {
            $syncError = ''
            try { Sync-OwnedPlugin $repositoryPlugin $projectDirectory }
            catch { $syncError = $_.Exception.Message }
            $syncError | Should Match 'reparse point'
            [System.IO.File]::ReadAllText($sentinelPath) | Should Be 'sentinel'
        }
        finally
        {
            if (Test-Path -LiteralPath $linkPath) { [System.IO.Directory]::Delete($linkPath) }
            Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
        }
    }

    It 'rolls back all six outputs when publication fails after the third replacement' {
        $fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) "godot-als-p3-publish-$([guid]::NewGuid().ToString('N'))"
        $projectRoot = Join-Path $fixtureRoot 'project'
        $outputRoot = Join-Path $fixtureRoot 'output'
        [void][System.IO.Directory]::CreateDirectory($outputRoot)
        $relativeDestinations = @(
            'assets\config\p3_locomotion_settings.json',
            'tests\Als.Core.Tests\Fixtures\P3\trace_idle_gaits.json',
            'tests\Als.Core.Tests\Fixtures\P3\trace_directions.json',
            'tests\Als.Core.Tests\Fixtures\P3\trace_crouch_clearance.json',
            'tests\Als.Core.Tests\Fixtures\P3\trace_rotation_modes.json',
            'tests\Als.Core.Tests\Fixtures\P3\trace_jump_land.json'
        )
        try
        {
            foreach ($relative in $relativeDestinations)
            {
                $destination = Join-Path $projectRoot $relative
                [void][System.IO.Directory]::CreateDirectory((Split-Path -Parent $destination))
                [System.IO.File]::WriteAllText($destination, "original:$relative")
                [System.IO.File]::WriteAllText((Join-Path $outputRoot (Split-Path -Leaf $relative)), "replacement:$relative")
            }

            $publishError = ''
            try { Publish-P3GeneratedOutputSet $outputRoot $projectRoot -InjectFailureAfter 3 }
            catch { $publishError = $_.Exception.Message }
            $publishError | Should Match 'Injected output publication failure'
            foreach ($relative in $relativeDestinations)
            {
                [System.IO.File]::ReadAllText((Join-Path $projectRoot $relative)) | Should Be "original:$relative"
            }
            @(Get-ChildItem -LiteralPath (Join-Path $projectRoot '.p3-output-transactions') -Force `
                -ErrorAction SilentlyContinue).Count | Should Be 0
        }
        finally
        {
            Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
        }
    }
}

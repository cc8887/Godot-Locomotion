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
    It 'rejects trace content swapped between two valid filenames' {
        $fixtureRoot = New-P3GeneratedFixture
        try
        {
            $directionsPath = Join-Path $fixtureRoot 'trace_directions.json'
            $rotationModesPath = Join-Path $fixtureRoot 'trace_rotation_modes.json'
            $directions = [System.IO.File]::ReadAllText($directionsPath)
            $rotationModes = [System.IO.File]::ReadAllText($rotationModesPath)
            [System.IO.File]::WriteAllText($directionsPath, $rotationModes,
                [System.Text.UTF8Encoding]::new($false))
            [System.IO.File]::WriteAllText($rotationModesPath, $directions,
                [System.Text.UTF8Encoding]::new($false))

            $validationError = ''
            try { Validate-GeneratedOutput $fixtureRoot $script:SchemaPath }
            catch { $validationError = $_.Exception.Message }
            $validationError | Should Match 'trace_(directions|rotation_modes)\.json.*name'
        }
        finally
        {
            Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
        }
    }

    It 'rejects a frame time offset by five tenths of a nanosecond' {
        $fixtureRoot = New-P3GeneratedFixture
        try
        {
            $tracePath = Join-Path $fixtureRoot 'trace_idle_gaits.json'
            $trace = Get-Content -LiteralPath $tracePath -Raw | ConvertFrom-Json
            $trace.frames[1].time = [double]$trace.frames[1].time + 5e-10
            [System.IO.File]::WriteAllText($tracePath, ($trace | ConvertTo-Json -Depth 20),
                [System.Text.UTF8Encoding]::new($false))

            $validationError = ''
            try { Validate-GeneratedOutput $fixtureRoot $script:SchemaPath }
            catch { $validationError = $_.Exception.Message }
            $validationError | Should Match 'Non-deterministic tick/index/time.*frame 1'
        }
        finally
        {
            Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
        }
    }

    It 'rejects the wrong frame count for every named trace' {
        $expectedCounts = [ordered]@{
            idle_gaits = 240
            directions = 240
            crouch_clearance = 210
            rotation_modes = 240
            jump_land = 240
        }
        foreach ($entry in $expectedCounts.GetEnumerator())
        {
            $fixtureRoot = New-P3GeneratedFixture
            try
            {
                $tracePath = Join-Path $fixtureRoot "trace_$($entry.Key).json"
                $trace = Get-Content -LiteralPath $tracePath -Raw | ConvertFrom-Json
                $trace.frames = @($trace.frames | Select-Object -First ($entry.Value - 1))
                [System.IO.File]::WriteAllText($tracePath, ($trace | ConvertTo-Json -Depth 20),
                    [System.Text.UTF8Encoding]::new($false))

                $validationError = ''
                try { Validate-GeneratedOutput $fixtureRoot $script:SchemaPath }
                catch { $validationError = $_.Exception.Message }
                $validationError | Should Match "$($entry.Key).*frame count.*$($entry.Value)"
            }
            finally
            {
                Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
            }
        }
    }

    It 'rejects gait labels without idle-to-moving locomotion evidence' {
        $fixtureRoot = New-P3GeneratedFixture
        try
        {
            $idlePath = Join-Path $fixtureRoot 'trace_idle_gaits.json'
            $idle = Get-Content -LiteralPath $idlePath -Raw | ConvertFrom-Json
            foreach ($frame in $idle.frames)
            {
                $frame.physicalActual.position.x = 0.0
                $frame.physicalActual.position.y = 0.0
                $frame.physicalActual.position.z = 0.0
                $frame.physicalActual.velocity.x = 0.0
                $frame.physicalActual.velocity.y = 0.0
                $frame.physicalActual.velocity.z = 0.0
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
                $frame.physicalActual.velocity.x = 3.75
                $frame.physicalActual.velocity.y = 0.0
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
        (@($jump.frames[0].physicalActual.PSObject.Properties.Name) -contains 'maxAcceleration') | Should Be $true
        (@($jump.frames[0].physicalActual.PSObject.Properties.Name) -contains 'maxBrakingDeceleration') | Should Be $true

        $settings = Get-Content -LiteralPath (Join-Path $script:RepositoryRoot `
            'assets\config\p3_locomotion_settings.json') -Raw | ConvertFrom-Json
        (@($settings.values.PSObject.Properties.Name) -contains 'initialMaxAcceleration') | Should Be $true
        (@($settings.values.PSObject.Properties.Name) -contains 'initialMaxBrakingDeceleration') | Should Be $true
    }

    It 'separates physical evidence native observations and port expectations' {
        $trace = Get-Content -LiteralPath (Join-Path $script:RepositoryRoot `
            'tests\Als.Core.Tests\Fixtures\P3\trace_directions.json') -Raw | ConvertFrom-Json
        $names = @($trace.frames[0].PSObject.Properties.Name)
        ($names -contains 'physicalActual') | Should Be $true
        ($names -contains 'nativeActual') | Should Be $true
        ($names -contains 'portExpected') | Should Be $true
        ($names -contains 'actual') | Should Be $false
        $trace.frames[0].nativeActual.stride | Should Not Be $trace.frames[0].portExpected.stride
    }

    It 'locks native and port landing recovery to their independent windows' {
        $trace = Get-Content -LiteralPath (Join-Path $script:RepositoryRoot `
            'tests\Als.Core.Tests\Fixtures\P3\trace_jump_land.json') -Raw | ConvertFrom-Json
        $nativeFrames = @($trace.frames | Where-Object {
            $_.nativeActual.observedAnimationState -ceq 'LandRecovery'
        } | ForEach-Object { $_.index })
        $portFrames = @($trace.frames | Where-Object {
            $_.portExpected.animationState -ceq 'LandRecovery'
        } | ForEach-Object { $_.index })

        ($nativeFrames -join ',') | Should Be ((81..93) -join ',')
        ($portFrames -join ',') | Should Be ((81..92) -join ',')
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

    It 'keeps the committed plugin after backup cleanup failure and cleans the residue on retry' {
        $fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) "godot-als-p3-plugin-commit-$([guid]::NewGuid().ToString('N'))"
        $repositoryPlugin = Join-Path $fixtureRoot 'repository-plugin'
        $projectDirectory = Join-Path $fixtureRoot 'project'
        $destination = Join-Path $projectDirectory 'Plugins\AlsLocomotionTrace'
        [void][System.IO.Directory]::CreateDirectory((Join-Path $repositoryPlugin 'Source'))
        [void][System.IO.Directory]::CreateDirectory((Join-Path $destination 'Source'))
        [System.IO.File]::WriteAllText((Join-Path $repositoryPlugin 'Source\Owned.txt'), 'new-plugin')
        [System.IO.File]::WriteAllText((Join-Path $destination 'Source\Owned.txt'), 'old-plugin')
        [System.IO.File]::WriteAllText((Join-Path $destination 'Source\OldOnly.txt'), 'old-only')
        try
        {
            $syncError = ''
            try { Sync-OwnedPlugin $repositoryPlugin $projectDirectory -InjectBackupCleanupFailure }
            catch { $syncError = $_.Exception.Message }
            $syncError | Should Match 'committed.*cleanup failure'
            [System.IO.File]::ReadAllText((Join-Path $destination 'Source\Owned.txt')) | Should Be 'new-plugin'
            (Test-Path -LiteralPath (Join-Path $destination 'Source\OldOnly.txt')) | Should Be $false

            $pluginsDirectory = Join-Path $projectDirectory 'Plugins'
            $residualBackups = @(Get-ChildItem -LiteralPath $pluginsDirectory -Directory -Force |
                Where-Object { $_.Name -like '.AlsLocomotionTrace.exchange-backup.*' })
            $residualBackups.Count | Should Be 1
            [System.IO.File]::ReadAllText((Join-Path $residualBackups[0].FullName 'Source\Owned.txt')) |
                Should Be 'old-plugin'

            Sync-OwnedPlugin $repositoryPlugin $projectDirectory
            [System.IO.File]::ReadAllText((Join-Path $destination 'Source\Owned.txt')) | Should Be 'new-plugin'
            @(Get-ChildItem -LiteralPath $pluginsDirectory -Force |
                Where-Object { $_.Name -like '.AlsLocomotionTrace.*backup.*' }).Count | Should Be 0
        }
        finally
        {
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

    It 'keeps all six committed outputs when cleanup fails and recovery removes only residue' {
        $fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) "godot-als-p3-publish-commit-$([guid]::NewGuid().ToString('N'))"
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
            try { Publish-P3GeneratedOutputSet $outputRoot $projectRoot -InjectCleanupFailureAfterCommit }
            catch { $publishError = $_.Exception.Message }
            $publishError | Should Match 'committed.*cleanup failure'
            foreach ($relative in $relativeDestinations)
            {
                [System.IO.File]::ReadAllText((Join-Path $projectRoot $relative)) |
                    Should Be "replacement:$relative"
            }

            $transactionRoot = Join-Path $projectRoot '.p3-output-transactions'
            $transactions = @(Get-ChildItem -LiteralPath $transactionRoot -Directory -Force)
            $transactions.Count | Should Be 1
            $journal = Get-Content -LiteralPath (Join-Path $transactions[0].FullName 'journal.json') -Raw |
                ConvertFrom-Json
            $journal.state | Should Be 'committed'

            Recover-P3OutputTransactions $projectRoot
            foreach ($relative in $relativeDestinations)
            {
                [System.IO.File]::ReadAllText((Join-Path $projectRoot $relative)) |
                    Should Be "replacement:$relative"
            }
            @(Get-ChildItem -LiteralPath $transactionRoot -Force -ErrorAction SilentlyContinue).Count | Should Be 0
        }
        finally
        {
            Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
        }
    }
}

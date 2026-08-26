$script:RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:GeneratorScript = Join-Path $script:RepositoryRoot 'scripts\generate-p3-golden.ps1'
$script:SchemaPath = Join-Path $script:RepositoryRoot 'tools\schemas\als_locomotion_trace.schema.json'

. $script:GeneratorScript -UnrealEditorCmd 'validation-only' -UProject 'validation-only'

Describe 'generate-p3-golden.ps1 semantic validation' {
    It 'rejects gait labels without idle-to-moving locomotion evidence' {
        $fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) "godot-als-p3-validation-$([guid]::NewGuid().ToString('N'))"
        [void][System.IO.Directory]::CreateDirectory($fixtureRoot)
        try
        {
            $settingsPath = Join-Path $script:RepositoryRoot 'assets\config\p3_locomotion_settings.json'
            [System.IO.File]::Copy($settingsPath, (Join-Path $fixtureRoot 'p3_locomotion_settings.json'))
            foreach ($trace in Get-ChildItem -Path (Join-Path $script:RepositoryRoot 'tests\Als.Core.Tests\Fixtures\P3\trace_*.json'))
            {
                [System.IO.File]::Copy($trace.FullName, (Join-Path $fixtureRoot $trace.Name))
            }

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
}

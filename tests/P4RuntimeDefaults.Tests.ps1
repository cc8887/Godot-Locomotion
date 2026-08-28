$script:RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

Describe 'P4 runtime worker entry defaults' {
    $workerPaths = @(
        'src\Als.Godot\Dispatch\AlsVisualWorkerRoot.cs',
        'src\Als.Godot\Locomotion\AlsP3aWorkerRoot.cs',
        'src\Als.Godot\Locomotion\AlsP3WorkerRoot.cs'
    )

    foreach ($workerPath in $workerPaths) {
        It "initializes and validates runtime state in $workerPath" {
            $source = [IO.File]::ReadAllText((Join-Path $script:RepositoryRoot $workerPath))

            $source | Should Match `
                'private AlsRuntimeState _runtimeState = AlsRuntimeState\.CreateDefault\(\);'
            $source | Should Match `
                'AlsRuntimeState\.ValidateP4Defaults\(in _runtimeState\);'
        }
    }
}

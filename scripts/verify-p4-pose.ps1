param(
    [Parameter(Mandatory)]
    [string]$GodotExecutable,
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'
$projectRootPath = (Resolve-Path -LiteralPath $ProjectRoot).Path
$projectPath = Join-Path $projectRootPath 'GodotALS.csproj'
$hadDotnetTiering = Test-Path Env:DOTNET_TieredCompilation
$hadComPlusTiering = Test-Path Env:COMPlus_TieredCompilation
$previousDotnetTiering = [Environment]::GetEnvironmentVariable(
    'DOTNET_TieredCompilation',
    [EnvironmentVariableTarget]::Process)
$previousComPlusTiering = [Environment]::GetEnvironmentVariable(
    'COMPlus_TieredCompilation',
    [EnvironmentVariableTarget]::Process)

function Invoke-P4PoseScene
{
    param(
        [Parameter(Mandatory)]
        [string]$PhaseName,
        [Parameter(Mandatory)]
        [string]$ScenePath
    )

    $output = @(& $GodotExecutable --headless --path $projectRootPath $ScenePath *>&1)
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Host $_ }
    $lines = @($output | ForEach-Object { "$_" })
    if ($exitCode -ne 0)
    {
        throw "$PhaseName exited with code $exitCode."
    }
    $errors = @($lines | Where-Object { $_ -match 'SCRIPT ERROR:|ERROR:' })
    if ($errors.Count -ne 0)
    {
        throw "$PhaseName emitted error output:$([Environment]::NewLine)$($errors -join [Environment]::NewLine)"
    }
    $failures = @($lines | Where-Object {
        $_ -match '(?:\A|\s)GODOT_ALS_[A-Z0-9_]*FAIL(?:\z|\s)'
    })
    if ($failures.Count -ne 0)
    {
        throw "$PhaseName emitted an ALS failure marker:$([Environment]::NewLine)$($failures -join [Environment]::NewLine)"
    }
    return $lines
}

function Assert-P4PoseMarker
{
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [string[]]$Lines,
        [Parameter(Mandatory)]
        [string]$MarkerName,
        [Parameter(Mandatory)]
        [string]$Pattern
    )

    $candidates = @($Lines | Where-Object {
        $_ -ceq $MarkerName -or $_.StartsWith("$MarkerName ", [StringComparison]::Ordinal)
    })
    $matches = @($candidates | Where-Object {
        [regex]::IsMatch(
            $_,
            $Pattern,
            [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
    })
    if ($candidates.Count -ne 1 -or $matches.Count -ne 1)
    {
        throw "Expected exactly one valid $MarkerName marker; candidates=$($candidates.Count) matches=$($matches.Count)."
    }
}

try
{
    if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf))
    {
        throw "Godot executable not found: $GodotExecutable"
    }

    $env:DOTNET_TieredCompilation = '0'
    $env:COMPlus_TieredCompilation = '0'

    dotnet restore $projectPath
    if ($LASTEXITCODE -ne 0) { throw "P4 Pose restore failed with code $LASTEXITCODE." }
    dotnet build $projectPath -c Debug -p:Optimize=true --no-restore --no-incremental
    if ($LASTEXITCODE -ne 0) { throw "P4 Pose optimized build failed with code $LASTEXITCODE." }

    $graphLines = @(Invoke-P4PoseScene `
        -PhaseName 'P4 animation graph' `
        -ScenePath 'res://scenes/tests/p4_animation_graph_smoke.tscn')
    Assert-P4PoseMarker `
        -Lines $graphLines `
        -MarkerName 'P4_ANIMATION_GRAPH_OK' `
        -Pattern '\AP4_ANIMATION_GRAPH_OK frames=240 advances=240 digest=(?!0000000000000000)[0-9A-F]{16}\z'

    $poseLines = @(Invoke-P4PoseScene `
        -PhaseName 'P4 component pose' `
        -ScenePath 'res://scenes/tests/p4_pose_smoke.tscn')
    Assert-P4PoseMarker `
        -Lines $poseLines `
        -MarkerName 'P4_POSE_TOPOLOGY_PERF' `
        -Pattern '\AP4_POSE_TOPOLOGY_PERF iterations=10000 elapsed_ms=[0-9]+(?:\.[0-9]+)? bones=68 alloc=0B\z'
    Assert-P4PoseMarker `
        -Lines $poseLines `
        -MarkerName 'P4_POSE_ACTIVE_PERF' `
        -Pattern '\AP4_POSE_ACTIVE_PERF iterations=10000 elapsed_ms=[0-9]+(?:\.[0-9]+)? bones=68 affected=[1-9][0-9]* alloc=0B writes=1\z'
    Assert-P4PoseMarker `
        -Lines $poseLines `
        -MarkerName 'P4_POSE_OK' `
        -Pattern '\AP4_POSE_OK aim=(?!0000000000000000)[0-9A-F]{16} turn=(?!0000000000000000)[0-9A-F]{16} rotate=(?!0000000000000000)[0-9A-F]{16} rollback=1 alloc=0B allocation_mode=controlled\z'
    if (@($poseLines | Where-Object {
        $_.StartsWith('P4_POSE_ALLOCATION_UNCONTROLLED', [StringComparison]::Ordinal)
    }).Count -ne 0)
    {
        throw 'Controlled P4 Pose gate emitted an uncontrolled-allocation marker.'
    }

    Write-Host 'P4_POSE_VERIFICATION_OK graph=1 pose=1 zero_alloc=0B active_alloc=0B'
}
finally
{
    if ($hadDotnetTiering)
    {
        [Environment]::SetEnvironmentVariable(
            'DOTNET_TieredCompilation',
            $previousDotnetTiering,
            [EnvironmentVariableTarget]::Process)
    }
    else
    {
        [Environment]::SetEnvironmentVariable(
            'DOTNET_TieredCompilation',
            $null,
            [EnvironmentVariableTarget]::Process)
    }
    if ($hadComPlusTiering)
    {
        [Environment]::SetEnvironmentVariable(
            'COMPlus_TieredCompilation',
            $previousComPlusTiering,
            [EnvironmentVariableTarget]::Process)
    }
    else
    {
        [Environment]::SetEnvironmentVariable(
            'COMPlus_TieredCompilation',
            $null,
            [EnvironmentVariableTarget]::Process)
    }
}

param(
    [Parameter(Mandatory)]
    [string]$GodotExecutable,
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRootPath = (Resolve-Path -LiteralPath $ProjectRoot).Path
$projectPath = Join-Path $projectRootPath 'GodotALS.csproj'
$functionsPath = Join-Path $PSScriptRoot 'p4-verification-functions.ps1'
$hadDotnetTiering = Test-Path Env:DOTNET_TieredCompilation
$hadComPlusTiering = Test-Path Env:COMPlus_TieredCompilation
$previousDotnetTiering = [Environment]::GetEnvironmentVariable(
    'DOTNET_TieredCompilation',
    [EnvironmentVariableTarget]::Process)
$previousComPlusTiering = [Environment]::GetEnvironmentVariable(
    'COMPlus_TieredCompilation',
    [EnvironmentVariableTarget]::Process)

try
{
    if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf))
    {
        throw "Godot executable not found: $GodotExecutable"
    }
    if (-not (Test-Path -LiteralPath $functionsPath -PathType Leaf))
    {
        throw "P4 verification functions not found: $functionsPath"
    }
    . $functionsPath

    & dotnet build $projectPath -c Debug -p:Optimize=true --no-incremental
    if ($LASTEXITCODE -ne 0)
    {
        throw "P4 matrix optimized build failed with code $LASTEXITCODE."
    }

    $env:DOTNET_TieredCompilation = '0'
    $env:COMPlus_TieredCompilation = '0'
    $results = @{}
    $cells = @(
        [pscustomobject]@{ Characters = 1; Mode = 'single' },
        [pscustomobject]@{ Characters = 1; Mode = 'parallel' },
        [pscustomobject]@{ Characters = 10; Mode = 'single' },
        [pscustomobject]@{ Characters = 10; Mode = 'parallel' }
    )
    foreach ($cell in $cells)
    {
        $godotArguments = @(
            '--headless',
            '--path', $projectRootPath,
            'res://scenes/tests/p4_animation_harness.tscn',
            '--',
            "--mode=$($cell.Mode)",
            "--characters=$($cell.Characters)",
            '--warmup=120',
            '--frames=600'
        )
        $output = @(& $GodotExecutable @godotArguments *>&1)
        $exitCode = $LASTEXITCODE
        $lines = @($output | ForEach-Object { "$_" })
        $lines | ForEach-Object { Write-Output $_ }
        if ($exitCode -ne 0)
        {
            throw "P4 matrix cell $($cell.Characters)/$($cell.Mode) exited with code $exitCode. " +
                "Output:$([Environment]::NewLine)$($lines -join [Environment]::NewLine)"
        }
        $parsed = ConvertFrom-P4MatrixOutput `
            -OutputLines $lines `
            -ExpectedMode $cell.Mode `
            -ExpectedCharacterCount $cell.Characters
        $results["$($cell.Characters)/$($cell.Mode)"] = $parsed
    }

    foreach ($characterCount in @(1, 10))
    {
        Assert-P4MatrixPair `
            -Single $results["$characterCount/single"] `
            -Parallel $results["$characterCount/parallel"] `
            -CharacterCount $characterCount
    }
    Write-Output 'P4_MATRIX_VERIFICATION_OK cells=4 pairs=2'
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
        Remove-Item Env:DOTNET_TieredCompilation -ErrorAction SilentlyContinue
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
        Remove-Item Env:COMPlus_TieredCompilation -ErrorAction SilentlyContinue
    }
}

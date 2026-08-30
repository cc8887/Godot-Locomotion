param(
    [Parameter(Mandatory)]
    [string]$GodotExecutable,
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [ValidateRange(1, 3600)]
    [int]$BuildTimeoutSeconds = 120,
    [ValidateRange(1, 3600)]
    [int]$P3InputTimeoutSeconds = 60,
    [ValidateRange(1, 3600)]
    [int]$P4DemoTimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
$projectRootPath = (Resolve-Path -LiteralPath $ProjectRoot).Path
$godotProjectPath = Join-Path $projectRootPath 'GodotALS.csproj'
. (Join-Path $PSScriptRoot 'p4-verification-functions.ps1')

if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf))
{
    throw "Godot executable not found: $GodotExecutable"
}

$build = Invoke-P4VerificationProcess `
    -FilePath 'dotnet' `
    -Arguments @(
        'build', $godotProjectPath, '-c', 'Debug', '-p:Optimize=true',
        '--no-restore', '--no-incremental') `
    -TimeoutSeconds $BuildTimeoutSeconds `
    -Stage 'P4 demo build'
if ($build.ExitCode -ne 0)
{
    throw "P4 demo optimized build failed with code $($build.ExitCode)."
}

$p3Input = Invoke-P4VerificationProcess `
    -FilePath $GodotExecutable `
    -Arguments @(
        '--headless', '--path', $projectRootPath,
        'res://scenes/tests/p3_demo_input_smoke.tscn') `
    -TimeoutSeconds $P3InputTimeoutSeconds `
    -Stage 'P3 input smoke'
$p3Result = ConvertFrom-P3DemoInputOutput `
    -OutputLines $p3Input.OutputLines `
    -ExitCode $p3Input.ExitCode

$p4Demo = Invoke-P4VerificationProcess `
    -FilePath $GodotExecutable `
    -Arguments @(
        '--headless', '--path', $projectRootPath,
        'res://scenes/tests/p4_demo_smoke.tscn', '--', '--als-smoke-frames=300') `
    -TimeoutSeconds $P4DemoTimeoutSeconds `
    -Stage 'P4 demo smoke'
$p4Result = ConvertFrom-P4DemoOutput `
    -OutputLines $p4Demo.OutputLines `
    -ExitCode $p4Demo.ExitCode

Write-Output $p3Result.Marker
Write-Output $p4Result.Marker
Write-Output "P4_DEMO_VERIFICATION_OK frames=$($p4Result.Frames) rigs=$($p4Result.Rigs)"

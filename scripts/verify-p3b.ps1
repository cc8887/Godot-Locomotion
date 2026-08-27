param(
    [Parameter(Mandatory)]
    [string]$GodotExecutable,
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [switch]$SkipRegression
)

$ErrorActionPreference = 'Stop'
$projectRootPath = (Resolve-Path -LiteralPath $ProjectRoot).Path
$godotProjectPath = Join-Path $projectRootPath 'GodotALS.csproj'
$scenePath = 'res://scenes/tests/p3b_animation_harness.tscn'
. (Join-Path $PSScriptRoot 'p3b-verification-functions.ps1')

$env:DOTNET_TieredCompilation = '0'
$env:COMPlus_TieredCompilation = '0'

if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf))
{
    throw "Godot executable not found: $GodotExecutable"
}

dotnet restore $godotProjectPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $SkipRegression)
{
    $pesterResult = Invoke-Pester (Join-Path $projectRootPath 'tests\*.Tests.ps1') -PassThru
    if ($pesterResult.FailedCount -ne 0)
    {
        throw "P3B PowerShell regression suite failed: $($pesterResult.FailedCount) failing test(s)."
    }
}

dotnet build $godotProjectPath -c Debug -p:Optimize=true --no-restore --no-incremental
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

function Invoke-P3bHarness
{
    param(
        [Parameter(Mandatory)]
        [ValidateSet('single', 'parallel')]
        [string]$Mode,
        [Parameter(Mandatory)]
        [ValidateSet(1, 10)]
        [int]$CharacterCount
    )

    $godotOutput = & $GodotExecutable --headless --path $projectRootPath $scenePath -- `
        "--als-mode=$Mode" "--als-characters=$CharacterCount" 2>&1
    $godotExitCode = $LASTEXITCODE
    $godotOutput | ForEach-Object { Write-Host $_ }
    if ($godotExitCode -ne 0)
    {
        throw "Godot P3B harness failed for mode=$Mode characters=$CharacterCount with exit code $godotExitCode."
    }

    return ConvertFrom-P3bHarnessOutput `
        -OutputLines $godotOutput `
        -ExpectedMode $Mode `
        -ExpectedCharacterCount $CharacterCount
}

foreach ($characterCount in @(1, 10))
{
    $single = Invoke-P3bHarness -Mode single -CharacterCount $characterCount
    $parallel = Invoke-P3bHarness -Mode parallel -CharacterCount $characterCount
    Assert-P3bResultPair -Single $single -Parallel $parallel -CharacterCount $characterCount
}

Write-Output 'P3B_VERIFICATION_OK'
exit 0

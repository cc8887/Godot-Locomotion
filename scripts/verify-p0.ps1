param(
    [Parameter(Mandatory)]
    [string]$GodotExecutable,
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'godot-output-functions.ps1')

if (-not (Test-Path -LiteralPath $GodotExecutable -PathType Leaf)) {
    throw "Godot executable not found: $GodotExecutable"
}

dotnet restore (Join-Path $ProjectRoot 'GodotALS.sln')
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet build (Join-Path $ProjectRoot 'GodotALS.sln') --no-restore
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet test (Join-Path $ProjectRoot 'GodotALS.sln') --no-build --no-restore
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$godotOutput = & $GodotExecutable --headless --path $ProjectRoot `
    'res://scenes/tests/headless_smoke.tscn' 2>&1
$godotExitCode = $LASTEXITCODE
$godotOutput | Write-Output

Assert-GodotInvocationSucceeded `
    -OutputLines $godotOutput `
    -ExitCode $godotExitCode `
    -Context 'Godot P0 smoke'

if (-not ($godotOutput -match 'GODOT_ALS_P0_OK')) {
    throw 'Godot smoke marker was not emitted.'
}

Write-Output 'P0_VERIFICATION_OK'
exit 0

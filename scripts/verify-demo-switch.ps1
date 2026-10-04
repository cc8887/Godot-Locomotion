param(
    [string]$GodotExecutable = $env:GODOT_EXECUTABLE,
    [string]$ProjectRoot = (Join-Path $PSScriptRoot '..'),
    [ValidateSet(30, 60, 120)][int[]]$Rates = @(30, 60, 120),
    [switch]$WithCompanions
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common/LocomotionPaths.ps1')
$switchRoot = Resolve-LocomotionPath -Path $ProjectRoot
$switchGodot = Resolve-LocomotionPath -Path $GodotExecutable -EnvironmentVariable 'GODOT_EXECUTABLE'
$switchOutput = Join-Path $switchRoot ('artifacts/demo-switch/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $switchOutput -Force | Out-Null
foreach ($rate in $Rates) {
    foreach ($initial in @('als', 'lyra')) {
        $switchReport = Join-Path $switchOutput "$initial-$rate.json"
        $switchLog = Join-Path $switchOutput "$initial-$rate.log"
        $switchArguments = @('--headless', '--path', $switchRoot, 'res://scenes/tests/demo_locomotion_switch_smoke.tscn',
            '--', "--locomotion=$initial", '--lyra-profile=rifle', "--switch-hz=$rate", "--switch-report=$switchReport")
        if ($WithCompanions) { $switchArguments += '--lyra-characters=10' }
        & $switchGodot @switchArguments *> $switchLog
        $switchExit = $LASTEXITCODE
        $switchText = Get-Content -LiteralPath $switchLog -Raw
        if ($switchExit -ne 0 -or $switchText -notmatch 'DEMO_LOCOMOTION_SWITCH_OK' -or
            $switchText -match '(?m)^\s*(ERROR|WARNING):' -or !(Test-Path -LiteralPath $switchReport)) {
            throw "Demo switch case failed: $initial-$rate; inspect $switchLog"
        }
        $switchData = Get-Content -LiteralPath $switchReport -Raw | ConvertFrom-Json
        if (!$switchData.passed -or $switchData.maxFeetError -gt 0.00001 -or $switchData.maxVelocityError -gt 0.00001 -or
            ($WithCompanions -and $switchData.companionCount -ne 9)) { throw "Invalid switch report: $switchReport" }
        Write-Output "DEMO_SWITCH_CASE_OK $initial-$rate"
    }
}
Write-Output "DEMO_SWITCH_SUITE_OK reports=$switchOutput"

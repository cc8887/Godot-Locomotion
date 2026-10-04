param(
    [string]$GodotExecutable = $env:GODOT_EXECUTABLE,
    [string]$ProjectRoot = (Join-Path $PSScriptRoot '..'),
    [ValidateSet('Smoke', 'Direction', 'Terrain', 'All')][string]$Suite = 'Smoke',
    [ValidateSet(30, 60, 120)][int[]]$Rates = @(60)
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$releaseRoot = [IO.Path]::GetFullPath($ProjectRoot)
if ([string]::IsNullOrWhiteSpace($GodotExecutable)) { throw 'Set GODOT_EXECUTABLE or pass -GodotExecutable.' }
$releaseGodot = (Resolve-Path -LiteralPath $GodotExecutable).Path
$releaseOut = Join-Path $releaseRoot ('artifacts/lyra-release/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $releaseOut -Force | Out-Null
$releaseCases = [Collections.Generic.List[object]]::new()
foreach ($rate in $Rates) {
    if ($Suite -in @('Smoke', 'All')) {
        foreach ($profile in @('unarmed', 'pistol', 'rifle')) {
            $releaseCases.Add(@{ name = "$profile-$rate"; hz = $rate; profile = $profile; extra = @(); terrain = $false })
        }
        $releaseCases.Add(@{ name = "ten-$rate"; hz = $rate; profile = 'rifle';
            extra = @('--lyra-characters=10', '--lyra-main-rebind', '--lyra-main-weapons'); terrain = $false })
    }
    if ($Suite -in @('Direction', 'All')) {
        $releaseCases.Add(@{ name = "direction-short-$rate"; hz = $rate; profile = 'rifle';
            extra = @('--lyra-direction-diagnostic'); terrain = $false; steady = $false })
        $releaseCases.Add(@{ name = "direction-steady-$rate"; hz = $rate; profile = 'rifle';
            extra = @('--lyra-direction-diagnostic', '--lyra-direction-steady'); terrain = $false; steady = $true })
    }
    if ($Suite -in @('Terrain', 'All')) {
        $releaseCases.Add(@{ name = "terrain-$rate"; hz = $rate; profile = 'rifle'; extra = @(); terrain = $true })
    }
}
foreach ($case in $releaseCases) {
    $report = Join-Path $releaseOut "$($case.name).json"
    $log = Join-Path $releaseOut "$($case.name).log"
    $arguments = @('--headless', '--path', $releaseRoot, '--', '--locomotion=lyra', "--lyra-profile=$($case.profile)")
    if ($case.terrain) {
        $arguments += @('--lyra-terrain-smoke', "--lyra-terrain-hz=$($case.hz)", "--lyra-terrain-report=$report")
        $marker = 'LYRA_TERRAIN_COURSE_GODOT_OK'
    } else {
        $arguments += @('--lyra-main-smoke', '--lyra-main-retry', "--lyra-main-hz=$($case.hz)", "--lyra-main-report=$report") + $case.extra
        $marker = if ($case.name.StartsWith('ten-')) { 'LYRA_MAIN_MULTI_DEMO_GODOT_OK' } else { 'LYRA_MAIN_MODEL_GODOT_OK' }
    }
    Write-Host "Running $($case.name)"
    & $releaseGodot @arguments *> $log
    $exitCode = $LASTEXITCODE
    $output = Get-Content -LiteralPath $log -Raw
    if ($exitCode -ne 0 -or -not $output.Contains($marker) -or
        $output -match '(?m)^\s*(ERROR|WARNING):' -or -not (Test-Path -LiteralPath $report)) {
        throw "Lyra release case failed: $($case.name); inspect $log"
    }
    $data = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if ($case.name.StartsWith('direction-')) {
        $index = if ($case.steady) { [int][Math]::Ceiling($case.hz * 3.8) - 1 } else { $case.hz * 2 - 1 }
        $right = $data.directionChanges[$index]
        if ($right.state -ne 2 -or $right.direction -ne 3 -or [Math]::Abs($right.warpAngle * 180 / [Math]::PI) -ge 5) {
            throw "Wrong Rifle right-movement direction: $($case.name)"
        }
        foreach ($row in $data.directionChanges) {
            if ([Math]::Abs($row.relativeForwardX) -gt 0.00001 -or [Math]::Abs($row.relativeForwardY + 1) -gt 0.00001) {
                throw "Wrong ALS component basis: $($case.name) frame=$($row.frame)"
            }
        }
    }
    Write-Host "LYRA_RELEASE_CASE_OK $($case.name)"
}
Write-Host "LYRA_RELEASE_TESTS_OK cases=$($releaseCases.Count) reports=$releaseOut"

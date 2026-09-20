param(
    [Parameter(Mandatory)][ValidateSet(30,60,120)][int]$Hz,
    [Parameter(Mandatory)][string]$GodotExecutable,
    [Parameter(Mandatory)][string]$Prefix,
    [float[]]$Delays = @(0, .15, .3),
    [ValidateSet('walk','run')][string[]]$Gaits = @('walk','run'),
    [switch]$PinToes
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $repo
if ($Prefix -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Prefix must be a simple artifact name.' }
$failed = $false
foreach ($gait in $Gaits) {
    foreach ($delay in $Delays) {
        $delayText = $delay.ToString('0.##', [System.Globalization.CultureInfo]::InvariantCulture)
        $label = "$Prefix-$Hz-$gait-$($delayText.Replace('.', '_'))"
        $directory = Join-Path $repo "artifacts/$label"
        if (Test-Path -LiteralPath $directory) { throw "Refusing to overwrite a capture: $directory" }
        $arguments = @('--path', $repo, '--rendering-method', 'gl_compatibility',
            'res://scenes/tests/p4_movement_visual_smoke.tscn', '--', '--als-mode=parallel',
            '--als-cycle', '--layered-frame', '--foot-ik-frame', '--based-foot-lock',
            '--refactored-pose-curves', '--refactored-movement-curves', '--refactored-foot-frame',
            '--foot-lock-gravity-twist', '--foot-lock-final-contact', '--foot-ground-clearance',
            '--production-graph-capture', '--production-idle-capture', '--sole-contact-trace',
            '--strafe', '--diagnostic-capture', "--hz=$Hz", "--reversal-delay=$delayText",
            "--capture-step=$($Hz / 10)", "--capture-dir=$directory")
        if ($gait -eq 'run') { $arguments += '--run' }
        if ($PinToes) { $arguments += '--foot-contact-toes' }
        & $GodotExecutable @arguments *> "artifacts/$label.log"
        $runtimeCode = $LASTEXITCODE
        if (Test-Path -LiteralPath "$directory/graph.json") {
            & node tools/diagnostics/analyze_movement_sole_stages.mjs $directory artifacts/movement-200-stages "artifacts/$label-report.json" *> "artifacts/$label-analysis.log"
            $analysisCode = $LASTEXITCODE
        } else { $analysisCode = -1 }
        Write-Output "MOVEMENT_MATRIX_CASE label=$label runtime_exit=$runtimeCode analysis_exit=$analysisCode"
        if ($runtimeCode -ne 0 -or $analysisCode -ne 0) { $failed = $true }
    }
}
if ($failed) { exit 1 }

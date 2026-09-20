param(
    [Parameter(Mandatory)][string]$CaptureDirectory,
    [string]$BeforeDirectory,
    [switch]$Strafe
)

$ErrorActionPreference = 'Stop'
$capturePath = (Resolve-Path -LiteralPath $CaptureDirectory).Path
$frames = Get-Content -LiteralPath (Join-Path $capturePath 'frames.json') -Raw | ConvertFrom-Json
if ($frames.Count -ne 720) { throw 'Expected 720 committed frames.' }

function Get-RotationAngle($a, $b) {
    $dot = [Math]::Abs($a.X*$b.X + $a.Y*$b.Y + $a.Z*$b.Z + $a.W*$b.W)
    $length = [Math]::Sqrt(($a.X*$a.X + $a.Y*$a.Y + $a.Z*$a.Z + $a.W*$a.W) *
        ($b.X*$b.X + $b.Y*$b.Y + $b.Z*$b.Z + $b.W*$b.W))
    if ($length -le 0) { throw 'Invalid foot quaternion.' }
    return 2 * [Math]::Acos([Math]::Min([double]1, [double]($dot / $length))) * 180 / [Math]::PI
}

$deltas = for ($index = 1; $index -lt $frames.Count; $index++) {
    if ($frames[$index].Frame -ne $index + 1) { throw 'Non-contiguous frame sequence.' }
    foreach ($side in @('Left', 'Right')) {
        [pscustomobject]@{
            Frame = $index + 1
            Foot = $side
            FinalDegrees = [Math]::Round((Get-RotationAngle $frames[$index].FootPose."${side}FootWorldRotation" $frames[$index - 1].FootPose."${side}FootWorldRotation"), 3)
            ControllerOutputDegrees = [Math]::Round((Get-RotationAngle $frames[$index].FootPose."Uncorrected${side}FootWorldRotation" $frames[$index - 1].FootPose."Uncorrected${side}FootWorldRotation"), 3)
        }
    }
}
$deltas | Sort-Object FinalDegrees -Descending | Select-Object -First 10 | Format-Table
$deltas | Where-Object Frame -eq 668 | Format-Table

Add-Type -AssemblyName System.Drawing
function Write-ContactSheet([string]$Name, [object[]]$Tiles, [int]$Columns, [bool]$Crop) {
    $width = if ($Crop) { 200 } else { 426 }
    $height = if ($Crop) { 284 } else { 240 }
    $bitmap = [System.Drawing.Bitmap]::new($Columns * $width,
        [int][Math]::Ceiling($Tiles.Count / [double]$Columns) * ($height + 24))
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $font = [System.Drawing.Font]::new('Arial', 11)
    try {
        $graphics.Clear([System.Drawing.Color]::FromArgb(30, 30, 30))
        for ($index = 0; $index -lt $Tiles.Count; $index++) {
            $tile = $Tiles[$index]
            $image = [System.Drawing.Image]::FromFile((Join-Path $tile.Directory ('frame-{0:D4}.png' -f $tile.Frame)))
            try {
                $x = ($index % $Columns) * $width
                $y = [int][Math]::Floor($index / [double]$Columns) * ($height + 24)
                $graphics.DrawString("$($tile.Label) frame $($tile.Frame)", $font, [System.Drawing.Brushes]::White, $x + 5, $y + 3)
                $source = if ($Crop) { [System.Drawing.Rectangle]::new(525, 290, 240, 340) }
                    else { [System.Drawing.Rectangle]::new(0, 0, $image.Width, $image.Height) }
                $graphics.DrawImage($image, [System.Drawing.Rectangle]::new($x, $y + 24, $width, $height),
                    $source, [System.Drawing.GraphicsUnit]::Pixel)
            } finally { $image.Dispose() }
        }
        $bitmap.Save((Join-Path $capturePath $Name), [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $font.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
}

$movement = 0..29 | ForEach-Object { @{ Directory = $capturePath; Frame = 186 + $_ * 6; Label = 'After' } }
Write-ContactSheet 'movement-contact-sheet.png' $movement 6 $true
if ($Strafe) {
    $sides = foreach ($frame in @(180, 360, 540)) {
        @{ Directory = $capturePath; Frame = $frame; Label = 'After' }
    }
    Write-ContactSheet 'strafe-directions.png' $sides 3 $true
    foreach ($window in @(@{ Name = 'startup-contact-sheet.png'; Start = 60 },
        @{ Name = 'stopping-contact-sheet.png'; Start = 600 })) {
        $tiles = @(0..11 | ForEach-Object { @{ Directory = $capturePath; Frame = $window.Start + $_ * 6; Label = 'Run' } })
        if (@($tiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $capturePath ('frame-{0:D4}.png' -f $_.Frame))) }).Count -eq 0) {
            Write-ContactSheet $window.Name $tiles 6 $true
        }
    }
}
if ($BeforeDirectory) {
    $beforePath = (Resolve-Path -LiteralPath $BeforeDirectory).Path
    if ($Strafe) {
        Write-ContactSheet 'upper-body-before-after.png' @(
            @{ Directory = $beforePath; Frame = 180; Label = 'Before' },
            @{ Directory = $capturePath; Frame = 180; Label = 'After' }
        ) 2 $true
        return
    }
    $turns = foreach ($directory in @($beforePath, $capturePath)) {
        foreach ($frame in @(576, 600, 666)) {
            @{ Directory = $directory; Frame = $frame; Label = $(if ($directory -eq $beforePath) { 'Before' } else { 'After' }) }
        }
    }
    Write-ContactSheet 'turn-before-after.png' $turns 3 $false
}

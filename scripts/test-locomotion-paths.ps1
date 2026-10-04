$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common/LocomotionPaths.ps1')
$expectedRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location ([IO.Path]::GetTempPath())
try {
    $relative = Resolve-LocomotionPath -Path '../Source Project/Source.uproject'
    $expected = [IO.Path]::GetFullPath((Join-Path $expectedRoot '../Source Project/Source.uproject'))
    if ($relative -cne $expected) { throw 'Relative dependency uses caller cwd.' }
    $absolute = Resolve-LocomotionPath -Path $expected
    if ($absolute -cne $expected) { throw 'Absolute override changed.' }
    $fallback = Resolve-LocomotionPath -Fallback '../UE_5.8'
    if ($fallback -cne [IO.Path]::GetFullPath((Join-Path $expectedRoot '../UE_5.8'))) { throw 'Wrong fallback.' }
    Write-Output 'LOCOMOTION_PORTABLE_PATHS_OK cases=3'
} finally { Pop-Location }

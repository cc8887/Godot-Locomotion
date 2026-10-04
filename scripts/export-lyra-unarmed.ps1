[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EngineRoot,
    [Parameter(Mandatory = $true)][string]$UnrealProject
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$engine = [IO.Path]::GetFullPath($EngineRoot)
$project = [IO.Path]::GetFullPath($UnrealProject)
$editor = Join-Path $engine 'Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
$script = Join-Path $root 'tools\unreal\retarget_export_lyra_unarmed.py'
$timingScript = Join-Path $root 'tools\unreal\export_lyra_unarmed_timing.py'
$notifyScript = Join-Path $root 'tools\unreal\export_lyra_unarmed_notifies.py'
$defaultsScript = Join-Path $root 'tools\unreal\export_lyra_unarmed_layer_defaults.py'
$rootYawScript = Join-Path $root 'tools\unreal\inspect_lyra_root_yaw_defaults.py'
$rootScript = Join-Path $root 'tools\unreal\export_lyra_unarmed_root_motion.py'
$playbackScript = Join-Path $root 'tools\unreal\export_lyra_unarmed_playback.py'
$manifest = Join-Path $root 'tools\unreal\lyra_unarmed_clips.json'
$output = Join-Path $root 'assets\generated\lyra_als'
foreach ($path in @($editor, $project, $script, $timingScript, $notifyScript, $defaultsScript, $rootYawScript, $rootScript, $playbackScript, $manifest)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing required file: $path" }
}
if (-not (Test-Path -LiteralPath $output)) { New-Item -ItemType Directory -Path $output | Out-Null }
$noMcpListener = '-ini:EditorPerProjectUserSettings:[/Script/ModelContextProtocolEngine.ModelContextProtocolSettings]:bAutoStartServer=false'

try {
    [Environment]::SetEnvironmentVariable('LYRA_CLIP_MANIFEST', $manifest, 'Process')
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $output, 'Process')
    $result = & $editor $project '-run=pythonscript' "-Script=$script" `
        $noMcpListener '-DisablePlugins=AnimationData,ModelContextProtocol' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1
    $exit = $LASTEXITCODE
    $markers = @($result | Where-Object { "$_" -match 'LYRA_UNARMED_(CLIP|EXPORT)_OK' })
    if ($exit -ne 0 -or -not (($markers | Out-String).Contains('LYRA_UNARMED_EXPORT_OK clips=21'))) {
        throw "Lyra export failed with exit code $exit.`n$($result | Select-Object -Last 80 | Out-String)"
    }
    $markers

    $timingResult = & $editor $project '-run=pythonscript' "-Script=$timingScript" `
        $noMcpListener '-DisablePlugins=AnimationData,ModelContextProtocol' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1
    $timingExit = $LASTEXITCODE
    $timingMarkers = @($timingResult | Where-Object { "$_" -match 'LYRA_UNARMED_TIMING_(CLIP_)?OK' })
    if ($timingExit -ne 0 -or -not (($timingMarkers | Out-String).Contains('LYRA_UNARMED_TIMING_OK clips=21'))) {
        throw "Lyra timing export failed with exit code $timingExit.`n$($timingResult | Select-Object -Last 80 | Out-String)"
    }
    $timingMarkers

    $notifyResult = & $editor $project '-run=pythonscript' "-Script=$notifyScript" `
        $noMcpListener '-DisablePlugins=AnimationData,ModelContextProtocol' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1
    $notifyExit = $LASTEXITCODE
    $notifyMarkers = @($notifyResult | Where-Object { "$_" -match 'LYRA_UNARMED_NOTIFY_(CLIP_)?OK' })
    if ($notifyExit -ne 0 -or -not (($notifyMarkers | Out-String).Contains('LYRA_UNARMED_NOTIFY_OK clips=21'))) {
        throw "Lyra notify export failed with exit code $notifyExit.`n$($notifyResult | Select-Object -Last 80 | Out-String)"
    }
    $notifyMarkers

    $defaultsResult = & $editor $project '-run=pythonscript' "-Script=$defaultsScript" `
        $noMcpListener '-DisablePlugins=AnimationData,ModelContextProtocol' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1
    $defaultsExit = $LASTEXITCODE
    $defaultsMarkers = @($defaultsResult | Where-Object { "$_" -match 'LYRA_UNARMED_LAYER_DEFAULTS_(CLIP_)?OK' })
    if ($defaultsExit -ne 0 -or -not (($defaultsMarkers | Out-String).Contains('LYRA_UNARMED_LAYER_DEFAULTS_OK layers=2'))) {
        throw "Lyra layer defaults export failed with exit code $defaultsExit.`n$($defaultsResult | Select-Object -Last 80 | Out-String)"
    }
    $defaultsMarkers

    $rootYawResult = & $editor $project '-run=pythonscript' "-Script=$rootYawScript" `
        $noMcpListener '-DisablePlugins=AnimationData,ModelContextProtocol' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1
    $rootYawExit = $LASTEXITCODE
    $rootYawMarkers = @($rootYawResult | Where-Object { "$_" -match 'LYRA_ROOT_YAW_DEFAULTS_OK' })
    if ($rootYawExit -ne 0 -or $rootYawMarkers.Count -eq 0) {
        throw "Lyra root-yaw defaults export failed with exit code $rootYawExit.`n$($rootYawResult | Select-Object -Last 80 | Out-String)"
    }
    $rootYawMarkers

    $rootResult = & $editor $project '-run=pythonscript' "-Script=$rootScript" `
        $noMcpListener '-DisablePlugins=AnimationData,ModelContextProtocol' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1
    $rootExit = $LASTEXITCODE
    $rootMarkers = @($rootResult | Where-Object { "$_" -match 'LYRA_UNARMED_ROOT_(CLIP_)?OK' })
    if ($rootExit -ne 0 -or -not (($rootMarkers | Out-String).Contains('LYRA_UNARMED_ROOT_OK clips=21'))) {
        throw "Lyra root-motion export failed with exit code $rootExit.`n$($rootResult | Select-Object -Last 80 | Out-String)"
    }
    $rootMarkers

    $playbackResult = & $editor $project '-run=pythonscript' "-Script=$playbackScript" `
        $noMcpListener '-DisablePlugins=AnimationData,ModelContextProtocol' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1
    $playbackExit = $LASTEXITCODE
    $playbackMarkers = @($playbackResult | Where-Object { "$_" -match 'LYRA_UNARMED_PLAYBACK_(CLIP_)?OK' })
    if ($playbackExit -ne 0 -or -not (($playbackMarkers | Out-String).Contains('LYRA_UNARMED_PLAYBACK_OK clips=21'))) {
        throw "Lyra playback export failed with exit code $playbackExit.`n$($playbackResult | Select-Object -Last 80 | Out-String)"
    }
    $playbackMarkers
}
finally {
    [Environment]::SetEnvironmentVariable('LYRA_CLIP_MANIFEST', $null, 'Process')
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $null, 'Process')
}

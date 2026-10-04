[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EngineRoot,
    [Parameter(Mandatory = $true)][string]$UnrealProject,
    [ValidateSet('pistol', 'rifle')][string]$Profile = 'pistol',
    [ValidateRange(0, 64)][int]$BatchNew = 2
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$editor = Join-Path ([IO.Path]::GetFullPath($EngineRoot)) 'Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
$project = [IO.Path]::GetFullPath($UnrealProject)
$script = Join-Path $root 'tools\unreal\retarget_export_lyra_pistol.py'
$output = Join-Path $root 'assets\generated\lyra_als'
foreach ($path in @($editor, $project, $script, (Join-Path $output 'linked_layer_inventory.json'))) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing required file: $path" }
}
$noMcpListener = '-ini:EditorPerProjectUserSettings:[/Script/ModelContextProtocolEngine.ModelContextProtocolSettings]:bAutoStartServer=false'
$expectedClips = if ($Profile -eq 'rifle') { 64 } else { 63 }
$consecutiveCompressionCrashes = 0
$clipDirectory = Join-Path $output "animations\$Profile"
try {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $output, 'Process')
    [Environment]::SetEnvironmentVariable('LYRA_ITEM_PROFILE', $Profile, 'Process')
    [Environment]::SetEnvironmentVariable('LYRA_ITEM_BATCH_NEW', "$BatchNew", 'Process')
    for ($attempt = 0; $attempt -le 128; $attempt++) {
        $before = @(Get-ChildItem -LiteralPath $clipDirectory -Filter '*.source.json' -File -ErrorAction SilentlyContinue).Count
        $result = & $editor $project '-run=pythonscript' "-Script=$script" `
            $noMcpListener '-DisablePlugins=AnimationData,ModelContextProtocol' `
            -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1
        $exit = $LASTEXITCODE
        $markers = @($result | Where-Object { "$_" -match "LYRA_ITEM_(BATCH|EXPORT)_OK profile=$Profile " })
        $log = $result | Out-String
        $knownCompressionCrash = $Profile -eq 'rifle' -and $exit -eq 3 -and
            $log.Contains('LogAnimationCompression: Display: Building compressed animation data') -and (
            $log.Contains('Assertion failed: IsRotationNormalized()') -or
            ($log.Contains('BonePose.h] [Line: 664]') -and
                $log.Contains('NaN created in during FTransform Multiplication')) -or
            $log.Contains('Assertion failed: !Bone.ContainsNaN()'))
        if ($knownCompressionCrash) {
            $after = @(Get-ChildItem -LiteralPath $clipDirectory -Filter '*.source.json' -File -ErrorAction SilentlyContinue).Count
            $consecutiveCompressionCrashes = if ($after -gt $before) { 0 } else { $consecutiveCompressionCrashes + 1 }
            if ($consecutiveCompressionCrashes -le 6) {
                Write-Warning "Lyra rifle compression assertion; retrying saved batch ($after/$expectedClips clips, consecutive=$consecutiveCompressionCrashes)."
                continue
            }
        }
        if ($exit -ne 0 -or $markers.Count -ne 1) {
            throw "Lyra $Profile export failed with exit code $exit.`n$($result | Select-Object -Last 100 | Out-String)"
        }
        $consecutiveCompressionCrashes = 0
        $markers
        if (($markers | Out-String).Contains("LYRA_ITEM_EXPORT_OK profile=$Profile clips=$expectedClips")) { return }
    }
    throw "Lyra $Profile export did not finish after 129 batches."
}
finally {
    [Environment]::SetEnvironmentVariable('LYRA_ITEM_BATCH_NEW', $null, 'Process')
    [Environment]::SetEnvironmentVariable('LYRA_ITEM_PROFILE', $null, 'Process')
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $null, 'Process')
}

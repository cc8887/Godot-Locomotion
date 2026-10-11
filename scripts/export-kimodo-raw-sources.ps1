param(
    [Parameter(Mandatory = $true)] [string] $UnrealEditorCmd,
    [Parameter(Mandatory = $true)] [string] $UProjectPath,
    [Parameter(Mandatory = $true)] [string] $ProjectRoot
)
$ErrorActionPreference = 'Stop'

# Robust commandlet driver: UE runs with -log (own on-disk log at GASP58/Saved/Logs)
# and its stdout/stderr are NOT redirected into pipes, so an agent/terminal
# disconnect can never stall the UnrealEditor-Cmd process on a broken pipe.
# Success is judged from UE's own saved log marker GODOT_ALS_KIMODO_RAW_EXPORT_OK
# plus the six on-disk index products.

$requestDirectory = Join-Path $ProjectRoot 'artifacts\kimodo_als'
$outputDirectory = Join-Path $ProjectRoot 'assets\generated\kimodo_als_raw'
$originalRawDirectory = Join-Path $ProjectRoot 'assets\config'
$manifestPath = Join-Path $ProjectRoot 'assets\generated\kimodo_als\als_manifest.json'
$exporterScript = Join-Path $ProjectRoot 'tools\unreal\export_kimodo_raw_sources_driver.py'
$ueLogDirectory = Join-Path (Split-Path -Parent $UProjectPath) 'Saved\Logs'
$ueLogPath = Join-Path $ueLogDirectory 'GASP58.log'

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

[Environment]::SetEnvironmentVariable('KIMODO_RAW_REQUEST_DIR', $requestDirectory, 'Process')
[Environment]::SetEnvironmentVariable('KIMODO_RAW_OUTPUT_DIR', $outputDirectory, 'Process')
[Environment]::SetEnvironmentVariable('KIMODO_ORIGINAL_RAW_DIR', $originalRawDirectory, 'Process')
[Environment]::SetEnvironmentVariable('KIMODO_MANIFEST_PATH', $manifestPath, 'Process')

# Delete stale six-domain products so success cannot be faked by leftovers.
foreach ($name in @('recovery_movement', 'aim', 'overlay', 'ragdoll', 'stop', 'overlay_prop')) {
    $indexPath = Join-Path $outputDirectory ('v4_' + $name + '_source_inputs.json')
    if (Test-Path $indexPath) { Remove-Item -Force $indexPath }
}
$rawSequences = Join-Path $outputDirectory 'raw_sequences'
if (Test-Path $rawSequences) { Remove-Item -Recurse -Force $rawSequences }
New-Item -ItemType Directory -Force -Path $rawSequences | Out-Null

$arguments = @(
    '"' + $UProjectPath + '"',
    '-run=pythonscript',
    '-script="' + $exporterScript + '"',
    '-DisableSplash',
    '-unattended',
    '-nopause',
    '-nosplash',
    '-log',
    '"-DisablePlugins=ModelContextProtocol"'
)

$processStart = New-Object System.Diagnostics.ProcessStartInfo
$processStart.FileName = $UnrealEditorCmd
$processStart.Arguments = ($arguments -join ' ')
$processStart.WorkingDirectory = $ProjectRoot
$processStart.UseShellExecute = $false
$processStart.RedirectStandardOutput = $false
$processStart.RedirectStandardError = $false
$processStart.CreateNoWindow = $true

$process = New-Object System.Diagnostics.Process
$process.StartInfo = $processStart
[void]$process.Start()
$processId = $process.Id
Write-Output ("KIMODO_RAW_EXPORT_UE_STARTED pid=" + $processId)

# Detach from the Process object entirely: do not WaitForExit here. The caller
# (or a future session) can poll the on-disk log and products independently.

Write-Output ("KIMODO_RAW_EXPORT_DRIVER_LAUNCHED ue_log=" + $ueLogPath)

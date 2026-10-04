[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EngineRoot,
    [Parameter(Mandatory = $true)][string]$UnrealProject
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$editor = Join-Path $EngineRoot 'Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
$script = Join-Path $root 'tools\unreal\export_lyra_pose_layer_contracts.py'
$noMcpListener = '-ini:EditorPerProjectUserSettings:[/Script/ModelContextProtocolEngine.ModelContextProtocolSettings]:bAutoStartServer=false'
$previousOutput = [Environment]::GetEnvironmentVariable('LYRA_OUTPUT_ROOT', 'Process')
try {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', (Join-Path $root 'assets\generated\lyra_als'), 'Process')
    $result = & $editor $UnrealProject '-run=pythonscript' "-Script=$script" $noMcpListener `
        '-DisablePlugins=AnimationData,ModelContextProtocol' -unattended -nop4 -nosplash -nullrhi -stdout -FullStdOutLogOutput 2>&1
    $exit = $LASTEXITCODE
    $result | Set-Content -LiteralPath (Join-Path $root 'artifacts\lyra-analysis\pose-layer-contract-ue-full.log') -Encoding utf8
    $markers = @($result | Where-Object { "$_" -match 'LYRA_POSE_LAYER_CONTRACT_OK' })
    $graphErrors = @($result | ForEach-Object {
        if ("$_" -match 'Log\w+: Error: (.*)$') { $Matches[1] }
    } | Sort-Object -Unique)
    $knownAimOnly = $exit -eq 1 -and $markers.Count -eq 1 -and $graphErrors.Count -eq 4 -and
        @($graphErrors | Where-Object { $_ -notmatch "^\[SKIP:LinkedPureExpression\] Node 'K2Node_Knot_[0-3]' feeding 'AnimGraphNode_RotationOffsetBlendSpace_[13]\.[XY]' could not be exported: Pure expression source 'AnimGraphNode_LinkedInputPose_0' \(AnimGraphNode_LinkedInputPose\) feeding pin 'InputPin' cannot be represented by BlueprintLisp$" }).Count -eq 0
    if (($exit -ne 0 -and -not $knownAimOnly) -or $markers.Count -ne 1) {
        $errors = @($result | Where-Object { "$_" -match 'LogPython: Error|Traceback|RuntimeError' })
        throw "Lyra pose-layer export failed ($exit).`n$($errors | Out-String)"
    }
    $markers
    if ($knownAimOnly) { 'LYRA_POSE_LAYER_SCOPE_OK engineExit=1 excludedAimErrors=4' }
}
finally {
    [Environment]::SetEnvironmentVariable('LYRA_OUTPUT_ROOT', $previousOutput, 'Process')
}

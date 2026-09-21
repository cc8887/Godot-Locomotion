param(
    [Parameter(Mandatory=$true)][string]$InputFile,
    [Parameter(Mandatory=$true)][string]$OutputFile,
    [ValidateRange(1,10)][int]$Frames=3,
    [ValidateRange(0,1199)][int]$StartFrame=0
)
$ErrorActionPreference='Stop'
if(-not [IO.Path]::IsPathFullyQualified($OutputFile) -or (Test-Path -LiteralPath $OutputFile)) { throw 'Output must be a new absolute file.' }
$root=[System.Text.Json.Nodes.JsonNode]::Parse([IO.File]::ReadAllText((Resolve-Path -LiteralPath $InputFile)))
foreach($case in $root['cases'].AsArray()) {
    $samples=$case['samples'].AsArray()
    if($samples.Count -le $StartFrame+$Frames) { throw 'Source window is incomplete.' }
    for($frame=$StartFrame+1;$frame -le $StartFrame+$Frames;$frame++) {
        if($samples[$frame]['frame'].GetValue[int]() -ne $frame) { throw 'Source must be the full contiguous native world.' }
        if(-not $samples[$frame].AsObject().ContainsKey('contactsAfterSolve')) { throw 'Source lacks contact diagnostics.' }
    }
    if($samples[$StartFrame]['frame'].GetValue[int]() -ne $StartFrame) { throw 'Source baseline frame differs.' }
    while($samples.Count -gt $StartFrame+$Frames+1) { $samples.RemoveAt($samples.Count-1) }
    for($i=0;$i -lt $StartFrame;$i++) { $samples.RemoveAt(0) }
    foreach($name in @('allSleepFrame','heldSleepingFrames','oneSecondSleepBudget','lastSecondMaxLinear','lastSecondMaxAngular')) {
        [void]$case.AsObject().Remove($name)
    }
}
$root['sourceSha256']=[System.Text.Json.Nodes.JsonNode]::Parse(('"'+(Get-FileHash -LiteralPath $InputFile).Hash+'"'))
$root['windowFrames']=[System.Text.Json.Nodes.JsonNode]::Parse($Frames.ToString([Globalization.CultureInfo]::InvariantCulture))
if($StartFrame -gt 0) { $root['windowStartFrame']=[System.Text.Json.Nodes.JsonNode]::Parse($StartFrame.ToString([Globalization.CultureInfo]::InvariantCulture)) }
$stream=[IO.File]::Open($OutputFile,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write)
try { $bytes=[Text.UTF8Encoding]::new($false).GetBytes($root.ToJsonString());$stream.Write($bytes,0,$bytes.Length) }
finally { $stream.Dispose() }
Write-Output "Extracted frames $StartFrame..$($StartFrame+$Frames); source fingerprint recorded."

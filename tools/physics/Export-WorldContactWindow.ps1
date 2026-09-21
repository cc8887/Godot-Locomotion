param(
    [Parameter(Mandatory=$true)][string]$InputFile,
    [Parameter(Mandatory=$true)][string]$OutputFile,
    [ValidateRange(1,10)][int]$Frames=3
)
$ErrorActionPreference='Stop'
if(-not [IO.Path]::IsPathFullyQualified($OutputFile) -or (Test-Path -LiteralPath $OutputFile)) { throw 'Output must be a new absolute file.' }
$root=[System.Text.Json.Nodes.JsonNode]::Parse([IO.File]::ReadAllText((Resolve-Path -LiteralPath $InputFile)))
foreach($case in $root['cases'].AsArray()) {
    $samples=$case['samples'].AsArray()
    if($samples.Count -le $Frames) { throw 'Source window is incomplete.' }
    for($frame=1;$frame -le $Frames;$frame++) {
        if(-not $samples[$frame].AsObject().ContainsKey('contactsAfterSolve')) { throw 'Source lacks contact diagnostics.' }
    }
    while($samples.Count -gt $Frames+1) { $samples.RemoveAt($samples.Count-1) }
    foreach($name in @('allSleepFrame','heldSleepingFrames','oneSecondSleepBudget','lastSecondMaxLinear','lastSecondMaxAngular')) {
        [void]$case.AsObject().Remove($name)
    }
}
$root['sourceSha256']=[System.Text.Json.Nodes.JsonNode]::Parse(('"'+(Get-FileHash -LiteralPath $InputFile).Hash+'"'))
$root['windowFrames']=[System.Text.Json.Nodes.JsonNode]::Parse($Frames.ToString([Globalization.CultureInfo]::InvariantCulture))
$stream=[IO.File]::Open($OutputFile,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write)
try { $bytes=[Text.UTF8Encoding]::new($false).GetBytes($root.ToJsonString());$stream.Write($bytes,0,$bytes.Length) }
finally { $stream.Dispose() }
Write-Output "Extracted initial $Frames frames; source fingerprint recorded."

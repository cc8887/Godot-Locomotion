param(
    [Parameter(Mandatory)][string]$InputPath,
    [Parameter(Mandatory)][string[]]$Graphs,
    [switch]$History
)
$ErrorActionPreference = 'Stop'
$bridge = Get-Content -LiteralPath $InputPath -Raw | ConvertFrom-Json
$graphSet = if ($History) { $bridge.rotationHistoryGraphs } else { $bridge.graphs }
foreach ($graphName in $Graphs) {
    $graphText = ($graphSet | Where-Object name -eq $graphName).nativeText
    if (-not $graphText) { throw "Missing graph $graphName" }
    $nodes = @{}
    foreach ($nodeMatch in [regex]::Matches($graphText, '(?ms)^   Begin Object Name="([^"]+)"[^\r\n]*\r?\n(.*?)^   End Object')) {
        $nodeName = $nodeMatch.Groups[1].Value
        $body = $nodeMatch.Groups[2].Value
        $member = [regex]::Match($body, '(?:VariableReference|FunctionReference)=\([^\r\n]*MemberName="([^"]+)"').Groups[1].Value
        $pins = @{}
        foreach ($pinMatch in [regex]::Matches($body, 'CustomProperties Pin [^\r\n]+')) {
            $line = $pinMatch.Value
            $pinId = [regex]::Match($line, 'PinId=(\w+)').Groups[1].Value
            $pins[$pinId] = @{
                Name = [regex]::Match($line, 'PinName="([^"]+)"').Groups[1].Value
                Output = $line.Contains('Direction="EGPD_Output"')
                Links = [regex]::Match($line, 'LinkedTo=\(([^)]*)\)').Groups[1].Value
                Default = [regex]::Match($line, '(?:^|,)DefaultValue="([^"]*)"').Groups[1].Value
            }
        }
        $nodes[$nodeName] = @{ Member = $member; Pins = $pins }
    }
    "GRAPH $graphName"
    foreach ($nodeName in ($nodes.Keys | Sort-Object)) {
        $node = $nodes[$nodeName]
        if ($nodeName.StartsWith('EdGraphNode_Comment')) { continue }
        $inputs = foreach ($pin in $node.Pins.Values) {
            if ($pin.Output -or $pin.Name -eq 'self') { continue }
            $targets = foreach ($link in [regex]::Matches($pin.Links, '(\w+) (\w+)')) {
                $other = $nodes[$link.Groups[1].Value]
                "$($link.Groups[1].Value).$($other.Pins[$link.Groups[2].Value].Name)"
            }
            if ($targets) { "$($pin.Name)<-$($targets -join ',')" }
            elseif ($pin.Default) { "$($pin.Name)=$($pin.Default)" }
        }
        "$nodeName [$($node.Member)] $($inputs -join '; ')"
    }
}

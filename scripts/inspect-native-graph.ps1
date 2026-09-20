param(
    [Parameter(Mandatory=$true)][string]$InputJson,
    [Parameter(Mandatory=$true)][string]$Graph
)
$ErrorActionPreference='Stop'
$document=Get-Content -LiteralPath $InputJson -Raw | ConvertFrom-Json
$selected=@($document.graphs | Where-Object name -CEQ $Graph)
if($selected.Count -ne 1) { throw "Expected one exact graph: $Graph" }
$native=$selected[0].nativeText
$classes=@{}
foreach($match in [regex]::Matches($native,'(?m)^   Begin Object Class=/Script/\w+\.(\w+) Name="([^"]+)"')) {
    $classes[$match.Groups[2].Value]=$match.Groups[1].Value
}
$nodes=@(); $pins=@{}
foreach($match in [regex]::Matches($native,'(?ms)^   Begin Object Name="([^"]+)"[^\r\n]*\r?\n(.*?)^   End Object')) {
    $name=$match.Groups[1].Value; $body=$match.Groups[2].Value
    $member=[regex]::Match($body,'(?m)^      (?:VariableReference|FunctionReference)=\([^\r\n]*MemberName="([^"]+)"').Groups[1].Value
    $nodePins=@()
    foreach($line in [regex]::Matches($body,'(?m)^      CustomProperties Pin [^\r\n]+')) {
        $pinName=[regex]::Match($line.Value,'PinName="([^"]+)"').Groups[1].Value
        $id=[regex]::Match($line.Value,'PinId=(\w+)').Groups[1].Value
        $pins[$name+' '+$id]=$name+'.'+$pinName
        $nodePins+=@{name=$pinName; output=$line.Value.Contains('Direction="EGPD_Output"');
            literal=[regex]::Match($line.Value,'(?:^|,)DefaultValue="([^"]*)"').Groups[1].Value;
            links=[regex]::Match($line.Value,'LinkedTo=\(([^)]*)\)').Groups[1].Value}
    }
    $nodes+=@{name=$name; kind=$classes[$name]; member=$member;
        settings=@([regex]::Matches($body,'(?m)^      (?:Node=|FunctionReference=|VariableReference=|Enum=)[^\r\n]+') | ForEach-Object {$_.Value.Trim()}); pins=$nodePins}
}
foreach($node in $nodes) {
    foreach($pin in $node.pins) {
        $pin.links=@([regex]::Matches($pin.links,'(\w+) (\w+),') | ForEach-Object {$pins[$_.Groups[1].Value+' '+$_.Groups[2].Value]})
    }
    $node | ConvertTo-Json -Depth 8 -Compress
}

param(
    [Parameter(Mandatory=$true)][string]$WorldReference,
    [Parameter(Mandatory=$true)][string]$CoupledReference,
    [Parameter(Mandatory=$true)][string]$Report
)
$ErrorActionPreference='Stop'
if(-not [IO.Path]::IsPathFullyQualified($Report) -or (Test-Path -LiteralPath $Report)) {
    throw 'Report must be a new absolute file.'
}
$world=[System.Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText((Resolve-Path -LiteralPath $WorldReference)))
$coupled=[System.Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText((Resolve-Path -LiteralPath $CoupledReference)))
try {
    $rows=@()
    foreach($case in $world.RootElement.GetProperty('cases').EnumerateArray()) {
        $setup=$case.GetProperty('setup');$mesh=$setup.GetProperty('mesh').GetString()
        $hz=$setup.GetProperty('hz').GetInt32();$seen=[Collections.Generic.HashSet[int]]::new()
        foreach($fixture in $coupled.RootElement.GetProperty('cases').EnumerateArray()) {
            $capture=$fixture.GetProperty('capture');$input=$capture.GetProperty('input')
            if($input.GetProperty('mesh').GetString() -ne $mesh) { continue }
            if([Math]::Abs($input.GetProperty('dt').GetDouble()-1.0/$hz) -gt 1e-12) { continue }
            $frame=$input.GetProperty('frame').GetInt32()
            if(-not $seen.Add($frame)) { throw 'Ambiguous capture frame.' }
            $samples=$case.GetProperty('samples')
            if($frame+1 -ge $samples.GetArrayLength()) { throw 'Capture exceeds native trajectory.' }
            $stages=$capture.GetProperty('coreSamples');$last=$stages[$stages.GetArrayLength()-1]
            if($last.GetProperty('stage').GetString() -ne 'corrected') { throw 'Missing final solver stage.' }
            $actual=$last.GetProperty('bodies');$bodies=$input.GetProperty('bodies')
            $expected=$samples[$frame+1].GetProperty('bodies')
            $maxV=0.0;$maxW=0.0;$vBone='';$wBone=''
            for($i=0;$i -lt $expected.GetArrayLength();$i++) {
                $name=$expected[$i].GetProperty('name').GetString()
                if($name -ne $bodies[$i].GetProperty('name').GetString()) { throw 'Body order differs.' }
                $v=0.0;$w=0.0
                for($axis=0;$axis -lt 3;$axis++) {
                    $v+=[Math]::Pow($expected[$i].GetProperty('linearVelocity')[$axis].GetDouble()-$actual[$i].GetProperty('v')[$axis].GetDouble(),2)
                    $w+=[Math]::Pow($expected[$i].GetProperty('angularVelocity')[$axis].GetDouble()-$actual[$i].GetProperty('w')[$axis].GetDouble(),2)
                }
                if([Math]::Sqrt($v) -gt $maxV) { $maxV=[Math]::Sqrt($v);$vBone=$name }
                if([Math]::Sqrt($w) -gt $maxW) { $maxW=[Math]::Sqrt($w);$wBone=$name }
            }
            $rows += [pscustomobject]@{mesh=$mesh;hz=$hz;completedStep=$frame+1;maxLinearDifferenceCmPerSecond=$maxV;
                linearBone=$vBone;maxAngularDifferenceRadPerSecond=$maxW;angularBone=$wBone}
        }
        if($seen.Count -eq 0) { throw "No matching captures for $mesh/$hz." }
    }
    $result=[ordered]@{schemaVersion=1;observation='Saved world velocity differences, no trajectory parity asserted. Caller must supply matching initial conditions.';
        worldSha256=(Get-FileHash -LiteralPath $WorldReference).Hash;coupledSha256=(Get-FileHash -LiteralPath $CoupledReference).Hash;
        samples=@($rows | Sort-Object mesh,completedStep)}
    $stream=[IO.File]::Open($Report,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write)
    try { $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($result | ConvertTo-Json -Depth 8));$stream.Write($bytes,0,$bytes.Length) }
    finally { $stream.Dispose() }
    Write-Output "Compared $($rows.Count) saved steps; report=$Report"
}
finally { $world.Dispose();$coupled.Dispose() }

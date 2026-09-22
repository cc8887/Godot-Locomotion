param(
    [Parameter(Mandatory=$true)][string]$InputFile,
    [Parameter(Mandatory=$true)][string]$OutputFile
)
$ErrorActionPreference='Stop'
if(-not [IO.Path]::IsPathFullyQualified($OutputFile) -or (Test-Path -LiteralPath $OutputFile)) {throw 'Output must be a new absolute file.'}
$native=Get-Content -LiteralPath $InputFile -Raw|ConvertFrom-Json
if($native.schemaVersion -ne 1 -or $native.cases.Count -eq 0) {throw 'Invalid native coupled reference.'}
$rows=foreach($case in $native.cases) {
    if($case.jointGather.Count -ne $case.capture.input.joints.Count) {throw 'Missing or incomplete joint Gather observations.'}
    [ordered]@{input=$case.capture.input;nativeJoints=$case.jointGather}
}
$result=[ordered]@{schemaVersion=1;engine=$native.engine;sourceSha256=(Get-FileHash -LiteralPath $InputFile).Hash;
    observation='Native joint conditioning/inertia utilities and independent linear container response from zero corrections; angular rows/drives disabled only in copied diagnostic solvers; original coupled replay unchanged.';
    cases=@($rows)}
$stream=[IO.File]::Open($OutputFile,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write)
try {$bytes=[Text.UTF8Encoding]::new($false).GetBytes(($result|ConvertTo-Json -Depth 64 -Compress));$stream.Write($bytes,0,$bytes.Length)}
finally {$stream.Dispose()}
Write-Output "Extracted $($rows.Count) native joint Gather cases."

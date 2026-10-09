param(
    [Parameter(Mandatory=$true)][string]$KimodoRoot,
    [Parameter(Mandatory=$true)][string]$GodotExecutable,
    [string]$Version = (Get-Date -Format 'yyyyMMdd_HHmmss'),
    [string]$Bindings,
    [string]$DotnetExecutable
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
Set-Location $projectRoot
if($Version -notmatch '^[A-Za-z0-9_]+$'){throw 'Version must contain only letters, digits and underscores'}
$python=Join-Path $KimodoRoot '.venv/Scripts/python.exe'
if(!$DotnetExecutable){$DotnetExecutable=Join-Path $env:USERPROFILE '.dotnet/dotnet.exe'}
if(!(Test-Path -LiteralPath $DotnetExecutable)){$DotnetExecutable=(Get-Command dotnet).Source}
$work=Join-Path $projectRoot ('artifacts/kimodo_als/non-prop-'+$Version)
$pack=Join-Path $projectRoot ('assets/generated/als_nonprop_'+$Version)
if((Test-Path -LiteralPath $work) -or (Test-Path -LiteralPath $pack)){throw 'Use a fresh version and preserve previous evidence'}
$deps=Join-Path $projectRoot 'artifacts/kimodo_als/offline-python-deps'
if(!(Test-Path -LiteralPath (Join-Path $deps 'ufbx/__init__.py'))){
    $uv=(Get-Command uv).Source
    & $uv pip install --python $python --target $deps --no-deps --only-binary :all: 'ufbx==0.0.5'
    if($LASTEXITCODE -ne 0){throw 'Offline readback dependency installation failed'}
}
New-Item -ItemType Directory -Path $work | Out-Null
$arguments=@('tools/produce_als_resources.py','run','--scope','non-prop','--keep-going',
    '--rules','tools/kimodo_pipeline/directional_gait_rules.json','--kimodo-repo',$KimodoRoot,
    '--python',$python,'--output',(Join-Path $work 'authoring'),'--pack-output',$pack,
    '--godot',$GodotExecutable,'--dotnet',$DotnetExecutable,'--offline-deps',$deps)
if($Bindings){$arguments+=@('--bindings',$Bindings)}
& $python @arguments *> (Join-Path $work 'full-pipeline.log')
$pipelineExit=$LASTEXITCODE
if($pipelineExit -ne 0){Write-Output ('ALS_PIPELINE_REJECTED: reports retained under '+$work)}
else{Write-Output ('ALS_PIPELINE_COMPLETE: '+$pack+' (activation separately gated)')}
exit $pipelineExit

param([ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$EvidenceTag='build')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$mathRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$mathOutput=Join-Path $mathRoot "artifacts/native-math/$EvidenceTag"
if(Test-Path -LiteralPath $mathOutput){throw 'Preserve native math build evidence.'}
$mathVswhere="${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
$mathInstall=& $mathVswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if($LASTEXITCODE -ne 0 -or -not $mathInstall){throw 'MSVC x64 build tools are required for the Win64 numerical bridge.'}
$mathVars=Join-Path $mathInstall 'VC/Auxiliary/Build/vcvars64.bat'
$mathSource=Join-Path $mathRoot 'tools/native/LyraNativeMath.cpp'
New-Item -ItemType Directory -Path $mathOutput | Out-Null
Push-Location $mathOutput
try {
    & cmd /d /c "`"$mathVars`" >nul && cl /nologo /O2 /fp:precise /LD /MT `"$mathSource`" /link /out:LyraNativeMath.dll" *> build.log
    if($LASTEXITCODE -ne 0){throw 'Native math compilation failed; preserve build.log.'}
} finally {Pop-Location}
$mathAsset=Join-Path $mathRoot 'assets/generated/lyra_als/native_win64'
New-Item -ItemType Directory -Path $mathAsset -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $mathOutput 'LyraNativeMath.dll') -Destination $mathAsset
Get-FileHash -LiteralPath (Join-Path $mathAsset 'LyraNativeMath.dll') -Algorithm SHA256

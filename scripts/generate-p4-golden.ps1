param(
    [string]$UnrealEditorCmd,
    [Alias('UProject')]
    [string]$UnrealProject,
    [string]$ReferenceRoot = '../GodotALS-References\ALS-Refactored',
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$lockedCommit = 'b754d6f0f2bb03741d301f8fb88077ebfe561e17'
$lockedPatchHash = '3dc561f194045d3dc01bd65c7f7c3bd4acd0a30c0fab31ea0cd16d676d312e5f'
$expectedNames = @(
    'trace_p4_aim.json',
    'trace_p4_feet.json',
    'trace_p4_platform.json',
    'trace_p4_rotate.json',
    'trace_p4_turn.json')
$expectedCaseIds = @(
    'aim_center', 'aim_up', 'aim_down', 'aim_left', 'aim_right',
    'turn_standing_left_90', 'turn_standing_right_90', 'turn_standing_left_180', 'turn_standing_right_180',
    'turn_crouching_left_90', 'turn_crouching_right_90', 'turn_crouching_left_180', 'turn_crouching_right_180',
    'rotate_standing_left', 'rotate_standing_right', 'rotate_crouching_left', 'rotate_crouching_right',
    'feet_flat', 'feet_slope', 'feet_stairs',
    'platform_translate', 'platform_rotate', 'platform_base_change', 'platform_teleport', 'platform_release')
$script:p4SchemaValidatorPath = $null

function Get-FullPath([string]$Path)
{
    [System.IO.Path]::GetFullPath($Path).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
}

function Assert-P4ReferenceLock([string]$Root, [string]$Reference)
{
    $lockPath = Join-Path $Root 'reference\als-refactored.lock.json'
    $lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
    if ($lock.repository -cne 'https://github.com/Sixze/ALS-Refactored.git' -or
        $lock.commit -cne $lockedCommit -or $lock.targetEngine -cne '5.9.0' -or
        @($lock.compatibilityPatches).Count -ne 1 -or
        $lock.compatibilityPatches[0].sha256 -cne $lockedPatchHash)
    {
        throw 'P4 reference lock does not match the approved repository, commit, engine, and patch.'
    }

    $patchPath = Get-FullPath (Join-Path $Root ([string]$lock.compatibilityPatches[0].path))
    $actualPatchHash = (Get-FileHash -LiteralPath $patchPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualPatchHash -cne $lockedPatchHash)
    {
        throw "P4 compatibility patch SHA256 mismatch: $actualPatchHash"
    }

    $head = (& git -C $Reference rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $head -cne $lockedCommit)
    {
        throw "P4 reference HEAD mismatch: $head"
    }

    & git -C $Reference symbolic-ref -q HEAD 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0)
    {
        throw 'P4 reference must remain at a detached HEAD.'
    }
}

function Invoke-ReferencePreparation([string]$Root, [string]$Reference)
{
    $output = @(& (Join-Path $Root 'scripts\prepare-p3-reference.ps1') `
        -ReferenceRoot $Reference -ProjectRoot $Root)
    $marker = "P3_REFERENCE_OK commit=$lockedCommit patches=1"
    if ($LASTEXITCODE -ne 0 -or $output.Count -ne 1 -or $output[0] -cne $marker)
    {
        throw "P4 reference preparation did not return exact marker '$marker'."
    }
}

function Invoke-P3ReadyCheck(
    [string]$Root,
    [string]$Editor,
    [string]$Project,
    [string]$Reference)
{
    $output = @(& (Join-Path $Root 'scripts\generate-p3-golden.ps1') `
        -UnrealEditorCmd $Editor -UProject $Project -ReferenceRoot $Reference `
        -ProjectRoot $Root -ReadyCheck)
    if ($LASTEXITCODE -ne 0 -or $output[-1] -cne 'P3_TRACE_READY_OK')
    {
        throw 'P3 ReadyCheck failed while preparing the shared trace commandlet.'
    }
}

function Invoke-P4Commandlet(
    [string]$Editor,
    [string]$Project,
    [string]$Reference,
    [string]$OutputDirectory,
    [string]$LogPath)
{
    $arguments = @(
        $Project,
        '-run=AlsLocomotionTrace',
        '-TraceKind=P4',
        "-Output=$OutputDirectory",
        "-ReferenceRoot=$Reference",
        "-ReferenceCommit=$lockedCommit",
        "-PatchHashes=$lockedPatchHash",
        '-stdout', '-FullStdOutLogOutput', '-unattended', '-nosplash', '-nullrhi', '-nosound',
        "-abslog=$LogPath")
    & $Editor @arguments *> "$LogPath.process.log"
    if ($LASTEXITCODE -ne 0)
    {
        throw "P4 commandlet failed with exit code $LASTEXITCODE; log=$LogPath.process.log"
    }
    $expectedMarker = "P4_TRACE_GENERATION_OK cases=25 commit=$lockedCommit"
    $markerLines = @()
    if (Test-Path -LiteralPath $LogPath -PathType Leaf) {
        $markerLines = @(Get-Content -LiteralPath $LogPath | ForEach-Object {
            if ($_ -ceq $expectedMarker) { $_ }
            elseif ($_ -cmatch '^\[[0-9.:-]+\]\[\s*[0-9]+\]LogTemp: Display: (?<message>.*)$' -and
                $Matches.message -ceq $expectedMarker) { $Matches.message }
            elseif ($_.IndexOf('P4_TRACE_GENERATION_OK', [System.StringComparison]::Ordinal) -ge 0) {
                '__INVALID_P4_MARKER_LINE__'
            }
        })
    }
    if ($markerLines.Count -ne 1 -or $markerLines[0] -cne $expectedMarker)
    {
        throw "P4 commandlet did not emit its exact success marker; log=$LogPath"
    }
}

function Initialize-P4SchemaValidator
{
    if ($null -ne $script:p4SchemaValidatorPath -and
        (Test-Path -LiteralPath $script:p4SchemaValidatorPath -PathType Leaf))
    {
        return $script:p4SchemaValidatorPath
    }
    $validatorProject = Join-Path $PSScriptRoot '..\tools\Als.TraceSchemaValidator\Als.TraceSchemaValidator.csproj'
    $build = & dotnet build $validatorProject --configuration Release --nologo
    if ($LASTEXITCODE -ne 0)
    {
        throw "P4 schema validator build failed: $($build -join [Environment]::NewLine)"
    }
    $script:p4SchemaValidatorPath = Join-Path $PSScriptRoot '..\tools\Als.TraceSchemaValidator\bin\Release\net8.0\Als.TraceSchemaValidator.dll'
    return $script:p4SchemaValidatorPath
}

function Write-P4PortOracle([string]$Root, [string]$RawDirectory, [string]$OutputDirectory)
{
    $build = & dotnet build (Join-Path $Root 'src\Als.Core\Als.Core.csproj') `
        --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Als.Core release build failed: $($build -join [Environment]::NewLine)" }
    $assemblyPath = Join-Path $Root 'src\Als.Core\bin\Release\net8.0\Als.Core.dll'
    $assembly = [System.Reflection.Assembly]::LoadFrom($assemblyPath)
    $type = $assembly.GetType('GodotAls.Core.Pose.AlsPoseTrace', $true)
    $method = $type.GetMethod('WritePortOracle', [System.Reflection.BindingFlags]'Public,Static')
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    foreach ($name in $expectedNames)
    {
        $source = Join-Path $RawDirectory $name
        if (-not (Test-Path -LiteralPath $source -PathType Leaf))
        {
            throw "P4 commandlet output is missing '$name'."
        }
        $arguments = [object[]]@([string]$source, [string](Join-Path $OutputDirectory $name))
        $method.Invoke($null, $arguments) | Out-Null
    }
}

function Assert-P4Schema([string]$Directory, [string]$SchemaPath)
{
    Assert-P4ExactFixtureTree $Directory
    $files = @(Get-ChildItem -LiteralPath $Directory -Filter '*.json' -File)
    $actualNames = @($files.Name | Sort-Object)
    $lockedNames = @($expectedNames | Sort-Object)
    if (@(Compare-Object -ReferenceObject $lockedNames -DifferenceObject $actualNames `
            -CaseSensitive).Count -ne 0)
    {
        throw "P4 output file set is not exact: $($files.Name -join ', ')"
    }

    $validator = Initialize-P4SchemaValidator
    $validation = & dotnet $validator $SchemaPath @($files.FullName) 2>&1
    if ($LASTEXITCODE -ne 0)
    {
        throw "P4 fixture failed schema validation: $($validation -join [Environment]::NewLine)"
    }

    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($file in $files)
    {
        $json = Get-Content -LiteralPath $file.FullName -Raw
        $document = $json | ConvertFrom-Json
        foreach ($case in @($document.cases))
        {
            if (-not $seen.Add([string]$case.caseId))
            {
                throw "Duplicate P4 caseId '$($case.caseId)'."
            }
        }
    }
    if ($seen.Count -ne 25 -or @($expectedCaseIds | Where-Object { -not $seen.Contains($_) }).Count -ne 0)
    {
        throw 'P4 output does not contain the exact locked 25-case matrix.'
    }
}

function Assert-P4RunsIdentical([string]$First, [string]$Second)
{
    foreach ($name in $expectedNames)
    {
        $left = Join-Path $First $name
        $right = Join-Path $Second $name
        if (-not (Test-Path -LiteralPath $left -PathType Leaf) -or
            -not (Test-Path -LiteralPath $right -PathType Leaf) -or
            -not [System.Linq.Enumerable]::SequenceEqual(
                [byte[]][System.IO.File]::ReadAllBytes($left),
                [byte[]][System.IO.File]::ReadAllBytes($right)))
        {
            throw "P4 consecutive runs are not byte-for-byte identical: $name"
        }
    }
}

function Assert-P4PublishedCopyIdentical([string]$Source, [string]$Staging)
{
    foreach ($name in $expectedNames)
    {
        $left = Join-Path $Source $name
        $right = Join-Path $Staging $name
        if (-not [System.Linq.Enumerable]::SequenceEqual(
                [byte[]][System.IO.File]::ReadAllBytes($left),
                [byte[]][System.IO.File]::ReadAllBytes($right)))
        {
            throw "P4 staged publication differs from validated source: $name"
        }
    }
}

function Assert-P4PathHasNoReparsePoint([string]$Path)
{
    $current = Get-FullPath $Path
    while (-not [string]::IsNullOrEmpty($current))
    {
        if (Test-Path -LiteralPath $current)
        {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
            {
                throw "P4 publication refuses reparse point: $current"
            }
        }
        $next = Split-Path -Parent $current
        if ([string]::IsNullOrEmpty($next) -or $next -ceq $current) { break }
        $current = $next
    }
}

function Assert-P4TreeHasNoReparsePoint([string]$Path)
{
    Assert-P4PathHasNoReparsePoint $Path
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return }
    foreach ($child in @(Get-ChildItem -LiteralPath $Path -Force))
    {
        if (($child.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
        {
            throw "P4 publication refuses reparse point: $($child.FullName)"
        }
        if ($child.PSIsContainer)
        {
            Assert-P4TreeHasNoReparsePoint $child.FullName
        }
    }
}

function Assert-P4ExactFixtureTree([string]$Directory)
{
    $fullDirectory = Get-FullPath $Directory
    if (-not (Test-Path -LiteralPath $fullDirectory -PathType Container))
    {
        throw "P4 publication source is not a directory: $fullDirectory"
    }
    Assert-P4TreeHasNoReparsePoint $fullDirectory
    $children = @(Get-ChildItem -LiteralPath $fullDirectory -Force)
    $directories = @($children | Where-Object PSIsContainer)
    $files = @($children | Where-Object { -not $_.PSIsContainer })
    $actualNames = @($files.Name | Sort-Object)
    $lockedNames = @($expectedNames | Sort-Object)
    if ($directories.Count -ne 0 -or $files.Count -ne 5 -or
        @(Compare-Object -ReferenceObject $lockedNames -DifferenceObject $actualNames -CaseSensitive).Count -ne 0)
    {
        throw "P4 publication requires the exact five-file fixture tree: $($children.Name -join ', ')"
    }
}

function Assert-P4OwnedPublicationPath([string]$Path, [string]$Parent, [string]$LeafPattern)
{
    $fullPath = Get-FullPath $Path
    $fullParent = Get-FullPath $Parent
    if ((Split-Path -Parent $fullPath) -cne $fullParent -or
        (Split-Path -Leaf $fullPath) -cnotmatch $LeafPattern)
    {
        throw "P4 publication path is outside its owned parent or has an invalid name: $fullPath"
    }
    Assert-P4PathHasNoReparsePoint $fullParent
    Assert-P4PathHasNoReparsePoint $fullPath
}

function Remove-P4OwnedPublicationPath(
    [string]$Path,
    [string]$Parent,
    [string]$LeafPattern)
{
    Assert-P4OwnedPublicationPath $Path $Parent $LeafPattern
    if (Test-Path -LiteralPath $Path)
    {
        Assert-P4TreeHasNoReparsePoint $Path
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Move-P4OwnedPublicationPath(
    [string]$Source,
    [string]$Destination,
    [string]$Parent,
    [string]$SourcePattern,
    [string]$DestinationPattern)
{
    Assert-P4OwnedPublicationPath $Source $Parent $SourcePattern
    Assert-P4OwnedPublicationPath $Destination $Parent $DestinationPattern
    Assert-P4TreeHasNoReparsePoint $Source
    Move-Item -LiteralPath $Source -Destination $Destination
}

function Get-P4PublicationMutexName([string]$FixtureDirectory)
{
    $bytes = [System.Text.Encoding]::UTF8.GetBytes((Get-FullPath $FixtureDirectory).ToUpperInvariant())
    $hash = [System.Security.Cryptography.SHA256]::HashData($bytes)
    return 'Local\GodotALS.P4.Publish.' + [Convert]::ToHexString($hash)
}

function Enter-P4PublicationLock([string]$FixtureDirectory)
{
    $mutex = [System.Threading.Mutex]::new($false, (Get-P4PublicationMutexName $FixtureDirectory))
    $acquired = $false
    try
    {
        try { $acquired = $mutex.WaitOne(0) }
        catch [System.Threading.AbandonedMutexException] { $acquired = $true }
        if (-not $acquired)
        {
            throw "P4 fixture publication is already active: $(Get-FullPath $FixtureDirectory)"
        }
        return $mutex
    }
    catch
    {
        if (-not $acquired) { $mutex.Dispose() }
        throw
    }
}

function Exit-P4PublicationLock([System.Threading.Mutex]$Lease)
{
    $Lease.ReleaseMutex()
    $Lease.Dispose()
}

function Repair-P4FixturePublication([string]$FixtureDirectory)
{
    $destination = Get-FullPath $FixtureDirectory
    $parent = Split-Path -Parent $destination
    $leaf = Split-Path -Leaf $destination
    $backup = Join-Path $parent ".$leaf.p4-backup"
    Assert-P4OwnedPublicationPath $destination $parent ('^' + [regex]::Escape($leaf) + '$')
    Assert-P4OwnedPublicationPath $backup $parent ('^\.' + [regex]::Escape($leaf) + '\.p4-backup$')
    foreach ($staging in @(Get-ChildItem -LiteralPath $parent -Directory -Filter ".$leaf.p4-staging.*" -ErrorAction SilentlyContinue))
    {
        Remove-P4OwnedPublicationPath $staging.FullName $parent ('^\.' + [regex]::Escape($leaf) + '\.p4-staging\.[0-9a-f]{32}$')
    }
    if (Test-Path -LiteralPath $backup)
    {
        if (Test-Path -LiteralPath $destination)
        {
            Remove-P4OwnedPublicationPath $backup $parent ('^\.' + [regex]::Escape($leaf) + '\.p4-backup$')
        }
        else
        {
            Move-P4OwnedPublicationPath $backup $destination $parent `
                ('^\.' + [regex]::Escape($leaf) + '\.p4-backup$') `
                ('^' + [regex]::Escape($leaf) + '$')
        }
    }
}

function Publish-P4FixtureDirectory([string]$ValidatedOutput, [string]$FixtureDirectory)
{
    $source = Get-FullPath (Resolve-Path -LiteralPath $ValidatedOutput).Path
    $destination = Get-FullPath $FixtureDirectory
    $parent = Split-Path -Parent $destination
    Assert-P4ExactFixtureTree $source
    $schemaPath = Join-Path $PSScriptRoot '..\tools\schemas\als_pose_trace.schema.json'
    Assert-P4Schema $source $schemaPath
    Assert-P4PathHasNoReparsePoint $parent
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $publicationLock = Enter-P4PublicationLock $destination
    try
    {
        Repair-P4FixturePublication $destination
    $leaf = Split-Path -Leaf $destination
    $transactionId = [guid]::NewGuid().ToString('N')
    $staging = Join-Path $parent ".$leaf.p4-staging.$transactionId"
    $backup = Join-Path $parent ".$leaf.p4-backup"
    Assert-P4OwnedPublicationPath $destination $parent ('^' + [regex]::Escape($leaf) + '$')
    Assert-P4OwnedPublicationPath $staging $parent ('^\.' + [regex]::Escape($leaf) + '\.p4-staging\.[0-9a-f]{32}$')
    Assert-P4OwnedPublicationPath $backup $parent ('^\.' + [regex]::Escape($leaf) + '\.p4-backup$')
    $destinationMoved = $false
    $stagingMoved = $false
    try
    {
        Copy-Item -LiteralPath $source -Destination $staging -Recurse
        if ($env:GODOTALS_P4_GENERATOR_FAIL_STAGE -ceq 'corrupt-after-copy')
        {
            Add-Content -LiteralPath (Join-Path $staging 'trace_p4_aim.json') -Value 'INVALID_STAGING_JSON'
        }
        Assert-P4ExactFixtureTree $staging
        Assert-P4Schema $staging $schemaPath
        if ($env:GODOTALS_P4_GENERATOR_FAIL_STAGE -ceq 'drift-after-schema')
        {
            Add-Content -LiteralPath (Join-Path $staging 'trace_p4_aim.json') -Value ' '
        }
        Assert-P4PublishedCopyIdentical $source $staging
        if (Test-Path -LiteralPath $destination)
        {
            Move-P4OwnedPublicationPath $destination $backup $parent `
                ('^' + [regex]::Escape($leaf) + '$') `
                ('^\.' + [regex]::Escape($leaf) + '\.p4-backup$')
            $destinationMoved = $true
        }
        if ($env:GODOTALS_P4_GENERATOR_FAIL_STAGE -ceq 'after-backup')
        {
            throw 'Injected P4 publication failure after backup.'
        }
        Move-P4OwnedPublicationPath $staging $destination $parent `
            ('^\.' + [regex]::Escape($leaf) + '\.p4-staging\.[0-9a-f]{32}$') `
            ('^' + [regex]::Escape($leaf) + '$')
        $stagingMoved = $true
        if ($env:GODOTALS_P4_GENERATOR_FAIL_STAGE -ceq 'after-publish')
        {
            throw 'Injected P4 publication failure after publish.'
        }
        if ($destinationMoved)
        {
            Remove-P4OwnedPublicationPath $backup $parent `
                ('^\.' + [regex]::Escape($leaf) + '\.p4-backup$')
        }
    }
    catch
    {
        if ($stagingMoved -and (Test-Path -LiteralPath $destination))
        {
            Remove-P4OwnedPublicationPath $destination $parent `
                ('^' + [regex]::Escape($leaf) + '$')
        }
        if ($destinationMoved -and (Test-Path -LiteralPath $backup))
        {
            Move-P4OwnedPublicationPath $backup $destination $parent `
                ('^\.' + [regex]::Escape($leaf) + '\.p4-backup$') `
                ('^' + [regex]::Escape($leaf) + '$')
        }
        if (Test-Path -LiteralPath $staging)
        {
            Remove-P4OwnedPublicationPath $staging $parent `
                ('^\.' + [regex]::Escape($leaf) + '\.p4-staging\.[0-9a-f]{32}$')
        }
        if (Test-Path -LiteralPath $backup)
        {
            Remove-P4OwnedPublicationPath $backup $parent `
                ('^\.' + [regex]::Escape($leaf) + '\.p4-backup$')
        }
        throw
    }
    }
    finally
    {
        Exit-P4PublicationLock $publicationLock
    }
}

function Invoke-P4Generation(
    [string]$Root,
    [string]$Reference,
    [string]$Editor,
    [string]$Project)
{
    $temporaryRoot = $null
    try
    {
        Assert-P4ReferenceLock $Root $Reference
        Invoke-ReferencePreparation $Root $Reference
        Invoke-P3ReadyCheck $Root $Editor $Project $Reference
        Initialize-P4SchemaValidator | Out-Null

        $temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "godot-als-p4-$([guid]::NewGuid().ToString('N'))"
        $rawA = Join-Path $temporaryRoot 'raw-a'
        $rawB = Join-Path $temporaryRoot 'raw-b'
        $runA = Join-Path $temporaryRoot 'run-a'
        $runB = Join-Path $temporaryRoot 'run-b'
        New-Item -ItemType Directory -Path $rawA, $rawB | Out-Null
        $logRoot = Join-Path (Split-Path -Parent $Project) 'Saved\Logs\P4Trace'
        New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
        Invoke-P4Commandlet $Editor $Project $Reference $rawA (Join-Path $logRoot "$([guid]::NewGuid().ToString('N')).log")
        Invoke-P4Commandlet $Editor $Project $Reference $rawB (Join-Path $logRoot "$([guid]::NewGuid().ToString('N')).log")
        Write-P4PortOracle $Root $rawA $runA
        Write-P4PortOracle $Root $rawB $runB
        $schemaPath = Join-Path $Root 'tools\schemas\als_pose_trace.schema.json'
        Assert-P4Schema $runA $schemaPath
        Assert-P4Schema $runB $schemaPath
        Assert-P4RunsIdentical $runA $runB
        Publish-P4FixtureDirectory $runA (Join-Path $Root 'tests\Als.Core.Tests\Fixtures\P4')
        Write-Output 'P4_GOLDEN_OK'
    }
    finally
    {
        if ($null -ne $temporaryRoot -and (Test-Path -LiteralPath $temporaryRoot))
        {
            Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
        }
    }
}

if ($MyInvocation.InvocationName -ceq '.') { return }

if ([string]::IsNullOrWhiteSpace($UnrealEditorCmd) -or
    [string]::IsNullOrWhiteSpace($UnrealProject))
{
    throw 'UnrealEditorCmd and UnrealProject are required for production generation.'
}

$root = Get-FullPath (Resolve-Path -LiteralPath $ProjectRoot).Path
$reference = Get-FullPath (Resolve-Path -LiteralPath $ReferenceRoot).Path
$editor = (Resolve-Path -LiteralPath $UnrealEditorCmd).Path
$project = (Resolve-Path -LiteralPath $UnrealProject).Path
Invoke-P4Generation $root $reference $editor $project

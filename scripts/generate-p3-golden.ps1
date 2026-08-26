param(
    [Parameter(Mandatory = $true)]
    [string]$UnrealEditorCmd,

    [Parameter(Mandatory = $true)]
    [string]$UProject,

    [string]$ReferenceRoot = '../GodotALS-References\ALS-Refactored',
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [switch]$ReadyCheck
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$lockedCommit = 'b754d6f0f2bb03741d301f8fb88077ebfe561e17'
$lockedPatchHash = '3dc561f194045d3dc01bd65c7f7c3bd4acd0a30c0fab31ea0cd16d676d312e5f'
$sequenceNames = @('idle_gaits', 'directions', 'crouch_clearance', 'rotation_modes', 'jump_land')
$expectedOutputNames = @($sequenceNames | ForEach-Object { "trace_$_.json" }) + 'p3_locomotion_settings.json'
$outputDestinations = [ordered]@{
    'p3_locomotion_settings.json' = 'assets\config\p3_locomotion_settings.json'
    'trace_idle_gaits.json' = 'tests\Als.Core.Tests\Fixtures\P3\trace_idle_gaits.json'
    'trace_directions.json' = 'tests\Als.Core.Tests\Fixtures\P3\trace_directions.json'
    'trace_crouch_clearance.json' = 'tests\Als.Core.Tests\Fixtures\P3\trace_crouch_clearance.json'
    'trace_rotation_modes.json' = 'tests\Als.Core.Tests\Fixtures\P3\trace_rotation_modes.json'
    'trace_jump_land.json' = 'tests\Als.Core.Tests\Fixtures\P3\trace_jump_land.json'
}
$temporaryOutput = $null
$resolvedProject = $null
$projectFileHashAtStart = $null

function Get-FullPath([string]$Path)
{
    [System.IO.Path]::GetFullPath($Path).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
}

function Test-IsChildPath([string]$Path, [string]$Root)
{
    $fullPath = Get-FullPath $Path
    $fullRoot = Get-FullPath $Root
    $fullPath.StartsWith($fullRoot + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-IsSameOrChildPath([string]$Path, [string]$Root)
{
    $fullPath = Get-FullPath $Path
    $fullRoot = Get-FullPath $Root
    [System.StringComparer]::OrdinalIgnoreCase.Equals($fullPath, $fullRoot) -or
        (Test-IsChildPath $fullPath $fullRoot)
}

function Assert-NoReparsePathComponents([string]$Path, [string]$Root, [string]$Description)
{
    $fullPath = Get-FullPath $Path
    $fullRoot = Get-FullPath $Root
    if (-not (Test-IsSameOrChildPath $fullPath $fullRoot))
    {
        throw "$Description escapes its trusted root: $fullPath"
    }
    if (-not (Test-Path -LiteralPath $fullRoot -PathType Container))
    {
        throw "$Description trusted root does not exist: $fullRoot"
    }

    $current = $fullRoot
    $components = @()
    $relative = [System.IO.Path]::GetRelativePath($fullRoot, $fullPath)
    if ($relative -ne '.') { $components = @($relative -split '[\\/]+') }
    foreach ($component in @('') + $components)
    {
        if (-not [string]::IsNullOrEmpty($component)) { $current = Join-Path $current $component }
        if (-not (Test-Path -LiteralPath $current)) { break }
        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
        {
            throw "$Description contains a reparse point: $($item.FullName)"
        }
    }
}

function Get-ValidatedTreeEntries([string]$Root, [string]$Description)
{
    $fullRoot = Get-FullPath $Root
    if (-not (Test-Path -LiteralPath $fullRoot -PathType Container))
    {
        throw "$Description tree does not exist: $fullRoot"
    }
    $rootItem = Get-Item -LiteralPath $fullRoot -Force
    if (($rootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
    {
        throw "$Description root is a reparse point: $fullRoot"
    }

    $entries = [System.Collections.Generic.List[object]]::new()
    $directories = [System.Collections.Generic.Queue[string]]::new()
    $directories.Enqueue($fullRoot)
    while ($directories.Count -gt 0)
    {
        $directory = $directories.Dequeue()
        foreach ($item in @(Get-ChildItem -LiteralPath $directory -Force))
        {
            if (-not (Test-IsChildPath $item.FullName $fullRoot))
            {
                throw "$Description entry escapes its tree: $($item.FullName)"
            }
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
            {
                throw "$Description contains a reparse point: $($item.FullName)"
            }
            $entries.Add($item)
            if ($item.PSIsContainer) { $directories.Enqueue($item.FullName) }
        }
    }
    return @($entries)
}

function Remove-ValidatedTree([string]$Root, [string]$TrustedParent, [string]$Description)
{
    if (-not (Test-Path -LiteralPath $Root)) { return }
    if (-not (Test-IsChildPath $Root $TrustedParent)) { throw "Unsafe $Description removal path: $Root" }
    $entries = @(Get-ValidatedTreeEntries $Root $Description)
    foreach ($file in @($entries | Where-Object { -not $_.PSIsContainer }))
    {
        Remove-Item -LiteralPath $file.FullName -Force
    }
    foreach ($directory in @($entries | Where-Object { $_.PSIsContainer } |
        Sort-Object @{ Expression = { $_.FullName.Length }; Descending = $true }))
    {
        Remove-Item -LiteralPath $directory.FullName -Force
    }
    Remove-Item -LiteralPath (Get-FullPath $Root) -Force
}

function Invoke-NativeTool([string]$FileName, [string[]]$Arguments, [string]$LogPath)
{
    $logDirectory = Split-Path -Parent $LogPath
    New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
    $process = [System.Diagnostics.Process]::new()
    try
    {
        $process.StartInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $process.StartInfo.FileName = $FileName
        $process.StartInfo.UseShellExecute = $false
        $process.StartInfo.CreateNoWindow = $true
        $process.StartInfo.RedirectStandardOutput = $true
        $process.StartInfo.RedirectStandardError = $true
        foreach ($argument in $Arguments)
        {
            [void]$process.StartInfo.ArgumentList.Add($argument)
        }
        if (-not $process.Start()) { throw "Could not start process: $FileName" }
        $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
        $standardErrorTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $standardOutput = $standardOutputTask.GetAwaiter().GetResult()
        $standardError = $standardErrorTask.GetAwaiter().GetResult()
        $combined = $standardOutput + $standardError
        [System.IO.File]::WriteAllText($LogPath, $combined, [System.Text.UTF8Encoding]::new($false))
        if (-not [string]::IsNullOrWhiteSpace($combined)) { [Console]::Out.Write($combined) }
        [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $combined; Log = $LogPath }
    }
    finally
    {
        if (-not $process.HasExited) { $process.Kill($true) }
        $process.Dispose()
    }
}

function Test-UnrealLogMarker([string]$LogPath, [string]$Marker)
{
    if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) { return $false }
    $escapedMarker = [regex]::Escape($Marker)
    return [bool](Get-Content -LiteralPath $LogPath | Where-Object {
        $_ -match "(?:^|LogTemp: Display: )$escapedMarker\s*$"
    } | Select-Object -First 1)
}

function Ensure-AlsJunction([string]$ProjectDirectory, [string]$ResolvedReferenceRoot)
{
    $pluginsDirectory = Join-Path $ProjectDirectory 'Plugins'
    New-Item -ItemType Directory -Force -Path $pluginsDirectory | Out-Null
    $junctionPath = Join-Path $pluginsDirectory 'ALS'
    if (-not (Test-IsChildPath $junctionPath $pluginsDirectory))
    {
        throw "Refusing unsafe ALS junction path: $junctionPath"
    }

    if (Test-Path -LiteralPath $junctionPath)
    {
        $item = Get-Item -LiteralPath $junctionPath -Force
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -eq 0)
        {
            throw "Plugins\ALS exists but is not a junction/reparse point: $junctionPath"
        }
        $target = @($item.Target) | Select-Object -First 1
        if (-not [System.IO.Path]::IsPathRooted("$target")) { $target = Join-Path $item.Parent.FullName "$target" }
        if (-not [System.StringComparer]::OrdinalIgnoreCase.Equals((Get-FullPath "$target"), $ResolvedReferenceRoot))
        {
            throw "Plugins\ALS targets '$target', expected '$ResolvedReferenceRoot'. Refusing replacement."
        }
        return
    }

    [void](New-Item -ItemType Junction -Path $junctionPath -Target $ResolvedReferenceRoot)
    $created = Get-Item -LiteralPath $junctionPath -Force
    if (($created.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -eq 0)
    {
        throw "Failed to create Plugins\ALS junction: $junctionPath"
    }
}

function Sync-OwnedPlugin([string]$RepositoryPlugin, [string]$ProjectDirectory)
{
    $projectRoot = Get-FullPath $ProjectDirectory
    Assert-NoReparsePathComponents $projectRoot $projectRoot 'UE project root'
    $pluginsDirectory = Get-FullPath (Join-Path $projectRoot 'Plugins')
    Assert-NoReparsePathComponents $pluginsDirectory $projectRoot 'UE Plugins directory'
    if (-not (Test-Path -LiteralPath $pluginsDirectory))
    {
        New-Item -ItemType Directory -Path $pluginsDirectory | Out-Null
    }
    Assert-NoReparsePathComponents $pluginsDirectory $projectRoot 'UE Plugins directory'

    $destination = Get-FullPath (Join-Path $pluginsDirectory 'AlsLocomotionTrace')
    Assert-NoReparsePathComponents $destination $projectRoot 'Owned trace plugin destination'
    $sourceRoot = Get-FullPath $RepositoryPlugin
    $sourceEntries = @(Get-ValidatedTreeEntries $sourceRoot 'Repository trace plugin source')
    $sourceFiles = @($sourceEntries | Where-Object { -not $_.PSIsContainer } | Sort-Object FullName)
    if (Test-Path -LiteralPath $destination)
    {
        [void](Get-ValidatedTreeEntries $destination 'Owned trace plugin destination')
    }

    $transactionId = [guid]::NewGuid().ToString('N')
    $staging = Join-Path $pluginsDirectory ".AlsLocomotionTrace.staging.$transactionId"
    $backup = Join-Path $pluginsDirectory ".AlsLocomotionTrace.backup.$transactionId"
    Assert-NoReparsePathComponents $staging $projectRoot 'Owned trace plugin staging path'
    Assert-NoReparsePathComponents $backup $projectRoot 'Owned trace plugin backup path'
    New-Item -ItemType Directory -Path $staging | Out-Null
    $destinationMoved = $false
    $stagingMoved = $false
    try
    {
        foreach ($sourceFile in $sourceFiles)
        {
            $relative = [System.IO.Path]::GetRelativePath($sourceRoot, $sourceFile.FullName)
            $target = Get-FullPath (Join-Path $staging $relative)
            if (-not (Test-IsChildPath $target $staging)) { throw "Repository plugin file escapes staging: $relative" }
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
            Copy-Item -LiteralPath $sourceFile.FullName -Destination $target
        }
        [void](Get-ValidatedTreeEntries $staging 'Staged trace plugin')

        if (Test-Path -LiteralPath $destination)
        {
            Move-Item -LiteralPath $destination -Destination $backup
            $destinationMoved = $true
        }
        Move-Item -LiteralPath $staging -Destination $destination
        $stagingMoved = $true
        [void](Get-ValidatedTreeEntries $destination 'Synchronized trace plugin')
        if ($destinationMoved)
        {
            $destinationMoved = $false
            Remove-ValidatedTree $backup $pluginsDirectory 'Owned trace plugin backup'
        }
    }
    catch
    {
        if ($stagingMoved -and (Test-Path -LiteralPath $destination))
        {
            Remove-ValidatedTree $destination $pluginsDirectory 'Failed synchronized trace plugin'
            $stagingMoved = $false
        }
        if ($destinationMoved -and (Test-Path -LiteralPath $backup))
        {
            Move-Item -LiteralPath $backup -Destination $destination
            $destinationMoved = $false
        }
        throw
    }
    finally
    {
        if (Test-Path -LiteralPath $staging)
        {
            Remove-ValidatedTree $staging $pluginsDirectory 'Owned trace plugin staging tree'
        }
    }
}

function Build-And-AuditEditorTarget([string]$ProjectPath, [string]$ProjectDirectory, [string]$EngineRoot)
{
    $targetFiles = @(Get-ChildItem -LiteralPath (Join-Path $ProjectDirectory 'Source') `
        -Filter '*Editor.Target.cs' -File -Recurse)
    if ($targetFiles.Count -ne 1)
    {
        throw "Expected exactly one native Editor target, found $($targetFiles.Count)."
    }
    $targetName = $targetFiles[0].Name -replace '\.Target\.cs$', ''
    $targetText = Get-Content -LiteralPath $targetFiles[0].FullName -Raw
    if ($targetText -match 'class\s+(\w+)Target\s*:') { $targetName = $Matches[1] }

    $dotnet = Join-Path $EngineRoot 'Engine\Binaries\ThirdParty\DotNet\10.0\win-x64\dotnet.exe'
    $ubt = Join-Path $EngineRoot 'Engine\Binaries\DotNET\UnrealBuildTool\UnrealBuildTool.dll'
    if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf) -or -not (Test-Path -LiteralPath $ubt -PathType Leaf))
    {
        throw "UE bundled .NET host or UnrealBuildTool is missing under $EngineRoot."
    }
    $buildLog = Join-Path $ProjectDirectory ("Saved\Logs\P3Trace\ubt-" +
        [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [guid]::NewGuid().ToString('N') + '.log')
    $result = Invoke-NativeTool $dotnet @(
        $ubt,
        $targetName,
        'Win64',
        'Development',
        "-Project=$ProjectPath",
        '-EnablePlugin=AlsLocomotionTrace+ALS',
        '-WaitMutex',
        '-NoHotReloadFromIDE'
    ) $buildLog
    if ($result.ExitCode -ne 0) { throw "Complete Editor target UBT build failed; log=$buildLog" }
    if ($result.Output -match "(?im)^\s*(?:warning\s*:\s*)?Plugin\s+'[^']+'\s+does not list plugin\s+'[^']+'\s+as a dependency")
    {
        throw "UBT emitted a missing plugin dependency warning; log=$buildLog"
    }

    $receiptPath = Join-Path $ProjectDirectory "Binaries\Win64\$targetName.target"
    if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) { throw "Missing Editor target receipt: $receiptPath" }
    $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    if ($receipt.Version.MajorVersion -ne 5 -or $receipt.Version.MinorVersion -ne 9 -or $receipt.Version.PatchVersion -ne 0)
    {
        throw "Editor receipt engine identity is not 5.9.0: $receiptPath"
    }
    $targetBuildId = [string]$receipt.Version.BuildId
    if ([string]::IsNullOrWhiteSpace($targetBuildId)) { throw "Editor receipt has no BuildId: $receiptPath" }

    $expectedModules = [ordered]@{
        ALS = Join-Path $ProjectDirectory 'Plugins\ALS'
        AlsLocomotionTrace = Join-Path $ProjectDirectory 'Plugins\AlsLocomotionTrace'
    }
    foreach ($entry in $expectedModules.GetEnumerator())
    {
        $manifestPath = Join-Path $entry.Value 'Binaries\Win64\UnrealEditor.modules'
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Missing module manifest: $manifestPath" }
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ([string]$manifest.BuildId -cne $targetBuildId)
        {
            throw "BuildId mismatch for $($entry.Key): target=$targetBuildId module=$($manifest.BuildId)"
        }
        $moduleProperty = $manifest.Modules.PSObject.Properties[$entry.Key]
        if ($null -eq $moduleProperty) { throw "Manifest does not map module $($entry.Key): $manifestPath" }
        $dllName = [string]$moduleProperty.Value
        if ([System.IO.Path]::GetFileName($dllName) -cne $dllName -or $dllName -notmatch '\.dll$')
        {
            throw "Unsafe module DLL mapping for $($entry.Key): $dllName"
        }
        $dllPath = Join-Path (Split-Path -Parent $manifestPath) $dllName
        if (-not (Test-Path -LiteralPath $dllPath -PathType Leaf)) { throw "Mapped module DLL does not exist: $dllPath" }
    }
}

function Assert-FiniteNumber([object]$Value, [string]$Path)
{
    if ($Value -is [double] -or $Value -is [float] -or $Value -is [decimal] -or
        $Value -is [int] -or $Value -is [long])
    {
        $number = [double]$Value
        if ([double]::IsNaN($number) -or [double]::IsInfinity($number))
        {
            throw "Non-finite number at $Path"
        }
    }
    elseif ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string])
    {
        $index = 0
        foreach ($element in $Value) { Assert-FiniteNumber $element "$Path[$index]"; $index++ }
    }
    elseif ($null -ne $Value -and $Value.PSObject -and $Value -isnot [string] -and $Value -isnot [bool])
    {
        foreach ($property in $Value.PSObject.Properties) { Assert-FiniteNumber $property.Value "$Path.$($property.Name)" }
    }
}

function Assert-ExactProperties([object]$Object, [string[]]$Expected, [string]$Path)
{
    $actual = @($Object.PSObject.Properties.Name | Sort-Object)
    $sortedExpected = @($Expected | Sort-Object)
    if (($actual -join '|') -cne ($sortedExpected -join '|'))
    {
        throw "$Path properties mismatch. Expected '$($sortedExpected -join ',')', actual '$($actual -join ',')'."
    }
}

function Assert-SortedProperties([object]$Value, [string]$Path)
{
    if ($null -eq $Value -or $Value -is [string] -or $Value -is [bool] -or
        $Value -is [double] -or $Value -is [float] -or $Value -is [decimal] -or
        $Value -is [int] -or $Value -is [long])
    {
        return
    }
    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [pscustomobject])
    {
        $index = 0
        foreach ($element in $Value) { Assert-SortedProperties $element "$Path[$index]"; $index++ }
        return
    }

    $names = @($Value.PSObject.Properties.Name)
    $sortedNames = @($names | Sort-Object -CaseSensitive)
    if (($names -join '|') -cne ($sortedNames -join '|'))
    {
        throw "$Path properties are not deterministically sorted: '$($names -join ',')'."
    }
    foreach ($property in $Value.PSObject.Properties)
    {
        Assert-SortedProperties $property.Value "$Path.$($property.Name)"
    }
}

function Validate-GeneratedOutput([string]$Directory, [string]$SchemaPath)
{
    $schema = Get-Content -LiteralPath $SchemaPath -Raw | ConvertFrom-Json
    if ($null -eq $schema.oneOf -or $null -eq $schema.'$defs'.traceDocument -or $null -eq $schema.'$defs'.settingsDocument)
    {
        throw "Trace schema is missing its settings/trace document definitions: $SchemaPath"
    }

    $files = @(Get-ChildItem -LiteralPath $Directory -File -Filter '*.json' | Sort-Object Name)
    $actualNames = @($files.Name)
    if (($actualNames -join '|') -cne (@($expectedOutputNames | Sort-Object) -join '|'))
    {
        throw "Generated filenames mismatch. Expected '$($expectedOutputNames -join ',')', actual '$($actualNames -join ',')'."
    }

    $seenSequences = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $settingsValues = $null
    $observedMaxAccelerations = [System.Collections.Generic.List[double]]::new()
    $observedMaxBrakingDecelerations = [System.Collections.Generic.List[double]]::new()
    $hasDynamicMaxAcceleration = $false
    $hasDynamicMaxBrakingDeceleration = $false
    foreach ($file in $files)
    {
        $raw = Get-Content -LiteralPath $file.FullName -Raw
        try
        {
            $schemaValid = Test-Json -Json $raw -SchemaFile $SchemaPath -ErrorAction Stop
        }
        catch
        {
            throw "JSON Schema validation failed for $($file.Name): $($_.Exception.Message)"
        }
        if (-not $schemaValid) { throw "JSON Schema validation returned false for $($file.Name)." }
        $document = $raw | ConvertFrom-Json
        Assert-FiniteNumber $document $file.Name
        Assert-SortedProperties $document $file.Name
        if ($document.schemaVersion -ne 1 -or $document.referenceCommit -cne $lockedCommit -or
            [math]::Abs([double]$document.fixedDeltaSeconds - (1.0 / 60.0)) -gt 1e-12)
        {
            throw "Metadata mismatch in $($file.Name)."
        }
        if (@($document.patchHashes).Count -ne 1 -or [string]$document.patchHashes[0] -cne $lockedPatchHash)
        {
            throw "Locked patch hash mismatch in $($file.Name)."
        }

        if ($file.Name -eq 'p3_locomotion_settings.json')
        {
            Assert-ExactProperties $document @('fixedDeltaSeconds', 'kind', 'patchHashes', 'referenceCommit', 'schemaVersion', 'sources', 'values') $file.Name
            if ($document.kind -cne 'settings' -or $null -eq $document.sources -or $null -eq $document.values)
            {
                throw "Invalid settings document: $($file.Name)"
            }
            Assert-ExactProperties $document.sources @('animation', 'character', 'movement', 'portDefaults') "$($file.Name).sources"
            $requiredValues = @('walkForwardSpeed', 'runForwardSpeed', 'sprintSpeed', 'initialMaxAcceleration',
                'initialMaxBrakingDeceleration', 'crouchedHalfHeight', 'rotationInterpolationHalfLife',
                'velocitySmoothingHalfLife', 'accelerationSmoothingHalfLife', 'leanHalfLife', 'jumpSpeed',
                'landingRecoveryDuration', 'animatedWalkSpeed', 'animatedRunSpeed', 'animatedSprintSpeed',
                'animatedCrouchSpeed', 'playRateMinimum', 'playRateMaximum', 'crouchRunForwardSpeed',
                'crouchWalkForwardSpeed', 'gravity', 'movingSpeedThreshold', 'runBackwardSpeed',
                'standingHalfHeight', 'targetYawInterpolationSpeed', 'velocityAngleInterpolationEnd',
                'velocityAngleInterpolationStart', 'walkBackwardSpeed')
            Assert-ExactProperties $document.values $requiredValues "$($file.Name).values"
            foreach ($requiredValue in $requiredValues)
            {
                if ($requiredValue -notin $document.values.PSObject.Properties.Name)
                {
                    throw "Settings document is missing '$requiredValue'."
                }
            }
            $settingsValues = $document.values
            continue
        }

        Assert-ExactProperties $document @('fixedDeltaSeconds', 'frames', 'kind', 'name', 'patchHashes', 'referenceCommit', 'schemaVersion') $file.Name
        if ($document.kind -cne 'trace' -or $document.name -cnotin $sequenceNames -or
            -not $seenSequences.Add([string]$document.name) -or @($document.frames).Count -eq 0)
        {
            throw "Invalid or duplicate trace document: $($file.Name)"
        }
        $expectedIndex = 0
        foreach ($frame in @($document.frames))
        {
            Assert-ExactProperties $frame @('actual', 'command', 'index', 'tick', 'time') "$($file.Name).frames[$expectedIndex]"
            Assert-ExactProperties $frame.command @('aimYaw', 'jumpPressed', 'movementAxes', 'requestedGait',
                'requestedRotationMode', 'requestedStance', 'standBlocked', 'viewYaw') "$($file.Name).frames[$expectedIndex].command"
            Assert-ExactProperties $frame.actual @('acceleration', 'animationPhase', 'animationState',
                'blendCoordinates', 'gait', 'grounded', 'jumpTransition', 'lean', 'locomotionState',
                'maxAcceleration', 'maxBrakingDeceleration', 'playRate', 'position', 'rotationMode',
                'stance', 'stride', 'targetYaw', 'velocity', 'yaw') `
                "$($file.Name).frames[$expectedIndex].actual"
            Assert-ExactProperties $frame.command.movementAxes @('x', 'y') "$($file.Name).frames[$expectedIndex].command.movementAxes"
            foreach ($vectorName in @('acceleration', 'position', 'velocity'))
            {
                Assert-ExactProperties $frame.actual.$vectorName @('x', 'y', 'z') "$($file.Name).frames[$expectedIndex].actual.$vectorName"
            }
            foreach ($vectorName in @('blendCoordinates', 'lean'))
            {
                Assert-ExactProperties $frame.actual.$vectorName @('x', 'y') "$($file.Name).frames[$expectedIndex].actual.$vectorName"
            }
            if ($frame.index -ne $expectedIndex -or $frame.tick -ne $expectedIndex -or
                [math]::Abs([double]$frame.time - ($expectedIndex / 60.0)) -gt 1e-9)
            {
                throw "Non-deterministic tick/index/time at $($file.Name) frame $expectedIndex."
            }
            if ($null -eq $frame.command.movementAxes -or $null -eq $frame.actual.position -or
                $null -eq $frame.actual.velocity -or $null -eq $frame.actual.acceleration -or
                $null -eq $frame.actual.blendCoordinates -or $null -eq $frame.actual.lean)
            {
                throw "Trace frame lacks required command/actual vectors at $($file.Name) frame $expectedIndex."
            }
            if ([double]$frame.actual.maxAcceleration -lt 0.0 -or
                [double]$frame.actual.maxBrakingDeceleration -lt 0.0)
            {
                throw "Trace frame has a negative dynamic movement limit at $($file.Name) frame $expectedIndex."
            }
            $observedMaxAccelerations.Add([double]$frame.actual.maxAcceleration)
            $observedMaxBrakingDecelerations.Add([double]$frame.actual.maxBrakingDeceleration)
            $expectedIndex++
        }

        $frames = @($document.frames)
        if (@($frames.actual.maxAcceleration | Sort-Object -Unique).Count -gt 1)
        {
            $hasDynamicMaxAcceleration = $true
        }
        if (@($frames.actual.maxBrakingDeceleration | Sort-Object -Unique).Count -gt 1)
        {
            $hasDynamicMaxBrakingDeceleration = $true
        }
        $maximumHorizontalSpeed = ($frames | ForEach-Object {
            [math]::Sqrt(([double]$_.actual.velocity.x * [double]$_.actual.velocity.x) +
                ([double]$_.actual.velocity.y * [double]$_.actual.velocity.y))
        } | Measure-Object -Maximum).Maximum
        if ($document.name -ne 'idle_gaits' -and $maximumHorizontalSpeed -le 0.1)
        {
            throw "Trace '$($document.name)' never produced real horizontal movement."
        }

        switch ($document.name)
        {
            'idle_gaits'
            {
                $initialIdleFrames = @($frames | Where-Object { $_.index -ge 0 -and $_.index -le 29 })
                if ($initialIdleFrames.Count -ne 30) { throw 'idle_gaits initial idle segment must contain frames 0 through 29.' }
                $idleOrigin = $initialIdleFrames[0].actual.position
                $idleMaximumSpeed = ($initialIdleFrames | ForEach-Object {
                    [math]::Sqrt(([double]$_.actual.velocity.x * [double]$_.actual.velocity.x) +
                        ([double]$_.actual.velocity.y * [double]$_.actual.velocity.y))
                } | Measure-Object -Maximum).Maximum
                $idleMaximumDisplacement = ($initialIdleFrames | ForEach-Object {
                    $deltaX = [double]$_.actual.position.x - [double]$idleOrigin.x
                    $deltaY = [double]$_.actual.position.y - [double]$idleOrigin.y
                    [math]::Sqrt(($deltaX * $deltaX) + ($deltaY * $deltaY))
                } | Measure-Object -Maximum).Maximum
                if ($idleMaximumSpeed -gt 0.01 -or $idleMaximumDisplacement -gt 0.005)
                {
                    throw "idle_gaits initial idle segment moved beyond tolerance: speed=$idleMaximumSpeed displacement=$idleMaximumDisplacement."
                }

                $movementPhases = @(
                    [pscustomobject]@{ Name = 'walk'; Start = 30; End = 89; Gait = 'Walking'; MinimumSpeed = 0.25; MinimumDisplacement = 1.0 },
                    [pscustomobject]@{ Name = 'run'; Start = 90; End = 149; Gait = 'Running'; MinimumSpeed = 1.5; MinimumDisplacement = 2.0 },
                    [pscustomobject]@{ Name = 'sprint'; Start = 150; End = 209; Gait = 'Sprinting'; MinimumSpeed = 3.0; MinimumDisplacement = 3.0 }
                )
                foreach ($phase in $movementPhases)
                {
                    $phaseFrames = @($frames | Where-Object { $_.index -ge $phase.Start -and $_.index -le $phase.End })
                    $invalidCommands = @($phaseFrames | Where-Object {
                        $_.command.requestedGait -cne $phase.Gait -or
                        [math]::Sqrt(([double]$_.command.movementAxes.x * [double]$_.command.movementAxes.x) +
                            ([double]$_.command.movementAxes.y * [double]$_.command.movementAxes.y)) -lt 0.5
                    })
                    if ($phaseFrames.Count -ne 60 -or $invalidCommands.Count -ne 0)
                    {
                        throw "idle_gaits $($phase.Name) command segment does not match its defined gait and movement input."
                    }

                    $movingWithActualGait = @($phaseFrames | Where-Object {
                        $_.actual.gait -ceq $phase.Gait -and
                        [math]::Sqrt(([double]$_.actual.velocity.x * [double]$_.actual.velocity.x) +
                            ([double]$_.actual.velocity.y * [double]$_.actual.velocity.y)) -ge $phase.MinimumSpeed
                    })
                    $firstPosition = $phaseFrames[0].actual.position
                    $lastPosition = $phaseFrames[-1].actual.position
                    $deltaX = [double]$lastPosition.x - [double]$firstPosition.x
                    $deltaY = [double]$lastPosition.y - [double]$firstPosition.y
                    $phaseDisplacement = [math]::Sqrt(($deltaX * $deltaX) + ($deltaY * $deltaY))
                    if ($movingWithActualGait.Count -lt 30 -or $phaseDisplacement -lt $phase.MinimumDisplacement)
                    {
                        throw "idle_gaits $($phase.Name) segment lacks real movement evidence for actual gait $($phase.Gait): movingFrames=$($movingWithActualGait.Count) displacement=$phaseDisplacement."
                    }
                }
            }
            'directions'
            {
                $movingFrames = @($frames | Where-Object {
                    [math]::Abs([double]$_.actual.velocity.x) -gt 0.1 -or
                    [math]::Abs([double]$_.actual.velocity.y) -gt 0.1
                })
                if (-not ($movingFrames.actual.velocity.x | Where-Object { $_ -gt 0.1 }) -or
                    -not ($movingFrames.actual.velocity.x | Where-Object { $_ -lt -0.1 }) -or
                    -not ($movingFrames.actual.velocity.y | Where-Object { $_ -gt 0.1 }) -or
                    -not ($movingFrames.actual.velocity.y | Where-Object { $_ -lt -0.1 }))
                {
                    throw 'directions does not cover positive and negative movement on both horizontal axes.'
                }
            }
            'crouch_clearance'
            {
                $blockedCrouching = @($frames | Where-Object {
                    $_.command.standBlocked -and $_.command.requestedStance -ceq 'Standing' -and
                    $_.actual.stance -ceq 'Crouching'
                })
                if ($blockedCrouching.Count -eq 0)
                {
                    throw 'crouch_clearance never keeps the actual stance crouched while a blocked stand is requested.'
                }
                $lastBlockedFrame = ($blockedCrouching.index | Measure-Object -Maximum).Maximum
                $clearStanding = @($frames | Where-Object {
                    $_.index -gt $lastBlockedFrame -and -not $_.command.standBlocked -and
                    $_.command.requestedStance -ceq 'Standing' -and $_.actual.stance -ceq 'Standing'
                })
                if ($clearStanding.Count -eq 0)
                {
                    throw 'crouch_clearance never transitions to actual standing after overhead clearance is restored.'
                }
            }
            'rotation_modes'
            {
                foreach ($mode in @('VelocityDirection', 'LookingDirection', 'Aiming'))
                {
                    if ($mode -cnotin @($frames.actual.rotationMode)) { throw "rotation_modes never reached $mode." }
                }
            }
            'jump_land'
            {
                if ($null -eq $settingsValues) { throw 'jump_land validation requires the settings document.' }
                $analogFrames = @($frames | Where-Object {
                    $_.index -ge 100 -and $_.index -le 169 -and $_.actual.grounded -and
                    $_.command.requestedGait -ceq 'Running' -and
                    [math]::Abs([math]::Sqrt(
                        ([double]$_.command.movementAxes.x * [double]$_.command.movementAxes.x) +
                        ([double]$_.command.movementAxes.y * [double]$_.command.movementAxes.y)) - 0.65) -lt 1e-6
                })
                if ($analogFrames.Count -lt 60)
                {
                    throw "jump_land analog-limited segment is missing its grounded 0.65 input frames."
                }
                $analogSpeeds = @($analogFrames | ForEach-Object {
                    [math]::Sqrt(([double]$_.actual.velocity.x * [double]$_.actual.velocity.x) +
                        ([double]$_.actual.velocity.y * [double]$_.actual.velocity.y))
                })
                $analogSteadySpeeds = @($analogSpeeds | Select-Object -Last 30)
                $analogMinimum = ($analogSteadySpeeds | Measure-Object -Minimum).Minimum
                $analogMaximum = ($analogSteadySpeeds | Measure-Object -Maximum).Maximum
                $fullRunSpeed = [double]$settingsValues.runForwardSpeed
                if ($analogMinimum -le 0.1 -or $analogMaximum -ge ($fullRunSpeed * 0.9))
                {
                    throw "jump_land analog-limited 0.65 input does not produce a nonzero speed materially below the full running cap: min=$analogMinimum max=$analogMaximum fullRun=$fullRunSpeed."
                }

                $jumpStart = @($frames | Where-Object {
                    $_.actual.animationState -ceq 'JumpStart' -and -not $_.actual.grounded -and
                    $_.actual.jumpTransition -and [double]$_.actual.velocity.z -gt 0
                } | Select-Object -First 1)
                if ($jumpStart.Count -eq 0)
                {
                    throw 'jump_land lacks a real positive-vertical-motion JumpStart transition.'
                }
                $fallLoop = @($frames | Where-Object {
                    $_.index -gt $jumpStart[0].index -and -not $_.actual.grounded -and
                    $_.actual.animationState -ceq 'FallLoop'
                } | Select-Object -First 1)
                if ($fallLoop.Count -eq 0)
                {
                    throw 'jump_land lacks FallLoop after JumpStart.'
                }
                $landRecovery = @($frames | Where-Object {
                    $_.index -gt $fallLoop[0].index -and $_.actual.grounded -and
                    $_.actual.animationState -ceq 'LandRecovery'
                } | Select-Object -First 1)
                if ($landRecovery.Count -eq 0)
                {
                    throw 'jump_land lacks grounded LandRecovery after FallLoop.'
                }
            }
        }
    }
    if ($seenSequences.Count -ne 5) { throw "Expected five unique trace sequences, got $($seenSequences.Count)." }
    if ($null -eq $settingsValues) { throw 'Generated output has no settings values.' }
    if (-not $hasDynamicMaxAcceleration -or -not $hasDynamicMaxBrakingDeceleration)
    {
        throw "Generated traces do not observe runtime max acceleration/braking variation: acceleration=$hasDynamicMaxAcceleration braking=$hasDynamicMaxBrakingDeceleration."
    }
    foreach ($limit in @(
        [pscustomobject]@{ Name = 'initialMaxAcceleration'; Initial = [double]$settingsValues.initialMaxAcceleration; Observed = $observedMaxAccelerations },
        [pscustomobject]@{ Name = 'initialMaxBrakingDeceleration'; Initial = [double]$settingsValues.initialMaxBrakingDeceleration; Observed = $observedMaxBrakingDecelerations }
    ))
    {
        $matchingInitialSample = @($limit.Observed | Where-Object { [math]::Abs($_ - $limit.Initial) -le 1e-6 })
        if ($matchingInitialSample.Count -eq 0)
        {
            throw "Settings $($limit.Name)=$($limit.Initial) is not represented by any per-frame runtime sample."
        }
    }
}

function Replace-Atomically([string]$Source, [string]$Destination)
{
    $destinationDirectory = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
    $temporary = Join-Path $destinationDirectory ('.' + [System.IO.Path]::GetFileName($Destination) + '.' + [guid]::NewGuid().ToString('N') + '.tmp')
    Copy-Item -LiteralPath $Source -Destination $temporary
    try
    {
        if (Test-Path -LiteralPath $Destination -PathType Leaf)
        {
            $backup = "$temporary.bak"
            [System.IO.File]::Replace($temporary, $Destination, $backup, $true)
            if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Force }
        }
        else
        {
            Move-Item -LiteralPath $temporary -Destination $Destination
        }
    }
    finally
    {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}

function Get-P3OutputTransactionRoot([string]$ProjectRoot)
{
    Get-FullPath (Join-Path (Get-FullPath $ProjectRoot) '.p3-output-transactions')
}

function Get-ValidatedP3OutputMappings([string]$OutputDirectory, [string]$ProjectRoot)
{
    $outputRoot = Get-FullPath $OutputDirectory
    $repositoryRoot = Get-FullPath $ProjectRoot
    Assert-NoReparsePathComponents $outputRoot $outputRoot 'Generated output directory'
    [void](Get-ValidatedTreeEntries $outputRoot 'Generated output directory')
    Assert-NoReparsePathComponents $repositoryRoot $repositoryRoot 'Repository root'

    $mappings = [System.Collections.Generic.List[object]]::new()
    foreach ($entry in $outputDestinations.GetEnumerator())
    {
        $source = Get-FullPath (Join-Path $outputRoot $entry.Key)
        if (-not (Test-IsChildPath $source $outputRoot) -or -not (Test-Path -LiteralPath $source -PathType Leaf))
        {
            throw "Generated output source is missing or unsafe: $source"
        }
        Assert-NoReparsePathComponents $source $outputRoot 'Generated output source'

        $destination = Get-FullPath (Join-Path $repositoryRoot $entry.Value)
        if (-not (Test-IsChildPath $destination $repositoryRoot))
        {
            throw "Generated output destination escapes repository root: $destination"
        }
        Assert-NoReparsePathComponents $destination $repositoryRoot 'Generated output destination'
        if (Test-Path -LiteralPath $destination)
        {
            $destinationItem = Get-Item -LiteralPath $destination -Force
            if ($destinationItem.PSIsContainer -or
                ($destinationItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
            {
                throw "Generated output destination is not a regular file: $destination"
            }
        }
        $mappings.Add([pscustomobject]@{
            FileName = [string]$entry.Key
            RelativeDestination = [string]$entry.Value
            Source = $source
            Destination = $destination
        })
    }
    return @($mappings)
}

function Restore-P3OutputTransaction([string]$TransactionDirectory, [string]$ProjectRoot)
{
    $repositoryRoot = Get-FullPath $ProjectRoot
    $transactionRoot = Get-P3OutputTransactionRoot $repositoryRoot
    $transactionDirectoryFull = Get-FullPath $TransactionDirectory
    if (-not (Test-IsChildPath $transactionDirectoryFull $transactionRoot))
    {
        throw "P3 output transaction escapes its owned root: $transactionDirectoryFull"
    }
    Assert-NoReparsePathComponents $transactionDirectoryFull $transactionRoot 'P3 output transaction'
    [void](Get-ValidatedTreeEntries $transactionDirectoryFull 'P3 output transaction')
    $journalPath = Join-Path $transactionDirectoryFull 'journal.json'
    if (-not (Test-Path -LiteralPath $journalPath -PathType Leaf))
    {
        throw "P3 output transaction has no recovery journal: $transactionDirectoryFull"
    }
    $journal = Get-Content -LiteralPath $journalPath -Raw | ConvertFrom-Json
    Assert-ExactProperties $journal @('entries', 'version') $journalPath
    if ($journal.version -ne 1 -or @($journal.entries).Count -ne $outputDestinations.Count)
    {
        throw "Invalid P3 output transaction journal: $journalPath"
    }

    $journalByRelativePath = @{}
    foreach ($entry in @($journal.entries))
    {
        Assert-ExactProperties $entry @('backupName', 'fileName', 'hadOriginal', 'relativeDestination') "$journalPath entry"
        if ($entry.relativeDestination -cnotin @($outputDestinations.Values) -or
            $outputDestinations[[string]$entry.fileName] -cne [string]$entry.relativeDestination -or
            $journalByRelativePath.ContainsKey([string]$entry.relativeDestination))
        {
            throw "P3 output transaction journal contains an unexpected destination: $($entry.relativeDestination)"
        }
        if ([System.IO.Path]::GetFileName([string]$entry.backupName) -cne [string]$entry.backupName)
        {
            throw "P3 output transaction journal contains an unsafe backup name: $($entry.backupName)"
        }
        $journalByRelativePath[[string]$entry.relativeDestination] = $entry
    }
    if ($journalByRelativePath.Count -ne $outputDestinations.Count)
    {
        throw "P3 output transaction journal does not contain the exact six destinations: $journalPath"
    }

    foreach ($relativeDestination in @($outputDestinations.Values))
    {
        $entry = $journalByRelativePath[[string]$relativeDestination]
        $destination = Get-FullPath (Join-Path $repositoryRoot $relativeDestination)
        Assert-NoReparsePathComponents $destination $repositoryRoot 'P3 rollback destination'
        if ([bool]$entry.hadOriginal)
        {
            $backup = Get-FullPath (Join-Path $transactionDirectoryFull ([string]$entry.backupName))
            if (-not (Test-IsChildPath $backup $transactionDirectoryFull) -or
                -not (Test-Path -LiteralPath $backup -PathType Leaf))
            {
                throw "P3 rollback backup is missing or unsafe: $backup"
            }
            Replace-Atomically $backup $destination
        }
        elseif (Test-Path -LiteralPath $destination)
        {
            $destinationItem = Get-Item -LiteralPath $destination -Force
            if ($destinationItem.PSIsContainer -or
                ($destinationItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
            {
                throw "P3 rollback refuses a non-regular destination: $destination"
            }
            Remove-Item -LiteralPath $destination -Force
        }
    }
    Remove-ValidatedTree $transactionDirectoryFull $transactionRoot 'Completed P3 output transaction'
}

function Recover-P3OutputTransactions([string]$ProjectRoot)
{
    $repositoryRoot = Get-FullPath $ProjectRoot
    $transactionRoot = Get-P3OutputTransactionRoot $repositoryRoot
    Assert-NoReparsePathComponents $transactionRoot $repositoryRoot 'P3 output transaction root'
    if (-not (Test-Path -LiteralPath $transactionRoot)) { return }
    [void](Get-ValidatedTreeEntries $transactionRoot 'P3 output transaction root')
    foreach ($transaction in @(Get-ChildItem -LiteralPath $transactionRoot -Force))
    {
        if (-not $transaction.PSIsContainer) { throw "Unexpected file in P3 output transaction root: $($transaction.FullName)" }
        $journalPath = Join-Path $transaction.FullName 'journal.json'
        if (Test-Path -LiteralPath $journalPath -PathType Leaf)
        {
            Restore-P3OutputTransaction $transaction.FullName $repositoryRoot
        }
        else
        {
            # Publication never starts before the journal is durable, so an unjournaled staging directory is inert.
            Remove-ValidatedTree $transaction.FullName $transactionRoot 'Unjournaled P3 output transaction'
        }
    }
}

function Publish-P3GeneratedOutputSet(
    [string]$OutputDirectory,
    [string]$ProjectRoot,
    [int]$InjectFailureAfter = 0)
{
    $repositoryRoot = Get-FullPath $ProjectRoot
    Recover-P3OutputTransactions $repositoryRoot
    $mappings = @(Get-ValidatedP3OutputMappings $OutputDirectory $repositoryRoot)
    if ($mappings.Count -ne $outputDestinations.Count) { throw 'P3 output publication requires exactly six mappings.' }

    $transactionRoot = Get-P3OutputTransactionRoot $repositoryRoot
    if (-not (Test-Path -LiteralPath $transactionRoot))
    {
        New-Item -ItemType Directory -Path $transactionRoot | Out-Null
    }
    Assert-NoReparsePathComponents $transactionRoot $repositoryRoot 'P3 output transaction root'
    $transactionDirectory = Join-Path $transactionRoot ([guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $transactionDirectory | Out-Null
    $journalWritten = $false
    try
    {
        $journalEntries = [System.Collections.Generic.List[object]]::new()
        $backupIndex = 0
        foreach ($mapping in $mappings)
        {
            $hadOriginal = Test-Path -LiteralPath $mapping.Destination -PathType Leaf
            $backupName = "$backupIndex.bak"
            if ($hadOriginal)
            {
                Copy-Item -LiteralPath $mapping.Destination -Destination (Join-Path $transactionDirectory $backupName)
            }
            $journalEntries.Add([ordered]@{
                backupName = $backupName
                fileName = $mapping.FileName
                hadOriginal = $hadOriginal
                relativeDestination = $mapping.RelativeDestination
            })
            $backupIndex++
        }
        $journal = [ordered]@{ entries = @($journalEntries); version = 1 }
        $journalTemporary = Join-Path $transactionDirectory 'journal.tmp'
        [System.IO.File]::WriteAllText($journalTemporary, ($journal | ConvertTo-Json -Depth 5),
            [System.Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $journalTemporary -Destination (Join-Path $transactionDirectory 'journal.json')
        $journalWritten = $true

        $replacementCount = 0
        foreach ($mapping in $mappings)
        {
            Replace-Atomically $mapping.Source $mapping.Destination
            $replacementCount++
            if ($InjectFailureAfter -gt 0 -and $replacementCount -eq $InjectFailureAfter)
            {
                throw "Injected output publication failure after $replacementCount replacements."
            }
        }
        Remove-ValidatedTree $transactionDirectory $transactionRoot 'Successful P3 output transaction'
        $journalWritten = $false
    }
    catch
    {
        $publicationError = $_
        if ($journalWritten -and (Test-Path -LiteralPath $transactionDirectory))
        {
            Restore-P3OutputTransaction $transactionDirectory $repositoryRoot
            $journalWritten = $false
        }
        elseif (Test-Path -LiteralPath $transactionDirectory)
        {
            Remove-ValidatedTree $transactionDirectory $transactionRoot 'Failed unjournaled P3 output transaction'
        }
        throw $publicationError
    }
}

if ($MyInvocation.InvocationName -ceq '.') { return }

try
{
    $resolvedProjectRoot = Get-FullPath (Resolve-Path -LiteralPath $ProjectRoot).Path
    $resolvedReferenceRoot = Get-FullPath (Resolve-Path -LiteralPath $ReferenceRoot).Path
    $resolvedEditor = (Resolve-Path -LiteralPath $UnrealEditorCmd).Path
    $resolvedProject = (Resolve-Path -LiteralPath $UProject).Path
    $projectFileHashAtStart = (Get-FileHash -LiteralPath $resolvedProject -Algorithm SHA256).Hash
    $expectedProject = Get-FullPath '../AdvancedLocomotionSystemV\AdvancedLocomotionSystemV.uproject'
    if (-not [System.StringComparer]::OrdinalIgnoreCase.Equals((Get-FullPath $resolvedProject), $expectedProject))
    {
        throw "Unexpected UE project file: $resolvedProject; expected $expectedProject"
    }
    Recover-P3OutputTransactions $resolvedProjectRoot

    $referenceOutput = @(& (Join-Path $resolvedProjectRoot 'scripts\prepare-p3-reference.ps1') `
        -ReferenceRoot $resolvedReferenceRoot -ProjectRoot $resolvedProjectRoot)
    $expectedReferenceMarker = "P3_REFERENCE_OK commit=$lockedCommit patches=1"
    if ($LASTEXITCODE -ne 0 -or $referenceOutput.Count -ne 1 -or $referenceOutput[0] -cne $expectedReferenceMarker)
    {
        throw "P3 reference preparation did not return exact marker '$expectedReferenceMarker': $($referenceOutput -join [Environment]::NewLine)"
    }

    $engineRoot = Get-FullPath (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $resolvedEditor))))
    $buildVersionPath = Join-Path $engineRoot 'Engine\Build\Build.version'
    $buildVersion = Get-Content -LiteralPath $buildVersionPath -Raw | ConvertFrom-Json
    $engineVersion = "$($buildVersion.MajorVersion).$($buildVersion.MinorVersion).$($buildVersion.PatchVersion)"
    if ($engineVersion -cne '5.9.0') { throw "Expected Unreal Engine 5.9.0, got $engineVersion at $engineRoot." }

    $projectDirectory = Get-FullPath (Split-Path -Parent $resolvedProject)
    Ensure-AlsJunction $projectDirectory $resolvedReferenceRoot
    Sync-OwnedPlugin (Join-Path $resolvedProjectRoot 'tools\unreal\AlsLocomotionTrace') $projectDirectory

    Build-And-AuditEditorTarget $resolvedProject $projectDirectory $engineRoot

    $temporaryOutput = Join-Path ([System.IO.Path]::GetTempPath()) "godot-als-p3-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $temporaryOutput | Out-Null
    $invocationId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [guid]::NewGuid().ToString('N')
    $commandletLog = Join-Path $projectDirectory "Saved\Logs\P3Trace\$invocationId.log"
    $arguments = @(
        $resolvedProject,
        '-run=AlsLocomotionTrace',
        "-Output=$temporaryOutput",
        "-ReferenceRoot=$resolvedReferenceRoot",
        "-ReferenceCommit=$lockedCommit",
        "-PatchHashes=$lockedPatchHash",
        '-stdout',
        '-FullStdOutLogOutput',
        '-unattended',
        '-nosplash',
        '-nullrhi',
        '-nosound',
        "-abslog=$commandletLog"
    )
    if ($ReadyCheck) { $arguments += '-ReadyCheck' }
    $processLog = "$commandletLog.process.log"
    $result = Invoke-NativeTool $resolvedEditor $arguments $processLog
    if ($result.ExitCode -ne 0) { throw "ALS locomotion commandlet failed ($($result.ExitCode)); log=$processLog" }

    if ($ReadyCheck)
    {
        if (-not (Test-UnrealLogMarker $commandletLog 'P3_TRACE_READY_OK'))
        {
            throw "Ready check did not emit exact marker P3_TRACE_READY_OK; log=$processLog"
        }
        if (@(Get-ChildItem -LiteralPath $temporaryOutput -Force).Count -ne 0)
        {
            throw 'Ready check unexpectedly wrote output artifacts.'
        }
        if ((Get-FileHash -LiteralPath $resolvedProject -Algorithm SHA256).Hash -cne $projectFileHashAtStart)
        {
            throw 'UE project descriptor changed during ReadyCheck.'
        }
        Write-Output 'P3_TRACE_READY_OK'
        exit 0
    }

    $expectedGenerationMarker = "P3_TRACE_GENERATION_OK sequences=5 commit=$lockedCommit"
    if (-not (Test-UnrealLogMarker $commandletLog $expectedGenerationMarker))
    {
        throw "Generation did not emit exact marker '$expectedGenerationMarker'; log=$processLog"
    }
    $schemaPath = Join-Path $resolvedProjectRoot 'tools\schemas\als_locomotion_trace.schema.json'
    Validate-GeneratedOutput $temporaryOutput $schemaPath

    Publish-P3GeneratedOutputSet $temporaryOutput $resolvedProjectRoot
    if ((Get-FileHash -LiteralPath $resolvedProject -Algorithm SHA256).Hash -cne $projectFileHashAtStart)
    {
        throw 'UE project descriptor changed during trace generation.'
    }
    Write-Output $expectedGenerationMarker
}
finally
{
    if ($null -ne $temporaryOutput -and (Test-Path -LiteralPath $temporaryOutput))
    {
        Remove-Item -LiteralPath $temporaryOutput -Recurse -Force
    }
}

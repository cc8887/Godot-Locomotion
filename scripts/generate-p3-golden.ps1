param(
    [Parameter(Mandatory = $true)]
    [string]$UnrealEditorCmd,

    [Parameter(Mandatory = $true)]
    [string]$UProject,

    [string]$ReferenceRoot = 'D:\GodotALS-References\ALS-Refactored',
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [switch]$ReadyCheck
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$lockedCommit = 'b754d6f0f2bb03741d301f8fb88077ebfe561e17'
$lockedPatchHash = '3dc561f194045d3dc01bd65c7f7c3bd4acd0a30c0fab31ea0cd16d676d312e5f'
$sequenceNames = @('idle_gaits', 'directions', 'crouch_clearance', 'rotation_modes', 'jump_land')
$expectedOutputNames = @($sequenceNames | ForEach-Object { "trace_$_.json" }) + 'p3_locomotion_settings.json'
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
    $pluginsDirectory = Get-FullPath (Join-Path $ProjectDirectory 'Plugins')
    $destination = Join-Path $pluginsDirectory 'AlsLocomotionTrace'
    if (-not (Test-IsChildPath $destination $pluginsDirectory)) { throw "Unsafe plugin destination: $destination" }
    if (Test-Path -LiteralPath $destination)
    {
        $destinationItem = Get-Item -LiteralPath $destination -Force
        if (($destinationItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
        {
            throw "Owned plugin destination must not be a reparse point: $destination"
        }
    }
    else
    {
        New-Item -ItemType Directory -Path $destination | Out-Null
    }

    $sourceFiles = @(Get-ChildItem -LiteralPath $RepositoryPlugin -Recurse -File | Sort-Object FullName)
    $relativeFiles = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($sourceFile in $sourceFiles)
    {
        $relative = [System.IO.Path]::GetRelativePath($RepositoryPlugin, $sourceFile.FullName)
        [void]$relativeFiles.Add($relative)
        $target = Join-Path $destination $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $sourceFile.FullName -Destination $target -Force
    }

    foreach ($existing in @(Get-ChildItem -LiteralPath $destination -Recurse -File | Sort-Object FullName -Descending))
    {
        $relative = [System.IO.Path]::GetRelativePath($destination, $existing.FullName)
        if ($relative -like 'Binaries\*' -or $relative -like 'Intermediate\*') { continue }
        if (-not $relativeFiles.Contains($relative)) { Remove-Item -LiteralPath $existing.FullName -Force }
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
            $requiredValues = @('walkForwardSpeed', 'runForwardSpeed', 'sprintSpeed', 'maxAcceleration',
                'maxBrakingDeceleration', 'crouchedHalfHeight', 'rotationInterpolationHalfLife',
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
                'playRate', 'position', 'rotationMode', 'stance', 'stride', 'targetYaw', 'velocity', 'yaw') `
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
            $expectedIndex++
        }

        $frames = @($document.frames)
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
                foreach ($gait in @('Walking', 'Running', 'Sprinting'))
                {
                    if ($gait -cnotin @($frames.actual.gait)) { throw "idle_gaits never reached $gait." }
                }
                if (@($frames | Select-Object -First 30 | Where-Object {
                    [math]::Abs([double]$_.actual.velocity.x) -gt 0.01 -or
                    [math]::Abs([double]$_.actual.velocity.y) -gt 0.01
                }).Count -ne 0) { throw 'idle_gaits failed its initial idle segment.' }
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

try
{
    $resolvedProjectRoot = Get-FullPath (Resolve-Path -LiteralPath $ProjectRoot).Path
    $resolvedReferenceRoot = Get-FullPath (Resolve-Path -LiteralPath $ReferenceRoot).Path
    $resolvedEditor = (Resolve-Path -LiteralPath $UnrealEditorCmd).Path
    $resolvedProject = (Resolve-Path -LiteralPath $UProject).Path
    $projectFileHashAtStart = (Get-FileHash -LiteralPath $resolvedProject -Algorithm SHA256).Hash
    $expectedProject = Get-FullPath 'D:\AdvancedLocomotionSystemV\AdvancedLocomotionSystemV.uproject'
    if (-not [System.StringComparer]::OrdinalIgnoreCase.Equals((Get-FullPath $resolvedProject), $expectedProject))
    {
        throw "Unexpected UE project file: $resolvedProject; expected $expectedProject"
    }

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

    Replace-Atomically (Join-Path $temporaryOutput 'p3_locomotion_settings.json') `
        (Join-Path $resolvedProjectRoot 'assets\config\p3_locomotion_settings.json')
    foreach ($sequenceName in $sequenceNames)
    {
        $fileName = "trace_$sequenceName.json"
        Replace-Atomically (Join-Path $temporaryOutput $fileName) `
            (Join-Path $resolvedProjectRoot "tests\Als.Core.Tests\Fixtures\P3\$fileName")
    }
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

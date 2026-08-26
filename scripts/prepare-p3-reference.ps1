param(
    [string]$ReferenceRoot = '../GodotALS-References\ALS-Refactored',
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$lockPath = Join-Path $ProjectRoot 'reference\als-refactored.lock.json'
if (-not (Test-Path -LiteralPath $lockPath -PathType Leaf))
{
    throw "P3 reference lock file does not exist: $lockPath"
}

$lockJson = Get-Content -LiteralPath $lockPath -Raw
try
{
    $lockDocument = [System.Text.Json.JsonDocument]::Parse(
        $lockJson,
        [System.Text.Json.JsonDocumentOptions]@{
            AllowTrailingCommas = $false
            CommentHandling = [System.Text.Json.JsonCommentHandling]::Disallow
        })
}
catch
{
    throw "P3 reference lock file is not valid strict JSON: $($_.Exception.Message)"
}

try
{
    $lockRoot = $lockDocument.RootElement
    if ($lockRoot.ValueKind -ne [System.Text.Json.JsonValueKind]::Object)
    {
        throw 'P3 reference lock root must be a JSON object.'
    }

    $expectedProperties = @(
        'schemaVersion',
        'repository',
        'commit',
        'observedDate',
        'targetEngine',
        'compatibilityPatches'
    )
    $seenProperties = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($property in $lockRoot.EnumerateObject())
    {
        if (-not $seenProperties.Add($property.Name))
        {
            throw "P3 reference lock contains duplicate property '$($property.Name)'."
        }

        if ($property.Name -cnotin $expectedProperties)
        {
            throw "P3 reference lock contains unknown property '$($property.Name)'."
        }
    }

    foreach ($propertyName in $expectedProperties)
    {
        if (-not $seenProperties.Contains($propertyName))
        {
            throw "P3 reference lock is missing required property '$propertyName'."
        }
    }

    $schemaVersionElement = $lockRoot.GetProperty('schemaVersion')
    if ($schemaVersionElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Number -or
        $schemaVersionElement.GetRawText() -cne '1')
    {
        throw "P3 reference lock schemaVersion must be the integer 1."
    }

    foreach ($propertyName in @('repository', 'commit', 'observedDate', 'targetEngine'))
    {
        if ($lockRoot.GetProperty($propertyName).ValueKind -ne [System.Text.Json.JsonValueKind]::String)
        {
            throw "P3 reference lock property '$propertyName' must be a string."
        }
    }

    $lockedRepository = $lockRoot.GetProperty('repository').GetString()
    $lockedCommit = $lockRoot.GetProperty('commit').GetString()
    $patchesElement = $lockRoot.GetProperty('compatibilityPatches')
    if ($patchesElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Array)
    {
        throw "P3 reference lock property 'compatibilityPatches' must be an array."
    }

    if ($lockedCommit -cnotmatch '^[0-9a-f]{40}$')
    {
        throw 'P3 reference lock commit must be a lowercase 40-character SHA.'
    }

    if (-not (Test-Path -LiteralPath $ReferenceRoot -PathType Container))
    {
        throw "ALS-Refactored repository directory does not exist: $ReferenceRoot"
    }

    $referenceFullPath = [System.IO.Path]::GetFullPath($ReferenceRoot).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $gitRootOutput = @(& git -C $referenceFullPath rev-parse --show-toplevel 2>&1)
    if ($LASTEXITCODE -ne 0)
    {
        throw "ALS-Refactored directory is not a git repository: $($gitRootOutput -join [Environment]::NewLine)"
    }

    $gitRoot = [System.IO.Path]::GetFullPath(($gitRootOutput -join '').Trim()).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    if (-not [System.StringComparer]::OrdinalIgnoreCase.Equals($gitRoot, $referenceFullPath))
    {
        throw "ALS-Refactored path is not the git repository root. Expected '$referenceFullPath', actual '$gitRoot'."
    }

    $originOutput = @(& git -C $referenceFullPath remote get-url origin 2>&1)
    if ($LASTEXITCODE -ne 0)
    {
        throw "ALS-Refactored repository has no readable origin remote: $($originOutput -join [Environment]::NewLine)"
    }

    $origin = $originOutput -join ''
    $normalizedOrigin = $origin
    if ($normalizedOrigin.EndsWith('.git', [System.StringComparison]::Ordinal))
    {
        $normalizedOrigin = $normalizedOrigin.Substring(0, $normalizedOrigin.Length - 4)
    }

    $normalizedLockedRepository = $lockedRepository
    if ($normalizedLockedRepository.EndsWith('.git', [System.StringComparison]::Ordinal))
    {
        $normalizedLockedRepository = $normalizedLockedRepository.Substring(0, $normalizedLockedRepository.Length - 4)
    }

    if (-not [System.StringComparer]::Ordinal.Equals($normalizedOrigin, $normalizedLockedRepository))
    {
        throw "ALS-Refactored origin mismatch. Expected '$lockedRepository', actual '$origin'."
    }

    $headOutput = @(& git -C $referenceFullPath rev-parse HEAD 2>&1)
    if ($LASTEXITCODE -ne 0)
    {
        throw "Could not read ALS-Refactored HEAD: $($headOutput -join [Environment]::NewLine)"
    }

    $headCommit = ($headOutput -join '').Trim()
    if ($headCommit -cne $lockedCommit)
    {
        throw "ALS-Refactored HEAD mismatch. Expected '$lockedCommit', actual '$headCommit'."
    }

    $statusOutput = @(& git -C $referenceFullPath status --porcelain 2>&1)
    if ($LASTEXITCODE -ne 0)
    {
        throw "Could not read ALS-Refactored worktree status: $($statusOutput -join [Environment]::NewLine)"
    }

    if ($statusOutput.Count -ne 0)
    {
        throw "ALS-Refactored worktree is not clean: $($statusOutput -join [Environment]::NewLine)"
    }

    $patchCount = 0
    $validatedPatches = @()
    $referencePathPrefix = $referenceFullPath + [System.IO.Path]::DirectorySeparatorChar
    foreach ($patchElement in $patchesElement.EnumerateArray())
    {
        $patchCount++
        if ($patchElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Object)
        {
            throw "Compatibility patch at index $($patchCount - 1) must be an object."
        }

        $patchProperties = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($property in $patchElement.EnumerateObject())
        {
            if (-not $patchProperties.Add($property.Name))
            {
                throw "Compatibility patch at index $($patchCount - 1) contains duplicate property '$($property.Name)'."
            }

            if ($property.Name -cnotin @('path', 'sha256'))
            {
                throw "Compatibility patch at index $($patchCount - 1) contains unknown property '$($property.Name)'."
            }
        }

        foreach ($propertyName in @('path', 'sha256'))
        {
            if (-not $patchProperties.Contains($propertyName))
            {
                throw "Compatibility patch at index $($patchCount - 1) is missing required property '$propertyName'."
            }

            if ($patchElement.GetProperty($propertyName).ValueKind -ne [System.Text.Json.JsonValueKind]::String)
            {
                throw "Compatibility patch at index $($patchCount - 1) property '$propertyName' must be a string."
            }
        }

        $patchRelativePath = $patchElement.GetProperty('path').GetString()
        $expectedSha256 = $patchElement.GetProperty('sha256').GetString()
        if ([string]::IsNullOrWhiteSpace($patchRelativePath) -or [System.IO.Path]::IsPathRooted($patchRelativePath))
        {
            throw "Compatibility patch path must be a non-empty repository-relative path: '$patchRelativePath'."
        }

        $patchFullPath = [System.IO.Path]::GetFullPath((Join-Path $referenceFullPath $patchRelativePath))
        if (-not $patchFullPath.StartsWith($referencePathPrefix, [System.StringComparison]::OrdinalIgnoreCase))
        {
            throw "Compatibility patch path escapes the ALS-Refactored repository: '$patchRelativePath'."
        }

        if (-not (Test-Path -LiteralPath $patchFullPath -PathType Leaf))
        {
            throw "Compatibility patch file does not exist: '$patchRelativePath'."
        }

        $rootItem = Get-Item -LiteralPath $referenceFullPath -Force
        if (($rootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
        {
            throw "ALS-Refactored repository root must not be a reparse point: '$referenceFullPath'."
        }

        $normalizedRelativePath = [System.IO.Path]::GetRelativePath($referenceFullPath, $patchFullPath)
        $pathComponents = @($normalizedRelativePath -split '[\\/]+')
        $currentPath = $referenceFullPath
        for ($componentIndex = 0; $componentIndex -lt $pathComponents.Count; $componentIndex++)
        {
            $currentPath = Join-Path $currentPath $pathComponents[$componentIndex]
            $currentItem = Get-Item -LiteralPath $currentPath -Force
            if (($currentItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
            {
                throw "Compatibility patch path contains a reparse point: '$currentPath'."
            }

            if ($componentIndex -lt $pathComponents.Count - 1 -and -not $currentItem.PSIsContainer)
            {
                throw "Compatibility patch path component is not a directory: '$currentPath'."
            }
        }

        if ($currentItem.PSIsContainer -or $currentItem -isnot [System.IO.FileInfo] -or
            -not $currentItem.FullName.StartsWith($referencePathPrefix, [System.StringComparison]::OrdinalIgnoreCase))
        {
            throw "Compatibility patch must resolve to an ordinary file within the ALS-Refactored repository: '$patchRelativePath'."
        }

        if ($expectedSha256 -cnotmatch '^[0-9a-f]{64}$')
        {
            throw "Compatibility patch sha256 must be 64 lowercase hexadecimal characters: '$expectedSha256'."
        }

        $actualSha256 = (Get-FileHash -LiteralPath $patchFullPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualSha256 -cne $expectedSha256)
        {
            throw "Compatibility patch SHA256 mismatch for '$patchRelativePath'. Expected '$expectedSha256', actual '$actualSha256'."
        }

        $validatedPatches += [pscustomobject]@{
            RelativePath = $patchRelativePath
            FullPath = $patchFullPath
        }
    }

    foreach ($validatedPatch in $validatedPatches)
    {
        $applyOutput = @(& git -C $referenceFullPath apply -- $validatedPatch.FullPath 2>&1)
        if ($LASTEXITCODE -ne 0)
        {
            throw "Could not apply compatibility patch '$($validatedPatch.RelativePath)': $($applyOutput -join [Environment]::NewLine)"
        }
    }
}
finally
{
    $lockDocument.Dispose()
}

Write-Output "P3_REFERENCE_OK commit=$lockedCommit patches=$patchCount"

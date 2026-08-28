function Read-AlsExportLock {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$LockPath)

    if (-not (Test-Path -LiteralPath $LockPath -PathType Leaf)) { throw "ALS export lock does not exist: $LockPath" }
    try { $document = [System.Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText($LockPath)) }
    catch { throw "ALS export lock is not valid JSON: $($_.Exception.Message)" }
    try {
        if ($document.RootElement.ValueKind -ne [Text.Json.JsonValueKind]::Object) { throw 'ALS export lock must be a JSON object.' }
        $required = @('schemaVersion', 'manifestSha256', 'assetCount', 'fileCount', 'animationCount', 'exporterVersion', 'sourceProjectId')
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $values = @{}
        foreach ($property in $document.RootElement.EnumerateObject()) {
            if ($property.Name -cnotin $required) { throw "ALS export lock contains unknown property: $($property.Name)" }
            if (-not $seen.Add($property.Name)) { throw "ALS export lock contains duplicate property: $($property.Name)" }
            $values[$property.Name] = $property.Value.Clone()
        }
        foreach ($name in $required) { if (-not $seen.Contains($name)) { throw "ALS export lock is missing property: $name" } }
        $schemaVersion = 0; $assetCount = 0; $fileCount = 0; $animationCount = 0
        if (-not $values.schemaVersion.TryGetInt32([ref]$schemaVersion) -or $schemaVersion -ne 1) { throw 'ALS export lock schemaVersion must be exactly 1.' }
        if (-not $values.assetCount.TryGetInt32([ref]$assetCount) -or $assetCount -ne 267) { throw 'ALS export lock assetCount must be exactly 267.' }
        if (-not $values.fileCount.TryGetInt32([ref]$fileCount) -or $fileCount -ne 141) { throw 'ALS export lock fileCount must be exactly 141.' }
        if (-not $values.animationCount.TryGetInt32([ref]$animationCount) -or $animationCount -ne 126) { throw 'ALS export lock animationCount must be exactly 126.' }
        $manifestSha256 = $values.manifestSha256.GetString()
        $exporterVersion = $values.exporterVersion.GetString()
        $sourceProjectId = $values.sourceProjectId.GetString()
        if ($manifestSha256 -cnotmatch '^[0-9a-f]{64}$') { throw 'ALS export lock manifestSha256 must be lowercase SHA-256.' }
        if ([string]::IsNullOrWhiteSpace($exporterVersion)) { throw 'ALS export lock exporterVersion must be non-empty.' }
        if ([string]::IsNullOrWhiteSpace($sourceProjectId)) { throw 'ALS export lock sourceProjectId must be non-empty.' }
        [pscustomobject]@{ SchemaVersion=$schemaVersion; ManifestSha256=$manifestSha256; AssetCount=$assetCount; FileCount=$fileCount; AnimationCount=$animationCount; ExporterVersion=$exporterVersion; SourceProjectId=$sourceProjectId }
    }
    finally { $document.Dispose() }
}

function Publish-AlsExportLock {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ManifestPath, [Parameter(Mandatory)][string]$LockPath)

    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    $assetCount = [int]$manifest.auditSummary.assetCount
    $fileCount = @($manifest.files).Count
    $animationCount = @($manifest.animations).Count
    if ($manifest.auditSummary.status -cne 'complete' -or [int]$manifest.auditSummary.errorCount -ne 0 -or
        $assetCount -ne 267 -or $fileCount -ne 141 -or $animationCount -ne 126) {
        throw "Refusing to publish ALS export lock from incomplete or unexpected manifest counts: assets=$assetCount files=$fileCount animations=$animationCount"
    }
    $value = [ordered]@{
        schemaVersion = 1
        manifestSha256 = (Get-FileHash -LiteralPath $ManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
        assetCount = $assetCount
        fileCount = $fileCount
        animationCount = $animationCount
        exporterVersion = [string]$manifest.exporterVersion
        sourceProjectId = [string]$manifest.sourceProjectId
    }
    if ([string]::IsNullOrWhiteSpace($value.exporterVersion) -or [string]::IsNullOrWhiteSpace($value.sourceProjectId)) { throw 'Refusing to publish ALS export lock without producer identity.' }
    $lockFullPath = [IO.Path]::GetFullPath($LockPath)
    $directory = [IO.Path]::GetDirectoryName($lockFullPath)
    [void][IO.Directory]::CreateDirectory($directory)
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes("$($value | ConvertTo-Json)$([Environment]::NewLine)")
    $temporaryPath = Join-Path $directory ".$([IO.Path]::GetFileName($lockFullPath)).$([guid]::NewGuid().ToString('N')).tmp"
    try {
        $stream = [System.IO.FileStream]::new($temporaryPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
        try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
        if ([System.IO.File]::Exists($lockFullPath)) { [System.IO.File]::Replace($temporaryPath, $lockFullPath, [Management.Automation.Language.NullString]::Value) }
        else { [System.IO.File]::Move($temporaryPath, $lockFullPath) }
    }
    catch { throw "Failed to publish ALS export lock atomically: $($_.Exception.Message)" }
    finally { if ([IO.File]::Exists($temporaryPath)) { [IO.File]::Delete($temporaryPath) } }
}

function Assert-AlsExportLock {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ManifestPath, [Parameter(Mandatory)][string]$AssetRoot, [Parameter(Mandatory)][string]$LockPath)

    $lock = Read-AlsExportLock -LockPath $LockPath
    $actualManifestHash = (Get-FileHash -LiteralPath $ManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualManifestHash -cne $lock.ManifestSha256) { throw "Formal ALS manifest SHA-256 mismatch: expected=$($lock.ManifestSha256) actual=$actualManifestHash" }
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    if ([int]$manifest.auditSummary.assetCount -ne $lock.AssetCount -or @($manifest.files).Count -ne $lock.FileCount -or
        @($manifest.animations).Count -ne $lock.AnimationCount -or [string]$manifest.exporterVersion -cne $lock.ExporterVersion -or
        [string]$manifest.sourceProjectId -cne $lock.SourceProjectId) { throw 'Formal ALS manifest counts or producer identity differ from tracked lock.' }
    foreach ($file in @($manifest.files)) {
        $path = Join-Path $AssetRoot ([string]$file.relativePath)
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Locked ALS export file is missing: $($file.relativePath)" }
        $info = Get-Item -LiteralPath $path
        if ($info.Length -ne [long]$file.size) { throw "Locked ALS export file size mismatch: $($file.relativePath)" }
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -cne [string]$file.sha256) { throw "Locked ALS export file SHA-256 mismatch: $($file.relativePath)" }
    }
    return $lock
}

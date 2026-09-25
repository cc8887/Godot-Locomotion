param(
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][string]$ArchiveDirectory
)
$ErrorActionPreference = 'Stop'
$sourcePath = (Get-Item -LiteralPath $Source).FullName.TrimEnd('\')
if ((Get-Item -LiteralPath $sourcePath).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked worktree root is not supported.' }
$dirty = @(git -C $sourcePath status --porcelain)
if ($LASTEXITCODE -ne 0 -or $dirty.Count) { throw 'Worktree has uncommitted or untracked changes.' }
$head = git -C $sourcePath rev-parse HEAD
git -C $sourcePath merge-base --is-ancestor $head main
if ($LASTEXITCODE -ne 0) { throw 'Worktree HEAD is not included in main.' }
$links = @(Get-ChildItem -LiteralPath $sourcePath -Recurse -Force -Attributes ReparsePoint)
if ($links.Count) { throw 'Worktree contains reparse points; inspect them before archiving.' }
$files = @(git -c core.quotepath=false -C $sourcePath ls-files --others --ignored --exclude-standard)
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate ignored worktree files.' }
$excludedPattern = '(^|/)(\.godot|bin|obj|__pycache__)(/|$)'
$selected = @($files | Where-Object { $_ -notmatch $excludedPattern })
$excluded = @($files | Where-Object { $_ -match $excludedPattern })
$directory = [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($ArchiveDirectory)).FullName
if ($directory.StartsWith($sourcePath + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Archive must be outside the source worktree.' }
$zipPath = Join-Path $directory ((Split-Path $sourcePath -Leaf) + '.zip')
$manifestPath = $zipPath + '.manifest.json'
if ((Test-Path -LiteralPath $zipPath) -or (Test-Path -LiteralPath $manifestPath)) { throw 'Archive already exists; refusing to overwrite.' }
$records = [Collections.Generic.List[object]]::new()
$stream = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew)
$zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($relative in $selected) {
        $path = [IO.Path]::GetFullPath((Join-Path $sourcePath $relative))
        if (!$path.StartsWith($sourcePath + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Escaping source path: $relative" }
        $inputStream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        try {
            $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($inputStream))
            $length = $inputStream.Length
            $inputStream.Position = 0
            $entry = $zip.CreateEntry($relative, [IO.Compression.CompressionLevel]::Fastest)
            $outputStream = $entry.Open()
            try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose() }
            $records.Add([pscustomobject]@{Path=$relative; Length=$length; SHA256=$hash})
        } finally { $inputStream.Dispose() }
        if ($records.Count % 2000 -eq 0) { Write-Output "Archived $($records.Count)/$($selected.Count): $sourcePath" }
    }
} finally { $zip.Dispose(); $stream.Dispose() }
$readZip = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    if ($readZip.Entries.Count -ne $records.Count) { throw 'Archive entry count differs.' }
    $verified = 0
    foreach ($record in $records) {
        $entry = $readZip.GetEntry($record.Path)
        if (!$entry -or $entry.Length -ne $record.Length) { throw "Archive entry length differs: $($record.Path)" }
        $entryStream = $entry.Open()
        try { $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($entryStream)) }
        finally { $entryStream.Dispose() }
        if ($actual -ne $record.SHA256) { throw "Archive hash differs: $($record.Path)" }
        $verified++
        if ($verified % 2000 -eq 0) { Write-Output "Verified $verified/$($records.Count): $sourcePath" }
    }
} finally { $readZip.Dispose() }
$manifest = [ordered]@{
    SchemaVersion=1; Source=$sourcePath; Head=$head; VerifiedUtc=[DateTime]::UtcNow.ToString('o')
    Archive=$zipPath; ArchiveSHA256=(Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    Files=$records.ToArray(); ExcludedRegenerableFiles=$excluded
}
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
Write-Output "ARCHIVE_VERIFIED files=$($records.Count) bytes=$((Get-Item -LiteralPath $zipPath).Length) path=$zipPath"

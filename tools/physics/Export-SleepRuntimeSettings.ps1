param(
    [string]$ReferencePath,
    [string]$OutputPath
)

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $ReferencePath) {
    $ReferencePath = Join-Path $repositoryRoot 'tests/Als.Import.Tests/Fixtures/Physics/v4_physics_sleep_reference.json'
}
if (-not $OutputPath) {
    $OutputPath = Join-Path $repositoryRoot 'assets/config/v4_physics_sleep_settings.json'
}

$reference = [IO.File]::ReadAllBytes($ReferencePath)
$document = [System.Text.Json.JsonDocument]::Parse([Text.Encoding]::UTF8.GetString($reference))
try {
    $root = $document.RootElement
    if ($root.GetProperty('schemaVersion').GetInt32() -ne 1 -or
        $root.GetProperty('cases').GetArrayLength() -eq 0 -or
        -not $root.GetProperty('sleepEnabled').GetBoolean()) {
        throw 'Expected a native sleep reference with enabled sleeping and trajectory cases.'
    }
    $options = [System.Text.Json.JsonWriterOptions]::new()
    $options.Indented = $true
    $stream = [IO.MemoryStream]::new()
    try {
        $writer = [System.Text.Json.Utf8JsonWriter]::new($stream, $options)
        try {
            $writer.WriteStartObject()
            $writer.WriteNumber('schemaVersion', 2)
            foreach ($name in @('sleepEnabled', 'sleepSettings', 'rigs')) {
                $writer.WritePropertyName($name)
                $root.GetProperty($name).WriteTo($writer)
            }
            $writer.WriteString('referenceSha256', [Convert]::ToHexString(
                [Security.Cryptography.SHA256]::HashData($reference)))
            $writer.WriteEndObject()
            $writer.Flush()
        }
        finally { $writer.Dispose() }
    }
    finally {
        $content = [Text.Encoding]::UTF8.GetString($stream.ToArray()).Replace("`r`n", "`n")
        $stream.Dispose()
    }
    [IO.File]::WriteAllText($OutputPath, $content, [Text.UTF8Encoding]::new($false))
}
finally { $document.Dispose() }

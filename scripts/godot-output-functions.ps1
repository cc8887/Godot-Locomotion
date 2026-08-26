function Assert-GodotInvocationSucceeded {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object[]]$OutputLines,
        [Parameter(Mandatory)]
        [int]$ExitCode,
        [Parameter(Mandatory)]
        [string]$Context
    )

    $errorLines = @($OutputLines | Where-Object { "$_" -match '^(SCRIPT ERROR|ERROR):' })
    if ($ExitCode -ne 0 -or $errorLines.Count -ne 0) {
        $details = $errorLines -join [Environment]::NewLine
        throw "$Context failed with exit code $ExitCode.$([Environment]::NewLine)$details"
    }
}

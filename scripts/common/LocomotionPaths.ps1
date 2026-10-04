# External dependency defaults are relative to the checkout, never to the caller's cwd.
$script:LocomotionRepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))

function Resolve-LocomotionPath {
    param([AllowEmptyString()][string]$Path, [string]$EnvironmentVariable, [string]$Fallback)
    if ([string]::IsNullOrWhiteSpace($Path) -and $EnvironmentVariable) {
        $Path = [Environment]::GetEnvironmentVariable($EnvironmentVariable, 'Process')
    }
    if ([string]::IsNullOrWhiteSpace($Path)) { $Path = $Fallback }
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'A dependency path must be configured.' }
    if (-not [IO.Path]::IsPathRooted($Path)) { $Path = Join-Path $script:LocomotionRepositoryRoot $Path }
    return [IO.Path]::GetFullPath($Path)
}

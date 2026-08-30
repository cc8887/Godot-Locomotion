Set-StrictMode -Version Latest

function ConvertFrom-P4MatrixOutput
{
    param(
        [Parameter(Mandatory)]
        [object[]]$OutputLines,
        [Parameter(Mandatory)]
        [ValidateSet('single', 'parallel')]
        [string]$ExpectedMode,
        [Parameter(Mandatory)]
        [ValidateSet(1, 10)]
        [int]$ExpectedCharacterCount
    )

    $textLines = @($OutputLines | ForEach-Object { "$_" })
    $errors = @($textLines | Where-Object {
        $_ -match '(^|\s)(SCRIPT ERROR:|ERROR:|P4_MATRIX_FAIL\b|GODOT_ALS_P3B_FAIL\b)'
    })
    if ($errors.Count -ne 0)
    {
        throw "P4 matrix emitted an error line:$([Environment]::NewLine)$($errors -join [Environment]::NewLine)"
    }

    $markers = @($textLines | Where-Object { $_ -match '^P4_MATRIX_OK(?:\s|$)' })
    if ($markers.Count -ne 1)
    {
        throw "P4 matrix must emit exactly one P4_MATRIX_OK line; observed $($markers.Count)."
    }

    $required = @(
        'mode', 'characters', 'warmup', 'frames',
        'result', 'pose', 'full_pose', 'root', 'aim', 'turn_rotate', 'feet',
        'missing', 'stale', 'generation', 'lag', 'thread',
        'model', 'curve', 'controller', 'modifier', 'skeleton', 'exchange', 'commit',
        'foot_gather', 'advances', 'modifiers', 'commits',
        'per_character_advances', 'per_character_modifiers', 'per_character_commits',
        'replacement', 'old_generation_rejected', 'lanes',
        'gather_commit_p95_us', 'worker_p95_us', 'total_p99_us'
    )
    $allowed = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in $required) { [void]$allowed.Add($name) }
    $values = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    $tokens = $markers[0].Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
    if ($tokens[0] -cne 'P4_MATRIX_OK')
    {
        throw 'P4 matrix marker prefix is malformed.'
    }
    for ($index = 1; $index -lt $tokens.Length; $index++)
    {
        $token = $tokens[$index]
        $separator = $token.IndexOf('=')
        if ($separator -le 0 -or $separator -eq $token.Length - 1)
        {
            throw "P4 matrix token is malformed: $token"
        }
        $name = $token.Substring(0, $separator)
        $value = $token.Substring($separator + 1)
        if (-not $allowed.Contains($name))
        {
            throw "P4 matrix field is unknown: $name"
        }
        if (-not $values.TryAdd($name, $value))
        {
            throw "P4 matrix field is duplicated: $name"
        }
    }
    foreach ($name in $required)
    {
        if (-not $values.ContainsKey($name))
        {
            throw "P4 matrix field is missing: $name"
        }
    }
    if ($values.Count -ne $required.Count)
    {
        throw 'P4 matrix field cardinality is invalid.'
    }

    if ($values['mode'] -cne $ExpectedMode)
    {
        throw "P4 matrix mode mismatch: expected $ExpectedMode, observed $($values['mode'])."
    }
    $characters = ConvertTo-P4Integer $values['characters'] 'characters'
    if ($characters -ne $ExpectedCharacterCount)
    {
        throw "P4 matrix character count mismatch: expected $ExpectedCharacterCount, observed $characters."
    }
    $warmup = ConvertTo-P4Integer $values['warmup'] 'warmup'
    $frames = ConvertTo-P4Integer $values['frames'] 'frames'
    if ($warmup -ne 120 -or $frames -ne 600)
    {
        throw "P4 matrix requires warmup=120 and frames=600; observed warmup=$warmup frames=$frames."
    }

    $digests = @{}
    foreach ($name in @('result', 'pose', 'full_pose', 'root', 'aim', 'turn_rotate', 'feet'))
    {
        $value = $values[$name]
        if ($value -cnotmatch '^[0-9A-F]{16}$')
        {
            throw "P4 matrix digest $name is malformed: $value"
        }
        $digests[$name] = $value
    }

    foreach ($name in @('missing', 'stale', 'generation', 'lag', 'thread',
        'model', 'curve', 'controller', 'modifier', 'skeleton', 'exchange', 'commit'))
    {
        $value = ConvertTo-P4Integer $values[$name] $name
        if ($value -ne 0)
        {
            throw "P4 matrix field $name must be zero; observed $value."
        }
    }
    $footGather = ConvertTo-P4Integer $values['foot_gather'] 'foot_gather'
    if ($footGather -lt 0)
    {
        throw "P4 matrix Foot Gather allocations must be reported separately and non-negative; observed $footGather."
    }

    $expectedTotal = $ExpectedCharacterCount * $frames
    foreach ($name in @('advances', 'modifiers', 'commits'))
    {
        $value = ConvertTo-P4Integer $values[$name] $name
        if ($value -ne $expectedTotal)
        {
            throw "P4 matrix field $name must equal $expectedTotal; observed $value."
        }
    }
    foreach ($name in @('per_character_advances', 'per_character_modifiers', 'per_character_commits'))
    {
        $value = ConvertTo-P4Integer $values[$name] $name
        if ($value -ne $frames)
        {
            throw "P4 matrix field $name must equal $frames; observed $value."
        }
    }
    foreach ($name in @('replacement', 'old_generation_rejected'))
    {
        $value = ConvertTo-P4Integer $values[$name] $name
        if ($value -ne 1)
        {
            throw "P4 matrix field $name must equal 1; observed $value."
        }
    }
    $lanes = ConvertTo-P4Integer $values['lanes'] 'lanes'
    if ($lanes -ne $ExpectedCharacterCount)
    {
        throw "P4 matrix lanes must equal $ExpectedCharacterCount; observed $lanes."
    }

    $gatherCommitP95 = ConvertTo-P4Integer $values['gather_commit_p95_us'] 'gather_commit_p95_us'
    $workerP95 = ConvertTo-P4Integer $values['worker_p95_us'] 'worker_p95_us'
    $totalP99 = ConvertTo-P4Integer $values['total_p99_us'] 'total_p99_us'
    if ($ExpectedMode -ceq 'parallel' -and $ExpectedCharacterCount -eq 10 -and
        ($gatherCommitP95 -gt 1500 -or $workerP95 -gt 2500 -or $totalP99 -gt 4000))
    {
        if ($gatherCommitP95 -gt 1500)
        {
            throw "P4 Gather+Commit p95 exceeded 1500us: $gatherCommitP95."
        }
        if ($workerP95 -gt 2500)
        {
            throw "P4 Worker p95 exceeded 2500us: $workerP95."
        }
        throw "P4 total p99 exceeded 4000us: $totalP99."
    }

    [pscustomobject]@{
        Mode = $ExpectedMode
        Characters = $characters
        Warmup = $warmup
        Frames = $frames
        ResultDigest = $digests['result']
        PoseDigest = $digests['pose']
        FullPoseDigest = $digests['full_pose']
        RootDigest = $digests['root']
        AimDigest = $digests['aim']
        TurnRotateDigest = $digests['turn_rotate']
        FeetDigest = $digests['feet']
        FootGatherAllocations = $footGather
        GatherCommitP95Microseconds = $gatherCommitP95
        WorkerP95Microseconds = $workerP95
        TotalP99Microseconds = $totalP99
    }
}

function Assert-P4MatrixPair
{
    param(
        [Parameter(Mandatory)][object]$Single,
        [Parameter(Mandatory)][object]$Parallel,
        [Parameter(Mandatory)][ValidateSet(1, 10)][int]$CharacterCount
    )

    if ($Single.Mode -cne 'single' -or $Parallel.Mode -cne 'parallel' -or
        $Single.Characters -ne $CharacterCount -or $Parallel.Characters -ne $CharacterCount)
    {
        throw "P4 matrix pair identity mismatch for characters=$CharacterCount."
    }
    foreach ($name in @('ResultDigest', 'PoseDigest', 'FullPoseDigest', 'RootDigest',
        'AimDigest', 'TurnRotateDigest', 'FeetDigest'))
    {
        if ($Single.$name -cne $Parallel.$name)
        {
            throw "P4 matrix $name mismatch for characters=${CharacterCount}: single=$($Single.$name), parallel=$($Parallel.$name)."
        }
    }
}

function Invoke-P4VerificationProcess
{
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$Arguments,
        [Parameter(Mandatory)]
        [ValidateRange(1, 3600)]
        [int]$TimeoutSeconds,
        [Parameter(Mandatory)]
        [string]$Stage
    )

    $command = Get-Command -Name $FilePath -CommandType Application -ErrorAction Stop |
        Select-Object -First 1
    $process = [Diagnostics.Process]::new()
    $started = $false
    try
    {
        $process.StartInfo = [Diagnostics.ProcessStartInfo]::new()
        $process.StartInfo.FileName = [Environment]::ProcessPath
        $process.StartInfo.UseShellExecute = $false
        $process.StartInfo.CreateNoWindow = $true
        $process.StartInfo.RedirectStandardOutput = $true
        $process.StartInfo.RedirectStandardError = $true
        [void]$process.StartInfo.ArgumentList.Add('-NoProfile')
        [void]$process.StartInfo.ArgumentList.Add('-NonInteractive')
        [void]$process.StartInfo.ArgumentList.Add('-CommandWithArgs')
        [void]$process.StartInfo.ArgumentList.Add(
            '$decoded = @($args | ForEach-Object { [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($_)) }); & $decoded[0] @($decoded[1..($decoded.Count - 1)]); $code = $LASTEXITCODE; if ($null -eq $code) { if ($?) { exit 0 } else { exit 1 } }; exit $code')
        foreach ($argument in @($command.Source) + $Arguments)
        {
            $encodedArgument = [Convert]::ToBase64String(
                [Text.Encoding]::UTF8.GetBytes($argument))
            [void]$process.StartInfo.ArgumentList.Add($encodedArgument)
        }

        if (-not $process.Start())
        {
            throw "Could not start ${Stage}: $FilePath"
        }
        $started = $true
        $standardOutputTask = $process.StandardOutput.ReadToEndAsync()
        $standardErrorTask = $process.StandardError.ReadToEndAsync()
        $timeoutMilliseconds = $TimeoutSeconds * 1000
        if (-not $process.WaitForExit($timeoutMilliseconds))
        {
            try
            {
                $process.Kill($true)
            }
            catch
            {
                throw "${Stage} timed out and its process tree could not be terminated."
            }
            $process.WaitForExit()
            [void]$standardOutputTask.GetAwaiter().GetResult()
            [void]$standardErrorTask.GetAwaiter().GetResult()
            $unit = if ($TimeoutSeconds -eq 1) { 'second' } else { 'seconds' }
            throw "${Stage} timed out after $TimeoutSeconds $unit."
        }

        $process.WaitForExit()
        $standardOutput = $standardOutputTask.GetAwaiter().GetResult()
        $standardError = $standardErrorTask.GetAwaiter().GetResult()
        $lines = @(
            @($standardOutput, $standardError) |
                ForEach-Object { [regex]::Split("$_", '\r\n|\n|\r') } |
                Where-Object { $_.Length -ne 0 }
        )
        [pscustomobject]@{
            ExitCode = $process.ExitCode
            OutputLines = $lines
        }
    }
    finally
    {
        if ($started -and -not $process.HasExited)
        {
            $process.Kill($true)
            $process.WaitForExit()
        }
        $process.Dispose()
    }
}

function ConvertFrom-P3DemoInputOutput
{
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]]$OutputLines,
        [Parameter(Mandatory)]
        [int]$ExitCode
    )

    $expected =
        'GODOT_ALS_P3_DEMO_INPUT_OK actions=11 directions=12 camera_basis=1 pitch=1 aiming=1 cleared=1 hud=1'
    $token = 'GODOT_ALS_P3_DEMO_INPUT_OK'
    $lines = @($OutputLines | ForEach-Object { "$_" })
    if ($ExitCode -ne 0)
    {
        throw "P3 input smoke exited with code $ExitCode."
    }

    $errorLines = @($lines | Where-Object {
        $_ -match 'SCRIPT ERROR:|ERROR:|GODOT_ALS_P3_DEMO_INPUT_FAIL'
    })
    if ($errorLines.Count -ne 0)
    {
        throw "P3 input smoke emitted an error:$([Environment]::NewLine)$($errorLines -join [Environment]::NewLine)"
    }

    $candidates = @($lines | Where-Object {
        $_.IndexOf($token, [StringComparison]::Ordinal) -ge 0
    })
    $exactMatches = @($candidates | Where-Object { $_ -ceq $expected })
    if ($candidates.Count -ne 1 -or $exactMatches.Count -ne 1)
    {
        throw "Expected exactly one complete P3 input marker; candidates=$($candidates.Count) matches=$($exactMatches.Count)."
    }

    [pscustomobject]@{ Marker = $expected }
}

function ConvertFrom-P4DemoOutput
{
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]]$OutputLines,
        [Parameter(Mandatory)]
        [int]$ExitCode
    )

    $lines = @($OutputLines | ForEach-Object { "$_" })
    if ($ExitCode -ne 0)
    {
        throw "P4 demo exited with code $ExitCode."
    }

    $errorLines = @($lines | Where-Object { $_ -match 'SCRIPT ERROR:|ERROR:' })
    if ($errorLines.Count -ne 0)
    {
        throw "P4 demo emitted an engine error:$([Environment]::NewLine)$($errorLines -join [Environment]::NewLine)"
    }

    $failureLines = @($lines | Where-Object { $_ -match 'P4_DEMO_FAIL' })
    if ($failureLines.Count -ne 0)
    {
        throw "P4 demo emitted a failure marker:$([Environment]::NewLine)$($failureLines -join [Environment]::NewLine)"
    }

    $expected = 'P4_DEMO_OK frames=300 rigs=1'
    $candidates = @($lines | Where-Object {
        $_.IndexOf('P4_DEMO_OK', [StringComparison]::Ordinal) -ge 0
    })
    $exactMatches = @($candidates | Where-Object { $_ -ceq $expected })
    if ($candidates.Count -ne 1 -or $exactMatches.Count -ne 1)
    {
        throw "Expected exactly one P4_DEMO_OK frames=300 rigs=1 marker; candidates=$($candidates.Count) matches=$($exactMatches.Count)."
    }

    [pscustomobject]@{
        Frames = 300
        Rigs = 1
        Marker = $expected
    }
}

function ConvertTo-P4Integer
{
    param([Parameter(Mandatory)][string]$Text, [Parameter(Mandatory)][string]$FieldName)
    $value = 0L
    if (-not [long]::TryParse(
        $Text,
        [Globalization.NumberStyles]::None,
        [Globalization.CultureInfo]::InvariantCulture,
        [ref]$value))
    {
        throw "P4 matrix field $FieldName must be a finite non-negative integer: $Text"
    }
    if ($value -lt 0)
    {
        throw "P4 matrix field $FieldName must be a finite non-negative integer: $Text"
    }
    return $value
}

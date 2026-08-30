Set-StrictMode -Version Latest

if ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [Runtime.InteropServices.OSPlatform]::Windows) -and
    $null -eq ('GodotAls.Verification.P4KillOnCloseJob' -as [type]))
{
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace GodotAls.Verification
{
    public sealed class P4KillOnCloseJob : IDisposable
    {
        private const uint KillOnJobClose = 0x00002000;
        private IntPtr handle;

        public P4KillOnCloseJob()
        {
            handle = CreateJobObject(IntPtr.Zero, null);
            if (handle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var information = new JobObjectExtendedLimitInformation();
            information.BasicLimitInformation.LimitFlags = KillOnJobClose;
            var size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
            var pointer = IntPtr.Zero;
            try
            {
                pointer = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(information, pointer, false);
                if (!SetInformationJobObject(handle, 9, pointer, (uint)size))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            catch
            {
                CloseHandle(handle);
                handle = IntPtr.Zero;
                throw;
            }
            finally
            {
                if (pointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pointer);
                }
            }
        }

        public void AssignProcess(IntPtr processHandle)
        {
            if (handle == IntPtr.Zero)
            {
                throw new ObjectDisposedException(nameof(P4KillOnCloseJob));
            }
            if (!AssignProcessToJobObject(handle, processHandle))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref handle, IntPtr.Zero);
            if (current != IntPtr.Zero)
            {
                CloseHandle(current);
            }
            GC.SuppressFinalize(this);
        }

        ~P4KillOnCloseJob()
        {
            Dispose();
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr attributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(
            IntPtr job,
            int informationClass,
            IntPtr information,
            uint informationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectExtendedLimitInformation
        {
            public JobObjectBasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }
    }
}
'@
}

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
        [ValidateRange(1, 10830)]
        [int]$TimeoutSeconds,
        [Parameter(Mandatory)]
        [string]$Stage
    )

    $command = Get-Command -Name $FilePath -CommandType Application -ErrorAction Stop |
        Select-Object -First 1
    $process = [Diagnostics.Process]::new()
    $started = $false
    $cancellation = $null
    $jobObject = $null
    $startGate = $null
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
        $useJobObject = [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
            [Runtime.InteropServices.OSPlatform]::Windows)
        if ($useJobObject)
        {
            $jobObject = [GodotAls.Verification.P4KillOnCloseJob]::new()
            $startGateName = 'Local\GodotALS-P4-' + [Guid]::NewGuid().ToString('N')
            $startGate = [Threading.EventWaitHandle]::new(
                $false,
                [Threading.EventResetMode]::ManualReset,
                $startGateName)
            $wrapperCommand = '$ErrorActionPreference = ''Stop''; $decoded = @($args | ForEach-Object { [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($_)) }); $gate = [Threading.EventWaitHandle]::OpenExisting($decoded[0]); try { [void]$gate.WaitOne() } finally { $gate.Dispose() }; $file = $decoded[1]; $commandArguments = if ($decoded.Count -gt 2) { @($decoded[2..($decoded.Count - 1)]) } else { @() }; & $file @commandArguments; $code = $LASTEXITCODE; if ($null -eq $code) { if ($?) { exit 0 } else { exit 1 } }; exit $code'
            $encodedValues = @($startGateName, $command.Source) + $Arguments
        }
        else
        {
            $wrapperCommand = '$ErrorActionPreference = ''Stop''; $decoded = @($args | ForEach-Object { [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($_)) }); $file = $decoded[0]; $commandArguments = if ($decoded.Count -gt 1) { @($decoded[1..($decoded.Count - 1)]) } else { @() }; & $file @commandArguments; $code = $LASTEXITCODE; if ($null -eq $code) { if ($?) { exit 0 } else { exit 1 } }; exit $code'
            $encodedValues = @($command.Source) + $Arguments
        }
        [void]$process.StartInfo.ArgumentList.Add($wrapperCommand)
        foreach ($argument in $encodedValues)
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
        if ($useJobObject)
        {
            try
            {
                $jobObject.AssignProcess($process.Handle)
                [void]$startGate.Set()
            }
            catch
            {
                $containmentError = $_.Exception.Message
                try { $process.Kill($true) } catch { }
                try { [void]$process.WaitForExit(1000) } catch { }
                throw "Could not contain ${Stage} in a Windows Job Object: $containmentError"
            }
        }
        $cancellation = [Threading.CancellationTokenSource]::new()
        $standardOutputTask = $process.StandardOutput.ReadToEndAsync($cancellation.Token)
        $standardErrorTask = $process.StandardError.ReadToEndAsync($cancellation.Token)
        $streamTasks = [Threading.Tasks.Task]::WhenAll(
            [Threading.Tasks.Task[]]@($standardOutputTask, $standardErrorTask))
        $timeoutMilliseconds = $TimeoutSeconds * 1000
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $processExited = $process.WaitForExit($timeoutMilliseconds)
        $remainingMilliseconds = [Math]::Max(
            0,
            $timeoutMilliseconds - [int][Math]::Min(
                [int]::MaxValue,
                $watch.ElapsedMilliseconds))
        $streamsCompleted = $false
        if ($processExited)
        {
            try
            {
                $streamsCompleted = $streamTasks.IsCompleted -or
                    ($remainingMilliseconds -gt 0 -and
                     $streamTasks.Wait($remainingMilliseconds))
            }
            catch
            {
                throw "${Stage} output capture failed: $($_.Exception.Message)"
            }
        }

        if (-not $processExited -or -not $streamsCompleted)
        {
            $terminationFailed = $false
            if ($null -ne $jobObject)
            {
                $jobObject.Dispose()
                $jobObject = $null
            }
            try
            {
                if (-not $process.HasExited)
                {
                    $process.Kill($true)
                }
            }
            catch
            {
                if (-not $process.HasExited)
                {
                    $terminationFailed = $true
                }
            }
            $cancellation.Cancel()
            try { $process.StandardOutput.Dispose() } catch { }
            try { $process.StandardError.Dispose() } catch { }
            if (-not $process.HasExited)
            {
                try
                {
                    if (-not $process.WaitForExit(1000))
                    {
                        $terminationFailed = $true
                    }
                }
                catch
                {
                    $terminationFailed = $true
                }
            }
            if ($terminationFailed)
            {
                throw "${Stage} timed out and its process tree could not be terminated."
            }
            $unit = if ($TimeoutSeconds -eq 1) { 'second' } else { 'seconds' }
            throw "${Stage} timed out after $TimeoutSeconds $unit."
        }

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
        if ($null -ne $startGate)
        {
            $startGate.Dispose()
        }
        if ($null -ne $jobObject)
        {
            $jobObject.Dispose()
        }
        if ($started -and -not $process.HasExited)
        {
            try { $process.Kill($true) } catch { }
            try { [void]$process.WaitForExit(1000) } catch { }
        }
        if ($null -ne $cancellation)
        {
            try { $cancellation.Cancel() } catch { }
            $cancellation.Dispose()
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

function Test-P4EngineErrorLine
{
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string]$Line
    )

    if ([regex]::IsMatch(
        $Line,
        '\A\s*\[\+\]\s',
        [Text.RegularExpressions.RegexOptions]::CultureInvariant))
    {
        return $false
    }

    return [regex]::IsMatch(
        $Line,
        '(?<![A-Z0-9_])(?:SCRIPT ERROR:|ERROR:)(?=\s|\z)',
        [Text.RegularExpressions.RegexOptions]::CultureInvariant)
}

function Test-P4FailureMarkerLine
{
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string]$Line
    )

    if ([regex]::IsMatch(
        $Line,
        '\A\s*\[\+\]\s',
        [Text.RegularExpressions.RegexOptions]::CultureInvariant))
    {
        return $false
    }

    return [regex]::IsMatch(
        $Line,
        '(?<![A-Z0-9_])(?:GODOT_ALS_[A-Z0-9_]*FAIL|P4_[A-Z0-9_]*FAIL)(?=\s|\z)',
        [Text.RegularExpressions.RegexOptions]::CultureInvariant)
}

function Test-P4ReservedTopLevelMarkerLine
{
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string]$Line
    )

    if ([regex]::IsMatch(
        $Line,
        '\A\s*\[\+\]\s',
        [Text.RegularExpressions.RegexOptions]::CultureInvariant))
    {
        return $false
    }

    return [regex]::IsMatch(
        $Line,
        '(?<![A-Z0-9_])P4_(?:FOCUSED_)?VERIFICATION_OK(?=\s|\z)',
        [Text.RegularExpressions.RegexOptions]::CultureInvariant)
}

function Format-P4CapturedOutputTail
{
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$Lines,
        [ValidateRange(1, 200)]
        [int]$MaximumLines = 40
    )

    $count = $Lines.Count
    if ($count -eq 0)
    {
        return "captured output tail: 0/0 lines$([Environment]::NewLine)<no output>"
    }

    $start = [Math]::Max(0, $count - $MaximumLines)
    $tail = @($Lines[$start..($count - 1)] | ForEach-Object {
        $_ -replace '(?<![A-Z0-9_])P4_(?:FOCUSED_)?VERIFICATION_OK(?=\s|$)',
            '[reserved-top-level-marker]'
    })
    return "captured output tail: $($tail.Count)/$count lines$([Environment]::NewLine)" +
        ($tail -join [Environment]::NewLine)
}

function Assert-P4ChildGateOutput
{
    param(
        [Parameter(Mandatory)]
        [string]$PhaseName,
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]]$OutputLines,
        [Parameter(Mandatory)]
        [int]$ExitCode,
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$ExpectedMarkers
    )

    $lines = @($OutputLines | ForEach-Object { "$_" })
    $capturedOutput = Format-P4CapturedOutputTail -Lines $lines
    if ($ExitCode -ne 0)
    {
        throw "$PhaseName exited with code $ExitCode.$([Environment]::NewLine)$capturedOutput"
    }

    $errorLines = @($lines | Where-Object { Test-P4EngineErrorLine -Line $_ })
    if ($errorLines.Count -ne 0)
    {
        throw "$PhaseName emitted an engine error.$([Environment]::NewLine)$capturedOutput"
    }

    $failureLines = @($lines | Where-Object { Test-P4FailureMarkerLine -Line $_ })
    if ($failureLines.Count -ne 0)
    {
        throw "$PhaseName emitted an ALS failure marker.$([Environment]::NewLine)$capturedOutput"
    }

    $reservedLines = @($lines | Where-Object {
        Test-P4ReservedTopLevelMarkerLine -Line $_
    })
    if ($reservedLines.Count -ne 0)
    {
        throw "$PhaseName emitted a reserved top-level marker.$([Environment]::NewLine)$capturedOutput"
    }

    $markerNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($expectedMarker in $ExpectedMarkers)
    {
        $name = $expectedMarker.Split(' ', 2)[0]
        if (-not $markerNames.Add($name))
        {
            throw "$PhaseName contains duplicate expected marker name '$name'.$([Environment]::NewLine)$capturedOutput"
        }
        $candidates = @($lines | Where-Object {
            $_ -ceq $name -or $_.StartsWith("$name ", [StringComparison]::Ordinal)
        })
        $matches = @($candidates | Where-Object { $_ -ceq $expectedMarker })
        if ($candidates.Count -ne 1 -or $matches.Count -ne 1)
        {
            throw "$PhaseName expected exactly one '$expectedMarker' marker; candidates=$($candidates.Count) matches=$($matches.Count).$([Environment]::NewLine)$capturedOutput"
        }
    }

    return $lines
}

function Assert-P4SceneGateOutput
{
    param(
        [Parameter(Mandatory)][string]$PhaseName,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$OutputLines,
        [Parameter(Mandatory)][int]$ExitCode,
        [AllowEmptyCollection()][string[]]$ExpectedExactMarkers = @(),
        [AllowEmptyCollection()][string[]]$ExpectedRegexMarkers = @()
    )

    $lines = @(Assert-P4ChildGateOutput `
        -PhaseName $PhaseName `
        -OutputLines $OutputLines `
        -ExitCode $ExitCode `
        -ExpectedMarkers $ExpectedExactMarkers)
    $markerNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($exactMarker in $ExpectedExactMarkers)
    {
        $name = $exactMarker.Split(' ', 2)[0]
        if (-not $markerNames.Add($name))
        {
            throw "$PhaseName contains duplicate marker name '$name'."
        }
    }
    if (@($ExpectedRegexMarkers | Select-Object -Unique).Count -ne
        $ExpectedRegexMarkers.Count)
    {
        throw "$PhaseName contains duplicate regex marker expectations."
    }
    foreach ($pattern in $ExpectedRegexMarkers)
    {
        $nameMatch = [regex]::Match(
            $pattern,
            '\A\\A(?<name>[A-Z0-9_]+)',
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)
        if (-not $nameMatch.Success -or
            -not $pattern.EndsWith('\z', [StringComparison]::Ordinal))
        {
            throw "$PhaseName regex markers must be anchored: $pattern"
        }
        $name = $nameMatch.Groups['name'].Value
        if (-not $markerNames.Add($name))
        {
            throw "$PhaseName contains duplicate marker name '$name'."
        }
        $candidates = @($lines | Where-Object {
            $_ -ceq $name -or $_.StartsWith("$name ", [StringComparison]::Ordinal)
        })
        $matches = @($candidates | Where-Object {
            [regex]::IsMatch(
                $_,
                $pattern,
                [Text.RegularExpressions.RegexOptions]::CultureInvariant)
        })
        if ($candidates.Count -ne 1 -or $matches.Count -ne 1)
        {
            throw "$PhaseName expected exactly one '$pattern' regex marker; candidates=$($candidates.Count) matches=$($matches.Count)."
        }
    }
    return $lines
}

function Assert-P4MatrixCertificateOutput
{
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]]$OutputLines,
        [Parameter(Mandatory)]
        [int]$ExitCode
    )

    $certificate = 'P4_MATRIX_VERIFICATION_OK cells=4 pairs=2'
    $lines = @(Assert-P4ChildGateOutput `
        -PhaseName 'P4 matrix certificate' `
        -OutputLines $OutputLines `
        -ExitCode $ExitCode `
        -ExpectedMarkers @($certificate))
    $candidates = @($lines | Where-Object {
        $_.IndexOf('P4_MATRIX_OK', [StringComparison]::Ordinal) -ge 0
    })
    if ($candidates.Count -ne 4)
    {
        throw "P4 matrix certificate requires exactly four matrix cells; observed $($candidates.Count)."
    }

    $expected = @(
        [pscustomobject]@{ Mode = 'single'; Characters = 1 },
        [pscustomobject]@{ Mode = 'parallel'; Characters = 1 },
        [pscustomobject]@{ Mode = 'single'; Characters = 10 },
        [pscustomobject]@{ Mode = 'parallel'; Characters = 10 }
    )
    $cells = @()
    for ($index = 0; $index -lt $expected.Count; $index++)
    {
        $cells += ConvertFrom-P4MatrixOutput `
            -OutputLines @($candidates[$index]) `
            -ExpectedMode $expected[$index].Mode `
            -ExpectedCharacterCount $expected[$index].Characters
    }
    Assert-P4MatrixPair -Single $cells[0] -Parallel $cells[1] -CharacterCount 1
    Assert-P4MatrixPair -Single $cells[2] -Parallel $cells[3] -CharacterCount 10

    [pscustomobject]@{
        Cells = $cells
        OutputLines = $lines
        Marker = $certificate
    }
}

function Assert-P4RepositoryClosure
{
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$BaseCommit
    )

    $resolvedRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
    $resolvedBaseOutput = @(& git -C $resolvedRoot rev-parse --verify "${BaseCommit}^{commit}" 2>&1)
    if ($LASTEXITCODE -ne 0 -or $resolvedBaseOutput.Count -ne 1 -or
        "$($resolvedBaseOutput[0])" -notmatch '\A[0-9a-fA-F]{40}\z')
    {
        throw "P4 base commit does not exist: $BaseCommit"
    }
    $resolvedBase = "$($resolvedBaseOutput[0])"

    $ancestorOutput = @(& git -C $resolvedRoot merge-base --is-ancestor $resolvedBase HEAD 2>&1)
    $ancestorExitCode = $LASTEXITCODE
    if ($ancestorExitCode -eq 1)
    {
        throw "P4 base commit is not an ancestor of HEAD: $resolvedBase"
    }
    if ($ancestorExitCode -ne 0)
    {
        throw "Could not verify P4 base ancestry:$([Environment]::NewLine)$($ancestorOutput -join [Environment]::NewLine)"
    }

    $committed = @(& git -C $resolvedRoot diff "${resolvedBase}..HEAD" --check -- 2>&1)
    if ($LASTEXITCODE -ne 0)
    {
        throw "Committed P4 range whitespace check failed:$([Environment]::NewLine)$($committed -join [Environment]::NewLine)"
    }
    $cached = @(& git -C $resolvedRoot diff --cached --check -- 2>&1)
    if ($LASTEXITCODE -ne 0)
    {
        throw "P4 index whitespace check failed:$([Environment]::NewLine)$($cached -join [Environment]::NewLine)"
    }
    $worktree = @(& git -C $resolvedRoot diff --check -- 2>&1)
    if ($LASTEXITCODE -ne 0)
    {
        throw "P4 worktree whitespace check failed:$([Environment]::NewLine)$($worktree -join [Environment]::NewLine)"
    }

    $trackedFiles = @(& git -C $resolvedRoot ls-files 2>&1)
    if ($LASTEXITCODE -ne 0)
    {
        throw "Could not enumerate tracked P4 files:$([Environment]::NewLine)$($trackedFiles -join [Environment]::NewLine)"
    }
    $forbiddenPattern = '(?i)(^|/)(\.godot|\.mono|bin|obj|Binaries|Intermediate|Saved|DerivedDataCache|StagedBuilds|Cooked)(/|$)|^(assets/generated|artifacts/(?!\.gdignore$)|benchmark-results/(?!\.gdignore$))|\.(dll|pdb|modules|target|ubulk|uexp|pak|ucas|utoc|sav|log)$'
    $forbidden = @($trackedFiles | ForEach-Object { "$_".Replace('\', '/') } |
        Where-Object { $_ -match $forbiddenPattern })
    if ($forbidden.Count -ne 0)
    {
        throw "Tracked generated/build output is forbidden:$([Environment]::NewLine)$($forbidden -join [Environment]::NewLine)"
    }

    return $resolvedBase
}

function Assert-P4CleanWorktree
{
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $resolvedRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
    $status = @(& git -C $resolvedRoot status --porcelain --untracked-files=all *>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0)
    {
        throw "Could not inspect P4 repository status (exit $exitCode):$([Environment]::NewLine)$($status -join [Environment]::NewLine)"
    }
    if ($status.Count -ne 0)
    {
        throw "P4 full verification requires a clean worktree:$([Environment]::NewLine)$($status -join [Environment]::NewLine)"
    }
}

function Assert-P4TrxTestRun
{
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$PhaseName,
        [AllowEmptyCollection()][string[]]$ExpectedTestClasses = @()
    )

    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $document = [Xml.XmlDocument]::new()
    $document.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create($resolvedPath, $settings)
    try
    {
        $document.Load($reader)
    }
    finally
    {
        $reader.Dispose()
    }

    $counters = @($document.SelectNodes("//*[local-name()='Counters']"))
    if ($counters.Count -ne 1)
    {
        throw "$PhaseName TRX must contain exactly one Counters element; observed $($counters.Count)."
    }
    $values = @{}
    foreach ($name in @('executed', 'passed', 'failed'))
    {
        $attribute = $counters[0].Attributes[$name]
        if ($null -eq $attribute)
        {
            throw "$PhaseName TRX counter is missing: $name"
        }
        $value = 0
        if (-not [int]::TryParse(
            $attribute.Value,
            [Globalization.NumberStyles]::None,
            [Globalization.CultureInfo]::InvariantCulture,
            [ref]$value) -or $value -lt 0)
        {
            throw "$PhaseName TRX counter is invalid: $name=$($attribute.Value)"
        }
        $values[$name] = $value
    }
    if ($values['executed'] -le 0)
    {
        throw "$PhaseName executed zero tests."
    }
    if ($values['failed'] -ne 0)
    {
        throw "$PhaseName reported $($values['failed']) failed tests."
    }
    if ($values['passed'] -ne $values['executed'])
    {
        throw "$PhaseName requires every executed test to pass; executed=$($values['executed']) passed=$($values['passed'])."
    }

    if ($ExpectedTestClasses.Count -ne 0)
    {
        $passedTestIds = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::OrdinalIgnoreCase)
        foreach ($result in @($document.SelectNodes("//*[local-name()='UnitTestResult']")))
        {
            $testId = $result.Attributes['testId']
            $outcome = $result.Attributes['outcome']
            if ($null -ne $testId -and $null -ne $outcome -and
                $outcome.Value -ceq 'Passed')
            {
                [void]$passedTestIds.Add($testId.Value)
            }
        }

        $passedClasses = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        foreach ($definition in @($document.SelectNodes("//*[local-name()='UnitTest']")))
        {
            $testId = $definition.Attributes['id']
            $method = $definition.SelectSingleNode("./*[local-name()='TestMethod']")
            $className = if ($null -ne $method) { $method.Attributes['className'] } else { $null }
            if ($null -eq $testId -or $null -eq $className -or
                -not $passedTestIds.Contains($testId.Value))
            {
                continue
            }
            $separator = $className.Value.LastIndexOf('.')
            $simpleName = if ($separator -ge 0)
            {
                $className.Value.Substring($separator + 1)
            }
            else
            {
                $className.Value
            }
            [void]$passedClasses.Add($simpleName)
        }

        $expectedClasses = [Collections.Generic.HashSet[string]]::new(
            [StringComparer]::Ordinal)
        foreach ($expectedClass in $ExpectedTestClasses)
        {
            if ([string]::IsNullOrWhiteSpace($expectedClass) -or
                -not $expectedClasses.Add($expectedClass))
            {
                throw "$PhaseName contains an invalid or duplicate expected test class: '$expectedClass'."
            }
            if (-not $passedClasses.Contains($expectedClass))
            {
                throw "$PhaseName did not execute expected test class '$expectedClass'."
            }
        }
    }

    return $values['executed']
}

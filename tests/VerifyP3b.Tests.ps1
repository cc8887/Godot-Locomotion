$script:RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:FunctionsPath = Join-Path $script:RepositoryRoot 'scripts\p3b-verification-functions.ps1'
$script:WorkerPath = Join-Path $script:RepositoryRoot 'src\Als.Godot\Locomotion\AlsP3WorkerRoot.cs'
$script:RuntimePath = Join-Path $script:RepositoryRoot 'src\Als.Godot\Locomotion\AlsP3RuntimeContext.cs'
$script:HarnessPath = Join-Path $script:RepositoryRoot 'src\Als.Godot\Locomotion\P3bAnimationHarness.cs'
$script:FrameOrderPath = Join-Path $script:RepositoryRoot 'src\Als.Godot\Locomotion\P3bFrameOrderSmoke.cs'
$script:VerifierPath = Join-Path $script:RepositoryRoot 'scripts\verify-p3b.ps1'
if (Test-Path -LiteralPath $script:FunctionsPath)
{
    . $script:FunctionsPath
}

$script:ValidMarker = 'GODOT_ALS_P3B_OK mode=single characters=1 warmup=120 frames=600 digest=0123456789ABCDEF pose=1111111111111111 full_pose=2222222222222222 root=3333333333333333 missing=0 stale=0 generation=0 off_main=0 lag=0 allocations=0 p95_us=100 p99_us=200'
$script:ValidFrameOrderMarker = 'GODOT_ALS_P3B_FRAME_ORDER_OK mode=single frames=180 digest=0123456789ABCDEF pose=1111111111111111 full_pose=2222222222222222 root=3333333333333333 lag=0 stale=0 generation=1 old_generation_rejected=1 retired_released=1 max_visible=1 recovery_zero_visible=1'
$script:ValidGraphMarker = 'GODOT_ALS_P3B_GRAPH_OK transitions=5 direction_poses=4 rotation_modes=3 direction_digest=0123456789ABCDEF digest=1111111111111111'
$script:ValidAllocation = 'GODOT_ALS_P3B_ALLOC model=0 controller=0 skeleton=0 exchange=0 commit=0'
$script:ValidReplacement = 'GODOT_ALS_P3B_REPLACEMENT character=0 old_generation_rejected=1'
$script:ValidPose = 'GODOT_ALS_P3B_POSE character=0 changes=42'
$script:VerifierSource = [System.IO.File]::ReadAllText($script:VerifierPath)
$script:HarnessAggregationPatterns = @(
    '(?m)^\s*AlsResultDigest\s*\.\s*Append\s*\(\s*ref\s+_resultDigest\s*,\s*diagnostics\s*\.\s*Result\s*\)\s*;\s*$',
    '(?m)^\s*Append\s*\(\s*ref\s+_poseDigest\s*,\s*diagnostics\s*\.\s*PoseDigest\s*\)\s*;\s*$',
    '(?m)^\s*Append\s*\(\s*ref\s+_fullPoseDigest\s*,\s*diagnostics\s*\.\s*FullPoseDigest\s*\)\s*;\s*$',
    '(?m)^\s*Append\s*\(\s*ref\s+_rootDigest\s*,\s*diagnostics\s*\.\s*RootDigest\s*\)\s*;\s*$')
$script:FrameOrderAggregationPatterns = @(
    '(?m)^\s*AlsResultDigest\s*\.\s*Append\s*\(\s*ref\s+_resultDigest\s*,\s*frame\s*\.\s*Result\s*\)\s*;\s*$',
    '(?m)^\s*Append\s*\(\s*ref\s+_poseDigest\s*,\s*frame\s*\.\s*PoseDigest\s*\)\s*;\s*$',
    '(?m)^\s*Append\s*\(\s*ref\s+_fullPoseDigest\s*,\s*frame\s*\.\s*FullPoseDigest\s*\)\s*;\s*$',
    '(?m)^\s*Append\s*\(\s*ref\s+_rootDigest\s*,\s*frame\s*\.\s*RootDigest\s*\)\s*;\s*$')

function Get-P3bOutput
{
    param(
        [string]$Marker = $script:ValidMarker,
        [string]$Allocation = $script:ValidAllocation,
        [string[]]$Advances = @('GODOT_ALS_P3B_ADVANCE character=0 frames=600'),
        [string[]]$Poses = @($script:ValidPose),
        [string]$Replacement = $script:ValidReplacement
    )

    return @('Godot Engine test', $Allocation) + $Advances + $Poses + @($Replacement, $Marker)
}

function Test-P3bParserRejects
{
    param(
        [object[]]$OutputLines,
        [string]$ExpectedMode = 'single',
        [int]$ExpectedCharacterCount = 1
    )

    try
    {
        ConvertFrom-P3bHarnessOutput `
            -OutputLines $OutputLines `
            -ExpectedMode $ExpectedMode `
            -ExpectedCharacterCount $ExpectedCharacterCount | Out-Null
        return $false
    }
    catch
    {
        return $true
    }
}

function Get-P3bFrameOrderOutput
{
    param([string]$Marker = $script:ValidFrameOrderMarker)

    return @('Godot Engine test', $Marker)
}

function Test-P3bFrameOrderParserRejects
{
    param(
        [object[]]$OutputLines,
        [string]$ExpectedMode = 'single'
    )

    try
    {
        ConvertFrom-P3bFrameOrderOutput `
            -OutputLines $OutputLines `
            -ExpectedMode $ExpectedMode | Out-Null
        return $false
    }
    catch
    {
        return $true
    }
}

function New-P3bParityResult
{
    param([string]$Mode)

    return [pscustomobject]@{
        Mode = $Mode
        Characters = 1
        Digest = '0123456789ABCDEF'
        Pose = '1111111111111111'
        FullPose = '2222222222222222'
        Root = '3333333333333333'
    }
}

function Test-P3bResultPairFieldRejects
{
    param([string]$Field)

    $single = New-P3bParityResult -Mode single
    $parallel = New-P3bParityResult -Mode parallel
    $parallel.$Field = 'FFFFFFFFFFFFFFFF'
    return Test-P3bResultPairRejects -Single $single -Parallel $parallel
}

function Test-P3bResultPairRejects
{
    param(
        [AllowNull()]
        [object]$Single,
        [AllowNull()]
        [object]$Parallel
    )

    try
    {
        Assert-P3bResultPair -Single $Single -Parallel $Parallel -CharacterCount 1
        return $false
    }
    catch
    {
        return $true
    }
}

function New-P3bFrameOrderParityResult
{
    param([string]$Mode)

    return [pscustomobject]@{
        Mode = $Mode
        Frames = 180
        Digest = '0123456789ABCDEF'
        Pose = '1111111111111111'
        FullPose = '2222222222222222'
        Root = '3333333333333333'
    }
}

function Test-P3bFrameOrderPairFieldRejects
{
    param([string]$Field)

    $single = New-P3bFrameOrderParityResult -Mode single
    $parallel = New-P3bFrameOrderParityResult -Mode parallel
    $parallel.$Field = 'FFFFFFFFFFFFFFFF'
    return Test-P3bFrameOrderPairRejects -Single $single -Parallel $parallel
}

function Test-P3bFrameOrderPairRejects
{
    param(
        [AllowNull()]
        [object]$Single,
        [AllowNull()]
        [object]$Parallel
    )

    try
    {
        Assert-P3bFrameOrderPair -Single $Single -Parallel $Parallel
        return $false
    }
    catch
    {
        return $true
    }
}

function Test-P3bChildGateRejects
{
    param(
        [object[]]$OutputLines,
        [int]$ExitCode,
        [string]$ExpectedMarker
    )

    try
    {
        Assert-P3bChildGateOutput `
            -PhaseName 'test' `
            -OutputLines $OutputLines `
            -ExitCode $ExitCode `
            -ExpectedMarker $ExpectedMarker
        return $false
    }
    catch
    {
        return $true
    }
}

function Test-P3bCleanWorktreeRejects
{
    param([string]$RepositoryRoot)

    try
    {
        Assert-P3bCleanWorktree -RepositoryRoot $RepositoryRoot
        return $false
    }
    catch
    {
        return $true
    }
}

function New-P3bSceneGateProbeScript
{
    param(
        [Parameter(Mandatory)]
        [string[]]$Lines,
        [int]$ExitCode = 0,
        [switch]$EmitArguments
    )

    $path = Join-Path $TestDrive ("scene-gate-{0}.ps1" -f ([Guid]::NewGuid().ToString('N')))
    $statements = @()
    for ($index = 0; $index -lt $Lines.Count; $index++)
    {
        $command = if ($index -eq 0) { 'Write-Output' } else { 'Write-Host' }
        $statements += "$command '{0}'" -f $Lines[$index].Replace("'", "''")
    }
    if ($EmitArguments)
    {
        $statements += "Write-Output ('PROBE_ARGS=' + (`$args -join '|'))"
    }
    $statements += "exit $ExitCode"
    [System.IO.File]::WriteAllText(
        $path,
        ($statements -join [Environment]::NewLine) + [Environment]::NewLine)
    return $path
}

function Test-P3bSceneGateRejects
{
    param(
        [Parameter(Mandatory)]
        [string[]]$Lines,
        [int]$ExitCode = 0,
        [string[]]$ExpectedExactMarkers = @('EXACT_ONE'),
        [string[]]$ExpectedRegexMarkers = @('\AREGEX value=([0-9A-F]{16})\z')
    )

    $probe = New-P3bSceneGateProbeScript -Lines $Lines -ExitCode $ExitCode
    try
    {
        Invoke-P3bSceneGate `
            -PhaseName 'probe' `
            -GodotExecutable $probe `
            -ProjectRoot $script:RepositoryRoot `
            -ScenePath 'res://probe.tscn' `
            -ExpectedExactMarkers $ExpectedExactMarkers `
            -ExpectedRegexMarkers $ExpectedRegexMarkers 6>$null | Out-Null
        return $false
    }
    catch
    {
        return $true
    }
}

function Test-P3bGraphParserRejects
{
    param(
        [AllowEmptyCollection()]
        [object[]]$OutputLines
    )

    try
    {
        ConvertFrom-P3bGraphOutput -OutputLines $OutputLines | Out-Null
        return $false
    }
    catch
    {
        return $true
    }
}

function New-P3bGraphParityResult
{
    param(
        [string]$DirectionDigest = '0123456789ABCDEF',
        [string]$Digest = '1111111111111111'
    )

    return [pscustomobject]@{
        DirectionDigest = $DirectionDigest
        Digest = $Digest
    }
}

function Test-P3bGraphPairRejects
{
    param(
        [AllowNull()]
        [object]$First,
        [AllowNull()]
        [object]$Second
    )

    try
    {
        Assert-P3bGraphPair -First $First -Second $Second
        return $false
    }
    catch
    {
        return $true
    }
}

function Get-P3bVerifierCommandAsts
{
    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile(
        $script:VerifierPath,
        [ref]$tokens,
        [ref]$parseErrors)
    if ($parseErrors.Count -ne 0)
    {
        throw "Verifier source did not parse: $($parseErrors -join [Environment]::NewLine)"
    }

    return @($ast.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.CommandAst]
    }, $true))
}

Describe 'P3B focused scene gate contracts' {
    It 'provides strict scene and graph digest gate functions' {
        Get-Command Invoke-P3bSceneGate -ErrorAction SilentlyContinue |
            Should Not BeNullOrEmpty
        Get-Command ConvertFrom-P3bGraphOutput -ErrorAction SilentlyContinue |
            Should Not BeNullOrEmpty
        Get-Command Assert-P3bGraphPair -ErrorAction SilentlyContinue |
            Should Not BeNullOrEmpty
    }

    It 'accepts multiple unique exact and regex markers from all captured streams' {
        if ($null -eq (Get-Command Invoke-P3bSceneGate -ErrorAction SilentlyContinue))
        {
            return
        }

        $probe = New-P3bSceneGateProbeScript -Lines @(
            'EXACT_ONE',
            'EXACT_TWO',
            'REGEX value=0123456789ABCDEF',
            'REGEX_TWO digest=1111111111111111',
            'GODOT_ALS_P3B_FAILURE_POLICY_OK phase=recovery') -EmitArguments
        $output = @(Invoke-P3bSceneGate `
            -PhaseName 'probe' `
            -GodotExecutable $probe `
            -ProjectRoot $script:RepositoryRoot `
            -ScenePath 'res://probe.tscn' `
            -SceneArguments @('--probe=1') `
            -ExpectedExactMarkers @('EXACT_ONE', 'EXACT_TWO') `
            -ExpectedRegexMarkers @(
                '\AREGEX value=([0-9A-F]{16})\z',
                '\AREGEX_TWO digest=([0-9A-F]{16})\z'))

        @($output | Where-Object { $_ -ceq 'EXACT_ONE' }).Count | Should Be 1
        @($output | Where-Object {
            $_ -ceq 'REGEX_TWO digest=1111111111111111'
        }).Count | Should Be 1
        $expectedArguments = 'PROBE_ARGS=--headless|--path|{0}|res://probe.tscn|--|--probe=1' -f `
            (Resolve-Path -LiteralPath $script:RepositoryRoot).Path
        @($output | Where-Object { $_ -ceq $expectedArguments }).Count | Should Be 1
    }

    It 'rejects nonzero exit engine errors and exact ALS failure marker lines' {
        if ($null -eq (Get-Command Invoke-P3bSceneGate -ErrorAction SilentlyContinue))
        {
            return
        }

        $valid = @('EXACT_ONE', 'REGEX value=0123456789ABCDEF')
        Test-P3bSceneGateRejects -Lines $valid -ExitCode 7 | Should Be $true
        foreach ($failureLine in @(
            'SCRIPT ERROR: failed',
            'Godot: ERROR: failed',
            'GODOT_ALS_P3B_FAIL code=runtime',
            'GODOT_ALS_P3_DEMO_INPUT_FAIL code=input',
            'GODOT_ALS_P3_DEMO_FAIL code=demo',
            'GODOT_ALS_P3_PRESENTATION_FAIL code=presentation'))
        {
            Test-P3bSceneGateRejects -Lines ($valid + $failureLine) | Should Be $true
        }
    }

    It 'does not leak rejected probe fixtures into parent verifier output' {
        if ($null -eq (Get-Command Invoke-P3bSceneGate -ErrorAction SilentlyContinue))
        {
            return
        }

        $captured = @(Test-P3bSceneGateRejects -Lines @(
            'EXACT_ONE',
            'REGEX value=0123456789ABCDEF',
            'SCRIPT ERROR: failed') *>&1)

        $captured.Count | Should Be 1
        $captured[0] | Should Be $true
    }

    It 'rejects every missing duplicate and malformed expected marker' {
        if ($null -eq (Get-Command Invoke-P3bSceneGate -ErrorAction SilentlyContinue))
        {
            return
        }

        Test-P3bSceneGateRejects -Lines @('REGEX value=0123456789ABCDEF') |
            Should Be $true
        Test-P3bSceneGateRejects -Lines @(
            'EXACT_ONE', 'EXACT_ONE', 'REGEX value=0123456789ABCDEF') |
            Should Be $true
        Test-P3bSceneGateRejects -Lines @('EXACT_ONE') | Should Be $true
        Test-P3bSceneGateRejects -Lines @(
            'EXACT_ONE',
            'REGEX value=0123456789ABCDEF',
            'REGEX value=1111111111111111') | Should Be $true
        Test-P3bSceneGateRejects -Lines @('EXACT_ONE', 'REGEX value=malformed') |
            Should Be $true
        Test-P3bSceneGateRejects -Lines @(
            'EXACT_ONE',
            'EXACT_ONE malformed',
            'REGEX value=0123456789ABCDEF') | Should Be $true
        Test-P3bSceneGateRejects -Lines @(
            'EXACT_ONE',
            'REGEX value=0123456789ABCDEF',
            'REGEX value=malformed') | Should Be $true
    }

    It 'parses both graph captures and rejects malformed duplicate or failed output' {
        if ($null -eq (Get-Command ConvertFrom-P3bGraphOutput -ErrorAction SilentlyContinue))
        {
            return
        }

        $result = ConvertFrom-P3bGraphOutput -OutputLines @($script:ValidGraphMarker)
        $result.DirectionDigest | Should Be '0123456789ABCDEF'
        $result.Digest | Should Be '1111111111111111'
        Test-P3bGraphParserRejects -OutputLines @() | Should Be $true
        Test-P3bGraphParserRejects -OutputLines @(
            $script:ValidGraphMarker,
            $script:ValidGraphMarker) |
            Should Be $true
        Test-P3bGraphParserRejects -OutputLines @($script:ValidGraphMarker + ' extra=1') |
            Should Be $true
        Test-P3bGraphParserRejects -OutputLines @('GODOT_ALS_P3B_FAIL code=graph') |
            Should Be $true
    }

    It 'requires both graph digest captures to be valid and equal' {
        if ($null -eq (Get-Command Assert-P3bGraphPair -ErrorAction SilentlyContinue))
        {
            return
        }

        Assert-P3bGraphPair `
            -First (New-P3bGraphParityResult) `
            -Second (New-P3bGraphParityResult)
        foreach ($field in @('DirectionDigest', 'Digest'))
        {
            $first = New-P3bGraphParityResult
            $second = New-P3bGraphParityResult
            $first.$field = 'AAAAAAAAAAAAAAAA'
            Test-P3bGraphPairRejects -First $first -Second $second | Should Be $true

            $first = New-P3bGraphParityResult
            $second = New-P3bGraphParityResult
            $first.PSObject.Properties.Remove($field)
            Test-P3bGraphPairRejects -First $first -Second $second | Should Be $true

            foreach ($invalidValue in @($null, '', 'abcdef0123456789', '0123456789ABCDEG', 42))
            {
                $first = New-P3bGraphParityResult
                $second = New-P3bGraphParityResult
                $first.$field = $invalidValue
                $second.$field = $invalidValue
                Test-P3bGraphPairRejects -First $first -Second $second |
                    Should Be $true
            }
        }
    }

    It 'runs all focused scene gates in the required order before the harness matrix' {
        $commands = @(Get-P3bVerifierCommandAsts)
        $sceneCalls = @($commands | Where-Object {
            $_.GetCommandName() -ceq 'Invoke-P3bSceneGate'
        })
        $sceneCalls.Count | Should Be 9
        if ($sceneCalls.Count -ne 9)
        {
            return
        }

        $inputCall = @($sceneCalls | Where-Object {
            $_.Extent.Text.Contains('p3_demo_input_smoke.tscn')
        })
        $libraryCall = @($sceneCalls | Where-Object {
            $_.Extent.Text.Contains('p3b_animation_library_smoke.tscn')
        })
        $presentationCalls = @($sceneCalls | Where-Object {
            $_.Extent.Text.Contains('p3_presentation_smoke.tscn')
        } | Sort-Object { $_.Extent.StartOffset })
        $graphCalls = @($sceneCalls | Where-Object {
            $_.Extent.Text.Contains('p3b_animation_graph_smoke.tscn')
        } | Sort-Object { $_.Extent.StartOffset })
        $frameCalls = @($sceneCalls | Where-Object {
            $_.Extent.Text.Contains('p3b_frame_order_smoke.tscn')
        })
        $frameSingle = @($frameCalls | Where-Object {
            $_.Extent.Text.Contains('--als-mode=single')
        })
        $frameParallel = @($frameCalls | Where-Object {
            $_.Extent.Text.Contains('--als-mode=parallel')
        })
        $demoCall = @($sceneCalls | Where-Object {
            $_.Extent.Text.Contains('p3_locomotion_demo.tscn')
        })

        @(
            $inputCall.Count,
            $libraryCall.Count,
            $presentationCalls.Count,
            $graphCalls.Count,
            $frameSingle.Count,
            $frameParallel.Count,
            $demoCall.Count) -join ',' | Should Be '1,1,2,2,1,1,1'
        if ($inputCall.Count -ne 1 -or $libraryCall.Count -ne 1 -or
            $presentationCalls.Count -ne 2 -or $graphCalls.Count -ne 2 -or
            $frameSingle.Count -ne 1 -or $frameParallel.Count -ne 1 -or
            $demoCall.Count -ne 1)
        {
            return
        }

        $presentationCalls[0].Extent.Text | Should Not Match '--als-failure-policy=initial'
        $presentationCalls[1].Extent.Text | Should Match '--als-failure-policy=initial'
        $demoCall[0].Extent.Text | Should Match '--als-smoke-frames=300'
        $inputCall[0].Extent.Text | Should Match 'GODOT_ALS_P3_DEMO_INPUT_OK'
        $libraryCall[0].Extent.Text | Should Match 'GODOT_ALS_P3B_LIBRARY_OK'
        $libraryCall[0].Extent.Text | Should Match 'GODOT_ALS_P3B_LIBRARY_LIFECYCLE_OK'
        $presentationCalls[0].Extent.Text | Should Match 'GODOT_ALS_P3_PRESENTATION_OK'
        $presentationCalls[1].Extent.Text | Should Match 'GODOT_ALS_P3B_INITIAL_ROLLBACK_OK'
        foreach ($graphCall in $graphCalls)
        {
            $graphCall.Extent.Text | Should Match 'GODOT_ALS_P3B_GRAPH_LIFECYCLE_OK'
            $graphCall.Extent.Text | Should Match 'GODOT_ALS_P3B_GRAPH_OK'
        }
        $frameSingle[0].Extent.Text | Should Match 'GODOT_ALS_P3B_FRAME_ORDER_OK'
        $frameParallel[0].Extent.Text | Should Match 'GODOT_ALS_P3B_FRAME_ORDER_OK'
        $demoCall[0].Extent.Text | Should Match 'GODOT_ALS_P3_DEMO_OK'
        $orderedOffsets = @(
            $inputCall[0].Extent.StartOffset,
            $libraryCall[0].Extent.StartOffset,
            $presentationCalls[0].Extent.StartOffset,
            $presentationCalls[1].Extent.StartOffset,
            $graphCalls[0].Extent.StartOffset,
            $graphCalls[1].Extent.StartOffset,
            $frameSingle[0].Extent.StartOffset,
            $frameParallel[0].Extent.StartOffset,
            $demoCall[0].Extent.StartOffset)
        ($orderedOffsets -join ',') | Should Be (($orderedOffsets | Sort-Object) -join ',')
        $matrixIndex = $script:VerifierSource.IndexOf('foreach ($characterCount in @(1, 10))')
        $matrixIndex | Should BeGreaterThan $demoCall[0].Extent.EndOffset
    }

    It 'uses strict marker contracts and compares graph and frame-order pairs before the matrix' {
        $commands = @(Get-P3bVerifierCommandAsts)
        $sceneCalls = @($commands | Where-Object {
            $_.GetCommandName() -ceq 'Invoke-P3bSceneGate'
        })
        $sceneText = $sceneCalls.Extent.Text -join [Environment]::NewLine
        foreach ($marker in @(
            'GODOT_ALS_P3_DEMO_INPUT_OK actions=11 directions=12 camera_basis=1 pitch=1 aiming=1 cleared=1 hud=1',
            'GODOT_ALS_P3B_LIBRARY_OK bones=68 clips=28 skeletons=1',
            'GODOT_ALS_P3B_LIBRARY_LIFECYCLE_OK double_dispose=1 parent_free=1 partial=1 rebuild=1',
            'GODOT_ALS_P3B_GRAPH_LIFECYCLE_OK double_dispose=1 parent_free=1 partial=1 rebuild=1 borrowed=1',
            'GODOT_ALS_P3_DEMO_OK frames=300 errors=0 ready=1 visible=1 max_visible=1'))
        {
            $sceneText | Should Match ([regex]::Escape($marker))
        }
        foreach ($regexMarkerName in @(
            'GODOT_ALS_P3_PRESENTATION_OK',
            'GODOT_ALS_P3B_INITIAL_ROLLBACK_OK',
            'GODOT_ALS_P3B_GRAPH_OK',
            'GODOT_ALS_P3B_FRAME_ORDER_OK'))
        {
            $sceneText | Should Match $regexMarkerName
        }

        $graphParsers = @($commands | Where-Object {
            $_.GetCommandName() -ceq 'ConvertFrom-P3bGraphOutput'
        })
        $graphPairs = @($commands | Where-Object {
            $_.GetCommandName() -ceq 'Assert-P3bGraphPair'
        })
        $frameParsers = @($commands | Where-Object {
            $_.GetCommandName() -ceq 'ConvertFrom-P3bFrameOrderOutput'
        })
        $framePairs = @($commands | Where-Object {
            $_.GetCommandName() -ceq 'Assert-P3bFrameOrderPair'
        })
        $graphParsers.Count | Should Be 2
        $graphPairs.Count | Should Be 1
        $frameParsers.Count | Should Be 2
        $framePairs.Count | Should Be 1
        if ($graphParsers.Count -eq 2 -and $graphPairs.Count -eq 1 -and
            $frameParsers.Count -eq 2 -and $framePairs.Count -eq 1)
        {
            $graphParsers = @($graphParsers | Sort-Object { $_.Extent.StartOffset })
            $frameParsers = @($frameParsers | Sort-Object { $_.Extent.StartOffset })
            $graphCalls = @($sceneCalls | Where-Object {
                $_.Extent.Text.Contains('p3b_animation_graph_smoke.tscn')
            } | Sort-Object { $_.Extent.StartOffset })
            $frameCalls = @($sceneCalls | Where-Object {
                $_.Extent.Text.Contains('p3b_frame_order_smoke.tscn')
            } | Sort-Object { $_.Extent.StartOffset })
            ($graphCalls[0].Extent.StartOffset -lt $graphParsers[0].Extent.StartOffset -and
                $graphParsers[0].Extent.StartOffset -lt $graphCalls[1].Extent.StartOffset -and
                $graphCalls[1].Extent.StartOffset -lt $graphParsers[1].Extent.StartOffset -and
                $graphParsers[1].Extent.StartOffset -lt $graphPairs[0].Extent.StartOffset) |
                Should Be $true
            ($frameCalls[0].Extent.StartOffset -lt $frameParsers[0].Extent.StartOffset -and
                $frameParsers[0].Extent.StartOffset -lt $frameCalls[1].Extent.StartOffset -and
                $frameCalls[1].Extent.StartOffset -lt $frameParsers[1].Extent.StartOffset -and
                $frameParsers[1].Extent.StartOffset -lt $framePairs[0].Extent.StartOffset) |
                Should Be $true
        }
    }
}

Describe 'P3B verifier contracts' {
    It 'provides harness and frame-order parsers and pair validators' {
        Get-Command ConvertFrom-P3bHarnessOutput -ErrorAction SilentlyContinue |
            Should Not BeNullOrEmpty
        Get-Command Assert-P3bResultPair -ErrorAction SilentlyContinue |
            Should Not BeNullOrEmpty
        Get-Command ConvertFrom-P3bFrameOrderOutput -ErrorAction SilentlyContinue |
            Should Not BeNullOrEmpty
        Get-Command Assert-P3bFrameOrderPair -ErrorAction SilentlyContinue |
            Should Not BeNullOrEmpty
    }

    It 'accepts one complete marker and all required evidence' {
        if ($null -eq (Get-Command ConvertFrom-P3bHarnessOutput -ErrorAction SilentlyContinue))
        {
            return
        }

        $result = ConvertFrom-P3bHarnessOutput `
            -OutputLines (Get-P3bOutput) `
            -ExpectedMode single `
            -ExpectedCharacterCount 1

        $result.Digest | Should Be '0123456789ABCDEF'
        $result.Pose | Should Be '1111111111111111'
        $result.FullPose | Should Be '2222222222222222'
        $result.Root | Should Be '3333333333333333'
        $result.P95Microseconds | Should Be 100
        $result.P99Microseconds | Should Be 200
    }

    It 'rejects malformed duplicate or unsuccessful markers' {
        if ($null -eq (Get-Command ConvertFrom-P3bHarnessOutput -ErrorAction SilentlyContinue))
        {
            return
        }

        Test-P3bParserRejects ((Get-P3bOutput) + 'GODOT_ALS_P3B_OK malformed') |
            Should Be $true
        Test-P3bParserRejects ((Get-P3bOutput) + $script:ValidMarker) |
            Should Be $true
        Test-P3bParserRejects @('GODOT_ALS_P3B_FAIL code=runtime') |
            Should Be $true
    }

    It 'rejects wrong fixed fields identities and trailing fields' {
        if ($null -eq (Get-Command ConvertFrom-P3bHarnessOutput -ErrorAction SilentlyContinue))
        {
            return
        }

        Test-P3bParserRejects (Get-P3bOutput -Marker $script:ValidMarker.Replace('warmup=120', 'warmup=119')) |
            Should Be $true
        Test-P3bParserRejects (Get-P3bOutput -Marker ($script:ValidMarker + ' extra=1')) |
            Should Be $true
        Test-P3bParserRejects (Get-P3bOutput -Marker $script:ValidMarker.Replace(
            'pose=1111111111111111 full_pose=2222222222222222',
            'full_pose=2222222222222222 pose=1111111111111111')) | Should Be $true
        Test-P3bParserRejects (Get-P3bOutput -Marker $script:ValidMarker.Replace(
            ' full_pose=2222222222222222 root=3333333333333333', '')) | Should Be $true
        Test-P3bParserRejects (Get-P3bOutput) -ExpectedMode parallel | Should Be $true
        Test-P3bParserRejects (Get-P3bOutput) -ExpectedCharacterCount 10 | Should Be $true
    }

    It 'rejects every nonzero error and aggregate allocation counter' {
        if ($null -eq (Get-Command ConvertFrom-P3bHarnessOutput -ErrorAction SilentlyContinue))
        {
            return
        }

        foreach ($field in @('missing', 'stale', 'generation', 'lag', 'allocations'))
        {
            Test-P3bParserRejects (Get-P3bOutput -Marker $script:ValidMarker.Replace("$field=0", "$field=1")) |
                Should Be $true
        }
    }

    It 'rejects nonzero allocation buckets' {
        if ($null -eq (Get-Command ConvertFrom-P3bHarnessOutput -ErrorAction SilentlyContinue))
        {
            return
        }

        foreach ($field in @('model', 'controller', 'skeleton', 'exchange', 'commit'))
        {
            Test-P3bParserRejects (Get-P3bOutput -Allocation $script:ValidAllocation.Replace("$field=0", "$field=1")) |
                Should Be $true
        }
    }

    It 'requires exactly one 600-frame advance line for every character' {
        if ($null -eq (Get-Command ConvertFrom-P3bHarnessOutput -ErrorAction SilentlyContinue))
        {
            return
        }

        Test-P3bParserRejects (Get-P3bOutput -Advances @()) | Should Be $true
        Test-P3bParserRejects (Get-P3bOutput -Advances @('GODOT_ALS_P3B_ADVANCE character=0 frames=599')) |
            Should Be $true
        Test-P3bParserRejects (Get-P3bOutput -Advances @(
            'GODOT_ALS_P3B_ADVANCE character=0 frames=600',
            'GODOT_ALS_P3B_ADVANCE character=0 frames=600')) | Should Be $true

        $tenMarker = $script:ValidMarker.Replace('characters=1', 'characters=10')
        $tenAdvances = @(0..9 | ForEach-Object {
            "GODOT_ALS_P3B_ADVANCE character=$_ frames=600"
        })
        $tenPoses = @(0..9 | ForEach-Object {
            "GODOT_ALS_P3B_POSE character=$_ changes=42"
        })
        $result = ConvertFrom-P3bHarnessOutput `
            -OutputLines (Get-P3bOutput -Marker $tenMarker -Advances $tenAdvances -Poses $tenPoses) `
            -ExpectedMode single `
            -ExpectedCharacterCount 10
        $result.Advances.Count | Should Be 10
    }

    It 'requires production old-generation rejection evidence exactly once' {
        if ($null -eq (Get-Command ConvertFrom-P3bHarnessOutput -ErrorAction SilentlyContinue))
        {
            return
        }

        Test-P3bParserRejects (Get-P3bOutput -Replacement '') | Should Be $true
        Test-P3bParserRejects (Get-P3bOutput -Replacement 'GODOT_ALS_P3B_REPLACEMENT character=0 old_generation_rejected=0') |
            Should Be $true
    }

    It 'requires nonzero raw skeleton pose changes for every character' {
        Test-P3bParserRejects (Get-P3bOutput -Poses @()) | Should Be $true
        Test-P3bParserRejects (Get-P3bOutput -Poses @(
            'GODOT_ALS_P3B_POSE character=0 changes=0')) | Should Be $true
        Test-P3bParserRejects (Get-P3bOutput -Poses @(
            'GODOT_ALS_P3B_POSE character=0 changes=1',
            'GODOT_ALS_P3B_POSE character=0 changes=2')) | Should Be $true
    }

    It 'rejects wrong worker affinity and invalid percentiles' {
        if ($null -eq (Get-Command ConvertFrom-P3bHarnessOutput -ErrorAction SilentlyContinue))
        {
            return
        }

        Test-P3bParserRejects (Get-P3bOutput -Marker $script:ValidMarker.Replace('off_main=0', 'off_main=1')) |
            Should Be $true
        $parallelMarker = $script:ValidMarker.Replace('mode=single', 'mode=parallel').Replace('off_main=0', 'off_main=1')
        ConvertFrom-P3bHarnessOutput `
            -OutputLines (Get-P3bOutput -Marker $parallelMarker) `
            -ExpectedMode parallel `
            -ExpectedCharacterCount 1 | Out-Null
        Test-P3bParserRejects (Get-P3bOutput -Marker $script:ValidMarker.Replace('p95_us=100 p99_us=200', 'p95_us=201 p99_us=200')) |
            Should Be $true
    }

    It 'rejects Godot script and engine errors even with a valid marker' {
        if ($null -eq (Get-Command ConvertFrom-P3bHarnessOutput -ErrorAction SilentlyContinue))
        {
            return
        }

        Test-P3bParserRejects ((Get-P3bOutput) + 'SCRIPT ERROR: failed') | Should Be $true
        Test-P3bParserRejects ((Get-P3bOutput) + 'ERROR: failed') | Should Be $true
        Test-P3bParserRejects ((Get-P3bOutput) + '  SCRIPT ERROR: indented failure') |
            Should Be $true
        Test-P3bParserRejects ((Get-P3bOutput) + 'Godot: ERROR: prefixed failure') |
            Should Be $true
    }

    It 'accepts blank engine output lines without emitting binding errors' {
        $previousErrorActionPreference = $ErrorActionPreference
        try
        {
            $ErrorActionPreference = 'Stop'
            ConvertFrom-P3bHarnessOutput `
                -OutputLines ((Get-P3bOutput) + '') `
                -ExpectedMode single `
                -ExpectedCharacterCount 1 | Out-Null
            ConvertFrom-P3bFrameOrderOutput `
                -OutputLines ((Get-P3bFrameOrderOutput) + '') `
                -ExpectedMode single | Out-Null
        }
        finally
        {
            $ErrorActionPreference = $previousErrorActionPreference
        }
    }

    It 'accepts matching harness digest summaries between execution modes' {
        Assert-P3bResultPair `
            -Single (New-P3bParityResult -Mode single) `
            -Parallel (New-P3bParityResult -Mode parallel) `
            -CharacterCount 1
    }

    It 'rejects harness Digest mismatch independently' {
        Test-P3bResultPairFieldRejects -Field Digest | Should Be $true
    }

    It 'rejects harness Pose mismatch independently' {
        Test-P3bResultPairFieldRejects -Field Pose | Should Be $true
    }

    It 'rejects harness FullPose mismatch independently' {
        Test-P3bResultPairFieldRejects -Field FullPose | Should Be $true
    }

    It 'rejects harness Root mismatch independently' {
        Test-P3bResultPairFieldRejects -Field Root | Should Be $true
    }

    It 'rejects harness pairs with summaries absent from both or either result' {
        $singleWithoutSummaries = [pscustomobject]@{ Mode = 'single'; Characters = 1 }
        $parallelWithoutSummaries = [pscustomobject]@{ Mode = 'parallel'; Characters = 1 }
        Test-P3bResultPairRejects `
            -Single $singleWithoutSummaries `
            -Parallel $parallelWithoutSummaries | Should Be $true

        foreach ($field in @('Digest', 'Pose', 'FullPose', 'Root'))
        {
            $single = New-P3bParityResult -Mode single
            $parallel = New-P3bParityResult -Mode parallel
            $single.PSObject.Properties.Remove($field)
            Test-P3bResultPairRejects -Single $single -Parallel $parallel | Should Be $true

            $single = New-P3bParityResult -Mode single
            $parallel = New-P3bParityResult -Mode parallel
            $parallel.PSObject.Properties.Remove($field)
            Test-P3bResultPairRejects -Single $single -Parallel $parallel | Should Be $true
        }
    }

    It 'rejects equal invalid harness summary values before comparing parity' {
        foreach ($field in @('Digest', 'Pose', 'FullPose', 'Root'))
        {
            foreach ($invalidValue in @(
                $null,
                '',
                'abcdef0123456789',
                '0123456789ABCDEG',
                42))
            {
                $single = New-P3bParityResult -Mode single
                $parallel = New-P3bParityResult -Mode parallel
                $single.$field = $invalidValue
                $parallel.$field = $invalidValue
                Test-P3bResultPairRejects -Single $single -Parallel $parallel |
                    Should Be $true
            }
        }
    }

    It 'rejects missing mistyped and incorrect harness identities' {
        foreach ($field in @('Mode', 'Characters'))
        {
            $single = New-P3bParityResult -Mode single
            $parallel = New-P3bParityResult -Mode parallel
            $single.PSObject.Properties.Remove($field)
            Test-P3bResultPairRejects -Single $single -Parallel $parallel | Should Be $true

            $single = New-P3bParityResult -Mode single
            $parallel = New-P3bParityResult -Mode parallel
            $parallel.PSObject.Properties.Remove($field)
            Test-P3bResultPairRejects -Single $single -Parallel $parallel | Should Be $true
        }

        $single = New-P3bParityResult -Mode single
        $parallel = New-P3bParityResult -Mode parallel
        $single.Mode = 'parallel'
        Test-P3bResultPairRejects -Single $single -Parallel $parallel | Should Be $true

        $single = New-P3bParityResult -Mode single
        $parallel = New-P3bParityResult -Mode parallel
        $parallel.Characters = 10
        Test-P3bResultPairRejects -Single $single -Parallel $parallel | Should Be $true

        $single = New-P3bParityResult -Mode single
        $parallel = New-P3bParityResult -Mode parallel
        $single.Characters = '1'
        $parallel.Characters = '1'
        Test-P3bResultPairRejects -Single $single -Parallel $parallel | Should Be $true

        Test-P3bResultPairRejects -Single $null -Parallel $parallel | Should Be $true
        Test-P3bResultPairRejects -Single $single -Parallel $null | Should Be $true
    }

    It 'rejects singleton array harness identities and summaries' {
        $single = New-P3bParityResult -Mode single
        $parallel = New-P3bParityResult -Mode parallel
        $single.Mode = [string[]]@('single')
        Test-P3bResultPairRejects -Single $single -Parallel $parallel | Should Be $true

        $single = New-P3bParityResult -Mode single
        $parallel = New-P3bParityResult -Mode parallel
        $single.Characters = [int[]]@(1)
        Test-P3bResultPairRejects -Single $single -Parallel $parallel | Should Be $true

        foreach ($field in @('Digest', 'Pose', 'FullPose', 'Root'))
        {
            $single = New-P3bParityResult -Mode single
            $parallel = New-P3bParityResult -Mode parallel
            $single.$field = [string[]]@($single.$field)
            Test-P3bResultPairRejects -Single $single -Parallel $parallel | Should Be $true
        }
    }

    It 'rejects singleton generic-list harness identity and summary values' {
        $single = New-P3bParityResult -Mode single
        $parallel = New-P3bParityResult -Mode parallel
        $modeList = [System.Collections.Generic.List[string]]::new()
        [void]$modeList.Add('single')
        $single.Mode = $modeList
        Test-P3bResultPairRejects -Single $single -Parallel $parallel | Should Be $true

        $single = New-P3bParityResult -Mode single
        $parallel = New-P3bParityResult -Mode parallel
        $digestList = [System.Collections.Generic.List[string]]::new()
        [void]$digestList.Add($single.Digest)
        $single.Digest = $digestList
        Test-P3bResultPairRejects -Single $single -Parallel $parallel | Should Be $true
    }
}

Describe 'P3B frame-order verifier contracts' {
    It 'accepts one exact frame-order marker and returns all summaries' {
        $result = ConvertFrom-P3bFrameOrderOutput `
            -OutputLines (Get-P3bFrameOrderOutput) `
            -ExpectedMode single

        $result.Mode | Should Be 'single'
        $result.Frames | Should Be 180
        $result.Digest | Should Be '0123456789ABCDEF'
        $result.Pose | Should Be '1111111111111111'
        $result.FullPose | Should Be '2222222222222222'
        $result.Root | Should Be '3333333333333333'
    }

    It 'accepts the exact parallel frame-order identity' {
        $parallelMarker = $script:ValidFrameOrderMarker.Replace('mode=single', 'mode=parallel')
        $result = ConvertFrom-P3bFrameOrderOutput `
            -OutputLines (Get-P3bFrameOrderOutput -Marker $parallelMarker) `
            -ExpectedMode parallel
        $result.Mode | Should Be 'parallel'
    }

    It 'rejects malformed duplicate missing and trailing frame-order markers' {
        Test-P3bFrameOrderParserRejects @('GODOT_ALS_P3B_FRAME_ORDER_OK malformed') |
            Should Be $true
        Test-P3bFrameOrderParserRejects ((Get-P3bFrameOrderOutput) +
            $script:ValidFrameOrderMarker) | Should Be $true
        Test-P3bFrameOrderParserRejects @('Godot Engine test') | Should Be $true
        Test-P3bFrameOrderParserRejects (Get-P3bFrameOrderOutput -Marker (
            $script:ValidFrameOrderMarker + ' extra=1')) | Should Be $true
        Test-P3bFrameOrderParserRejects (Get-P3bFrameOrderOutput -Marker (
            $script:ValidFrameOrderMarker.Replace(
                'pose=1111111111111111 full_pose=2222222222222222',
                'full_pose=2222222222222222 pose=1111111111111111'))) | Should Be $true
    }

    It 'rejects wrong frame-order mode and fixed evidence' {
        Test-P3bFrameOrderParserRejects (Get-P3bFrameOrderOutput) -ExpectedMode parallel |
            Should Be $true
        foreach ($mutation in @(
            @('frames=180', 'frames=179'),
            @('lag=0', 'lag=1'),
            @('stale=0', 'stale=1'),
            @('generation=1', 'generation=0'),
            @('old_generation_rejected=1', 'old_generation_rejected=0'),
            @('retired_released=1', 'retired_released=0'),
            @('max_visible=1', 'max_visible=2'),
            @('recovery_zero_visible=1', 'recovery_zero_visible=0')
        ))
        {
            $marker = $script:ValidFrameOrderMarker.Replace($mutation[0], $mutation[1])
            Test-P3bFrameOrderParserRejects (Get-P3bFrameOrderOutput -Marker $marker) |
                Should Be $true
        }
    }

    It 'rejects Godot errors and explicit failure markers' {
        Test-P3bFrameOrderParserRejects ((Get-P3bFrameOrderOutput) +
            'SCRIPT ERROR: failed') | Should Be $true
        Test-P3bFrameOrderParserRejects ((Get-P3bFrameOrderOutput) +
            'ERROR: failed') | Should Be $true
        Test-P3bFrameOrderParserRejects ((Get-P3bFrameOrderOutput) +
            'GODOT_ALS_P3B_FAIL') | Should Be $true
        Test-P3bFrameOrderParserRejects ((Get-P3bFrameOrderOutput) +
            'GODOT_ALS_P3B_FAIL code=runtime') |
            Should Be $true
    }

    It 'ignores successful failure-policy evidence beside the frame-order marker' {
        $output = (Get-P3bFrameOrderOutput) + @(
            'GODOT_ALS_P3B_FAILURE_POLICY_OK mode=interactive motor_frame=12 pose_frame=10 diagnostics=2',
            'GODOT_ALS_P3B_FAILURE_RETENTION_OK mode=single diagnostics=256 pending=0 retained=0')

        $result = ConvertFrom-P3bFrameOrderOutput -OutputLines $output -ExpectedMode single
        $result.Mode | Should Be 'single'
    }

    It 'accepts matching frame-order digest summaries between execution modes' {
        Assert-P3bFrameOrderPair `
            -Single (New-P3bFrameOrderParityResult -Mode single) `
            -Parallel (New-P3bFrameOrderParityResult -Mode parallel)
    }

    It 'rejects frame-order Digest mismatch independently' {
        Test-P3bFrameOrderPairFieldRejects -Field Digest | Should Be $true
    }

    It 'rejects frame-order Pose mismatch independently' {
        Test-P3bFrameOrderPairFieldRejects -Field Pose | Should Be $true
    }

    It 'rejects frame-order FullPose mismatch independently' {
        Test-P3bFrameOrderPairFieldRejects -Field FullPose | Should Be $true
    }

    It 'rejects frame-order Root mismatch independently' {
        Test-P3bFrameOrderPairFieldRejects -Field Root | Should Be $true
    }

    It 'rejects frame-order pairs with summaries absent from both or either result' {
        $singleWithoutSummaries = [pscustomobject]@{ Mode = 'single'; Frames = 180 }
        $parallelWithoutSummaries = [pscustomobject]@{ Mode = 'parallel'; Frames = 180 }
        Test-P3bFrameOrderPairRejects `
            -Single $singleWithoutSummaries `
            -Parallel $parallelWithoutSummaries | Should Be $true

        foreach ($field in @('Digest', 'Pose', 'FullPose', 'Root'))
        {
            $single = New-P3bFrameOrderParityResult -Mode single
            $parallel = New-P3bFrameOrderParityResult -Mode parallel
            $single.PSObject.Properties.Remove($field)
            Test-P3bFrameOrderPairRejects -Single $single -Parallel $parallel | Should Be $true

            $single = New-P3bFrameOrderParityResult -Mode single
            $parallel = New-P3bFrameOrderParityResult -Mode parallel
            $parallel.PSObject.Properties.Remove($field)
            Test-P3bFrameOrderPairRejects -Single $single -Parallel $parallel | Should Be $true
        }
    }

    It 'rejects equal invalid frame-order summary values before comparing parity' {
        foreach ($field in @('Digest', 'Pose', 'FullPose', 'Root'))
        {
            foreach ($invalidValue in @(
                $null,
                '',
                'abcdef0123456789',
                '0123456789ABCDEG',
                42))
            {
                $single = New-P3bFrameOrderParityResult -Mode single
                $parallel = New-P3bFrameOrderParityResult -Mode parallel
                $single.$field = $invalidValue
                $parallel.$field = $invalidValue
                Test-P3bFrameOrderPairRejects -Single $single -Parallel $parallel |
                    Should Be $true
            }
        }
    }

    It 'rejects missing mistyped and incorrect frame-order identities' {
        foreach ($field in @('Mode', 'Frames'))
        {
            $single = New-P3bFrameOrderParityResult -Mode single
            $parallel = New-P3bFrameOrderParityResult -Mode parallel
            $single.PSObject.Properties.Remove($field)
            Test-P3bFrameOrderPairRejects -Single $single -Parallel $parallel | Should Be $true

            $single = New-P3bFrameOrderParityResult -Mode single
            $parallel = New-P3bFrameOrderParityResult -Mode parallel
            $parallel.PSObject.Properties.Remove($field)
            Test-P3bFrameOrderPairRejects -Single $single -Parallel $parallel | Should Be $true
        }

        $single = New-P3bFrameOrderParityResult -Mode single
        $parallel = New-P3bFrameOrderParityResult -Mode parallel
        $parallel.Mode = 'single'
        Test-P3bFrameOrderPairRejects -Single $single -Parallel $parallel | Should Be $true

        $single = New-P3bFrameOrderParityResult -Mode single
        $parallel = New-P3bFrameOrderParityResult -Mode parallel
        $single.Frames = 179
        Test-P3bFrameOrderPairRejects -Single $single -Parallel $parallel | Should Be $true

        $single = New-P3bFrameOrderParityResult -Mode single
        $parallel = New-P3bFrameOrderParityResult -Mode parallel
        $single.Frames = '180'
        $parallel.Frames = '180'
        Test-P3bFrameOrderPairRejects -Single $single -Parallel $parallel | Should Be $true

        Test-P3bFrameOrderPairRejects -Single $null -Parallel $parallel | Should Be $true
        Test-P3bFrameOrderPairRejects -Single $single -Parallel $null | Should Be $true
    }

    It 'rejects singleton array frame-order identities and summaries' {
        $single = New-P3bFrameOrderParityResult -Mode single
        $parallel = New-P3bFrameOrderParityResult -Mode parallel
        $single.Mode = [string[]]@('single')
        Test-P3bFrameOrderPairRejects -Single $single -Parallel $parallel | Should Be $true

        $single = New-P3bFrameOrderParityResult -Mode single
        $parallel = New-P3bFrameOrderParityResult -Mode parallel
        $single.Frames = [int[]]@(180)
        Test-P3bFrameOrderPairRejects -Single $single -Parallel $parallel | Should Be $true

        foreach ($field in @('Digest', 'Pose', 'FullPose', 'Root'))
        {
            $single = New-P3bFrameOrderParityResult -Mode single
            $parallel = New-P3bFrameOrderParityResult -Mode parallel
            $single.$field = [string[]]@($single.$field)
            Test-P3bFrameOrderPairRejects -Single $single -Parallel $parallel | Should Be $true
        }
    }

    It 'rejects singleton generic-list frame-order identity and summary values' {
        $single = New-P3bFrameOrderParityResult -Mode single
        $parallel = New-P3bFrameOrderParityResult -Mode parallel
        $modeList = [System.Collections.Generic.List[string]]::new()
        [void]$modeList.Add('single')
        $single.Mode = $modeList
        Test-P3bFrameOrderPairRejects -Single $single -Parallel $parallel | Should Be $true

        $single = New-P3bFrameOrderParityResult -Mode single
        $parallel = New-P3bFrameOrderParityResult -Mode parallel
        $rootList = [System.Collections.Generic.List[string]]::new()
        [void]$rootList.Add($single.Root)
        $single.Root = $rootList
        Test-P3bFrameOrderPairRejects -Single $single -Parallel $parallel | Should Be $true
    }
}

Describe 'P3B raw-pose and timing instrumentation' {
    It 'aggregates result pose full-pose and root independently in both smokes' {
        $runtimeSource = [System.IO.File]::ReadAllText($script:RuntimePath)
        $harnessSource = [System.IO.File]::ReadAllText($script:HarnessPath)
        $frameOrderSource = [System.IO.File]::ReadAllText($script:FrameOrderPath)

        $runtimeSource | Should Match 'ulong FullPoseDigest'
        foreach ($pattern in $script:HarnessAggregationPatterns)
        {
            $harnessSource | Should Match $pattern
        }
        foreach ($pattern in $script:FrameOrderAggregationPatterns)
        {
            $frameOrderSource | Should Match $pattern
        }
        $harnessSource | Should Not Match `
            '(?m)^\s*Append\s*\(\s*ref\s+_poseDigest\s*,\s*diagnostics\s*\.\s*FullPoseDigest\s*\)\s*;\s*$'
    }

    It 'does not accept commented aggregation calls as source evidence' {
        $commentOnlyHarness = @(
            '// AlsResultDigest.Append(ref _resultDigest, diagnostics.Result);',
            '// Append(ref _poseDigest, diagnostics.PoseDigest);',
            '// Append(ref _fullPoseDigest, diagnostics.FullPoseDigest);',
            '// Append(ref _rootDigest, diagnostics.RootDigest);') -join [Environment]::NewLine
        $commentOnlyFrameOrder = @(
            '// AlsResultDigest.Append(ref _resultDigest, frame.Result);',
            '// Append(ref _poseDigest, frame.PoseDigest);',
            '// Append(ref _fullPoseDigest, frame.FullPoseDigest);',
            '// Append(ref _rootDigest, frame.RootDigest);') -join [Environment]::NewLine

        foreach ($pattern in $script:HarnessAggregationPatterns)
        {
            $commentOnlyHarness | Should Not Match $pattern
        }
        foreach ($pattern in $script:FrameOrderAggregationPatterns)
        {
            $commentOnlyFrameOrder | Should Not Match $pattern
        }
    }

    It 'sums only bounded production segments into worker timing' {
        $workerSource = [System.IO.File]::ReadAllText($script:WorkerPath)

        $workerSource | Should Not Match 'workerStartedAt'
        $workerSource | Should Match 'RecordWorkerAdvance\(\s*measurementIndex,\s*productionElapsedTicks\)'
        ([regex]::Matches(
            $workerSource,
            'productionSegmentStartedAt = measure\s*\? Stopwatch\.GetTimestamp\(\)\s*:\s*0L;')).Count |
            Should Be 6
        ([regex]::Matches(
            $workerSource,
            'productionElapsedTicks \+= Stopwatch\.GetTimestamp\(\) - productionSegmentStartedAt;')).Count |
            Should Be 6

        $firstProbe = $workerSource.IndexOf('GC.GetAllocatedBytesForCurrentThread()')
        $firstSegment = $workerSource.IndexOf('productionSegmentStartedAt')
        $firstRecord = $workerSource.IndexOf('RecordWorkerAdvance')
        $lastAllocationRecord = $workerSource.LastIndexOf('AddExchangeAllocations')
        $firstProbe | Should BeGreaterThan -1
        $firstSegment | Should BeGreaterThan $firstProbe
        $firstRecord | Should BeGreaterThan $lastAllocationRecord
    }
}

Describe 'P3B final regression closure' {
    BeforeEach {
        $script:ClosureRepository = Join-Path $TestDrive ([Guid]::NewGuid().ToString('N'))
        [void](New-Item -ItemType Directory -Path $script:ClosureRepository)
        & git -C $script:ClosureRepository init --quiet
        & git -C $script:ClosureRepository config user.email 'p3b-tests@example.invalid'
        & git -C $script:ClosureRepository config user.name 'P3B Tests'
        [System.IO.File]::WriteAllText((Join-Path $script:ClosureRepository 'tracked.txt'), "clean`n")
        & git -C $script:ClosureRepository add tracked.txt
        & git -C $script:ClosureRepository commit --quiet -m baseline
        if ($LASTEXITCODE -ne 0) { throw 'Could not initialize P3B closure test repository.' }
    }

    It 'accepts every unique full P3A closure marker without errors' {
        $output = @(
            'P2B_VERIFICATION_OK',
            'P1_VERIFICATION_OK',
            'P0_VERIFICATION_OK',
            'P3A_VERIFICATION_OK'
        )
        foreach ($marker in $output)
        {
            Assert-P3bChildGateOutput `
                -PhaseName $marker `
                -OutputLines $output `
                -ExitCode 0 `
                -ExpectedMarker $marker
        }
    }

    It 'rejects missing duplicate and successful-looking error output' {
        foreach ($output in @(
            ,@('build output'),
            ,@('P2B_VERIFICATION_OK', 'P2B_VERIFICATION_OK'),
            ,@('P2B_VERIFICATION_OK', 'SCRIPT ERROR: failed'),
            ,@('P2B_VERIFICATION_OK', 'Godot: ERROR: failed')
        ))
        {
            Test-P3bChildGateRejects `
                -OutputLines $output `
                -ExitCode 0 `
                -ExpectedMarker 'P2B_VERIFICATION_OK' | Should Be $true
        }
    }

    It 'rejects a child gate nonzero exit even with the expected marker' {
        Test-P3bChildGateRejects `
            -OutputLines @('P1_VERIFICATION_OK') `
            -ExitCode 7 `
            -ExpectedMarker 'P1_VERIFICATION_OK' | Should Be $true
    }

    It 'captures and rejects information-stream errors from a real child script' {
        $probe = Join-Path $TestDrive 'information-error.ps1'
        [System.IO.File]::WriteAllText(
            $probe,
            "Write-Host 'Godot: ERROR: information stream failure'`nWrite-Output 'P3A_VERIFICATION_OK'`nexit 0`n")
        $output = @(& $probe *>&1)
        $exitCode = $LASTEXITCODE

        Test-P3bChildGateRejects `
            -OutputLines $output `
            -ExitCode $exitCode `
            -ExpectedMarker 'P3A_VERIFICATION_OK' | Should Be $true
    }

    It 'accepts Pester descriptions that name error tokens without emitting an error line' {
        Assert-P3bChildGateOutput `
            -PhaseName 'P3A' `
            -OutputLines @(
                '[+] rejects SCRIPT ERROR and ERROR lines even with exit zero',
                'P3A_VERIFICATION_OK') `
            -ExitCode 0 `
            -ExpectedMarker 'P3A_VERIFICATION_OK'
    }

    It 'runs full P3A and final Release tests after the P3B matrix' {
        $matrixIndex = $script:VerifierSource.IndexOf('foreach ($characterCount in @(1, 10))')
        $p3aIndex = $script:VerifierSource.IndexOf("'verify-p3a.ps1'", $matrixIndex)
        $testsIndex = $script:VerifierSource.IndexOf(
            'dotnet test $solutionPath -c Release --no-restore',
            $matrixIndex)
        $successIndex = $script:VerifierSource.LastIndexOf('Write-Output $completionMarker')

        $matrixIndex | Should BeGreaterThan -1
        $p3aIndex | Should BeGreaterThan $matrixIndex
        $testsIndex | Should BeGreaterThan $p3aIndex
        $successIndex | Should BeGreaterThan $testsIndex
    }

    It 'captures all full P3A streams and validates its four real completion markers' {
        $script:VerifierSource | Should Match ([regex]::Escape(
            '& $p3aScript -GodotExecutable $GodotExecutable -ProjectRoot $projectRootPath *>&1'))
        $script:VerifierSource | Should Not Match ([regex]::Escape(
            '& $p3aScript -GodotExecutable $GodotExecutable -ProjectRoot $projectRootPath -SkipRegression'))
        $script:VerifierSource | Should Match 'Assert-P3bChildGateOutput'
        foreach ($marker in @(
            'P3A_VERIFICATION_OK',
            'P2B_VERIFICATION_OK',
            'P1_VERIFICATION_OK',
            'P0_VERIFICATION_OK'))
        {
            $script:VerifierSource | Should Match ([regex]::Escape("ExpectedMarker '$marker'"))
        }
        $script:VerifierSource | Should Not Match "Write-Output 'P3A_VERIFICATION_OK"
        $script:VerifierSource | Should Not Match "Write-Output 'P2B_VERIFICATION_OK"
        $script:VerifierSource | Should Not Match "Write-Output 'P1_VERIFICATION_OK"
        $script:VerifierSource | Should Not Match "Write-Output 'P0_VERIFICATION_OK"
    }

    It 'uses a focused marker that cannot impersonate full verification' {
        Get-P3bCompletionMarker -RegressionSkipped $true |
            Should Be 'P3B_FOCUSED_VERIFICATION_OK regression=skipped'
        Get-P3bCompletionMarker -RegressionSkipped $false |
            Should Be 'P3B_VERIFICATION_OK'
        $script:VerifierSource | Should Match 'Get-P3bCompletionMarker -RegressionSkipped \(\[bool\]\$SkipRegression\)'
    }

    It 'runs the locked P3A closure and clean-worktree closure before full success' {
        $p3aClosureIndex = $script:VerifierSource.IndexOf(
            'Assert-P3aRepositoryClosure -RepositoryRoot $projectRootPath -BaseCommit $p3aBaseCommit')
        $cleanClosureIndex = $script:VerifierSource.IndexOf(
            'Assert-P3bCleanWorktree -RepositoryRoot $projectRootPath')
        $successIndex = $script:VerifierSource.LastIndexOf('Write-Output $completionMarker')

        $p3aClosureIndex | Should BeGreaterThan -1
        $cleanClosureIndex | Should BeGreaterThan $p3aClosureIndex
        $successIndex | Should BeGreaterThan $cleanClosureIndex
    }

    It 'accepts a clean repository' {
        $failure = ''
        try { Assert-P3bCleanWorktree -RepositoryRoot $script:ClosureRepository }
        catch { $failure = $_.Exception.Message }
        $failure | Should BeNullOrEmpty
    }

    It 'rejects a dirty tracked worktree' {
        [System.IO.File]::WriteAllText(
            (Join-Path $script:ClosureRepository 'tracked.txt'),
            "modified`n")
        Test-P3bCleanWorktreeRejects -RepositoryRoot $script:ClosureRepository |
            Should Be $true
    }

    It 'rejects staged changes' {
        [System.IO.File]::WriteAllText(
            (Join-Path $script:ClosureRepository 'staged.txt'),
            "staged`n")
        & git -C $script:ClosureRepository add staged.txt
        Test-P3bCleanWorktreeRejects -RepositoryRoot $script:ClosureRepository |
            Should Be $true
    }

    It 'rejects untracked files' {
        [System.IO.File]::WriteAllText(
            (Join-Path $script:ClosureRepository 'untracked.txt'),
            "untracked`n")
        Test-P3bCleanWorktreeRejects -RepositoryRoot $script:ClosureRepository |
            Should Be $true
    }
}
